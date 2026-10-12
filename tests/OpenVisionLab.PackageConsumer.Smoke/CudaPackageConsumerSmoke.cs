using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCvSharp;
using OpenVisionLab.Compute;
using OpenVisionLab.Inspection;
using OpenVisionLab.Vision2D.Pipeline;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Tool;
using OpenVisionLab.Vision3D.FeatureExtraction;
using OpenVisionLab.Vision3D.Geometry;

// Public package contracts only: no friend assembly, reflection invocation, or source project reference.
internal static class CudaPackageConsumerSmoke
{
    internal static int Run(string[] args)
    {
        if (args.Length != 3 || !new[] { "cpu", "missing", "discovery", "old", "cuda" }.Contains(args[1]))
            throw new ArgumentException("Use --cuda-local <cpu|missing|discovery|old|cuda> <fresh D: report>.");
        string reportPath = Path.GetFullPath(args[2]);
        Require(reportPath.StartsWith(@"D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\", StringComparison.OrdinalIgnoreCase), "Use the project D: evidence root.");
        Require(!File.Exists(reportPath), "Choose a fresh report; completed evidence is immutable.");
        string mode = args[1];
        bool requested = mode != "cpu";
        List<object> cases = new List<object>();
        Exception failure = null;
        bool sessionDisposed = false;
        string modulePath = null;
        try
        {
            VisionComputeOptions options = new VisionComputeOptions { UseCuda = requested };
            using VisionComputeSession session = new VisionComputeSession(options);
            options.UseCuda = !requested;
            Require(session.RequestedCuda == requested, "The session must retain its copied preference.");
            Require(session.Support.Reason == (requested ? CudaFallbackReason.NotProbed : CudaFallbackReason.Disabled), "Construction must not discover or execute.");
            string directory = Path.GetDirectoryName(typeof(VisionComputeSession).Assembly.Location);
            string adjacent = Path.Combine(directory, "OpenVisionLab.Cuda.Native.dll");
            string runtime = Path.Combine(directory, "runtimes", "win-x64", "native", "OpenVisionLab.Cuda.Native.dll");
            Require((File.Exists(adjacent) || File.Exists(runtime)) == (mode == "discovery" || mode == "old" || mode == "cuda"), "The selected module installation must match this case.");
            modulePath = File.Exists(adjacent) ? adjacent : runtime;
            double[] input = { 1, 1, 1, 1, 99, 1, 1, 1, 1 };
            long[] inputBits = input.Select(BitConverter.DoubleToInt64Bits).ToArray();
            DeterministicMedianFilterOptions medianOptions = new DeterministicMedianFilterOptions { KernelSize = 3 };
            DeterministicMedianFilterTool median = new DeterministicMedianFilterTool(session);
            DeterministicLocalMedianOutlierFilterTool local = new DeterministicLocalMedianOutlierFilterTool(session);
            Require(!median.Execute(0, 3, input, medianOptions).Success, "Invalid dimensions must retain the public failure result.");
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                bool canceled = false;
                try { median.Execute(3, 3, input, medianOptions, cancellation.Token); }
                catch (OperationCanceledException) { canceled = true; }
                Require(canceled, "Pre-canceled explicit execution must propagate cancellation.");
            }
            Require(session.Support.Reason == (requested ? CudaFallbackReason.NotProbed : CudaFallbackReason.Disabled), "Invalid/canceled requests must not probe.");
            cases.Add(new { Id = "P01", Expected = "Invalid/canceled -> no probe -> next valid call", Actual = "Passed" });

            CudaFallbackReason filterReason = mode switch
            {
                "cpu" => CudaFallbackReason.Disabled,
                "missing" => CudaFallbackReason.NativeModuleMissing,
                "discovery" => CudaFallbackReason.KernelNotImplemented,
                "old" => CudaFallbackReason.NativeExportMissing,
                _ => CudaFallbackReason.None
            };
            bool accelerated = mode == "cuda";
            DeterministicMedianFilterResult filtered = median.Execute(3, 3, input, medianOptions);
            Require(filtered.Success && filtered.Values.Count == 9 && filtered.Values.All(v => v == 1) && filtered.ChangedCount == 1, "Independent impulse median oracle differs.");
            CheckBackend(filtered.ComputeDiagnostics, requested, accelerated, filterReason);
            cases.Add(new { Id = "P02", Expected = "All nine values=1; ChangedCount=1", Actual = filtered });
            DeterministicLocalMedianOutlierFilterResult outliers = local.Execute(3, 3, input,
                new DeterministicLocalMedianOutlierFilterOptions { WindowSize = 3, MinimumValidNeighbors = 1, MaximumAbsoluteDeviation = 5 });
            Require(outliers.Success && outliers.Values.Count == 9 && outliers.OutlierIndices.SequenceEqual(new[] { 4 })
                && double.IsNaN(outliers.Values[4]) && outliers.Values.Where((v, i) => i != 4).All(v => v == 1), "Independent center-excluded local-median oracle differs.");
            CheckBackend(outliers.ComputeDiagnostics, requested, accelerated, filterReason);
            Require(input.Select(BitConverter.DoubleToInt64Bits).SequenceEqual(inputBits), "Filters must retain every caller input bit.");
            cases.Add(new { Id = "P03", Expected = "Only center index4 becomes NaN; input unchanged", Actual = outliers });

            // A missing/failed module takes precedence over an algorithm's CPU policy in public diagnostics.
            CudaFallbackReason policyReason = mode switch
            {
                "cpu" => CudaFallbackReason.Disabled,
                "missing" => CudaFallbackReason.NativeModuleMissing,
                "discovery" => CudaFallbackReason.KernelNotImplemented,
                _ => CudaFallbackReason.NoMeasuredBenefit
            };
            HeightMap3D map = new HeightMap3D(3, 3, -3, 7, 2, 4, Enumerable.Repeat(4.0, 9).ToArray(), "mm", "package-frame", "flat-plane");
            HeightMapNormalPreparationResult normals = new HeightMapNormalPreparationTool(session).Execute(map);
            Require(normals.Success && normals.RowCount == 3 && normals.ColumnCount == 3 && normals.InputFiniteSampleCount == 9
                && normals.CalculatedNormalCount == 9 && normals.UnavailableNormalCount == 0 && normals.CentralDerivativeCount == 6
                && normals.OneSidedDerivativeCount == 12 && normals.MissingDerivativeCount == 0 && normals.Samples.Count == 9
                && normals.ValidationState == HeightMapNormalValidationState.NotRequested, "Independent plane counts differ.");
            for (int i = 0; i < 9; i++)
            {
                HeightMapNormalSample sample = normals.Samples[i];
                Require(sample.Row == i / 3 && sample.Column == i % 3 && sample.Position.X == -3 + 2 * (i % 3)
                    && sample.Position.Y == 4 && sample.Position.Z == 7 + 4 * (i / 3)
                    && BitConverter.DoubleToInt64Bits(sample.Normal.X) == long.MinValue && sample.Normal.Y == 1
                    && BitConverter.DoubleToInt64Bits(sample.Normal.Z) == long.MinValue, "Every ordered normal/position must match the flat plane.");
            }
            CheckBackend(normals.ComputeDiagnostics, requested, false, policyReason);
            cases.Add(new { Id = "P04", Expected = "Nine ordered (negative-zero,1,negative-zero) normals, declared positions/counts; public CPU", Actual = normals });

            using Mat ramp = new Mat(3, 3, MatType.CV_8UC1);
            for (int row = 0; row < 3; row++) for (int column = 0; column < 3; column++) ramp.Set(row, column, (byte)column);
            VisionPipelineStep step = new VisionPipelineStep { ToolType = "edgeDetection" };
            step.Parameters[nameof(EdgeDetectionToolProperty.EdgeType)] = "Scharr";
            step.Parameters[nameof(EdgeDetectionToolProperty.ScharrDegreeX)] = "1";
            step.Parameters[nameof(EdgeDetectionToolProperty.ScharrDegreeY)] = "0";
            using EdgeDetectionTool edge = (EdgeDetectionTool)VisionPipelineToolFactory.Create(step, null, session);
            using var edgeResult = edge.Execute(ramp);
            byte[] expectedPixels = { 0, 32, 0, 0, 32, 0, 0, 32, 0 };
            byte[] pixels = new byte[9];
            Require(edgeResult.Success && edgeResult.ResultImage.Rows == 3 && edgeResult.ResultImage.Cols == 3
                && edgeResult.ResultImage.Type() == MatType.CV_8UC1, "Scharr must publish the declared image.");
            for (int row = 0; row < 3; row++) Marshal.Copy(edgeResult.ResultImage.Ptr(row), pixels, row * 3, 3);
            Require(pixels.SequenceEqual(expectedPixels), "Every Reflect101 ramp pixel must match the independent oracle.");
            ComputeExecutionEvidence edgeEvidence = edgeResult.ComputeDiagnostics;
            CheckBackend(edgeEvidence, requested, false, policyReason);
            Mat retained = edgeResult.ResultImage;
            edge.Dispose();
            Require(!retained.IsDisposed && !ramp.IsDisposed && ramp.At<byte>(1, 1) == 1, "Tool disposal must retain result/caller ownership.");
            edgeResult.Dispose();
            Require(retained.IsDisposed && !ramp.IsDisposed, "Result disposal must release its own image only.");
            cases.Add(new { Id = "P05", Expected = "0,32,0 repeated3; public CPU; result/caller image lifetimes", Actual = Convert.ToHexString(pixels), Diagnostics = edgeEvidence });

            if (accelerated)
            {
                double[] signedZero = { 0, 0, 0, 0, -0.0, 0, 0, 0, 0 };
                var cpu = new DeterministicMedianFilterTool().Execute(3, 3, signedZero, medianOptions);
                var fallback = median.Execute(3, 3, signedZero, medianOptions);
                Require(fallback.Success && fallback.Values.Select(BitConverter.DoubleToInt64Bits).SequenceEqual(cpu.Values.Select(BitConverter.DoubleToInt64Bits)), "Numerical fallback must preserve signed-zero CPU bits.");
                CheckBackend(fallback.ComputeDiagnostics, true, false, CudaFallbackReason.NumericalRangeUnsupported);
                cases.Add(new { Id = "P06", Expected = "Negative zero -> complete CPU result with preserved bits", Actual = fallback.ComputeDiagnostics });
            }
            var recovered = median.Execute(3, 3, input, medianOptions);
            Require(recovered.Success && recovered.Values.Count == 9 && recovered.Values.All(v => v == 1), "Borrower disposal/fallback must permit complete serial recovery.");
            CheckBackend(recovered.ComputeDiagnostics, requested, accelerated, filterReason);
            cases.Add(new { Id = "P07", Expected = "Same session usable after borrower disposal and CPU fallback", Actual = recovered.ComputeDiagnostics });
            session.Dispose();
            bool disposedRejected = false;
            try { median.Execute(3, 3, input, medianOptions); }
            catch (ObjectDisposedException) { disposedRejected = true; }
            Require(disposedRejected, "A borrowed tool must reject execution after host session disposal.");
            sessionDisposed = true;
            cases.Add(new { Id = "P08", Expected = "Host session Dispose -> later borrowed call rejected", Actual = "Passed" });
        }
        catch (Exception exception) { failure = exception; }

        // Force relevant public dependency types, then report the assemblies actually loaded by this process.
        _ = typeof(CombinedInspectionRunner).Assembly;
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name.StartsWith("OpenVisionLab.") || a.GetName().Name == "OpenCvSharp").OrderBy(a => a.GetName().Name).ToArray();
        var report = new
        {
            Status = failure == null ? "Passed" : "Failed", Mode = mode, Runtime = RuntimeInformation.FrameworkDescription,
            ProcessId = Environment.ProcessId, PackageOnly = true, SessionDisposed = sessionDisposed, Cases = cases,
            Failure = failure?.ToString(),
            LoadedAssemblies = assemblies.Select(a => new { Name = a.GetName().Name, Path = a.Location, Mvid = a.ManifestModule.ModuleVersionId, Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(a.Location))) }).ToArray(),
            Module = modulePath == null || !File.Exists(modulePath) ? null : new { Path = modulePath, Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(modulePath))) },
            NativeModules = ProcessNativeModules()
        };
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals }));
        Console.WriteLine($"{report.Status} | public package CUDA mode={mode} | cases={cases.Count} | {reportPath}");
        if (failure != null) Console.Error.WriteLine(failure);
        return failure == null ? 0 : 1;
    }

    private static object[] ProcessNativeModules()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        return process.Modules.Cast<System.Diagnostics.ProcessModule>().Where(m => m.ModuleName == "OpenCvSharpExtern.dll" || m.ModuleName == "nvcuda.dll" || m.ModuleName == "OpenVisionLab.Cuda.Native.dll")
            .Select(m => (object)new { Name = m.ModuleName, Path = m.FileName, Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(m.FileName))) }).ToArray();
    }

    private static void CheckBackend(ComputeExecutionEvidence evidence, bool requested, bool applied, CudaFallbackReason reason)
    {
        Require(evidence != null && evidence.RequestedCuda == requested && evidence.AccelerationApplied == applied
            && evidence.FallbackReason == reason && (applied ? evidence.GpuBatchCount > 0 : evidence.GpuBatchCount == 0), "Requested/applied/fallback diagnostics must match the selected public path.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
