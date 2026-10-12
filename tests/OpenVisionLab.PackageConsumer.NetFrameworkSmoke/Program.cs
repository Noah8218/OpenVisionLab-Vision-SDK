using OpenCvSharp;
using OpenVisionLab.Inspection;
using OpenVisionLab.Compute;
using OpenVisionLab.Vision2D;
using OpenVisionLab.Vision2D.Pipeline;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Tool;
using OpenVisionLab.Vision3D.FeatureExtraction;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

namespace OpenVisionLab.PackageConsumer.NetFrameworkSmoke
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.InputEncoding = System.Text.Encoding.UTF8;
            try
            {
                Require(Environment.Is64BitProcess, "Consumer must execute as x64.");
                Require(AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName == ".NETFramework,Version=v4.8", "Consumer must target .NET Framework 4.8.");
                Console.WriteLine("RUNTIME|" + Environment.Version + "|" + AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName + "|x64");
                string[] names = { "OpenVisionLab.Core", "OpenVisionLab.Compute", "OpenVisionLab.Vision2D", "OpenVisionLab.Vision2D.Blob", "OpenVisionLab.Vision3D", "OpenVisionLab.Inspection", "OpenCvSharp" };
                foreach (string name in names)
                {
                    Assembly assembly = Assembly.Load(name);
                    Console.WriteLine("ASSEMBLY|" + name + "|" + assembly.Location + "|" + assembly.ManifestModule.ModuleVersionId + "|" + Hash(assembly.Location));
                }

                if (args.Length == 1 && args[0] == "--expect-native-missing")
                {
                    try
                    {
                        using (Mat unexpected = new Mat(1, 1, MatType.CV_8UC1, Scalar.Black)) { }
                    }
                    catch (Exception exception)
                    {
                        for (Exception cause = exception; cause != null; cause = cause.InnerException)
                        {
                            if (cause is DllNotFoundException)
                            {
                                Console.WriteLine("PASS|native-missing-fails-explicitly|" + cause.GetType().FullName);
                                return 0;
                            }
                        }
                        throw;
                    }
                    throw new InvalidOperationException("Missing native DLL unexpectedly loaded.");
                }

                bool tiles = args.Length == 1 && args[0] == "--cuda-tiles";
                bool cuda = tiles || args.Length == 2 && args[0] == "--cuda-local";
                if (tiles) CheckCudaTiles();
                else if (cuda) CheckCuda(args[1]);
                else
                {
                    CheckTwoD();
                    CheckCanny();
                    CheckThreeD();
                }
                if (cuda) Console.WriteLine("PASS|framework-cuda-consumer");
                foreach (ProcessModule module in Process.GetCurrentProcess().Modules)
                {
                    if (string.Equals(module.ModuleName, "OpenCvSharpExtern.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine("NATIVE|" + module.FileName + "|" + Hash(module.FileName));
                        Console.WriteLine("PASS|framework-package-consumer");
                        return 0;
                    }
                }
                throw new InvalidOperationException("Loaded native module identity was not found.");
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        private static void CheckCanny()
        {
            using (Mat parent = new Mat(31, 43, MatType.CV_8UC3, Scalar.White))
            using (Mat color = parent.SubMat(new Rect(3, 4, 31, 23)))
            using (Mat gray = new Mat())
            using (Mat expectedColor = new Mat())
            {
                for (int row = 0; row < color.Rows; row++)
                    for (int column = 0; column < color.Cols; column++)
                        color.Set(row, column, column < 15 ? new Vec3b(255, 0, 0) : new Vec3b(0, 49, 0));
                Cv2.CvtColor(color, gray, ColorConversionCodes.BGR2GRAY);
                Cv2.ImEncode(".png", parent, out byte[] encodedInput);
                using (SHA256 hash = SHA256.Create())
                    Console.WriteLine("INPUT|canny-padded-bgr|" + BitConverter.ToString(hash.ComputeHash(encodedInput)).Replace("-", string.Empty));
                using (Mat before = parent.Clone())
                using (Mat referenceColor = color.Clone())
                {
                    Cv2.Canny(referenceColor, expectedColor, 100, 200, 3, false);
                    VisionToolResult retained = null;
                    try
                    {
                        using (EdgeDetectionTool tool = new EdgeDetectionTool())
                        {
                            tool.SetProperty(new EdgeDetectionToolProperty { UseL2Gradient = false });
                            retained = tool.Execute(color);
                            Require(retained.Success && retained.ResultImage.Type() == MatType.CV_8UC1, "Color Canny must return an 8UC1 image: " + retained.Message);
                            foreach (Mat input in new[] { gray, color })
                                using (Mat referenceInput = input.Clone())
                                using (Mat expected = new Mat())
                                using (VisionToolResult actual = tool.Execute(input))
                                {
                                    Cv2.Canny(referenceInput, expected, 100, 200, 3, false);
                                    Require(actual.Success && actual.ResultImage.Type() == MatType.CV_8UC1 && actual.ResultImage.Size() == input.Size()
                                        && Cv2.Norm(actual.ResultImage, expected, NormTypes.L1) == 0, "Packaged Canny must match the distinct-output native reference.");
                                    Require(actual.Metrics["SourceImageChannels"] == input.Channels() && actual.Metrics["ResultImageChannels"] == 1, "Canny source/output channel metrics differ.");
                                    Require(input == color ? Cv2.CountNonZero(actual.ResultImage) > 0 : Cv2.CountNonZero(actual.ResultImage) == 0, "Color Canny must retain gradients at equal grayscale intensity.");
                                }
                            tool.SetProperty(new EdgeDetectionToolProperty { CannyThresholdLow = 400, CannyThresholdHigh = 200 });
                            using (VisionToolResult invalid = tool.Execute(color))
                                Require(!invalid.Success && invalid.ErrorCode == VisionToolErrorCode.EdgeDetectionInvalidThreshold
                                    && invalid.Exception == null && invalid.ResultImage == null, "Invalid Canny settings must return a typed failure.");
                            tool.SetProperty(new EdgeDetectionToolProperty { UseL2Gradient = false });
                            using (Mat unsupported = new Mat(23, 31, MatType.CV_16UC3, Scalar.All(17)))
                            using (Mat unsupportedBefore = unsupported.Clone())
                            using (VisionToolResult failed = tool.Execute(unsupported))
                                Require(!failed.Success && failed.ErrorCode == VisionToolErrorCode.OpenCvExecutionFailed
                                    && failed.Exception != null && failed.ResultImage == null && Cv2.Norm(unsupported, unsupportedBefore, NormTypes.L1) == 0,
                                    "Unsupported Canny depth must fail without altering caller pixels.");
                            using (VisionToolResult recovered = tool.Execute(color))
                                Require(recovered.Success && Cv2.Norm(recovered.ResultImage, expectedColor, NormTypes.L1) == 0, "Canny must recover on the same instance after failure.");
                            VisionPipelineStep step = new VisionPipelineStep { ToolType = "edgeDetection" };
                            step.Parameters["UseL2Gradient"] = "false";
                            using (EdgeDetectionTool restored = (EdgeDetectionTool)VisionPipelineToolFactory.Create(step))
                            using (VisionToolResult result = restored.Execute(gray))
                                Require(result.Success && result.ResultImage.Type() == MatType.CV_8UC1 && Cv2.CountNonZero(result.ResultImage) == 0, "Restored Canny must preserve the grayscale contract.");
                        }
                        Require(!retained.ResultImage.IsDisposed && Cv2.Norm(retained.ResultImage, expectedColor, NormTypes.L1) == 0, "A returned Canny snapshot must survive later calls and Tool disposal.");
                        Require(Cv2.Norm(parent, before, NormTypes.L1) == 0, "Canny must preserve caller pixels and ROI padding.");
                    }
                    finally { retained?.Dispose(); }
                }
            }
            Console.WriteLine("PASS|canny-package-consumer|Gray=1|Color=3|PaddedRoi|NativeReference|InvalidDepthRecovery|RetainedResult|SourcePreserved");
        }

        private static void CheckCudaTiles()
        {
            // Selected large-input boundary only; the earlier module matrix remains closed.
            using (Mat nativeIdentity = new Mat(1, 1, MatType.CV_8UC1, Scalar.Black)) { }
            const int rows = 2049, columns = 2049;
            double[] input = Enumerable.Repeat(1.0, rows * columns).ToArray();
            int spike = 2040 * columns + 17;
            input[spike] = 99;
            input[spike + 1] = double.NaN;
            input[spike + 2] = double.PositiveInfinity;
            using (VisionComputeSession session = new VisionComputeSession(new VisionComputeOptions { UseCuda = true }))
            {
                Require(session.Support.Reason == CudaFallbackReason.NotProbed, "Construction must remain lazy.");
                var medianOptions = new DeterministicMedianFilterOptions { KernelSize = 7 };
                var cpu = new DeterministicMedianFilterTool().Execute(rows, columns, input, medianOptions);
                var gpu = new DeterministicMedianFilterTool(session).Execute(rows, columns, input, medianOptions);
                Require(cpu.Success && gpu.Success && cpu.ChangedCount == gpu.ChangedCount, "Large median result/count differs.");
                for (int i = 0; i < input.Length; i++)
                    Require(double.IsNaN(cpu.Values[i]) && double.IsNaN(gpu.Values[i]) || BitConverter.DoubleToInt64Bits(cpu.Values[i]) == BitConverter.DoubleToInt64Bits(gpu.Values[i]), "Every large median value must match CPU.");
                Require(gpu.Values[spike] == 1 && double.IsNaN(gpu.Values[spike + 1]) && double.IsNaN(gpu.Values[spike + 2]), "Independent isolated-spike/missing-center expectation differs.");
                CheckCudaBackend("large-tiled-median", gpu.ComputeDiagnostics, true, CudaFallbackReason.None);
                Require(gpu.ComputeDiagnostics.PeakScratchBytes <= 64UL * 1024 * 1024, "Scratch cap changed.");
                var localOptions = new DeterministicLocalMedianOutlierFilterOptions { WindowSize = 5, MinimumValidNeighbors = 1, MaximumAbsoluteDeviation = 5 };
                var cpuLocal = new DeterministicLocalMedianOutlierFilterTool().Execute(rows, columns, input, localOptions);
                var gpuLocal = new DeterministicLocalMedianOutlierFilterTool(session).Execute(rows, columns, input, localOptions);
                Require(cpuLocal.Success && gpuLocal.Success && cpuLocal.OutlierIndices.SequenceEqual(gpuLocal.OutlierIndices) && gpuLocal.OutlierIndices.SequenceEqual(new[] { spike }), "Large original outlier indices differ.");
                for (int i = 0; i < input.Length; i++)
                    Require(double.IsNaN(cpuLocal.Values[i]) && double.IsNaN(gpuLocal.Values[i]) || BitConverter.DoubleToInt64Bits(cpuLocal.Values[i]) == BitConverter.DoubleToInt64Bits(gpuLocal.Values[i]), "Every large local-median value must match CPU.");
                CheckCudaBackend("large-tiled-local-median", gpuLocal.ComputeDiagnostics, true, CudaFallbackReason.None);
                Require(gpuLocal.ComputeDiagnostics.PeakScratchBytes <= 64UL * 1024 * 1024 && gpu.ComputeDiagnostics.AccelerationApplied, "Later execution must retain the earlier result/evidence.");
                session.Dispose();
                Require(gpu.Values[spike] == 1 && double.IsNaN(gpuLocal.Values[spike]), "Host disposal must retain large result values.");
                Console.WriteLine("PASS|framework-large-tiles|" + rows + "|" + columns + "|Cells=" + input.Length + "|AllValuesExact|Outlier=" + spike);
            }
        }

        private static void CheckCuda(string mode)
        {
            Require(mode == "missing" || mode == "discovery" || mode == "old" || mode == "cuda", "Unknown CUDA module case.");
            VisionComputeOptions options = new VisionComputeOptions { UseCuda = true };
            using (VisionComputeSession session = new VisionComputeSession(options))
            {
                options.UseCuda = false;
                Require(session.RequestedCuda && session.Support.Reason == CudaFallbackReason.NotProbed, "Construction must copy preference without discovery or execution.");
                double[] input = { 1, 1, 1, 1, 99, 1, 1, 1, 1 };
                long[] original = input.Select(BitConverter.DoubleToInt64Bits).ToArray();
                DeterministicMedianFilterOptions medianOptions = new DeterministicMedianFilterOptions { KernelSize = 3 };
                DeterministicMedianFilterTool median = new DeterministicMedianFilterTool(session);
                Require(!median.Execute(0, 3, input, medianOptions).Success && session.Support.Reason == CudaFallbackReason.NotProbed, "Invalid dimensions must reject without probing.");
                CudaFallbackReason edgeReason = mode == "missing" ? CudaFallbackReason.NativeModuleMissing : mode == "discovery" ? CudaFallbackReason.KernelNotImplemented : CudaFallbackReason.None;
                CudaFallbackReason localReason = mode == "old" ? CudaFallbackReason.NativeExportMissing : edgeReason;
                using (Mat template = new Mat(32, 32, MatType.CV_8UC1, Scalar.Black))
                using (Mat image = new Mat(512, 512, MatType.CV_8UC1, Scalar.Black))
                using (EdgeBasedTemplateMatchingTool cpu = CudaEdgeTool(null))
                using (EdgeBasedTemplateMatchingTool gpu = CudaEdgeTool(session))
                {
                    Cv2.Rectangle(template, new Rect(3, 3, 25, 24), Scalar.All(220), 2);
                    Cv2.Line(template, new Point(5, 26), new Point(26, 6), Scalar.All(150), 2);
                    Cv2.Circle(template, new Point(10, 10), 2, Scalar.All(255), -1);
                    using (Mat target = new Mat(image, new Rect(17, 23, 32, 32))) { template.CopyTo(target); }
                    byte[] imageBefore = image.ToBytes(".png");
                    cpu.SetTemplateImage(template);
                    gpu.SetTemplateImage(template);
                    using (VisionToolResult expected = cpu.Execute(image))
                    using (VisionToolResult actual = gpu.Execute(image))
                    {
                        Require(expected.Success && actual.Success && cpu.results.Count > 0 && cpu.results.Count == gpu.results.Count, "Embedded asymmetric template must match through both package Tool callers.");
                        foreach (var metric in expected.Metrics.Where(pair => !pair.Key.StartsWith("Phase.", StringComparison.Ordinal)))
                            Require(actual.Metrics.TryGetValue(metric.Key, out double value) && metric.Value.Equals(value), "Edge non-time metric differs.");
                        for (int i = 0; i < cpu.results.Count; i++)
                        {
                            var first = cpu.results[i];
                            var second = gpu.results[i];
                            Require(BitConverter.DoubleToInt64Bits(first.Score) == BitConverter.DoubleToInt64Bits(second.Score)
                                && BitConverter.DoubleToInt64Bits(first.EdgeScore) == BitConverter.DoubleToInt64Bits(second.EdgeScore)
                                && first.Center == second.Center && first.Angle == second.Angle && first.Scale == second.Scale
                                && first.Bounding == second.Bounding && first.PolarityReversed == second.PolarityReversed, "Every Edge score, ordered pose and polarity must match CPU.");
                        }
                        Require(expected.ResultImage.ToBytes(".png").SequenceEqual(actual.ResultImage.ToBytes(".png")), "Edge output pixels differ.");
                        CheckCudaBackend("edge", actual.ComputeDiagnostics, mode == "old" || mode == "cuda", edgeReason);
                        gpu.Dispose();
                        Require(!actual.ResultImage.IsDisposed && !image.IsDisposed && imageBefore.SequenceEqual(image.ToBytes(".png")), "Borrowed Tool disposal must retain result, caller input and session.");
                    }
                }

                DeterministicMedianFilterResult filtered = median.Execute(3, 3, input, medianOptions);
                Require(filtered.Success && filtered.Values.Count == 9 && filtered.Values.All(value => value == 1) && filtered.ChangedCount == 1, "Independent median impulse oracle differs.");
                CheckCudaBackend("median", filtered.ComputeDiagnostics, mode == "cuda", localReason);
                DeterministicLocalMedianOutlierFilterTool local = new DeterministicLocalMedianOutlierFilterTool(session);
                DeterministicLocalMedianOutlierFilterResult outliers = local.Execute(3, 3, input, new DeterministicLocalMedianOutlierFilterOptions { WindowSize = 3, MinimumValidNeighbors = 1, MaximumAbsoluteDeviation = 5 });
                Require(outliers.Success && outliers.OutlierIndices.SequenceEqual(new[] { 4 }) && double.IsNaN(outliers.Values[4]) && outliers.Values.Where((value, index) => index != 4).All(value => value == 1), "Independent center-excluded local-median oracle differs.");
                CheckCudaBackend("local-median", outliers.ComputeDiagnostics, mode == "cuda", localReason);
                Require(input.Select(BitConverter.DoubleToInt64Bits).SequenceEqual(original), "Caller input bits changed.");
                session.Dispose();
                bool disposed = false;
                try { median.Execute(3, 3, input, medianOptions); }
                catch (ObjectDisposedException) { disposed = true; }
                Require(disposed && filtered.Values.All(value => value == 1), "Host disposal must reject borrowed session reuse while retaining earlier values.");
            }
            using (VisionComputeSession fresh = new VisionComputeSession(new VisionComputeOptions { UseCuda = true }))
            {
                DeterministicMedianFilterResult recovered = new DeterministicMedianFilterTool(fresh).Execute(3, 3, new double[] { 1, 1, 1, 1, 99, 1, 1, 1, 1 }, new DeterministicMedianFilterOptions { KernelSize = 3 });
                Require(recovered.Success && recovered.Values.All(value => value == 1), "Fresh session must return complete result.");
                CudaFallbackReason reason = mode == "missing" ? CudaFallbackReason.NativeModuleMissing : mode == "discovery" ? CudaFallbackReason.KernelNotImplemented : mode == "old" ? CudaFallbackReason.NativeExportMissing : CudaFallbackReason.None;
                CheckCudaBackend("fresh-session-median", recovered.ComputeDiagnostics, mode == "cuda", reason);
            }
            Console.WriteLine("PASS|framework-cuda-contracts|" + mode);
        }

        private static EdgeBasedTemplateMatchingTool CudaEdgeTool(VisionComputeSession session)
        {
            EdgeBasedTemplateMatchingTool tool = new EdgeBasedTemplateMatchingTool(session);
            tool.SetProperty(new EdgeBasedTemplateMatchingToolProperty
            {
                SCORE_MIN = 0.5, GREEDINESS = 0.8, SEARCH_STEP = 2, MAX_TEMPLATE_POINTS = 100,
                USE_HYBRID_VERIFY = false, USE_FIND_ANGLE = false, USE_FIND_SCALE = false, USE_DRAW_IMAGE = false,
                USE_POSITION_REFINE = true, USE_SUBPIXEL_REFINE = true, CANNY_HIGH = 100, MIN_GRADIENT_MAGNITUDE = 5
            });
            return tool;
        }

        private static void CheckCudaBackend(string operation, ComputeExecutionEvidence evidence, bool applied, CudaFallbackReason reason)
        {
            Require(evidence != null && evidence.RequestedCuda && evidence.AccelerationApplied == applied && evidence.FallbackReason == reason, "CUDA request/application/fallback differs: " + operation + " actual=" + evidence.FallbackReason + " expected=" + reason + " applied=" + evidence.AccelerationApplied + " support=" + evidence.Support.Reason);
            Require(applied ? evidence.GpuBatchCount > 0 : evidence.GpuBatchCount == 0, "Actual CUDA launches differ from claimed application.");
            string path = evidence.Support.ModulePath;
            Console.WriteLine("CUDA|" + operation + "|Applied=" + evidence.AccelerationApplied + "|Support=" + evidence.Support.Reason + "|Detected=" + evidence.Support.DeviceDetected + "|Reason=" + evidence.FallbackReason + "|Batches=" + evidence.GpuBatchCount + "|Scratch=" + evidence.PeakScratchBytes + "|Module=" + path + "|SHA=" + (path != null && File.Exists(path) ? Hash(path) : "absent"));
        }

        private static void CheckTwoD()
        {
            using (Mat source = new Mat(3, 3, MatType.CV_8UC1, Scalar.Black))
            using (FilterTool tool = new FilterTool())
            {
                source.Set<byte>(0, 0, 90);
                tool.SetProperty(new FilterToolProperty { FilterType = FilterToolType.BoxFilter, BorderType = BorderTypes.Replicate });
                using (VisionToolResult retained = tool.Execute(source))
                {
                    RequirePixels(retained);
                    tool.property.KernelWidth = 0;
                    using (VisionToolResult failure = tool.Execute(source))
                    {
                        Require(!failure.Success && failure.ErrorCode == VisionToolErrorCode.FilterInvalidKernel && failure.ResultImage == null, "Invalid kernel did not fail explicitly.");
                    }
                    tool.property.KernelWidth = 3;
                    using (VisionToolResult recovered = tool.Execute(source)) { RequirePixels(recovered); }
                    RequirePixels(retained);
                }

                VisionPipelineStep step = new VisionPipelineStep { ToolType = "filter" };
                step.Parameters["FilterType"] = "BoxFilter";
                step.Parameters["BorderType"] = "Replicate";
                using (FilterTool factory = (FilterTool)VisionPipelineToolFactory.Create(step))
                using (VisionToolResult result = factory.Execute(source)) { RequirePixels(result); }
                using (CombinedInspectionRunResult combined = new CombinedInspectionRunner().Run(new CombinedInspectionInput { Image = source }, new IVisionTool[] { tool }, null))
                {
                    Require(combined.Success && combined.Steps.Count == 1, "CombinedInspection did not execute its package-only 2D step.");
                    RequirePixels(combined.Steps[0].VisionResult);
                }
                tool.Dispose();
                Require(source.At<byte>(0, 0) == 90 && Cv2.CountNonZero(source) == 1, "Tool/result disposal modified or released caller input.");
            }
            Console.WriteLine("PASS|2d-direct-factory-recovery-retained-result-combined-ownership");
        }

        private static void RequirePixels(VisionToolResult result)
        {
            byte[] expected = { 40, 20, 0, 20, 10, 0, 0, 0, 0 };
            Require(result.Success && result.ResultImage != null && result.ResultImage.Type() == MatType.CV_8UC1, "BoxFilter did not return the expected image type.");
            for (int row = 0; row < 3; row++)
                for (int column = 0; column < 3; column++)
                    Require(result.ResultImage.At<byte>(row, column) == expected[row * 3 + column], "BoxFilter differs from independent normalized corner sums.");
        }

        private static void CheckThreeD()
        {
            double[] input = { 1, 1, 1, 1, 99, 1, 1, 1, 1 };
            double[] original = (double[])input.Clone();
            DeterministicMedianFilterTool tool = new DeterministicMedianFilterTool();
            DeterministicMedianFilterResult retained = tool.Execute(3, 3, input, new DeterministicMedianFilterOptions { KernelSize = 3 });
            Require(retained.Success && retained.Values.All(value => value == 1d), "3D CPU median differs from independent eight-one/one-outlier oracle.");
            DeterministicMedianFilterResult rejected = tool.Execute(3, 3, input, new DeterministicMedianFilterOptions { KernelSize = 2 });
            Require(!rejected.Success && rejected.Values.Count == 0, "3D invalid kernel was not rejected.");
            DeterministicMedianFilterResult recovered = tool.Execute(3, 3, input, new DeterministicMedianFilterOptions { KernelSize = 3 });
            Require(recovered.Success && recovered.Values.All(value => value == 1d) && retained.Values.All(value => value == 1d) && input.SequenceEqual(original), "3D recovery corrupted input or retained result.");
            Require(recovered.ComputeDiagnostics == null, "CPU-only median unexpectedly reports a borrowed compute session.");
            Console.WriteLine("PASS|3d-cpu-median-failure-recovery-input-result-ownership");
        }

        private static string Hash(string path)
        {
            using (SHA256 algorithm = SHA256.Create())
            using (Stream stream = File.OpenRead(path))
                return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", string.Empty);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
