using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using OpenCvSharp;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Tool;
using OpenVisionLab.Vision3D.Geometry;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    // Opt-in measurement path, separate from deterministic functional smoke.
    internal static class HardeningEvaluation
    {
        private const int Warmups = 3, Repeats = 30, SoakFrames = 200;
        private static readonly string[] CacheNames = { "rotatedModels", "scaledModels", "searchModels" };
        private static readonly string[] CacheArrayNames = { "OffsetXValues", "OffsetYValues", "UnitDxValues", "UnitDyValues" };
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true };

        internal static int Run(string directory, string imageManifest)
        {
            string root = Path.GetFullPath(directory);
            Require(root.StartsWith(@"D:\", StringComparison.OrdinalIgnoreCase) && !Directory.Exists(root), "Choose a new D: study directory.");
            Directory.CreateDirectory(root);
            using Process process = Process.GetCurrentProcess();
            using Mat template = EdgePoseRefinementSmokeSuite.CreateTemplate();
            var native = process.Modules.Cast<ProcessModule>().Where(m => string.Equals(m.ModuleName, "OpenCvSharpExtern.dll", StringComparison.OrdinalIgnoreCase)).Select(m => new { m.FileName, Sha256 = Hash(m.FileName) }).ToArray();
            Require(native.Length == 1, "Expected exactly one loaded OpenCvSharpExtern native runtime after template creation.");
            Write(root, "plan.json", new
            {
                Warmups, Repeats, SoakFrames, FixedSeed = 8218, Threshold = 0.6,
                Percentile = "nearest-rank: sorted[ceil(p*N)-1]; no claim of stable tail latency from 30 samples",
                Timing = "external complete Execute including source/snapshot/metrics; internal Elapsed retained separately",
                SafetyPrivateGrowthBytes = 128L * 1024 * 1024, ProductionMemoryBudget = "not supplied",
                Scope = "bounded local regression/measurement, no field accuracy or production soak claim; no forced GC",
                Oracle = "authored affine pose and absent constant image; image crops have no independent field ground truth",
                TemplatePngSha256 = Convert.ToHexString(SHA256.HashData(template.ToBytes(".png", Array.Empty<int>()))),
                ImageManifestSha256 = Hash(imageManifest), AngleGrid = "-5,0,5 degrees", ScaleGrid = "0.95,1.0,1.05", SearchStep = 2,
                CacheBytes = "numeric array payload lower bound; excludes object headers, point lists and scaled models; process memory measured separately",
                CpuWorkers = Environment.ProcessorCount, Parallelism = "one serialized Tool; existing internal search policy",
                Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                OS = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                Binaries = new[] { typeof(HardeningEvaluation).Assembly, typeof(EdgeBasedTemplateMatchingTool).Assembly, typeof(HeightMap3D).Assembly, typeof(Mat).Assembly }
                    .Select(a => new { a.FullName, a.Location, Sha256 = Hash(a.Location), Version = a.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion }).ToArray(),
                Native = native
            });
            var accuracy = new List<object>();
            var timing = new List<object>();
            foreach (bool refine in new[] { false, true })
            {
                using EdgeBasedTemplateMatchingTool tool = EdgePoseRefinementSmokeSuite.CreateTool(template, refine);
                foreach (string condition in new[] { "exact", "off-grid", "brightness", "noise", "occlusion", "absent", "repeated", "cropped" })
                {
                    double angle = condition == "off-grid" ? 2.5 : 0;
                    double scale = condition == "off-grid" ? 1.025 : 1;
                    using Mat image = EdgePoseRefinementSmokeSuite.CreateSource(template, angle, scale);
                    if (condition == "brightness") image.ConvertTo(image, image.Type(), 0.7, 13);
                    if (condition == "noise")
                    {
                        Random random = new Random(8218);
                        for (int y = 0; y < image.Rows; y++)
                        for (int x = 0; x < image.Cols; x++) image.Set(y, x, (byte)Math.Max(0, Math.Min(255, image.At<byte>(y, x) + random.Next(-8, 9))));
                    }
                    if (condition == "occlusion") Cv2.Rectangle(image, new Rect(45, 30, 20, 25), Scalar.All(17), -1);
                    if (condition == "absent") image.SetTo(Scalar.All(33));
                    if (condition == "cropped") { image.SetTo(Scalar.All(17)); using Mat clipped = new Mat(image, new Rect(120, 40, 24, 64)); using Mat part = new Mat(template, new Rect(0, 0, 24, 64)); part.CopyTo(clipped); }
                    if (condition == "repeated") { using Mat second = new Mat(image, new Rect(0, 0, 64, 64)); template.CopyTo(second); }
                    long coldStart = Stopwatch.GetTimestamp();
                    using VisionToolResult result = tool.Execute(image);
                    double coldElapsed = Stopwatch.GetElapsedTime(coldStart).TotalMilliseconds;
                    var match = tool.results.FirstOrDefault();
                    Point2d truth = EdgePoseRefinementSmokeSuite.ExpectedPoint(new Point2d(32, 32), angle, scale);
                    bool poseOracle = condition == "exact" || condition == "off-grid" || condition == "brightness" || condition == "noise";
                    accuracy.Add(new
                    {
                        Condition = condition, Refine = refine, result.Success, Error = result.ErrorName, Count = tool.results.Count,
                        FirstExecuteOnTool = condition == "exact", TotalMilliseconds = coldElapsed, InternalMilliseconds = result.Elapsed.TotalMilliseconds,
                        AuthoredFullTargetCount = condition == "absent" || condition == "cropped" ? 0 : condition == "repeated" ? 2 : 1,
                        PositionErrorPx = poseOracle && match != null ? (double?)EdgePoseRefinementSmokeSuite.Distance(new Point2d(match.Center.X, match.Center.Y), truth) : null,
                        AngleErrorDegrees = poseOracle && match != null ? (double?)Math.Abs(match.Angle - angle) : null,
                        ScaleError = poseOracle && match != null ? (double?)Math.Abs(match.Scale - scale) : null,
                        Score = match?.Score, CacheEntries = CacheEntries(tool), CacheNumericArrayPayloadBytes = CachePayloadBytes(tool)
                    });
                }
                foreach (int width in new[] { 320, 1024 })
                {
                    using Mat image = new Mat(width * 3 / 4, width, MatType.CV_8UC1, Scalar.All(17));
                    using (Mat small = EdgePoseRefinementSmokeSuite.CreateSource(template, 2.5, 1.025))
                    using (Mat region = new Mat(image, new Rect(0, 0, small.Width, small.Height))) small.CopyTo(region);
                    for (int warm = 0; warm < Warmups; warm++) { using VisionToolResult ignored = tool.Execute(image); }
                    var raw = new List<object>();
                    var milliseconds = new List<double>();
                    for (int repeat = 0; repeat < Repeats; repeat++)
                    {
                        long allocation = GC.GetTotalAllocatedBytes(false);
                        long start = Stopwatch.GetTimestamp();
                        using VisionToolResult result = tool.Execute(image);
                        double total = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                        milliseconds.Add(total);
                        process.Refresh();
                        raw.Add(new { repeat, result.Success, Error = result.ErrorName, TotalMilliseconds = total, InternalMilliseconds = result.Elapsed.TotalMilliseconds,
                            ManagedAllocationBytes = GC.GetTotalAllocatedBytes(false) - allocation, process.PrivateMemorySize64, process.WorkingSet64, process.PeakWorkingSet64, CacheEntries = CacheEntries(tool), CacheNumericArrayPayloadBytes = CachePayloadBytes(tool) });
                    }
                    timing.Add(new { Width = width, Height = image.Height, Type = image.Type().ToString(), StrideBytes = image.Step(), Refine = refine,
                        P50 = Percentile(milliseconds, 0.50), P95 = Percentile(milliseconds, 0.95), P99 = Percentile(milliseconds, 0.99), Max = milliseconds.Max(), Samples = raw });
                }
            }
            Write(root, "accuracy.json", accuracy);
            Write(root, "latency.json", timing);

            var soak = new List<object>();
            using (EdgeBasedTemplateMatchingTool tool = EdgePoseRefinementSmokeSuite.CreateTool(template, true))
            using (Mat first = EdgePoseRefinementSmokeSuite.CreateSource(template, 0, 1))
            using (VisionToolResult retained = tool.Execute(first))
            {
                byte retainedPixel = retained.ResultImage.At<byte>(0, 0);
                process.Refresh();
                long startPrivate = process.PrivateMemorySize64;
                for (int frame = 0; frame < SoakFrames; frame++)
                {
                    using Mat image = EdgePoseRefinementSmokeSuite.CreateSource(template, 0.5 + (frame % 12) * 0.15, 0.99 + (frame % 12) * 0.001);
                    string action = frame % 37 == 0 ? "invalid" : frame % 19 == 0 ? "pre-cancel" : frame % 11 == 0 ? "absent" : "present";
                    if (action == "absent") image.SetTo(Scalar.All(33));
                    using Mat invalid = new Mat();
                    using CancellationTokenSource cancellation = new CancellationTokenSource();
                    if (action == "pre-cancel") cancellation.Cancel();
                    string outcome;
                    long allocation = GC.GetTotalAllocatedBytes(false);
                    long start = Stopwatch.GetTimestamp();
                    try
                    {
                        using VisionToolResult result = tool.Execute(action == "invalid" ? invalid : image, cancellation.Token);
                        outcome = result.Success ? "Success" : result.ErrorName;
                        if (action == "absent") Require(result.ResultImage.At<byte>(0, 0) == 33, "Soak retained an old result frame.");
                        if (action == "present") Require(result.Success, "Recovery after an induced failure did not match.");
                    }
                    catch (OperationCanceledException) { outcome = "Canceled"; }
                    long allocatedBytes = GC.GetTotalAllocatedBytes(false) - allocation;
                    Require(retained.ResultImage.At<byte>(0, 0) == retainedPixel, "Retained snapshot changed during soak.");
                    process.Refresh();
                    soak.Add(new { frame, action, outcome, ManagedAllocationBytes = allocatedBytes, TotalMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                        process.PrivateMemorySize64, process.PeakWorkingSet64, ManagedHeapBytes = GC.GetTotalMemory(false), Gc0 = GC.CollectionCount(0), Gc1 = GC.CollectionCount(1), Gc2 = GC.CollectionCount(2), CacheEntries = CacheEntries(tool), CacheNumericArrayPayloadBytes = CachePayloadBytes(tool) });
                    Require(process.PrivateMemorySize64 - startPrivate <= 128L * 1024 * 1024, "Local study safety memory guard exceeded; not a production memory qualification.");
                }
                tool.Dispose();
                Require(!retained.ResultImage.IsDisposed, "Retained result must outlive Tool disposal.");
            }
            Write(root, "soak.json", soak);
            var arrays = new List<object>();
            double[,] source = new double[512, 512];
            source[0, 0] = 5;
            for (int repeat = 0; repeat < Repeats; repeat++)
            {
                long allocation = GC.GetTotalAllocatedBytes(false), start = Stopwatch.GetTimestamp();
                HeightMap3D map = HeightMap3D.FromArray(source, 0, 0, 1, 1, "mm", "mm", "synthetic");
                double[] copy = map.CopyValues();
                source[0, 0] = 77; copy[0] = 88;
                Require(map.GetHeight(0, 0) == 5, "HeightMap defensive snapshots changed.");
                source[0, 0] = 5;
                arrays.Add(new { repeat, TotalMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds, ManagedAllocationBytes = GC.GetTotalAllocatedBytes(false) - allocation, FlattenedArrayArithmeticBytes = 512L * 512 * 8 });
            }
            Write(root, "heightmap-copy.json", arrays);
            var images = new List<object>();
            foreach (string path in JsonSerializer.Deserialize<string[]>(File.ReadAllText(imageManifest)))
            {
                using Mat raw = Cv2.ImRead(path, ImreadModes.Unchanged);
                if (raw.Empty() || raw.Depth() != MatType.CV_8U || (raw.Channels() != 1 && raw.Channels() != 3 && raw.Channels() != 4))
                { images.Add(new { path, Sha256 = Hash(path), Outcome = "Unsupported input; no silent depth conversion" }); continue; }
                using Mat gray = new Mat();
                if (raw.Channels() == 1) raw.CopyTo(gray);
                else Cv2.CvtColor(raw, gray, raw.Channels() == 3 ? ColorConversionCodes.BGR2GRAY : ColorConversionCodes.BGRA2GRAY);
                if (gray.Width > 640 || gray.Height > 480) Cv2.Resize(gray, gray, new Size(), Math.Min(640D / gray.Width, 480D / gray.Height), Math.Min(640D / gray.Width, 480D / gray.Height));
                string preview = Path.Combine(root, "image-" + images.Count + ".png");
                Cv2.ImWrite(preview, gray);
                int side = Math.Min(96, Math.Min(gray.Width, gray.Height) / 2);
                using Mat region = new Mat(gray, new Rect((gray.Width - side) / 2, (gray.Height - side) / 2, side, side));
                using EdgeBasedTemplateMatchingTool tool = EdgePoseRefinementSmokeSuite.CreateTool(region, false);
                tool.property.USE_FIND_ANGLE = false; tool.property.USE_FIND_SCALE = false;
                using VisionToolResult result = tool.Execute(gray);
                images.Add(new { path, Sha256 = Hash(path), Width = raw.Width, Height = raw.Height, Type = raw.Type().ToString(), Preview = preview,
                    Scope = "self-crop execution only; no independent field truth, no product OK/NG inference", result.Success, Error = result.ErrorName, Count = tool.results.Count });
            }
            Write(root, "images.json", images);
            Console.WriteLine("Hardening study finished: " + root);
            return 0;
        }

        private static int CacheEntries(EdgeBasedTemplateMatchingTool tool)
        {
            object store = typeof(EdgeBasedTemplateMatchingTool).GetField("modelStore", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(tool);
            object cache = store.GetType().GetProperty("Current").GetValue(store);
            return cache == null ? 0 : CacheNames.Sum(name => ((IDictionary)cache.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(cache)).Count);
        }

        private static double Percentile(List<double> values, double fraction) => values.OrderBy(value => value).ElementAt(Math.Max(0, (int)Math.Ceiling(fraction * values.Count) - 1));
        private static long CachePayloadBytes(EdgeBasedTemplateMatchingTool tool)
        {
            object store = typeof(EdgeBasedTemplateMatchingTool).GetField("modelStore", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(tool);
            object cache = store.GetType().GetProperty("Current").GetValue(store);
            if (cache == null) return 0;
            IDictionary models = (IDictionary)cache.GetType().GetField("rotatedModels", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(cache);
            long bytes = 0;
            foreach (object model in models.Values)
            foreach (string name in CacheArrayNames) bytes += Buffer.ByteLength((Array)model.GetType().GetProperty(name).GetValue(model));
            return bytes;
        }
        private static string Hash(string path) { using FileStream file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
        private static void Write(string root, string name, object value) => File.WriteAllText(Path.Combine(root, name), JsonSerializer.Serialize(value, Json));
    }
}
