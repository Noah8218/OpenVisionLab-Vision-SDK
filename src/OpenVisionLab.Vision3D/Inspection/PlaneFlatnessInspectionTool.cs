using OpenVisionLab.Vision3D.FeatureExtraction;
using System;
using System.Collections.Generic;

namespace OpenVisionLab.Vision3D.Inspection
{
    public sealed class PlaneFlatnessInspectionResult
    {
        public PlaneFlatnessInspectionResult(
            LeastSquaresHeightFieldPlaneFitResult referencePlane,
            int referenceSampleCount,
            int measurementSampleCount,
            double minimumSignedDistance,
            double maximumSignedDistance,
            double flatness,
            double rootMeanSquareDistance,
            ThreeDPoint minimumPoint,
            ThreeDPoint maximumPoint,
            ThreeDPoint minimumProjection,
            ThreeDPoint maximumProjection,
            double tolerance)
        {
            ReferencePlane = referencePlane;
            ReferenceSampleCount = referenceSampleCount;
            MeasurementSampleCount = measurementSampleCount;
            MinimumSignedDistance = minimumSignedDistance;
            MaximumSignedDistance = maximumSignedDistance;
            Flatness = flatness;
            RootMeanSquareDistance = rootMeanSquareDistance;
            MinimumPoint = minimumPoint;
            MaximumPoint = maximumPoint;
            MinimumProjection = minimumProjection;
            MaximumProjection = maximumProjection;
            Tolerance = tolerance;
        }

        public LeastSquaresHeightFieldPlaneFitResult ReferencePlane { get; }
        public int ReferenceSampleCount { get; }
        public int MeasurementSampleCount { get; }
        public double MinimumSignedDistance { get; }
        public double MaximumSignedDistance { get; }
        public double Flatness { get; }
        public double RootMeanSquareDistance { get; }
        public ThreeDPoint MinimumPoint { get; }
        public ThreeDPoint MaximumPoint { get; }
        public ThreeDPoint MinimumProjection { get; }
        public ThreeDPoint MaximumProjection { get; }
        public double Tolerance { get; }
        public bool Passed => Flatness <= Tolerance;
    }

    /// <summary>
    /// Fits a reference plane and measures orthogonal peak-to-valley and RMS
    /// for a separate surface. Unit, source identity, overlays, and recipes are
    /// deliberately owned by the caller.
    /// </summary>
    public sealed class PlaneFlatnessInspectionTool
    {
        private readonly LeastSquaresHeightFieldPlaneFitTool planeFitTool = new LeastSquaresHeightFieldPlaneFitTool();

        public PlaneFlatnessInspectionResult Execute(
            IReadOnlyList<HeightFieldPlaneFitSample> referenceSamples,
            IReadOnlyList<HeightFieldPlaneFitSample> measurementSamples,
            double tolerance)
        {
            if (referenceSamples == null || referenceSamples.Count < 3)
            {
                throw new ArgumentException("Reference ROI must contain at least three finite samples.", nameof(referenceSamples));
            }

            if (measurementSamples == null || measurementSamples.Count < 3)
            {
                throw new ArgumentException("Measurement surface must contain at least three finite samples.", nameof(measurementSamples));
            }

            if (double.IsNaN(tolerance) || double.IsInfinity(tolerance) || tolerance <= 0.0)
            {
                throw new ArgumentException("Flatness tolerance must be a positive finite value.", nameof(tolerance));
            }

            LeastSquaresHeightFieldPlaneFitResult referencePlane = planeFitTool.Execute(referenceSamples);
            double minimumDistance = double.PositiveInfinity;
            double maximumDistance = double.NegativeInfinity;
            double squaredDistanceSum = 0.0;
            ThreeDPoint minimumPoint = null;
            ThreeDPoint maximumPoint = null;

            foreach (HeightFieldPlaneFitSample sample in measurementSamples)
            {
                if (!IsFinite(sample))
                {
                    throw new ArgumentException("Measurement surface contains a non-finite sample.", nameof(measurementSamples));
                }

                double distance = LeastSquaresHeightFieldPlaneFitTool.SignedDistance(
                    sample.Position,
                    referencePlane.Normal,
                    referencePlane.Offset);
                if (!IsFinite(distance))
                {
                    throw new InvalidOperationException("Plane flatness calculation produced a non-finite distance.");
                }

                double squaredDistance = distance * distance;
                if (!IsFinite(squaredDistance))
                {
                    throw new InvalidOperationException("Plane flatness calculation overflowed its distance range.");
                }

                squaredDistanceSum += squaredDistance;
                if (!IsFinite(squaredDistanceSum))
                {
                    throw new InvalidOperationException("Plane flatness calculation overflowed its RMS accumulation range.");
                }
                if (distance < minimumDistance)
                {
                    minimumDistance = distance;
                    minimumPoint = sample.Position;
                }

                if (distance > maximumDistance)
                {
                    maximumDistance = distance;
                    maximumPoint = sample.Position;
                }
            }

            double flatness = maximumDistance - minimumDistance;
            double rms = Math.Sqrt(squaredDistanceSum / measurementSamples.Count);
            return new PlaneFlatnessInspectionResult(
                referencePlane,
                referenceSamples.Count,
                measurementSamples.Count,
                minimumDistance,
                maximumDistance,
                flatness,
                rms,
                minimumPoint,
                maximumPoint,
                LeastSquaresHeightFieldPlaneFitTool.Project(minimumPoint, referencePlane.Normal, minimumDistance),
                LeastSquaresHeightFieldPlaneFitTool.Project(maximumPoint, referencePlane.Normal, maximumDistance),
                tolerance);
        }

        private static bool IsFinite(HeightFieldPlaneFitSample sample)
        {
            return sample != null
                && sample.Position != null
                && sample.Position.IsFinite
                && !double.IsNaN(sample.RawHeight)
                && !double.IsInfinity(sample.RawHeight);
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
