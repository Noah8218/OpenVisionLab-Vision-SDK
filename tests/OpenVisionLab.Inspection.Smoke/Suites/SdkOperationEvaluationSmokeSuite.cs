using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using OpenCvSharp;
using OpenVisionLab.Compute;
using OpenVisionLab.Compute.Infrastructure;
using OpenVisionLab.Vision2D.Pipeline;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Tool;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    // Dev-only file/process Host and cost recorder. Does not change SDK policy or package contents.
    internal static class SdkOperationEvaluationSmokeSuite
    {
        private const int Repeats = 6;
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true };

        internal static int RunHost(string action, string directory)
        {
            directory = OwnedPath(directory);
            try
            {
                if (action == "prepare")
                {
                    Directory.CreateDirectory(directory);
                    foreach (string key in new[] { "a", "b", "utf8" })
                    {
                        using Mat image = QrCodeFixtures.Scene(key);
                        Cv2.ImWrite(Path.Combine(directory, key + ".png"), image);
                    }
                    using (Mat blank = QrCodeFixtures.Scene()) Cv2.ImWrite(Path.Combine(directory, "blank.png"), blank);
                    using (Mat partial = QrCodeFixtures.Partial()) Cv2.ImWrite(Path.Combine(directory, "partial.png"), partial);
                    File.WriteAllBytes(Path.Combine(directory, "corrupt.png"), new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0 });
                    File.WriteAllText(Path.Combine(directory, "profile.xml"), VisionPipelineSerializer.Serialize(SdkOperationSmokeSuite.QrProfile()), new UTF8Encoding(false));
                    using Mat original = EdgePoseRefinementSmokeSuite.CreateTemplate();
                    for (int index = 0; index < 3; index++)
                    {
                        using Mat template = new Mat();
                        int side = 48 + index * 8;
                        Cv2.Resize(original, template, new Size(side, side), 0, 0, InterpolationFlags.Nearest);
                        using Mat scene = SdkOperationSmokeSuite.EdgeScene(template);
                        using EdgeBasedTemplateMatchingTool tool = SdkOperationSmokeSuite.EdgeTool(template, null);
                        byte[] model = EdgeTemplateModelArtifactCodec.Encode(tool.PrepareLearnedModel("local-operation-" + index));
                        File.WriteAllBytes(Path.Combine(directory, "model-" + index + ".bin"), model);
                        Cv2.ImWrite(Path.Combine(directory, "edge-" + index + ".png"), scene);
                    }
                    WriteHost(new { Status = "Prepared", Identity = Identity(), Inputs = Directory.GetFiles(directory).Select(FileIdentity).ToArray(),
                        Oracle = "Independent QR modules from qrcode 8.2; PART-A/PART-B/오픈비전-부품A. Edge placement (17,23), template sides 48/56/64, odd source 257x193, step 1 and refinement disabled. Expected visual center derives from the supplied sampled-edge centroid and integer anchor rounding, not a physical tolerance.", InspectionPerformed = false });
                    return 0;
                }
                if (action == "stage" || action == "commit-stage" || action == "save-existing")
                {
                    string temporary = Path.Combine(directory, ".pending-" + Guid.NewGuid().ToString("N") + ".xml");
                    try
                    {
                        string xml = VisionPipelineSerializer.Serialize(SdkOperationSmokeSuite.QrProfile());
                        File.WriteAllText(temporary, action == "stage" ? xml.Substring(0, xml.Length / 2) : xml, new UTF8Encoding(false));
                        if (action != "stage")
                        {
                            _ = VisionPipelineSerializer.Deserialize(File.ReadAllText(temporary));
                            File.Move(temporary, Path.Combine(directory, action == "commit-stage" ? "committed.xml" : "profile.xml"), false);
                        }
                        WriteHost(new { Status = action == "stage" ? "Staged" : "Committed", Temporary = temporary, InspectionPerformed = false });
                        if (action != "save-existing") Console.ReadLine(); // Only the owned test parent interrupts this process.
                        return 0;
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                VisionPipeline pipeline = VisionPipelineSerializer.Deserialize(File.ReadAllText(Path.Combine(directory, "profile.xml")));
                using (QrCodeTool loaded = (QrCodeTool)VisionPipelineToolFactory.Create(pipeline.Steps.Single()))
                {
                    Require(loaded.LatestReading == null && loaded.imageSource.Empty(), "Restoration executed inspection.");
                }
                if (action == "load")
                {
                    WriteHost(new { Status = "LoadedOnly", Identity = Identity(), Profile = FileIdentity(Path.Combine(directory, "profile.xml")), InspectionPerformed = false });
                    return 0;
                }
                if (action == "edge")
                {
                    using Mat source = Cv2.ImRead(Path.Combine(directory, "edge-0.png"), ImreadModes.Grayscale);
                    using VisionComputeSession session = StudySession(true);
                    using EdgeBasedTemplateMatchingTool tool = new EdgeBasedTemplateMatchingTool(session);
                    tool.SetProperty(SdkOperationSmokeSuite.EdgeProperty());
                    EdgeTemplateModelArtifact model = EdgeTemplateModelArtifactCodec.Decode(File.ReadAllBytes(Path.Combine(directory, "model-0.bin")));
                    tool.SetLearnedModel(model);
                    using VisionToolResult result = tool.Execute(source);
                    SdkOperationSmokeSuite.RequirePreparedEdge(tool, result, model);
                    WriteHost(new { Status = "Completed", Identity = Identity(), Module = Environment.GetEnvironmentVariable("OVL_CUDA_TEST_MODULE"),
                        Diagnostics = Diagnostics(result.ComputeDiagnostics), StudyOverride = true, InspectionPerformed = true });
                    return 0;
                }
                if (!action.StartsWith("inspect-", StringComparison.Ordinal)) throw new ArgumentException("Unknown local operation action.");
                string keyName = action.Substring("inspect-".Length);
                using Mat input = Cv2.ImRead(Path.Combine(directory, keyName + ".png"), ImreadModes.Grayscale);
                if (input.Empty()) throw new InvalidDataException("Owned image cannot be decoded.");
                using VisionPipelineContext context = new VisionPipelineContext();
                context.SetLayer("Source", input);
                using VisionPipelineRunResult run = new VisionPipelineRuntime().RunWithFailureResults(pipeline, context);
                VisionToolResult current = run.StepResults.Single().ToolResult;
                if (keyName == "a" || keyName == "b" || keyName == "utf8") SdkOperationSmokeSuite.RequireQr(current, QrCodeFixtures.Text(keyName));
                WriteHost(new { Status = current.Success ? "Completed" : "Rejected", current.ErrorName, current.Message,
                    Texts = current.QrCodeReading?.Candidates.Select(value => value.Text).ToArray(), Identity = Identity(), InspectionPerformed = true });
                return current.Success ? 0 : 2;
            }
            catch (Exception error)
            {
                Exception cause = error.GetBaseException();
                bool backend = cause is DllNotFoundException || cause is EntryPointNotFoundException || cause is BadImageFormatException;
                WriteHost(new { Status = backend ? "BackendUnavailable" : "InvalidInputOrSettings", Error = cause.GetType().Name, cause.Message, Identity = Identity(), InspectionPerformed = false });
                return 3;
            }
        }

        internal static int RunEvaluation(string scenario, string inputDirectory, string reportPath)
        {
            inputDirectory = OwnedPath(inputDirectory);
            reportPath = OwnedPath(reportPath);
            if (scenario != "qr" && scenario != "qr-blank" && scenario != "qr-partial" && scenario != "qr-cancel" && scenario != "edge-cpu" && scenario != "edge3-cpu" && scenario != "edge-cuda" && scenario != "edge3-cuda")
                throw new ArgumentException("Select qr, qr-blank, qr-partial, qr-cancel, edge-cpu, edge3-cpu, edge-cuda or edge3-cuda.");
            Console.WriteLine("READY | local operation cost");
            bool qr = scenario.StartsWith("qr", StringComparison.Ordinal), cuda = scenario.EndsWith("-cuda", StringComparison.Ordinal);
            int count = scenario.StartsWith("edge3-", StringComparison.Ordinal) ? 3 : 1;
            string output = Path.Combine(Path.GetDirectoryName(reportPath), scenario + "-outputs");
            Directory.CreateDirectory(output);
            var samples = new List<object>();
            var resources = new List<object> { ResourceSnapshot("BeforeCalls") };
            var retained = new List<Mat>();
            VisionComputeSession session = null;
            double sessionCreateMs = 0, shutdownMs = 0;
            Stopwatch lifecycle = Stopwatch.StartNew();
            try
            {
                if (!qr) sessionCreateMs = Measure(() => session = StudySession(cuda));
                for (int repeat = 0; repeat < Repeats; repeat++)
                {
                    var images = new List<Mat>();
                    var tools = new List<EdgeBasedTemplateMatchingTool>();
                    var results = new List<VisionToolResult>();
                    var models = new List<byte[]>();
                    VisionPipelineContext context = null;
                    VisionPipelineRunResult run = null;
                    VisionPipeline profile = null;
                    string settings = null;
                    double readMs = 0, restoreMs = 0, executeMs = 0, consumeMs = 0, saveMs = 0, releaseMs = 0;
                    long allocated = GC.GetTotalAllocatedBytes(false);
                    int[] gc = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
                    Stopwatch entire = Stopwatch.StartNew();
                    try
                    {
                        readMs = Measure(() =>
                        {
                            if (qr)
                            {
                                settings = File.ReadAllText(Path.Combine(inputDirectory, "profile.xml"));
                                string inputName = scenario == "qr-blank" ? "blank.png" : scenario == "qr-partial" ? "partial.png" : repeat % 2 == 0 ? "a.png" : "b.png";
                                images.Add(Cv2.ImRead(Path.Combine(inputDirectory, inputName), ImreadModes.Grayscale));
                            }
                            else for (int index = 0; index < count; index++)
                            {
                                models.Add(File.ReadAllBytes(Path.Combine(inputDirectory, "model-" + index + ".bin")));
                                images.Add(Cv2.ImRead(Path.Combine(inputDirectory, "edge-" + index + ".png"), ImreadModes.Grayscale));
                            }
                            Require(images.All(image => !image.Empty()), "Measurement input decode failed.");
                        });
                        restoreMs = Measure(() =>
                        {
                            if (qr)
                            {
                                profile = VisionPipelineSerializer.Deserialize(settings);
                                if (scenario == "qr-partial") profile.Steps[0].Parameters["ExpectedCodeCount"] = "2";
                                context = new VisionPipelineContext();
                                context.SetLayer("Source", images[0]);
                            }
                            else foreach (byte[] bytes in models)
                            {
                                EdgeBasedTemplateMatchingTool tool = new EdgeBasedTemplateMatchingTool(session);
                                tools.Add(tool);
                                tool.SetProperty(SdkOperationSmokeSuite.EdgeProperty());
                                tool.SetLearnedModel(EdgeTemplateModelArtifactCodec.Decode(bytes));
                            }
                        });
                        executeMs = Measure(() =>
                        {
                            if (qr)
                            {
                                using CancellationTokenSource cancel = new CancellationTokenSource();
                                if (scenario == "qr-cancel") cancel.Cancel();
                                run = new VisionPipelineRuntime().RunWithFailureResults(profile, context, cancel.Token);
                                results.Add(run.StepResults.Single().ToolResult);
                            }
                            else for (int index = 0; index < count; index++) results.Add(tools[index].Execute(images[index]));
                        });
                        string serialized = null;
                        consumeMs = Measure(() =>
                        {
                            if (qr && scenario == "qr") SdkOperationSmokeSuite.RequireQr(results[0], repeat % 2 == 0 ? "PART-A" : "PART-B");
                            else if (qr)
                            {
                                VisionToolErrorCode expected = scenario == "qr-blank" ? VisionToolErrorCode.QrCodeNotDetected : scenario == "qr-partial" ? VisionToolErrorCode.QrCodeDecodeFailed : VisionToolErrorCode.StepCanceled;
                                Require(!results[0].Success && results[0].ErrorCode == expected, "Measured rejection/cancellation boundary differs.");
                                if (scenario == "qr-partial") Require(results[0].QrCodeReading.DecodedCount == 1 && results[0].QrCodeReading.DetectedCount == 2, "Partial evidence was lost.");
                            }
                            else for (int index = 0; index < count; index++)
                            {
                                SdkOperationSmokeSuite.RequirePreparedEdge(tools[index], results[index], EdgeTemplateModelArtifactCodec.Decode(models[index]));
                            }
                            if (repeat == 0 && results[0].ResultImage != null) retained.Add(results[0].ResultImage.Clone());
                            serialized = JsonSerializer.Serialize(results.Select(value => new { value.Success, value.ErrorName,
                                Texts = value.QrCodeReading?.Candidates.Select(candidate => candidate.Text).ToArray(), Diagnostics = Diagnostics(value.ComputeDiagnostics) }).ToArray(), Json);
                        });
                        saveMs = Measure(() => File.WriteAllText(Path.Combine(output, "result-" + repeat + ".json"), serialized, new UTF8Encoding(false)));
                    }
                    finally
                    {
                        releaseMs = Measure(() =>
                        {
                            if (run != null) run.Dispose();
                            else foreach (VisionToolResult value in results) value.Dispose();
                            context?.Dispose();
                            foreach (EdgeBasedTemplateMatchingTool tool in tools) tool.Dispose();
                            foreach (Mat image in images) image.Dispose();
                        });
                    }
                    entire.Stop();
                    Require(images.All(image => image.IsDisposed) && results.All(value => value.ResultImage == null || value.ResultImage.IsDisposed), "Completed call retained unowned image snapshots.");
                    samples.Add(new { Repeat = repeat, Temperature = repeat == 0 ? "FirstCallInFreshProcess" : "WarmProcess", ModelJobs = count,
                        WholeMilliseconds = entire.Elapsed.TotalMilliseconds, InputReadDecodeMilliseconds = readMs, SettingsModelRestoreMilliseconds = restoreMs,
                        ExecuteMilliseconds = executeMs, ResultValidationSerializationMilliseconds = consumeMs, SaveMilliseconds = saveMs, ReleaseMilliseconds = releaseMs,
                        AllocatedBytes = GC.GetTotalAllocatedBytes(false) - allocated, Collections = Enumerable.Range(0, 3).Select(index => GC.CollectionCount(index) - gc[index]).ToArray(),
                        Diagnostics = results.Select(value => Diagnostics(value.ComputeDiagnostics)).ToArray(), Resource = ResourceSnapshot("AfterCall"), OwnedImagesReleased = true });
                }
                resources.Add(ResourceSnapshot("WhileOneSnapshotAndSessionRetained"));
            }
            finally
            {
                shutdownMs = Measure(() => { foreach (Mat image in retained) image.Dispose(); session?.Dispose(); });
            }
            lifecycle.Stop();
            resources.Add(ResourceSnapshot("AfterExplicitShutdown"));
            Require(retained.All(image => image.IsDisposed), "Retained snapshots were not released at shutdown.");
            var report = new { Status = "Passed", Scenario = scenario, Repeats, ModelsPerWorkUnit = count,
                WorkUnit = qr ? "One explicit QR Pipeline job; qr-cancel requests cancellation before any native inspection" : "Sequential distinct prepared models, each with its own authored image",
                CudaStudyOverride = cuda, SessionLifetime = "One shared session in this process; Tools/results/contexts/inputs released after each call; one deliberately retained snapshot released at shutdown",
                SessionCreationMilliseconds = sessionCreateMs, FinalShutdownMilliseconds = shutdownMs, LifecycleMilliseconds = lifecycle.Elapsed.TotalMilliseconds,
                Samples = samples, Resources = resources, RetainedSnapshotsReleased = true, Identity = Identity(), Inputs = Directory.GetFiles(inputDirectory).Select(FileIdentity).ToArray(),
                Boundary = "Local six-call sample only. First call is process/JIT/library cold with OS file-cache state uncontrolled. Fresh process startup measured separately by parent. Warm samples reuse the process/session and restore model/settings per job. Timed phases include validation and file I/O; CUDA subphases are nested in Execute and are not added again. No forced GC; process/private/managed/native memory overlap. ScratchBytes is not total VRAM. No approved Takt/RAM, field accuracy, soak, deployment or production CUDA-policy qualification." };
            File.WriteAllText(reportPath, JsonSerializer.Serialize(report, Json), new UTF8Encoding(false));
            Console.WriteLine("PASS | local operation cost " + scenario + " | " + Repeats + " samples");
            return 0;
        }

        private static VisionComputeSession StudySession(bool cuda) => cuda
            ? new VisionComputeSession(new VisionComputeOptions { UseCuda = true }, token => CudaNativeProbe.Probe(Environment.GetEnvironmentVariable("OVL_CUDA_TEST_MODULE"), token), true)
            : new VisionComputeSession(new VisionComputeOptions());

        private static double Measure(Action action)
        {
            Stopwatch clock = Stopwatch.StartNew();
            action();
            clock.Stop();
            return clock.Elapsed.TotalMilliseconds;
        }

        private static object Diagnostics(ComputeExecutionEvidence value) => value == null ? null : new
        {
            value.RequestedCuda, value.AccelerationApplied, FallbackReason = value.FallbackReason.ToString(), value.NativeExecutionErrorCode,
            value.InitializationMilliseconds, value.UploadMilliseconds, value.KernelWaitMilliseconds, value.DownloadMilliseconds, value.PeakScratchBytes, value.GpuBatchCount
        };

        private static object ResourceSnapshot(string state)
        {
            using Process process = Process.GetCurrentProcess();
            process.Refresh();
            return new { State = state, WorkingSetBytes = process.WorkingSet64, PrivateBytes = process.PrivateMemorySize64,
                PeakWorkingSetBytes = process.PeakWorkingSet64, ManagedHeapBytes = GC.GetTotalMemory(false), TotalAllocatedBytes = GC.GetTotalAllocatedBytes(false),
                Handles = process.HandleCount, Threads = process.Threads.Count, Collections = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray() };
        }

        private static object Identity()
        {
            using Process process = Process.GetCurrentProcess();
            string[] selected = { "OpenCvSharpExtern", "OpenVisionLab.Cuda.Native", "execution-", "nvcuda", "vcruntime", "msvcp" };
            return new { ProcessId = Environment.ProcessId, Runtime = RuntimeInformation.FrameworkDescription, ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                RuntimeInformation.OSDescription, Environment.ProcessorCount, NativeModules = process.Modules.Cast<ProcessModule>()
                    .Where(module => selected.Any(name => module.ModuleName.StartsWith(name, StringComparison.OrdinalIgnoreCase))).Select(module => FileIdentity(module.FileName)).ToArray(),
                Assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(assembly => !assembly.IsDynamic &&
                    (assembly.GetName().Name.StartsWith("OpenVisionLab", StringComparison.Ordinal) || assembly.GetName().Name == "OpenCvSharp"))
                    .Select(assembly => new { Name = assembly.GetName().Name, Version = assembly.GetName().Version.ToString(),
                        InformationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                        Mvid = assembly.ManifestModule.ModuleVersionId.ToString("D"), File = FileIdentity(assembly.Location) }).ToArray() };
        }

        private static object FileIdentity(string file) => new { Path = Path.GetFullPath(file), Length = new FileInfo(file).Length, Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))) };

        private static string OwnedPath(string path)
        {
            string full = Path.GetFullPath(path);
            string root = @"D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\";
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Operation evidence must use the project D: root.");
            return full;
        }

        private static void WriteHost(object value) => Console.WriteLine(JsonSerializer.Serialize(value));
    }
}
