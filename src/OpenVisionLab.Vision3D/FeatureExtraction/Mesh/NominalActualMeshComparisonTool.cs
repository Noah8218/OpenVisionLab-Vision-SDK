using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    /// <summary>
    /// Declares stream size, inclusive signed tolerance bounds, and the
    /// maximum retained display evidence for one mesh comparison.
    /// </summary>
    public sealed class NominalActualMeshComparisonOptions
    {
        public NominalActualMeshComparisonOptions(
            long expectedPointCount,
            double lowerTolerance,
            double upperTolerance,
            int maximumDisplaySamples)
        {
            ExpectedPointCount = expectedPointCount;
            LowerTolerance = lowerTolerance;
            UpperTolerance = upperTolerance;
            MaximumDisplaySamples = maximumDisplaySamples;
        }

        public long ExpectedPointCount { get; }

        public double LowerTolerance { get; }

        public double UpperTolerance { get; }

        public int MaximumDisplaySamples { get; }
    }

    /// <summary>Reports deterministic processed and total point counts.</summary>
    public sealed class NominalActualMeshComparisonProgress
    {
        public NominalActualMeshComparisonProgress(
            long processedPointCount,
            long totalPointCount)
        {
            ProcessedPointCount = processedPointCount;
            TotalPointCount = totalPointCount;
        }

        public long ProcessedPointCount { get; }

        public long TotalPointCount { get; }
    }

    /// <summary>Population statistics for one mesh-deviation sequence.</summary>
    public sealed class MeshDeviationStatistics
    {
        public MeshDeviationStatistics(
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

        public long Count { get; }
        public double Minimum { get; }
        public double Maximum { get; }
        public double Mean { get; }
        public double StandardDeviationPopulation { get; }
        public double RootMeanSquare { get; }
    }

    /// <summary>
    /// Bounded display evidence retaining query order, closest triangle,
    /// signed/unsigned deviation, and sign-recovery state.
    /// </summary>
    public sealed class NominalActualMeshDeviationSample
    {
        public NominalActualMeshDeviationSample(
            long pointIndex,
            ThreeDPoint point,
            ThreeDPoint closestPoint,
            long sourceTriangleIndex,
            double unsignedDistance,
            double signedDistance,
            bool robustSignRecovered)
        {
            PointIndex = pointIndex;
            Point = point;
            ClosestPoint = closestPoint;
            SourceTriangleIndex = sourceTriangleIndex;
            UnsignedDistance = unsignedDistance;
            SignedDistance = signedDistance;
            RobustSignRecovered = robustSignRecovered;
        }

        public long PointIndex { get; }
        public ThreeDPoint Point { get; }
        public ThreeDPoint ClosestPoint { get; }
        public long SourceTriangleIndex { get; }
        public double UnsignedDistance { get; }
        public double SignedDistance { get; }
        public bool RobustSignRecovered { get; }
    }

    /// <summary>
    /// Streaming mesh-comparison counts, deviation statistics, sign evidence,
    /// bounded display samples, and execution timings.
    /// </summary>
    public sealed class NominalActualMeshComparisonResult
    {
        private NominalActualMeshComparisonResult(
            bool success,
            string message,
            long processedPointCount,
            MeshDeviationStatistics unsignedStatistics,
            MeshDeviationStatistics signedStatistics,
            long belowToleranceCount,
            long withinToleranceCount,
            long aboveToleranceCount,
            long directSignResolvedCount,
            long robustSignRecoveredCount,
            int displayStride,
            IReadOnlyList<NominalActualMeshDeviationSample> displaySamples,
            TimeSpan indexDuration,
            TimeSpan calculationDuration)
        {
            Success = success;
            Message = message;
            ProcessedPointCount = processedPointCount;
            UnsignedStatistics = unsignedStatistics;
            SignedStatistics = signedStatistics;
            BelowToleranceCount = belowToleranceCount;
            WithinToleranceCount = withinToleranceCount;
            AboveToleranceCount = aboveToleranceCount;
            DirectSignResolvedCount = directSignResolvedCount;
            RobustSignRecoveredCount = robustSignRecoveredCount;
            DisplayStride = displayStride;
            DisplaySamples = displaySamples;
            IndexDuration = indexDuration;
            CalculationDuration = calculationDuration;
        }

        public bool Success { get; }
        public string Message { get; }
        public long ProcessedPointCount { get; }
        public MeshDeviationStatistics UnsignedStatistics { get; }
        public MeshDeviationStatistics SignedStatistics { get; }
        public long BelowToleranceCount { get; }
        public long WithinToleranceCount { get; }
        public long AboveToleranceCount { get; }
        public long DirectSignResolvedCount { get; }
        public long RobustSignRecoveredCount { get; }
        public int DisplayStride { get; }
        public IReadOnlyList<NominalActualMeshDeviationSample> DisplaySamples
        {
            get;
        }

        public TimeSpan IndexDuration { get; }
        public TimeSpan CalculationDuration { get; }

        internal static NominalActualMeshComparisonResult Completed(
            long processedPointCount,
            MeshDeviationStatistics unsignedStatistics,
            MeshDeviationStatistics signedStatistics,
            long belowToleranceCount,
            long withinToleranceCount,
            long aboveToleranceCount,
            long directSignResolvedCount,
            long robustSignRecoveredCount,
            int displayStride,
            IReadOnlyList<NominalActualMeshDeviationSample> displaySamples,
            TimeSpan indexDuration,
            TimeSpan calculationDuration)
        {
            return new NominalActualMeshComparisonResult(
                true,
                "Nominal/actual mesh comparison completed.",
                processedPointCount,
                unsignedStatistics,
                signedStatistics,
                belowToleranceCount,
                withinToleranceCount,
                aboveToleranceCount,
                directSignResolvedCount,
                robustSignRecoveredCount,
                displayStride,
                displaySamples,
                indexDuration,
                calculationDuration);
        }

        internal static NominalActualMeshComparisonResult Failed(
            string message)
        {
            return new NominalActualMeshComparisonResult(
                false,
                message,
                0,
                null,
                null,
                0,
                0,
                0,
                0,
                0,
                0,
                new NominalActualMeshDeviationSample[0],
                TimeSpan.Zero,
                TimeSpan.Zero);
        }
    }

    /// <summary>
    /// Deterministically compares a streamed point sequence to a nominal
    /// triangle mesh. It owns BVH queries, closest-point/sign resolution,
    /// tolerance classification, display sampling, and deviation statistics.
    /// Source identity, units, frames, and product lifecycle remain with the
    /// caller.
    /// </summary>
    public sealed class NominalActualMeshComparisonTool
    {
        public NominalActualMeshComparisonResult Execute(
            IReadOnlyList<MeshTriangle> nominalTriangles,
            IEnumerable<ThreeDPoint> queryPoints,
            NominalActualMeshComparisonOptions options,
            IProgress<NominalActualMeshComparisonProgress> progress = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                ValidateInput(nominalTriangles, queryPoints, options);
                int displayStride = CalculateDisplayStride(
                    options.ExpectedPointCount,
                    options.MaximumDisplaySamples);
                List<NominalActualMeshDeviationSample> displaySamples =
                    options.MaximumDisplaySamples == 0
                        ? new List<NominalActualMeshDeviationSample>(0)
                        : new List<NominalActualMeshDeviationSample>(
                            Math.Min(
                                options.MaximumDisplaySamples,
                                65536));

                Stopwatch indexStopwatch = Stopwatch.StartNew();
                TriangleMeshDistanceTool distanceTool =
                    new TriangleMeshDistanceTool(nominalTriangles);
                indexStopwatch.Stop();

                RunningStatistics unsignedStatistics =
                    new RunningStatistics();
                RunningStatistics signedStatistics = new RunningStatistics();
                long belowToleranceCount = 0;
                long withinToleranceCount = 0;
                long aboveToleranceCount = 0;
                long directSignResolvedCount = 0;
                long robustSignRecoveredCount = 0;
                long unresolvedSignCount = 0;
                long processedPointCount = 0;

                Stopwatch calculationStopwatch = Stopwatch.StartNew();
                foreach (ThreeDPoint point in queryPoints)
                {
                    if ((processedPointCount & 1023) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    if (processedPointCount >= options.ExpectedPointCount)
                    {
                        throw new InvalidOperationException(
                            "The measured validation query produced more points than declared.");
                    }

                    PointMeshDistance closest = distanceTool.Execute(point);
                    unsignedStatistics.Add(closest.UnsignedDistance);

                    PointMeshDistance signed = closest;
                    bool robustSignRecovered = false;
                    if (closest.SignResolved)
                    {
                        directSignResolvedCount++;
                    }
                    else
                    {
                        signed = distanceTool.ExecuteRobustSign(
                            point,
                            closest.UnsignedDistance);
                        if (signed.SignResolved)
                        {
                            robustSignRecoveredCount++;
                            robustSignRecovered = true;
                        }
                    }

                    if (!signed.SignedDistance.HasValue
                        || !signed.SignResolved)
                    {
                        unresolvedSignCount++;
                        processedPointCount++;
                        continue;
                    }

                    double signedDistance = signed.SignedDistance.Value;
                    signedStatistics.Add(signedDistance);
                    if (signedDistance < options.LowerTolerance)
                    {
                        belowToleranceCount++;
                    }
                    else if (signedDistance > options.UpperTolerance)
                    {
                        aboveToleranceCount++;
                    }
                    else
                    {
                        withinToleranceCount++;
                    }

                    if (displayStride > 0
                        && processedPointCount % displayStride == 0
                        && displaySamples.Count
                            < options.MaximumDisplaySamples)
                    {
                        displaySamples.Add(
                            new NominalActualMeshDeviationSample(
                                processedPointCount,
                                point,
                                closest.ClosestPoint,
                                closest.SourceTriangleIndex,
                                closest.UnsignedDistance,
                                signedDistance,
                                robustSignRecovered));
                    }

                    processedPointCount++;
                    if (progress != null
                        && (processedPointCount % 65536 == 0
                            || processedPointCount
                                == options.ExpectedPointCount))
                    {
                        progress.Report(
                            new NominalActualMeshComparisonProgress(
                                processedPointCount,
                                options.ExpectedPointCount));
                    }
                }

                calculationStopwatch.Stop();
                if (processedPointCount != options.ExpectedPointCount)
                {
                    throw new InvalidOperationException(
                        "The measured validation query was not consumed completely.");
                }

                if (unresolvedSignCount != 0)
                {
                    throw new InvalidOperationException(
                        "Signed deviation remained unresolved for "
                        + unresolvedSignCount.ToString("N0", CultureInfo.InvariantCulture)
                        + " of "
                        + processedPointCount.ToString("N0", CultureInfo.InvariantCulture)
                        + " points.");
                }

                return NominalActualMeshComparisonResult.Completed(
                    processedPointCount,
                    unsignedStatistics.ToResult(),
                    signedStatistics.ToResult(),
                    belowToleranceCount,
                    withinToleranceCount,
                    aboveToleranceCount,
                    directSignResolvedCount,
                    robustSignRecoveredCount,
                    displayStride,
                    displaySamples,
                    indexStopwatch.Elapsed,
                    calculationStopwatch.Elapsed);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return NominalActualMeshComparisonResult.Failed(
                    exception.Message);
            }
        }

        private static void ValidateInput(
            IReadOnlyList<MeshTriangle> nominalTriangles,
            IEnumerable<ThreeDPoint> queryPoints,
            NominalActualMeshComparisonOptions options)
        {
            if (nominalTriangles == null)
            {
                throw new ArgumentNullException(nameof(nominalTriangles));
            }

            if (queryPoints == null)
            {
                throw new ArgumentNullException(nameof(queryPoints));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (options.ExpectedPointCount <= 0)
            {
                throw new ArgumentException(
                    "The expected query point count must be positive.",
                    nameof(options));
            }

            if (!IsFinite(options.LowerTolerance)
                || !IsFinite(options.UpperTolerance)
                || options.LowerTolerance >= 0.0
                || options.UpperTolerance <= 0.0
                || options.LowerTolerance >= options.UpperTolerance)
            {
                throw new ArgumentException(
                    "Comparison tolerances must be finite, zero-centred, and ordered lower < 0 < upper.",
                    nameof(options));
            }

            if (options.MaximumDisplaySamples < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    options.MaximumDisplaySamples,
                    "MaximumDisplaySamples must be non-negative.");
            }
        }

        private static int CalculateDisplayStride(
            long pointCount,
            int maximumDisplaySamples)
        {
            if (maximumDisplaySamples == 0)
            {
                return 0;
            }

            long stride = Math.Max(
                1L,
                (pointCount + maximumDisplaySamples - 1)
                    / maximumDisplaySamples);
            return (int)Math.Min(int.MaxValue, stride);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private sealed class RunningStatistics
        {
            private double mean;
            private double sumSquaredDeviation;
            private double sumSquares;

            public long Count { get; private set; }
            public double Minimum { get; private set; }
                = double.PositiveInfinity;
            public double Maximum { get; private set; }
                = double.NegativeInfinity;

            public void Add(double value)
            {
                if (!IsFinite(value))
                {
                    throw new InvalidOperationException(
                        "A mesh-deviation calculation produced a non-finite value.");
                }

                Count++;
                Minimum = Math.Min(Minimum, value);
                Maximum = Math.Max(Maximum, value);
                double delta = value - mean;
                mean += delta / Count;
                sumSquaredDeviation += delta * (value - mean);
                sumSquares += value * value;
            }

            public MeshDeviationStatistics ToResult()
            {
                if (Count == 0)
                {
                    throw new InvalidOperationException(
                        "The measured validation query produced no deviation values.");
                }

                return new MeshDeviationStatistics(
                    Count,
                    Minimum,
                    Maximum,
                    mean,
                    Math.Sqrt(
                        Math.Max(0.0, sumSquaredDeviation / Count)),
                    Math.Sqrt(sumSquares / Count));
            }
        }
    }
}
