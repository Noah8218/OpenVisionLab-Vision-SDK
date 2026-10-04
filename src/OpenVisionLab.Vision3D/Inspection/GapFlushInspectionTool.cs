using System;

namespace OpenVisionLab.Vision3D.Inspection
{
    public sealed class GapFlushInspectionOptions
    {
        public double ExpectedGap { get; set; }
        public double GapTolerance { get; set; }
        public double ExpectedFlush { get; set; }
        public double FlushTolerance { get; set; }
    }

    public sealed class GapFlushRegionStatistics
    {
        public GapFlushRegionStatistics(int sampleCount, double scalarHeightMean, double referenceHeightMean)
        {
            SampleCount = sampleCount;
            ScalarHeightMean = scalarHeightMean;
            ReferenceHeightMean = referenceHeightMean;
        }

        public int SampleCount { get; }
        public double ScalarHeightMean { get; }
        public double ReferenceHeightMean { get; }
    }

    public sealed class GapFlushInspectionResult
    {
        public GapFlushInspectionResult(
            double signedGap,
            double signedFlush,
            double signedReferenceFlush,
            int firstSampleCount,
            int secondSampleCount,
            bool gapPassed,
            bool flushPassed)
        {
            SignedGap = signedGap;
            SignedFlush = signedFlush;
            SignedReferenceFlush = signedReferenceFlush;
            FirstSampleCount = firstSampleCount;
            SecondSampleCount = secondSampleCount;
            GapPassed = gapPassed;
            FlushPassed = flushPassed;
        }

        public double SignedGap { get; }
        public double SignedFlush { get; }
        public double SignedReferenceFlush { get; }
        public int FirstSampleCount { get; }
        public int SecondSampleCount { get; }
        public bool GapPassed { get; }
        public bool FlushPassed { get; }
        public bool Passed => GapPassed && FlushPassed;
    }

    /// <summary>
    /// Measures signed separation between two authored U-axis regions and their
    /// mean-height difference. Units, frame identity, ROI extraction, recipes,
    /// and overlays deliberately remain caller-owned.
    /// </summary>
    public sealed class GapFlushInspectionTool
    {
        public GapFlushInspectionResult Execute(
            double firstMinimumU,
            double firstMaximumU,
            double secondMinimumU,
            double secondMaximumU,
            GapFlushRegionStatistics first,
            GapFlushRegionStatistics second,
            GapFlushInspectionOptions options)
        {
            ValidateRegion(firstMinimumU, firstMaximumU, first, nameof(first));
            ValidateRegion(secondMinimumU, secondMaximumU, second, nameof(second));
            Validate(options);

            double signedGap = secondMinimumU - firstMaximumU;
            double signedFlush = second.ScalarHeightMean - first.ScalarHeightMean;
            double signedReferenceFlush = second.ReferenceHeightMean - first.ReferenceHeightMean;
            if (!IsFinite(signedGap) || !IsFinite(signedFlush) || !IsFinite(signedReferenceFlush))
            {
                throw new InvalidOperationException("Gap/flush calculation produced a non-finite measurement.");
            }

            return new GapFlushInspectionResult(
                signedGap,
                signedFlush,
                signedReferenceFlush,
                first.SampleCount,
                second.SampleCount,
                Within(signedGap, options.ExpectedGap, options.GapTolerance),
                Within(signedFlush, options.ExpectedFlush, options.FlushTolerance));
        }

        private static void ValidateRegion(
            double minimumU,
            double maximumU,
            GapFlushRegionStatistics statistics,
            string parameterName)
        {
            if (!IsFinite(minimumU) || !IsFinite(maximumU) || maximumU <= minimumU)
            {
                throw new ArgumentException("Gap/flush regions must have finite ordered U-axis bounds.", parameterName);
            }
            if (statistics == null || statistics.SampleCount <= 0)
            {
                throw new ArgumentException("Both gap/flush regions require at least one sample.", parameterName);
            }
            if (!IsFinite(statistics.ScalarHeightMean) || !IsFinite(statistics.ReferenceHeightMean))
            {
                throw new ArgumentException("Gap/flush region means must be finite.", parameterName);
            }
        }

        private static void Validate(GapFlushInspectionOptions options)
        {
            if (options == null
                || !IsFinite(options.ExpectedGap)
                || !NonNegative(options.GapTolerance)
                || !IsFinite(options.ExpectedFlush)
                || !NonNegative(options.FlushTolerance))
            {
                throw new ArgumentException("Expected gap/flush values must be finite and tolerances must be non-negative.", nameof(options));
            }
        }

        private static bool Within(double actual, double expected, double tolerance) =>
            Math.Abs(actual - expected) <= tolerance;

        private static bool NonNegative(double value) => IsFinite(value) && value >= 0.0;
        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
