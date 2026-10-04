using System;
using System.Collections.Generic;

namespace OpenVisionLab.Vision3D.Inspection
{
    public sealed class CrossSectionDimensionsSample
    {
        public CrossSectionDimensionsSample(int index, double axisPosition, double scalarHeight)
        {
            Index = index;
            AxisPosition = axisPosition;
            ScalarHeight = scalarHeight;
        }

        public int Index { get; }
        public double AxisPosition { get; }
        public double ScalarHeight { get; }
    }

    public sealed class CrossSectionDimensionsInspectionOptions
    {
        public double ExpectedWidth { get; set; }
        public double WidthTolerance { get; set; }
        public double ExpectedHeightRange { get; set; }
        public double HeightTolerance { get; set; }
    }

    public sealed class CrossSectionDimensionsInspectionResult
    {
        public CrossSectionDimensionsInspectionResult(
            double width,
            double heightRange,
            double heightMinimum,
            double heightMaximum,
            int sampleCount,
            bool widthPassed,
            bool heightPassed)
        {
            Width = width;
            HeightRange = heightRange;
            HeightMinimum = heightMinimum;
            HeightMaximum = heightMaximum;
            SampleCount = sampleCount;
            WidthPassed = widthPassed;
            HeightPassed = heightPassed;
        }

        public double Width { get; }
        public double HeightRange { get; }
        public double HeightMinimum { get; }
        public double HeightMaximum { get; }
        public int SampleCount { get; }
        public bool WidthPassed { get; }
        public bool HeightPassed { get; }
        public bool Passed => WidthPassed && HeightPassed;
    }

    /// <summary>
    /// Measures a source-neutral one-dimensional section. Selection identity,
    /// row/column policy, units, recipes, and overlays remain caller-owned.
    /// </summary>
    public sealed class CrossSectionDimensionsInspectionTool
    {
        public CrossSectionDimensionsInspectionResult Execute(
            IReadOnlyList<CrossSectionDimensionsSample> samples,
            CrossSectionDimensionsInspectionOptions options)
        {
            if (samples == null || samples.Count < 2)
            {
                throw new ArgumentException("Cross-section requires at least two valid samples.", nameof(samples));
            }
            Validate(options);

            double minimumPosition = double.PositiveInfinity;
            double maximumPosition = double.NegativeInfinity;
            double minimumHeight = double.PositiveInfinity;
            double maximumHeight = double.NegativeInfinity;
            foreach (CrossSectionDimensionsSample sample in samples)
            {
                if (sample == null || sample.Index < 0 || !IsFinite(sample.AxisPosition) || !IsFinite(sample.ScalarHeight))
                {
                    throw new ArgumentException("Cross-section samples require non-negative indices and finite position/height values.", nameof(samples));
                }
                minimumPosition = Math.Min(minimumPosition, sample.AxisPosition);
                maximumPosition = Math.Max(maximumPosition, sample.AxisPosition);
                minimumHeight = Math.Min(minimumHeight, sample.ScalarHeight);
                maximumHeight = Math.Max(maximumHeight, sample.ScalarHeight);
            }

            double width = maximumPosition - minimumPosition;
            double heightRange = maximumHeight - minimumHeight;
            if (!IsFinite(width) || !IsFinite(heightRange))
            {
                throw new InvalidOperationException("Cross-section dimensions cannot be represented as finite values.");
            }
            return new CrossSectionDimensionsInspectionResult(
                width,
                heightRange,
                minimumHeight,
                maximumHeight,
                samples.Count,
                Math.Abs(width - options.ExpectedWidth) <= options.WidthTolerance,
                Math.Abs(heightRange - options.ExpectedHeightRange) <= options.HeightTolerance);
        }

        private static void Validate(CrossSectionDimensionsInspectionOptions options)
        {
            if (options == null
                || !IsFinite(options.ExpectedWidth)
                || !NonNegative(options.WidthTolerance)
                || !IsFinite(options.ExpectedHeightRange)
                || !NonNegative(options.HeightTolerance))
            {
                throw new ArgumentException("Expected dimensions must be finite and tolerances must be non-negative.", nameof(options));
            }
        }

        private static bool NonNegative(double value) => IsFinite(value) && value >= 0.0;
        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
