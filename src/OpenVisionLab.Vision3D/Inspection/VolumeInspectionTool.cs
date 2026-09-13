using System;
using System.Collections.Generic;
using OpenVisionLab.Vision3D.FeatureExtraction;

namespace OpenVisionLab.Vision3D.Inspection
{
    public sealed class VolumeInspectionOptions
    {
        public double SampleArea { get; set; }
        public double ExpectedNetVolume { get; set; }
        public double Tolerance { get; set; }
    }

    public sealed class VolumeInspectionResult
    {
        public VolumeInspectionResult(
            LeastSquaresHeightFieldPlaneFitResult referencePlane,
            int referenceSampleCount,
            int measurementSampleCount,
            double aboveVolume,
            double belowVolume,
            double netVolume,
            double expectedNetVolume,
            double tolerance,
            bool passed)
        {
            ReferencePlane = referencePlane;
            ReferenceSampleCount = referenceSampleCount;
            MeasurementSampleCount = measurementSampleCount;
            AboveVolume = aboveVolume;
            BelowVolume = belowVolume;
            NetVolume = netVolume;
            ExpectedNetVolume = expectedNetVolume;
            Tolerance = tolerance;
            Passed = passed;
        }

        public LeastSquaresHeightFieldPlaneFitResult ReferencePlane { get; }
        public int ReferenceSampleCount { get; }
        public int MeasurementSampleCount { get; }
        public double AboveVolume { get; }
        public double BelowVolume { get; }
        public double NetVolume { get; }
        public double ExpectedNetVolume { get; }
        public double Tolerance { get; }
        public bool Passed { get; }
    }

    /// <summary>
    /// Integrates signed height relative to a least-squares reference plane.
    /// Units, frame identity, ROI extraction, recipes, and physical calibration
    /// deliberately remain caller-owned.
    /// </summary>
    public sealed class VolumeInspectionTool
    {
        private readonly LeastSquaresHeightFieldPlaneFitTool planeFit = new LeastSquaresHeightFieldPlaneFitTool();

        public VolumeInspectionResult Execute(
            IReadOnlyList<HeightFieldPlaneFitSample> referenceSamples,
            IReadOnlyList<HeightFieldPlaneFitSample> measurementSamples,
            VolumeInspectionOptions options)
        {
            if (referenceSamples == null || referenceSamples.Count < 3)
            {
                throw new ArgumentException("Reference ROI requires at least three samples.", nameof(referenceSamples));
            }
            if (measurementSamples == null || measurementSamples.Count < 1)
            {
                throw new ArgumentException("Measurement ROI requires at least one sample.", nameof(measurementSamples));
            }
            Validate(options);

            LeastSquaresHeightFieldPlaneFitResult referencePlane = planeFit.Execute(referenceSamples);
            double aboveVolume = 0.0;
            double belowVolume = 0.0;
            foreach (HeightFieldPlaneFitSample sample in measurementSamples)
            {
                if (sample == null || sample.Position == null || !sample.Position.IsFinite || !IsFinite(sample.RawHeight))
                {
                    throw new ArgumentException("Measurement ROI samples must contain finite coordinates and heights.", nameof(measurementSamples));
                }

                double signedVolume = (sample.Position.Y - referencePlane.EvaluateY(sample.Position.X, sample.Position.Z))
                    * options.SampleArea;
                if (!IsFinite(signedVolume))
                {
                    throw new InvalidOperationException("Volume calculation produced a non-finite sample volume.");
                }

                if (signedVolume >= 0.0)
                {
                    aboveVolume += signedVolume;
                }
                else
                {
                    belowVolume += -signedVolume;
                }

                if (!IsFinite(aboveVolume) || !IsFinite(belowVolume))
                {
                    throw new InvalidOperationException("Volume calculation overflowed its finite accumulation range.");
                }
            }

            double netVolume = aboveVolume - belowVolume;
            if (!IsFinite(netVolume))
            {
                throw new InvalidOperationException("Volume calculation produced a non-finite net volume.");
            }

            double acceptanceDifference = netVolume - options.ExpectedNetVolume;
            if (!IsFinite(acceptanceDifference))
            {
                throw new InvalidOperationException("Volume calculation produced a non-finite acceptance difference.");
            }

            return new VolumeInspectionResult(
                referencePlane,
                referenceSamples.Count,
                measurementSamples.Count,
                aboveVolume,
                belowVolume,
                netVolume,
                options.ExpectedNetVolume,
                options.Tolerance,
                Math.Abs(acceptanceDifference) <= options.Tolerance);
        }

        private static void Validate(VolumeInspectionOptions options)
        {
            if (options == null
                || !IsFinite(options.SampleArea)
                || options.SampleArea <= 0.0
                || !IsFinite(options.ExpectedNetVolume)
                || !IsFinite(options.Tolerance)
                || options.Tolerance < 0.0)
            {
                throw new ArgumentException("Sample area must be positive; expected net volume and non-negative tolerance must be finite.", nameof(options));
            }
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
