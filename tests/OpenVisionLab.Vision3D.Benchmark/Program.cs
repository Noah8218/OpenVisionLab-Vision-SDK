using OpenVisionLab.Vision3D.FeatureExtraction;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenVisionLab.Vision3D.Benchmark
{
    internal static class Program
    {
        // ponytail: fixed inputs and the standard library are sufficient for this opt-in baseline.
        private const int CellCountPerAxis = 64;
        private const double CellSize = 3.0;
        private const double LowerTolerance = -0.5;
        private const double UpperTolerance = 0.5;
        private const int WarmupCount = 3;
        private const int MeasurementCount = 30;
        private const int OperationsPerMeasurement = 10;
        private const double TimingNoiseThresholdPercent = 5.0;
        private const double CrossTargetParityTolerance = 1e-12;
        private const double PerTargetOracleTolerance =
            CrossTargetParityTolerance / 2.0;

        private static readonly double[] DirectOffsets =
        {
            -0.75,
            -0.25,
            0.0,
            0.25,
            0.75
        };

        private static readonly double[] BoundaryOffsets =
        {
            -0.75,
            0.0,
            0.75
        };

        private static int Main(string[] args)
        {
            try
            {
                CommandOptions command = CommandOptions.Parse(args);
                if (command.Mode == "performance")
                {
                    ValidatePerformanceEnvironment();
                    ConfigurePerformanceProcess();
                }

                MeshTriangle[] mesh = CreateMesh();
                Workload workload = command.WorkloadId == "planar-direct-v1"
                    ? CreateDirectWorkload(mesh)
                    : CreateBoundaryWorkload(mesh);
                BenchmarkReport report = CreateReport(command, workload);

                if (command.Mode == "accuracy")
                {
                    report.Accuracy = RunAccuracy(workload);
                    Console.WriteLine(
                        "PASS | accuracy | "
                        + workload.Id
                        + " | fingerprint="
                        + report.Accuracy.ResultQuantizedFingerprint);
                }
                else
                {
                    report.Performance = RunPerformance(workload);
                }

                string outputPath = Path.GetFullPath(command.OutputPath);
                string outputDirectory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                File.WriteAllText(
                    outputPath,
                    JsonSerializer.Serialize(
                        report,
                        new JsonSerializerOptions
                        {
                            WriteIndented = true
                        }));
                Console.WriteLine("WROTE | " + outputPath);
                if (report.Performance != null)
                {
                    string sessionStatus =
                        report.Performance.SingleSessionNoisePassed
                            ? "PASS"
                            : "INVALID";
                    Console.WriteLine(
                        sessionStatus
                        + " | performance-session | "
                        + workload.Id
                        + " | median="
                        + report.Performance.WallMillisecondsPerOperation.Median.ToString(
                            "0.###",
                            CultureInfo.InvariantCulture)
                        + "ms | p95="
                        + report.Performance.WallMillisecondsPerOperation.P95.ToString(
                            "0.###",
                            CultureInfo.InvariantCulture)
                        + "ms | wall-noise="
                        + report.Performance.WallMillisecondsPerOperation.RelativeMadPercent.ToString(
                            "0.###",
                            CultureInfo.InvariantCulture)
                        + "% | index-noise="
                        + report.Performance.IndexMillisecondsPerOperation.RelativeMadPercent.ToString(
                            "0.###",
                            CultureInfo.InvariantCulture)
                        + "% | calculation-noise="
                        + report.Performance.CalculationMillisecondsPerOperation.RelativeMadPercent.ToString(
                            "0.###",
                            CultureInfo.InvariantCulture)
                        + "%");
                    return report.Performance.SingleSessionNoisePassed ? 0 : 2;
                }

                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("FAIL | " + exception.Message);
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        private static BenchmarkReport CreateReport(
            CommandOptions command,
            Workload workload)
        {
            Assembly harnessAssembly = Assembly.GetExecutingAssembly();
            Assembly targetAssembly =
                typeof(NominalActualMeshComparisonTool).Assembly;
            string harnessConfiguration =
                GetAssemblyConfiguration(harnessAssembly);
            string targetConfiguration =
                GetAssemblyConfiguration(targetAssembly);
            Require(harnessConfiguration == "Release",
                "The benchmark harness assembly must be a Release build.");
            Require(targetConfiguration == "Release",
                "The loaded target assembly must be a Release build.");
            string targetInformationalVersion =
                GetInformationalVersion(targetAssembly);
            string targetSourceRevision =
                GetSourceRevision(targetInformationalVersion);
            Require(
                string.Equals(
                    targetSourceRevision,
                    command.TargetCommit,
                    StringComparison.OrdinalIgnoreCase),
                "--target-commit does not match the loaded target assembly. "
                + "Expected="
                + targetSourceRevision
                + ", Actual="
                + command.TargetCommit
                + ".");
            return new BenchmarkReport
            {
                SchemaVersion = "openvisionlab-synthetic-mesh-benchmark-v1",
                CreatedUtc = DateTimeOffset.UtcNow,
                Mode = command.Mode,
                Session = command.Session,
                TargetCommit = command.TargetCommit,
                Workload = new WorkloadManifest
                {
                    Id = workload.Id,
                    InputSha256 = HashInput(workload),
                    CellCountPerAxis = CellCountPerAxis,
                    CellSize = CellSize,
                    TriangleCount = workload.Mesh.Length,
                    QueryCount = workload.Queries.Length,
                    LowerTolerance = LowerTolerance,
                    UpperTolerance = UpperTolerance,
                    MaximumDisplaySamples = command.Mode == "accuracy"
                        ? workload.Queries.Length
                        : 0,
                    ProgressObserverEnabled = false,
                    WarmupCount = command.Mode == "performance"
                        ? WarmupCount
                        : 0,
                    MeasurementCount = command.Mode == "performance"
                        ? MeasurementCount
                        : 0,
                    OperationsPerMeasurement = command.Mode == "performance"
                        ? OperationsPerMeasurement
                        : 0
                },
                Environment = new EnvironmentManifest
                {
                    FrameworkDescription = RuntimeInformation.FrameworkDescription,
                    RuntimeVersion = Environment.Version.ToString(),
                    OsDescription = RuntimeInformation.OSDescription,
                    OsArchitecture = RuntimeInformation.OSArchitecture.ToString(),
                    ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                    ProcessorCount = Environment.ProcessorCount,
                    IsServerGc = GCSettings.IsServerGC,
                    GcLatencyMode = GCSettings.LatencyMode.ToString(),
                    DebuggerAttached = Debugger.IsAttached,
                    StopwatchFrequency = Stopwatch.Frequency,
                    ProcessPriorityClass =
                        Process.GetCurrentProcess().PriorityClass.ToString(),
                    ProcessorAffinityMask = OperatingSystem.IsWindows()
                        ? "0x"
                            + Process.GetCurrentProcess()
                                .ProcessorAffinity.ToInt64()
                                .ToString("x", CultureInfo.InvariantCulture)
                        : "unsupported",
                    HarnessAssemblyVersion = harnessAssembly.GetName().Version?.ToString(),
                    HarnessAssemblyConfiguration = harnessConfiguration,
                    HarnessAssemblyInformationalVersion =
                        GetInformationalVersion(harnessAssembly),
                    HarnessAssemblySha256 = HashFile(harnessAssembly.Location),
                    TargetAssemblyVersion = targetAssembly.GetName().Version?.ToString(),
                    TargetAssemblyConfiguration = targetConfiguration,
                    TargetAssemblyInformationalVersion =
                        targetInformationalVersion,
                    TargetAssemblySourceRevision = targetSourceRevision,
                    TargetAssemblySha256 = HashFile(targetAssembly.Location)
                }
            };
        }

        private static void ConfigurePerformanceProcess()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new PlatformNotSupportedException(
                    "The fixed performance protocol currently requires Windows process controls.");
            }

            Process process = Process.GetCurrentProcess();
            process.PriorityClass = ProcessPriorityClass.High;
        }

        private static void ValidatePerformanceEnvironment()
        {
            Require(!Debugger.IsAttached,
                "Performance sessions cannot run with a debugger attached.");
            Require(RuntimeInformation.ProcessArchitecture == Architecture.X64,
                "Performance sessions require an x64 process.");
#if DEBUG
            throw new InvalidOperationException(
                "Performance sessions require a Release harness build.");
#endif
        }

        private static AccuracyReport RunAccuracy(Workload workload)
        {
            PointOracleEvidence pointEvidence =
                VerifyPointOracle(workload);
            NominalActualMeshComparisonResult first = Execute(
                workload,
                workload.Queries.Length);
            ValidateAggregate(first, workload);
            ValidateDisplaySamples(first, workload);
            double firstMaximumAbsoluteOracleError = Math.Max(
                pointEvidence.MaximumAbsoluteOracleError,
                Math.Max(
                    MaximumAggregateOracleError(first, workload),
                    MaximumDisplayOracleError(first, workload)));
            NominalActualMeshComparisonResult second = Execute(
                workload,
                workload.Queries.Length);
            ValidateAggregate(second, workload);
            ValidateDisplaySamples(second, workload);
            double secondMaximumAbsoluteOracleError = Math.Max(
                pointEvidence.MaximumAbsoluteOracleError,
                Math.Max(
                    MaximumAggregateOracleError(second, workload),
                    MaximumDisplayOracleError(second, workload)));

            Fingerprints firstFingerprints = HashDetailedResult(first);
            Fingerprints secondFingerprints = HashDetailedResult(second);
            Require(
                firstFingerprints.Exact == secondFingerprints.Exact,
                "Repeated detailed results changed their exact semantic fingerprint.");
            Require(
                firstFingerprints.Quantized == secondFingerprints.Quantized,
                "Repeated detailed results changed their quantized semantic fingerprint.");
            double maximumAbsoluteOracleError = Math.Max(
                firstMaximumAbsoluteOracleError,
                secondMaximumAbsoluteOracleError);
            Require(
                maximumAbsoluteOracleError <= PerTargetOracleTolerance,
                "The recorded oracle error exceeded the per-target parity budget.");

            return new AccuracyReport
            {
                OraclePassed = true,
                ProcessedPointCount = first.ProcessedPointCount,
                SourceTriangleIndexSum = pointEvidence.SourceTriangleIndexSum,
                PerTargetOracleTolerance = PerTargetOracleTolerance,
                CrossTargetParityTolerance = CrossTargetParityTolerance,
                MaximumAbsoluteOracleError = maximumAbsoluteOracleError,
                PointExactFingerprint = pointEvidence.ExactFingerprint,
                PointQuantizedFingerprint = pointEvidence.QuantizedFingerprint,
                ResultExactFingerprint = firstFingerprints.Exact,
                ResultQuantizedFingerprint = firstFingerprints.Quantized,
                RepeatedResultExactFingerprintMatch = true
            };
        }

        private static PerformanceReport RunPerformance(Workload workload)
        {
            IterationResult cold = Measure(workload, 0, 1);
            string expectedExactFingerprint = cold.ExactFingerprint;
            string expectedQuantizedFingerprint = cold.QuantizedFingerprint;

            List<IterationSample> warmups =
                new List<IterationSample>(WarmupCount);
            for (int index = 0; index < WarmupCount; index++)
            {
                IterationResult warmup = Measure(
                    workload,
                    -(index + 1),
                    OperationsPerMeasurement);
                Require(
                    warmup.ExactFingerprint == expectedExactFingerprint,
                    "A warm-up result changed its exact semantic fingerprint.");
                Require(
                    warmup.QuantizedFingerprint
                        == expectedQuantizedFingerprint,
                    "A warm-up result changed its quantized semantic fingerprint.");
                warmups.Add(warmup.Sample);
            }

            List<IterationSample> samples =
                new List<IterationSample>(MeasurementCount);
            for (int index = 0; index < MeasurementCount; index++)
            {
                IterationResult measured = Measure(
                    workload,
                    index + 1,
                    OperationsPerMeasurement);
                Require(
                    measured.ExactFingerprint == expectedExactFingerprint,
                    "A measured result changed its exact semantic fingerprint.");
                Require(
                    measured.QuantizedFingerprint
                        == expectedQuantizedFingerprint,
                    "A measured result changed its quantized semantic fingerprint.");
                samples.Add(measured.Sample);
            }

            MetricSummary wallMillisecondsPerOperation = Summarize(
                samples.Select(sample => sample.WallMillisecondsPerOperation),
                "wall milliseconds per operation");
            MetricSummary indexMillisecondsPerOperation = Summarize(
                samples.Select(sample => sample.IndexMillisecondsPerOperation),
                "index milliseconds per operation");
            MetricSummary calculationMillisecondsPerOperation = Summarize(
                samples.Select(
                    sample => sample.CalculationMillisecondsPerOperation),
                "calculation milliseconds per operation");
            return new PerformanceReport
            {
                Cold = cold.Sample,
                Warmups = warmups,
                AggregateExactFingerprint = expectedExactFingerprint,
                AggregateQuantizedFingerprint = expectedQuantizedFingerprint,
                Samples = samples,
                TimingNoiseThresholdPercent = TimingNoiseThresholdPercent,
                SingleSessionNoisePassed =
                    wallMillisecondsPerOperation.RelativeMadPercent
                        < TimingNoiseThresholdPercent
                    && indexMillisecondsPerOperation.RelativeMadPercent
                        < TimingNoiseThresholdPercent
                    && calculationMillisecondsPerOperation.RelativeMadPercent
                        < TimingNoiseThresholdPercent,
                WallMillisecondsPerOperation =
                    wallMillisecondsPerOperation,
                IndexMillisecondsPerOperation =
                    indexMillisecondsPerOperation,
                CalculationMillisecondsPerOperation =
                    calculationMillisecondsPerOperation,
                EndToEndPointsPerSecond = Summarize(
                    samples.Select(sample => sample.EndToEndPointsPerSecond),
                    "end-to-end points per second"),
                CalculationPointsPerSecond = Summarize(
                    samples.Select(sample => sample.CalculationPointsPerSecond),
                    "calculation points per second"),
                AllocatedBytesPerOperation = Summarize(
                    samples.Select(sample => sample.AllocatedBytesPerOperation),
                    "allocated bytes per operation")
            };
        }

        private static IterationResult Measure(
            Workload workload,
            int ordinal,
            int operations)
        {
            NominalActualMeshComparisonResult[] results =
                new NominalActualMeshComparisonResult[operations];
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            int generation0Before = GC.CollectionCount(0);
            int generation1Before = GC.CollectionCount(1);
            int generation2Before = GC.CollectionCount(2);
            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int index = 0; index < operations; index++)
            {
                results[index] = Execute(workload, 0);
            }

            stopwatch.Stop();
            long allocatedBytesTotal =
                GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            int generation0CollectionsTotal =
                GC.CollectionCount(0) - generation0Before;
            int generation1CollectionsTotal =
                GC.CollectionCount(1) - generation1Before;
            int generation2CollectionsTotal =
                GC.CollectionCount(2) - generation2Before;
            long indexTicks = 0;
            long calculationTicks = 0;
            Fingerprints fingerprints = null;
            for (int index = 0; index < operations; index++)
            {
                NominalActualMeshComparisonResult result = results[index];
                ValidateAggregate(result, workload);
                Fingerprints current = HashAggregateResult(result);
                if (fingerprints == null)
                {
                    fingerprints = current;
                }
                else
                {
                    Require(current.Exact == fingerprints.Exact,
                        "A batched operation changed its exact semantic fingerprint.");
                    Require(current.Quantized == fingerprints.Quantized,
                        "A batched operation changed its quantized semantic fingerprint.");
                }

                indexTicks += result.IndexDuration.Ticks;
                calculationTicks += result.CalculationDuration.Ticks;
            }

            double calculationSeconds =
                calculationTicks / (double)TimeSpan.TicksPerSecond;
            Require(calculationSeconds > 0.0,
                "The fixed workload completed without a measurable calculation duration.");
            double wallSeconds =
                stopwatch.ElapsedTicks / (double)Stopwatch.Frequency;
            double internalSeconds =
                (indexTicks + calculationTicks)
                / (double)TimeSpan.TicksPerSecond;
            double internalRoundingAllowanceSeconds =
                operations * 2.0 / TimeSpan.TicksPerSecond;
            Require(
                internalSeconds
                    <= wallSeconds + internalRoundingAllowanceSeconds,
                "Index plus calculation duration exceeded outer wall time.");

            return new IterationResult(
                new IterationSample
                {
                    Ordinal = ordinal,
                    Operations = operations,
                    WallStopwatchTicksTotal = stopwatch.ElapsedTicks,
                    WallMillisecondsPerOperation =
                        stopwatch.Elapsed.TotalMilliseconds / operations,
                    IndexTimeSpanTicksTotal = indexTicks,
                    IndexMillisecondsPerOperation =
                        indexTicks
                        * 1000.0
                        / TimeSpan.TicksPerSecond
                        / operations,
                    CalculationTimeSpanTicksTotal = calculationTicks,
                    CalculationMillisecondsPerOperation =
                        calculationTicks
                        * 1000.0
                        / TimeSpan.TicksPerSecond
                        / operations,
                    EndToEndPointsPerSecond =
                        workload.Queries.Length
                        * (double)operations
                        / wallSeconds,
                    CalculationPointsPerSecond =
                        workload.Queries.Length
                        * (double)operations
                        / calculationSeconds,
                    AllocatedBytesTotal = allocatedBytesTotal,
                    AllocatedBytesPerOperation =
                        allocatedBytesTotal / (double)operations,
                    Generation0CollectionsTotal =
                        generation0CollectionsTotal,
                    Generation1CollectionsTotal =
                        generation1CollectionsTotal,
                    Generation2CollectionsTotal =
                        generation2CollectionsTotal
                },
                fingerprints.Exact,
                fingerprints.Quantized);
        }

        private static NominalActualMeshComparisonResult Execute(
            Workload workload,
            int maximumDisplaySamples)
        {
            return new NominalActualMeshComparisonTool().Execute(
                workload.Mesh,
                workload.Points,
                new NominalActualMeshComparisonOptions(
                    workload.Points.Length,
                    LowerTolerance,
                    UpperTolerance,
                    maximumDisplaySamples));
        }

        private static PointOracleEvidence VerifyPointOracle(
            Workload workload)
        {
            TriangleMeshDistanceTool tool =
                new TriangleMeshDistanceTool(workload.Mesh);
            long sourceTriangleIndexSum = 0;
            double maximumAbsoluteOracleError = 0.0;
            using (IncrementalHash exact =
                IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            using (IncrementalHash quantized =
                IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                for (int index = 0; index < workload.Queries.Length; index++)
                {
                    ExpectedQuery expected = workload.Queries[index];
                    PointMeshDistance closest = tool.Execute(expected.Point);
                    RequireDistance(
                        closest,
                        expected,
                        expected.DirectSignResolved,
                        expected.DirectSignResolved
                            ? (double?)expected.SignedDistance
                            : null,
                        "initial point " + index);
                    maximumAbsoluteOracleError = Math.Max(
                        maximumAbsoluteOracleError,
                        MaximumDistanceOracleError(
                            closest,
                            expected,
                            expected.DirectSignResolved
                                ? (double?)expected.SignedDistance
                                : null));

                    PointMeshDistance resolved = closest;
                    if (!expected.DirectSignResolved)
                    {
                        resolved = tool.ExecuteRobustSign(
                            expected.Point,
                            closest.UnsignedDistance);
                        RequireDistance(
                            resolved,
                            expected,
                            true,
                            expected.SignedDistance,
                            "robust point " + index);
                        maximumAbsoluteOracleError = Math.Max(
                            maximumAbsoluteOracleError,
                            MaximumDistanceOracleError(
                                resolved,
                                expected,
                                expected.SignedDistance));
                    }

                    sourceTriangleIndexSum +=
                        resolved.SourceTriangleIndex;
                    AppendPointFingerprint(
                        exact,
                        index,
                        closest,
                        resolved,
                        false);
                    AppendPointFingerprint(
                        quantized,
                        index,
                        closest,
                        resolved,
                        true);
                }

                Require(
                    sourceTriangleIndexSum
                        == workload.ExpectedSourceTriangleIndexSum,
                    "The analytical source-triangle index sum changed.");
                return new PointOracleEvidence(
                    sourceTriangleIndexSum,
                    maximumAbsoluteOracleError,
                    ToHex(exact.GetHashAndReset()),
                    ToHex(quantized.GetHashAndReset()));
            }
        }

        private static double MaximumAggregateOracleError(
            NominalActualMeshComparisonResult result,
            Workload workload)
        {
            return Math.Max(
                MaximumStatisticsOracleError(
                    result.UnsignedStatistics,
                    workload.UnsignedStatistics),
                MaximumStatisticsOracleError(
                    result.SignedStatistics,
                    workload.SignedStatistics));
        }

        private static double MaximumStatisticsOracleError(
            MeshDeviationStatistics actual,
            ExpectedStatistics expected)
        {
            return new[]
            {
                Math.Abs(actual.Minimum - expected.Minimum),
                Math.Abs(actual.Maximum - expected.Maximum),
                Math.Abs(actual.Mean - expected.Mean),
                Math.Abs(
                    actual.StandardDeviationPopulation
                    - expected.StandardDeviationPopulation),
                Math.Abs(actual.RootMeanSquare - expected.RootMeanSquare)
            }.Max();
        }

        private static double MaximumDisplayOracleError(
            NominalActualMeshComparisonResult result,
            Workload workload)
        {
            double maximum = 0.0;
            for (int index = 0; index < workload.Queries.Length; index++)
            {
                ExpectedQuery expected = workload.Queries[index];
                NominalActualMeshDeviationSample actual =
                    result.DisplaySamples[index];
                maximum = Math.Max(
                    maximum,
                    MaximumPointOracleError(actual.Point, expected.Point));
                maximum = Math.Max(
                    maximum,
                    MaximumPointOracleError(
                        actual.ClosestPoint,
                        expected.ClosestPoint));
                maximum = Math.Max(
                    maximum,
                    Math.Abs(
                        actual.UnsignedDistance
                        - Math.Abs(expected.SignedDistance)));
                maximum = Math.Max(
                    maximum,
                    Math.Abs(actual.SignedDistance - expected.SignedDistance));
            }

            return maximum;
        }

        private static double MaximumDistanceOracleError(
            PointMeshDistance actual,
            ExpectedQuery expected,
            double? signedDistance)
        {
            double maximum = Math.Max(
                MaximumPointOracleError(
                    actual.ClosestPoint,
                    expected.ClosestPoint),
                MaximumPointOracleError(
                    actual.TriangleNormal,
                    Point(0.0, 0.0, 1.0)));
            maximum = Math.Max(
                maximum,
                Math.Abs(
                    actual.UnsignedDistance
                    - Math.Abs(expected.SignedDistance)));
            if (signedDistance.HasValue)
            {
                maximum = Math.Max(
                    maximum,
                    Math.Abs(actual.SignedDistance.Value - signedDistance.Value));
            }

            return maximum;
        }

        private static double MaximumPointOracleError(
            ThreeDPoint actual,
            ThreeDPoint expected)
        {
            return Math.Max(
                Math.Abs(actual.X - expected.X),
                Math.Max(
                    Math.Abs(actual.Y - expected.Y),
                    Math.Abs(actual.Z - expected.Z)));
        }

        private static void ValidateAggregate(
            NominalActualMeshComparisonResult result,
            Workload workload)
        {
            Require(result.Success,
                "The fixed workload failed: " + result.Message);
            Require(result.ProcessedPointCount == workload.Queries.Length
                    && result.BelowToleranceCount == workload.BelowCount
                    && result.WithinToleranceCount == workload.WithinCount
                    && result.AboveToleranceCount == workload.AboveCount
                    && result.DirectSignResolvedCount == workload.DirectCount
                    && result.RobustSignRecoveredCount == workload.RobustCount,
                "The fixed workload changed its analytical aggregate counts.");
            Require(
                result.BelowToleranceCount
                    + result.WithinToleranceCount
                    + result.AboveToleranceCount
                    == result.ProcessedPointCount,
                "Tolerance counts no longer cover every processed point.");
            Require(
                result.DirectSignResolvedCount
                    + result.RobustSignRecoveredCount
                    == result.ProcessedPointCount,
                "Sign-resolution counts no longer cover every processed point.");
            RequireStatistics(
                result.UnsignedStatistics,
                workload.UnsignedStatistics,
                "unsigned");
            RequireStatistics(
                result.SignedStatistics,
                workload.SignedStatistics,
                "signed");
        }

        private static void ValidateDisplaySamples(
            NominalActualMeshComparisonResult result,
            Workload workload)
        {
            Require(result.DisplayStride == 1
                    && result.DisplaySamples.Count == workload.Queries.Length,
                "The accuracy run did not retain every display sample.");
            for (int index = 0; index < workload.Queries.Length; index++)
            {
                ExpectedQuery expected = workload.Queries[index];
                NominalActualMeshDeviationSample actual =
                    result.DisplaySamples[index];
                Require(actual.PointIndex == index
                        && actual.SourceTriangleIndex
                            == expected.SourceTriangleIndex
                        && actual.RobustSignRecovered
                            == !expected.DirectSignResolved,
                    "Detailed comparison identity changed at point "
                    + index
                    + ".");
                RequirePoint(actual.Point, expected.Point,
                    "query point " + index);
                RequirePoint(actual.ClosestPoint, expected.ClosestPoint,
                    "closest point " + index);
                RequireApproximately(
                    actual.UnsignedDistance,
                    Math.Abs(expected.SignedDistance),
                    "unsigned display distance " + index);
                RequireApproximately(
                    actual.SignedDistance,
                    expected.SignedDistance,
                    "signed display distance " + index);
            }
        }

        private static void RequireDistance(
            PointMeshDistance actual,
            ExpectedQuery expected,
            bool signResolved,
            double? signedDistance,
            string label)
        {
            Require(actual.SourceTriangleIndex
                    == expected.SourceTriangleIndex
                    && actual.ClosestFeature == expected.Feature
                    && actual.SignResolved == signResolved,
                "Unexpected source, feature, or sign state for "
                + label
                + ".");
            RequirePoint(actual.ClosestPoint, expected.ClosestPoint,
                label + " closest point");
            RequirePoint(
                actual.TriangleNormal,
                Point(0.0, 0.0, 1.0),
                label + " normal");
            RequireApproximately(
                actual.UnsignedDistance,
                Math.Abs(expected.SignedDistance),
                label + " unsigned distance");
            Require(actual.SignedDistance.HasValue
                    == signedDistance.HasValue,
                "Unexpected signed-distance availability for "
                + label
                + ".");
            if (signedDistance.HasValue)
            {
                RequireApproximately(
                    actual.SignedDistance.Value,
                    signedDistance.Value,
                    label + " signed distance");
            }
        }

        private static void RequireStatistics(
            MeshDeviationStatistics actual,
            ExpectedStatistics expected,
            string label)
        {
            Require(actual != null && actual.Count == expected.Count,
                "Unexpected " + label + " statistics count.");
            RequireApproximately(actual.Minimum, expected.Minimum,
                label + " minimum");
            RequireApproximately(actual.Maximum, expected.Maximum,
                label + " maximum");
            RequireApproximately(actual.Mean, expected.Mean,
                label + " mean");
            RequireApproximately(
                actual.StandardDeviationPopulation,
                expected.StandardDeviationPopulation,
                label + " population standard deviation");
            RequireApproximately(actual.RootMeanSquare, expected.RootMeanSquare,
                label + " root mean square");
        }

        private static MetricSummary Summarize(
            IEnumerable<double> values,
            string label)
        {
            double[] sorted = values.OrderBy(value => value).ToArray();
            Require(sorted.Length == MeasurementCount,
                "A metric summary requires exactly 30 measured values.");
            Require(
                sorted.All(value => double.IsFinite(value) && value > 0.0),
                "A " + label + " sample was not finite and positive.");
            double median = Median(sorted);
            double[] deviations = sorted
                .Select(value => Math.Abs(value - median))
                .OrderBy(value => value)
                .ToArray();
            double medianAbsoluteDeviation = Median(deviations);
            return new MetricSummary
            {
                Minimum = sorted[0],
                Median = median,
                P95 = sorted[(int)Math.Ceiling(0.95 * sorted.Length) - 1],
                Maximum = sorted[sorted.Length - 1],
                MedianAbsoluteDeviation = medianAbsoluteDeviation,
                RelativeMadPercent =
                    100.0 * medianAbsoluteDeviation / median
            };
        }

        private static double Median(double[] sorted)
        {
            int middle = sorted.Length / 2;
            return (sorted[middle - 1] + sorted[middle]) / 2.0;
        }

        private static MeshTriangle[] CreateMesh()
        {
            MeshTriangle[] mesh = new MeshTriangle[
                CellCountPerAxis * CellCountPerAxis * 2];
            for (int y = 0; y < CellCountPerAxis; y++)
            {
                for (int x = 0; x < CellCountPerAxis; x++)
                {
                    int cellIndex = y * CellCountPerAxis + x;
                    long sourceIndex = cellIndex * 2L;
                    double x0 = x * CellSize;
                    double y0 = y * CellSize;
                    ThreeDPoint p00 = Point(x0, y0, 0.0);
                    ThreeDPoint p10 = Point(x0 + CellSize, y0, 0.0);
                    ThreeDPoint p11 = Point(
                        x0 + CellSize,
                        y0 + CellSize,
                        0.0);
                    ThreeDPoint p01 = Point(x0, y0 + CellSize, 0.0);
                    mesh[cellIndex * 2] = new MeshTriangle(
                        sourceIndex,
                        p00,
                        p10,
                        p11);
                    mesh[cellIndex * 2 + 1] = new MeshTriangle(
                        sourceIndex + 1,
                        p00,
                        p11,
                        p01);
                }
            }

            return mesh;
        }

        private static Workload CreateDirectWorkload(MeshTriangle[] mesh)
        {
            List<ExpectedQuery> queries =
                new List<ExpectedQuery>(mesh.Length * DirectOffsets.Length);
            for (int y = 0; y < CellCountPerAxis; y++)
            {
                for (int x = 0; x < CellCountPerAxis; x++)
                {
                    int cellIndex = y * CellCountPerAxis + x;
                    double x0 = x * CellSize;
                    double y0 = y * CellSize;
                    AddQueries(
                        queries,
                        cellIndex * 2L,
                        Point(x0 + 2.0, y0 + 1.0, 0.0),
                        MeshClosestFeature.FaceInterior,
                        true,
                        DirectOffsets);
                    AddQueries(
                        queries,
                        cellIndex * 2L + 1,
                        Point(x0 + 1.0, y0 + 2.0, 0.0),
                        MeshClosestFeature.FaceInterior,
                        true,
                        DirectOffsets);
                }
            }

            return new Workload(
                "planar-direct-v1",
                mesh,
                queries.ToArray(),
                8192,
                24576,
                8192,
                40960,
                0,
                167751680,
                new ExpectedStatistics(40960, 0.0, 0.75, 0.4, 0.3, 0.5),
                new ExpectedStatistics(40960, -0.75, 0.75, 0.0, 0.5, 0.5));
        }

        private static Workload CreateBoundaryWorkload(MeshTriangle[] mesh)
        {
            List<ExpectedQuery> queries =
                new List<ExpectedQuery>(
                    CellCountPerAxis
                    * CellCountPerAxis
                    * BoundaryOffsets.Length);
            for (int y = 0; y < CellCountPerAxis; y++)
            {
                for (int x = 0; x < CellCountPerAxis; x++)
                {
                    int cellIndex = y * CellCountPerAxis + x;
                    double x0 = x * CellSize;
                    double y0 = y * CellSize;
                    AddQueries(
                        queries,
                        cellIndex * 2L,
                        Point(x0 + 1.5, y0 + 1.5, 0.0),
                        MeshClosestFeature.Edge,
                        false,
                        BoundaryOffsets);
                }
            }

            double signedSpread = Math.Sqrt(3.0 / 8.0);
            return new Workload(
                "planar-boundary-v1",
                mesh,
                queries.ToArray(),
                4096,
                4096,
                4096,
                0,
                12288,
                50319360,
                new ExpectedStatistics(
                    12288,
                    0.0,
                    0.75,
                    0.5,
                    Math.Sqrt(1.0 / 8.0),
                    signedSpread),
                new ExpectedStatistics(
                    12288,
                    -0.75,
                    0.75,
                    0.0,
                    signedSpread,
                    signedSpread));
        }

        private static void AddQueries(
            List<ExpectedQuery> queries,
            long sourceTriangleIndex,
            ThreeDPoint closestPoint,
            MeshClosestFeature feature,
            bool directSignResolved,
            IReadOnlyList<double> offsets)
        {
            for (int index = 0; index < offsets.Count; index++)
            {
                double offset = offsets[index];
                queries.Add(
                    new ExpectedQuery(
                        Point(
                            closestPoint.X,
                            closestPoint.Y,
                            offset),
                        sourceTriangleIndex,
                        closestPoint,
                        feature,
                        offset,
                        directSignResolved));
            }
        }

        private static ThreeDPoint Point(double x, double y, double z)
        {
            return new ThreeDPoint(x, y, z);
        }

        private static string HashInput(Workload workload)
        {
            using (IncrementalHash hash =
                IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                Append(hash, workload.Id);
                Append(hash, LowerTolerance);
                Append(hash, UpperTolerance);
                foreach (MeshTriangle triangle in workload.Mesh)
                {
                    Append(hash, triangle.SourceTriangleIndex);
                    Append(hash, triangle.A);
                    Append(hash, triangle.B);
                    Append(hash, triangle.C);
                }

                foreach (ThreeDPoint point in workload.Points)
                {
                    Append(hash, point);
                }

                return ToHex(hash.GetHashAndReset());
            }
        }

        private static Fingerprints HashAggregateResult(
            NominalActualMeshComparisonResult result)
        {
            return HashResult(result, false);
        }

        private static Fingerprints HashDetailedResult(
            NominalActualMeshComparisonResult result)
        {
            return HashResult(result, true);
        }

        private static Fingerprints HashResult(
            NominalActualMeshComparisonResult result,
            bool includeSamples)
        {
            using (IncrementalHash exact =
                IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            using (IncrementalHash quantized =
                IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                AppendResult(exact, result, includeSamples, false);
                AppendResult(quantized, result, includeSamples, true);
                return new Fingerprints(
                    ToHex(exact.GetHashAndReset()),
                    ToHex(quantized.GetHashAndReset()));
            }
        }

        private static void AppendResult(
            IncrementalHash hash,
            NominalActualMeshComparisonResult result,
            bool includeSamples,
            bool quantized)
        {
            Append(hash, result.Success);
            Append(hash, result.ProcessedPointCount);
            AppendStatistics(hash, result.UnsignedStatistics, quantized);
            AppendStatistics(hash, result.SignedStatistics, quantized);
            Append(hash, result.BelowToleranceCount);
            Append(hash, result.WithinToleranceCount);
            Append(hash, result.AboveToleranceCount);
            Append(hash, result.DirectSignResolvedCount);
            Append(hash, result.RobustSignRecoveredCount);
            Append(hash, result.DisplayStride);
            Append(hash, result.DisplaySamples.Count);
            if (!includeSamples)
            {
                return;
            }

            foreach (NominalActualMeshDeviationSample sample
                in result.DisplaySamples)
            {
                Append(hash, sample.PointIndex);
                Append(hash, sample.Point, quantized);
                Append(hash, sample.ClosestPoint, quantized);
                Append(hash, sample.SourceTriangleIndex);
                Append(hash, sample.UnsignedDistance, quantized);
                Append(hash, sample.SignedDistance, quantized);
                Append(hash, sample.RobustSignRecovered);
            }
        }

        private static void AppendStatistics(
            IncrementalHash hash,
            MeshDeviationStatistics statistics,
            bool quantized)
        {
            Append(hash, statistics.Count);
            Append(hash, statistics.Minimum, quantized);
            Append(hash, statistics.Maximum, quantized);
            Append(hash, statistics.Mean, quantized);
            Append(hash, statistics.StandardDeviationPopulation, quantized);
            Append(hash, statistics.RootMeanSquare, quantized);
        }

        private static void AppendPointFingerprint(
            IncrementalHash hash,
            int index,
            PointMeshDistance initial,
            PointMeshDistance resolved,
            bool quantized)
        {
            Append(hash, index);
            Append(hash, initial.SourceTriangleIndex);
            Append(hash, (int)initial.ClosestFeature);
            Append(hash, initial.ClosestPoint, quantized);
            Append(hash, initial.TriangleNormal, quantized);
            Append(hash, initial.UnsignedDistance, quantized);
            Append(hash, initial.SignedDistance.HasValue);
            if (initial.SignedDistance.HasValue)
            {
                Append(hash, initial.SignedDistance.Value, quantized);
            }

            Append(hash, initial.SignResolved);
            Append(hash, resolved.SourceTriangleIndex);
            Append(hash, (int)resolved.ClosestFeature);
            Append(hash, resolved.ClosestPoint, quantized);
            Append(hash, resolved.TriangleNormal, quantized);
            Append(hash, resolved.UnsignedDistance, quantized);
            Append(hash, resolved.SignedDistance.Value, quantized);
            Append(hash, resolved.SignResolved);
        }

        private static void Append(
            IncrementalHash hash,
            ThreeDPoint point,
            bool quantized = false)
        {
            Append(hash, point.X, quantized);
            Append(hash, point.Y, quantized);
            Append(hash, point.Z, quantized);
        }

        private static void Append(
            IncrementalHash hash,
            double value,
            bool quantized = false)
        {
            Append(
                hash,
                quantized
                    ? Math.Round(value, 12, MidpointRounding.ToEven)
                        .ToString("R", CultureInfo.InvariantCulture)
                    : BitConverter.DoubleToInt64Bits(value)
                        .ToString("X16", CultureInfo.InvariantCulture));
        }

        private static void Append(
            IncrementalHash hash,
            object value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(
                Convert.ToString(value, CultureInfo.InvariantCulture)
                + "\n");
            hash.AppendData(bytes);
        }

        private static string HashFile(string path)
        {
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return ToHex(sha256.ComputeHash(stream));
            }
        }

        private static string GetInformationalVersion(Assembly assembly)
        {
            AssemblyInformationalVersionAttribute attribute =
                assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            Require(attribute != null
                    && !string.IsNullOrWhiteSpace(attribute.InformationalVersion),
                "An assembly informational version is required for provenance.");
            return attribute.InformationalVersion;
        }

        private static string GetAssemblyConfiguration(Assembly assembly)
        {
            AssemblyConfigurationAttribute attribute =
                assembly.GetCustomAttribute<AssemblyConfigurationAttribute>();
            Require(attribute != null
                    && !string.IsNullOrWhiteSpace(attribute.Configuration),
                "An assembly configuration is required for provenance.");
            return attribute.Configuration;
        }

        private static string GetSourceRevision(string informationalVersion)
        {
            int separator = informationalVersion.LastIndexOf('+');
            Require(separator >= 0
                    && separator + 41 == informationalVersion.Length,
                "The target informational version does not contain a 40-character source revision.");
            string revision = informationalVersion.Substring(separator + 1);
            Require(
                revision.All(character =>
                    character >= '0' && character <= '9'
                    || character >= 'a' && character <= 'f'
                    || character >= 'A' && character <= 'F'),
                "The target informational version contains an invalid source revision.");
            return revision.ToLowerInvariant();
        }

        private static string ToHex(byte[] bytes)
        {
            StringBuilder builder = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes)
            {
                builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        private static void RequirePoint(
            ThreeDPoint actual,
            ThreeDPoint expected,
            string label)
        {
            RequireApproximately(actual.X, expected.X, label + " X");
            RequireApproximately(actual.Y, expected.Y, label + " Y");
            RequireApproximately(actual.Z, expected.Z, label + " Z");
        }

        private static void RequireApproximately(
            double actual,
            double expected,
            string label)
        {
            Require(
                Math.Abs(actual - expected) <= PerTargetOracleTolerance,
                label
                + " changed. Expected="
                + expected.ToString("R", CultureInfo.InvariantCulture)
                + ", Actual="
                + actual.ToString("R", CultureInfo.InvariantCulture)
                + ".");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private sealed class CommandOptions
        {
            private CommandOptions(
                string mode,
                string workloadId,
                string outputPath,
                string targetCommit,
                string session)
            {
                Mode = mode;
                WorkloadId = workloadId;
                OutputPath = outputPath;
                TargetCommit = targetCommit;
                Session = session;
            }

            internal string Mode { get; }
            internal string WorkloadId { get; }
            internal string OutputPath { get; }
            internal string TargetCommit { get; }
            internal string Session { get; }

            internal static CommandOptions Parse(string[] args)
            {
                Dictionary<string, string> values =
                    new Dictionary<string, string>(StringComparer.Ordinal);
                for (int index = 0; index < args.Length; index += 2)
                {
                    if (index + 1 >= args.Length
                        || !args[index].StartsWith("--", StringComparison.Ordinal)
                        || values.ContainsKey(args[index]))
                    {
                        throw Usage();
                    }

                    values.Add(args[index], args[index + 1]);
                }

                string mode = Required(values, "--mode");
                string workload = Required(values, "--workload");
                string output = Required(values, "--output");
                string targetCommit = Required(values, "--target-commit");
                string session = Required(values, "--session");
                Require(values.Count == 5,
                    "An unknown benchmark argument was supplied.");
                Require(mode == "accuracy" || mode == "performance",
                    "--mode must be accuracy or performance.");
                Require(workload == "planar-direct-v1"
                        || workload == "planar-boundary-v1",
                    "--workload must name one fixed v1 workload.");
                return new CommandOptions(
                    mode,
                    workload,
                    output,
                    targetCommit,
                    session);
            }

            private static string Required(
                IReadOnlyDictionary<string, string> values,
                string name)
            {
                string value;
                if (!values.TryGetValue(name, out value)
                    || string.IsNullOrWhiteSpace(value))
                {
                    throw Usage();
                }

                return value;
            }

            private static ArgumentException Usage()
            {
                return new ArgumentException(
                    "Usage: --mode <accuracy|performance> "
                    + "--workload <planar-direct-v1|planar-boundary-v1> "
                    + "--output <result.json> --target-commit <sha> "
                    + "--session <id>.");
            }
        }

        private sealed class Workload
        {
            internal Workload(
                string id,
                MeshTriangle[] mesh,
                ExpectedQuery[] queries,
                long belowCount,
                long withinCount,
                long aboveCount,
                long directCount,
                long robustCount,
                long expectedSourceTriangleIndexSum,
                ExpectedStatistics unsignedStatistics,
                ExpectedStatistics signedStatistics)
            {
                Id = id;
                Mesh = mesh;
                Queries = queries;
                Points = queries.Select(query => query.Point).ToArray();
                BelowCount = belowCount;
                WithinCount = withinCount;
                AboveCount = aboveCount;
                DirectCount = directCount;
                RobustCount = robustCount;
                ExpectedSourceTriangleIndexSum =
                    expectedSourceTriangleIndexSum;
                UnsignedStatistics = unsignedStatistics;
                SignedStatistics = signedStatistics;
            }

            internal string Id { get; }
            internal MeshTriangle[] Mesh { get; }
            internal ExpectedQuery[] Queries { get; }
            internal ThreeDPoint[] Points { get; }
            internal long BelowCount { get; }
            internal long WithinCount { get; }
            internal long AboveCount { get; }
            internal long DirectCount { get; }
            internal long RobustCount { get; }
            internal long ExpectedSourceTriangleIndexSum { get; }
            internal ExpectedStatistics UnsignedStatistics { get; }
            internal ExpectedStatistics SignedStatistics { get; }
        }

        private sealed class ExpectedQuery
        {
            internal ExpectedQuery(
                ThreeDPoint point,
                long sourceTriangleIndex,
                ThreeDPoint closestPoint,
                MeshClosestFeature feature,
                double signedDistance,
                bool directSignResolved)
            {
                Point = point;
                SourceTriangleIndex = sourceTriangleIndex;
                ClosestPoint = closestPoint;
                Feature = feature;
                SignedDistance = signedDistance;
                DirectSignResolved = directSignResolved;
            }

            internal ThreeDPoint Point { get; }
            internal long SourceTriangleIndex { get; }
            internal ThreeDPoint ClosestPoint { get; }
            internal MeshClosestFeature Feature { get; }
            internal double SignedDistance { get; }
            internal bool DirectSignResolved { get; }
        }

        private sealed class ExpectedStatistics
        {
            internal ExpectedStatistics(
                long count,
                double minimum,
                double maximum,
                double mean,
                double standardDeviationPopulation,
                double rootMeanSquare)
            {
                Count = count;
                Minimum = minimum;
                Maximum = maximum;
                Mean = mean;
                StandardDeviationPopulation = standardDeviationPopulation;
                RootMeanSquare = rootMeanSquare;
            }

            internal long Count { get; }
            internal double Minimum { get; }
            internal double Maximum { get; }
            internal double Mean { get; }
            internal double StandardDeviationPopulation { get; }
            internal double RootMeanSquare { get; }
        }

        private sealed class PointOracleEvidence
        {
            internal PointOracleEvidence(
                long sourceTriangleIndexSum,
                double maximumAbsoluteOracleError,
                string exactFingerprint,
                string quantizedFingerprint)
            {
                SourceTriangleIndexSum = sourceTriangleIndexSum;
                MaximumAbsoluteOracleError = maximumAbsoluteOracleError;
                ExactFingerprint = exactFingerprint;
                QuantizedFingerprint = quantizedFingerprint;
            }

            internal long SourceTriangleIndexSum { get; }
            internal double MaximumAbsoluteOracleError { get; }
            internal string ExactFingerprint { get; }
            internal string QuantizedFingerprint { get; }
        }

        private sealed class Fingerprints
        {
            internal Fingerprints(string exact, string quantized)
            {
                Exact = exact;
                Quantized = quantized;
            }

            internal string Exact { get; }
            internal string Quantized { get; }
        }

        private sealed class IterationResult
        {
            internal IterationResult(
                IterationSample sample,
                string exactFingerprint,
                string quantizedFingerprint)
            {
                Sample = sample;
                ExactFingerprint = exactFingerprint;
                QuantizedFingerprint = quantizedFingerprint;
            }

            internal IterationSample Sample { get; }
            internal string ExactFingerprint { get; }
            internal string QuantizedFingerprint { get; }
        }

        public sealed class BenchmarkReport
        {
            public string SchemaVersion { get; set; }
            public DateTimeOffset CreatedUtc { get; set; }
            public string Mode { get; set; }
            public string Session { get; set; }
            public string TargetCommit { get; set; }
            public WorkloadManifest Workload { get; set; }
            public EnvironmentManifest Environment { get; set; }
            public AccuracyReport Accuracy { get; set; }
            public PerformanceReport Performance { get; set; }
        }

        public sealed class WorkloadManifest
        {
            public string Id { get; set; }
            public string InputSha256 { get; set; }
            public int CellCountPerAxis { get; set; }
            public double CellSize { get; set; }
            public int TriangleCount { get; set; }
            public int QueryCount { get; set; }
            public double LowerTolerance { get; set; }
            public double UpperTolerance { get; set; }
            public int MaximumDisplaySamples { get; set; }
            public bool ProgressObserverEnabled { get; set; }
            public int WarmupCount { get; set; }
            public int MeasurementCount { get; set; }
            public int OperationsPerMeasurement { get; set; }
        }

        public sealed class EnvironmentManifest
        {
            public string FrameworkDescription { get; set; }
            public string RuntimeVersion { get; set; }
            public string OsDescription { get; set; }
            public string OsArchitecture { get; set; }
            public string ProcessArchitecture { get; set; }
            public int ProcessorCount { get; set; }
            public bool IsServerGc { get; set; }
            public string GcLatencyMode { get; set; }
            public bool DebuggerAttached { get; set; }
            public long StopwatchFrequency { get; set; }
            public string ProcessPriorityClass { get; set; }
            public string ProcessorAffinityMask { get; set; }
            public string HarnessAssemblyVersion { get; set; }
            public string HarnessAssemblyConfiguration { get; set; }
            public string HarnessAssemblyInformationalVersion { get; set; }
            public string HarnessAssemblySha256 { get; set; }
            public string TargetAssemblyVersion { get; set; }
            public string TargetAssemblyConfiguration { get; set; }
            public string TargetAssemblyInformationalVersion { get; set; }
            public string TargetAssemblySourceRevision { get; set; }
            public string TargetAssemblySha256 { get; set; }
        }

        public sealed class AccuracyReport
        {
            public bool OraclePassed { get; set; }
            public long ProcessedPointCount { get; set; }
            public long SourceTriangleIndexSum { get; set; }
            public double PerTargetOracleTolerance { get; set; }
            public double CrossTargetParityTolerance { get; set; }
            public double MaximumAbsoluteOracleError { get; set; }
            public string PointExactFingerprint { get; set; }
            public string PointQuantizedFingerprint { get; set; }
            public string ResultExactFingerprint { get; set; }
            public string ResultQuantizedFingerprint { get; set; }
            public bool RepeatedResultExactFingerprintMatch { get; set; }
        }

        public sealed class PerformanceReport
        {
            public IterationSample Cold { get; set; }
            public List<IterationSample> Warmups { get; set; }
            public string AggregateExactFingerprint { get; set; }
            public string AggregateQuantizedFingerprint { get; set; }
            public List<IterationSample> Samples { get; set; }
            public double TimingNoiseThresholdPercent { get; set; }
            public bool SingleSessionNoisePassed { get; set; }
            public MetricSummary WallMillisecondsPerOperation { get; set; }
            public MetricSummary IndexMillisecondsPerOperation { get; set; }
            public MetricSummary CalculationMillisecondsPerOperation { get; set; }
            public MetricSummary EndToEndPointsPerSecond { get; set; }
            public MetricSummary CalculationPointsPerSecond { get; set; }
            public MetricSummary AllocatedBytesPerOperation { get; set; }
        }

        public sealed class IterationSample
        {
            public int Ordinal { get; set; }
            public int Operations { get; set; }
            public long WallStopwatchTicksTotal { get; set; }
            public double WallMillisecondsPerOperation { get; set; }
            public long IndexTimeSpanTicksTotal { get; set; }
            public double IndexMillisecondsPerOperation { get; set; }
            public long CalculationTimeSpanTicksTotal { get; set; }
            public double CalculationMillisecondsPerOperation { get; set; }
            public double EndToEndPointsPerSecond { get; set; }
            public double CalculationPointsPerSecond { get; set; }
            public long AllocatedBytesTotal { get; set; }
            public double AllocatedBytesPerOperation { get; set; }
            public int Generation0CollectionsTotal { get; set; }
            public int Generation1CollectionsTotal { get; set; }
            public int Generation2CollectionsTotal { get; set; }
        }

        public sealed class MetricSummary
        {
            public double Minimum { get; set; }
            public double Median { get; set; }
            public double P95 { get; set; }
            public double Maximum { get; set; }
            public double MedianAbsoluteDeviation { get; set; }
            public double RelativeMadPercent { get; set; }
        }
    }
}
