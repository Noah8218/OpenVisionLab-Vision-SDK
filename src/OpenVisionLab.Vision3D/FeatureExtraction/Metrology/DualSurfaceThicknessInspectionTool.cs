using System;
using System.Collections.Generic;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    public enum DualSurfaceThicknessDecision
    {
        Error = 0,
        Pass = 1,
        Fail = 2
    }

    public sealed class DualSurfaceThicknessInspectionResult
    {
        internal DualSurfaceThicknessInspectionResult(
            DualSurfaceThicknessDecision decision,
            string message,
            LeastSquaresHeightFieldPlaneFitResult referencePlane,
            double mean,
            double minimum,
            double maximum,
            double range,
            double rootMeanSquareSpread,
            double referenceFitHeightRootMeanSquare,
            int referenceSampleCount,
            int measurementSampleCount,
            int belowLowerLimitCount,
            int aboveUpperLimitCount,
            DatumPlaneFitResult datumPlaneFitEvidence = null)
        {
            Decision = decision;
            Message = message;
            ReferencePlane = referencePlane;
            Mean = mean;
            Minimum = minimum;
            Maximum = maximum;
            Range = range;
            RootMeanSquareSpread = rootMeanSquareSpread;
            ReferenceFitHeightRootMeanSquare = referenceFitHeightRootMeanSquare;
            ReferenceSampleCount = referenceSampleCount;
            MeasurementSampleCount = measurementSampleCount;
            BelowLowerLimitCount = belowLowerLimitCount;
            AboveUpperLimitCount = aboveUpperLimitCount;
            DatumPlaneFitEvidence = datumPlaneFitEvidence;
        }

        public DualSurfaceThicknessDecision Decision { get; }
        public bool Success => Decision != DualSurfaceThicknessDecision.Error;
        public string Message { get; }
        public LeastSquaresHeightFieldPlaneFitResult ReferencePlane { get; }
        public DatumPlaneFitResult DatumPlaneFitEvidence { get; }
        public double Mean { get; }
        public double Minimum { get; }
        public double Maximum { get; }
        public double Range { get; }
        public double RootMeanSquareSpread { get; }
        public double ReferenceFitHeightRootMeanSquare { get; }
        public int ReferenceSampleCount { get; }
        public int MeasurementSampleCount { get; }
        public int BelowLowerLimitCount { get; }
        public int AboveUpperLimitCount { get; }
    }

    /// <summary>
    /// Fits a reference height-field plane and measures signed raw-height
    /// separation at independent measurement samples. Source identity, unit,
    /// frame, recipe lifecycle, and presentation remain caller-owned.
    /// </summary>
    public sealed class DualSurfaceThicknessInspectionTool
    {
        public DualSurfaceThicknessInspectionResult Execute(
            IReadOnlyList<HeightFieldPlaneFitSample> referenceSamples,
            IReadOnlyList<HeightFieldPlaneFitSample> measurementSamples,
            double minimumThickness,
            double maximumThickness,
            int minimumValidSamples)
        {
            return ExecuteCore(referenceSamples, measurementSamples, minimumThickness, maximumThickness, minimumValidSamples, null);
        }

        public static DualSurfaceThicknessInspectionResult ExecuteWithDatum(
            DatumPlaneFitResult referenceDatum, IReadOnlyList<HeightFieldPlaneFitSample> measurementSamples,
            double minimumThickness, double maximumThickness, int minimumValidSamples)
        {
            if (referenceDatum == null) return Error("A completed reference datum is required.", 0, measurementSamples == null ? 0 : measurementSamples.Count);
            if (!referenceDatum.Success)
                return Error("Reference datum was rejected: " + referenceDatum.FailureReason + ". " + referenceDatum.Message,
                    referenceDatum.InputSampleCount, measurementSamples == null ? 0 : measurementSamples.Count, referenceDatum);
            return ExecuteCore(referenceDatum.InlierSamples, measurementSamples, minimumThickness, maximumThickness, minimumValidSamples, referenceDatum);
        }

        private static DualSurfaceThicknessInspectionResult ExecuteCore(
            IReadOnlyList<HeightFieldPlaneFitSample> referenceSamples, IReadOnlyList<HeightFieldPlaneFitSample> measurementSamples,
            double minimumThickness, double maximumThickness, int minimumValidSamples, DatumPlaneFitResult referenceDatum)
        {
            int referenceCount = referenceSamples == null ? 0 : referenceSamples.Count;
            int measurementCount = measurementSamples == null ? 0 : measurementSamples.Count;
            if (!IsFinite(minimumThickness)
                || !IsFinite(maximumThickness)
                || minimumThickness > maximumThickness)
            {
                return Error("Thickness limits must be finite and ordered minimum to maximum.", referenceCount, measurementCount, referenceDatum);
            }

            if (minimumValidSamples < 1)
            {
                return Error("Minimum valid measurement samples must be at least one.", referenceCount, measurementCount, referenceDatum);
            }

            if (referenceSamples == null || referenceSamples.Count < 3)
            {
                return Error("Reference ROI requires at least three finite height samples.", referenceCount, measurementCount, referenceDatum);
            }

            if (measurementSamples == null || measurementSamples.Count < minimumValidSamples)
            {
                return Error(
                    "Measurement ROI requires at least " + minimumValidSamples + " finite height sample(s).",
                    referenceCount,
                    measurementCount,
                    referenceDatum);
            }

            LeastSquaresHeightFieldPlaneFitResult plane;
            try
            {
                plane = referenceDatum == null ? new LeastSquaresHeightFieldPlaneFitTool().Execute(referenceSamples) : referenceDatum.Plane;
            }
            catch (ArgumentException exception)
            {
                return Error("Reference surface fit failed: " + exception.Message, referenceCount, measurementCount, referenceDatum);
            }

            List<double> values = new List<double>(measurementSamples.Count);
            foreach (HeightFieldPlaneFitSample sample in measurementSamples)
            {
                if (sample == null || sample.Position == null)
                {
                    continue;
                }

                double value = sample.RawHeight - plane.EvaluateY(sample.Position.X, sample.Position.Z);
                if (IsFinite(value))
                {
                    values.Add(value);
                }
            }

            if (values.Count < minimumValidSamples)
            {
                return Error(
                    "Measurement ROI contains " + values.Count + " usable height-axis sample(s); "
                    + minimumValidSamples + " required.",
                    referenceCount,
                    values.Count,
                    referenceDatum);
            }

            double sum = 0.0;
            double minimum = double.PositiveInfinity;
            double maximum = double.NegativeInfinity;
            int below = 0;
            int above = 0;
            foreach (double value in values)
            {
                sum += value;
                if (value < minimum) minimum = value;
                if (value > maximum) maximum = value;
                if (value < minimumThickness) below++;
                if (value > maximumThickness) above++;
            }

            double mean = sum / values.Count;
            double squaredSpreadSum = 0.0;
            foreach (double value in values)
            {
                double difference = value - mean;
                squaredSpreadSum += difference * difference;
            }

            double referenceSquaredResidualSum = 0.0;
            foreach (HeightFieldPlaneFitSample sample in referenceSamples)
            {
                double residual = sample.RawHeight - plane.EvaluateY(sample.Position.X, sample.Position.Z);
                referenceSquaredResidualSum += residual * residual;
            }

            DualSurfaceThicknessDecision decision = below == 0 && above == 0
                ? DualSurfaceThicknessDecision.Pass
                : DualSurfaceThicknessDecision.Fail;
            return new DualSurfaceThicknessInspectionResult(
                decision,
                decision == DualSurfaceThicknessDecision.Pass
                    ? "All measured H-axis separations from the fitted reference surface are within limits."
                    : "One or more measured H-axis separations from the fitted reference surface exceed the limits.",
                plane,
                mean,
                minimum,
                maximum,
                maximum - minimum,
                Math.Sqrt(squaredSpreadSum / values.Count),
                Math.Sqrt(referenceSquaredResidualSum / referenceSamples.Count),
                referenceSamples.Count,
                values.Count,
                below,
                above,
                referenceDatum);
        }

        private static DualSurfaceThicknessInspectionResult Error(string message, int referenceSampleCount, int measurementSampleCount, DatumPlaneFitResult datumEvidence = null)
        {
            return new DualSurfaceThicknessInspectionResult(
                DualSurfaceThicknessDecision.Error,
                message,
                null,
                double.NaN,
                double.NaN,
                double.NaN,
                double.NaN,
                double.NaN,
                double.NaN,
                referenceSampleCount,
                measurementSampleCount,
                0,
                0,
                datumEvidence);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
