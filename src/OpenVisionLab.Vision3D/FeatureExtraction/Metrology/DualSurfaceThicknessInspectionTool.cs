using System.Threading;
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
            => Execute(referenceSamples, measurementSamples, minimumThickness, maximumThickness, minimumValidSamples, CancellationToken.None);

        public DualSurfaceThicknessInspectionResult Execute(
            IReadOnlyList<HeightFieldPlaneFitSample> referenceSamples,
            IReadOnlyList<HeightFieldPlaneFitSample> measurementSamples,
            double minimumThickness,
            double maximumThickness,
            int minimumValidSamples, CancellationToken cancellationToken)
        {
            return ExecuteCore(referenceSamples, measurementSamples, minimumThickness, maximumThickness, minimumValidSamples, null, cancellationToken);
        }

        public static DualSurfaceThicknessInspectionResult ExecuteWithDatum(
            DatumPlaneFitResult referenceDatum, IReadOnlyList<HeightFieldPlaneFitSample> measurementSamples,
            double minimumThickness, double maximumThickness, int minimumValidSamples)
            => ExecuteWithDatum(referenceDatum, measurementSamples, minimumThickness, maximumThickness, minimumValidSamples, CancellationToken.None);

        public static DualSurfaceThicknessInspectionResult ExecuteWithDatum(
            DatumPlaneFitResult referenceDatum, IReadOnlyList<HeightFieldPlaneFitSample> measurementSamples,
            double minimumThickness, double maximumThickness, int minimumValidSamples, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (referenceDatum == null) return Complete(Error("A completed reference datum is required.", 0, measurementSamples == null ? 0 : measurementSamples.Count), cancellationToken);
            if (!referenceDatum.Success)
                return Complete(Error("Reference datum was rejected: " + referenceDatum.FailureReason + ". " + referenceDatum.Message,
                    referenceDatum.InputSampleCount, measurementSamples == null ? 0 : measurementSamples.Count, referenceDatum), cancellationToken);
            return ExecuteCore(referenceDatum.InlierSamples, measurementSamples, minimumThickness, maximumThickness, minimumValidSamples, referenceDatum, cancellationToken);
        }

        private static DualSurfaceThicknessInspectionResult Complete(DualSurfaceThicknessInspectionResult result, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }

        private static DualSurfaceThicknessInspectionResult ExecuteCore(
            IReadOnlyList<HeightFieldPlaneFitSample> referenceSamples, IReadOnlyList<HeightFieldPlaneFitSample> measurementSamples,
            double minimumThickness, double maximumThickness, int minimumValidSamples, DatumPlaneFitResult referenceDatum, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int referenceCount = referenceSamples == null ? 0 : referenceSamples.Count;
            int measurementCount = measurementSamples == null ? 0 : measurementSamples.Count;
            if (!IsFinite(minimumThickness)
                || !IsFinite(maximumThickness)
                || minimumThickness > maximumThickness)
            {
                return Complete(Error("Thickness limits must be finite and ordered minimum to maximum.", referenceCount, measurementCount, referenceDatum), cancellationToken);
            }

            if (minimumValidSamples < 1)
            {
                return Complete(Error("Minimum valid measurement samples must be at least one.", referenceCount, measurementCount, referenceDatum), cancellationToken);
            }

            if (referenceSamples == null || referenceSamples.Count < 3)
            {
                return Complete(Error("Reference ROI requires at least three finite height samples.", referenceCount, measurementCount, referenceDatum), cancellationToken);
            }

            if (measurementSamples == null || measurementSamples.Count < minimumValidSamples)
            {
                return Complete(Error(
                    "Measurement ROI requires at least " + minimumValidSamples + " finite height sample(s).",
                    referenceCount,
                    measurementCount,
                    referenceDatum), cancellationToken);
            }

            LeastSquaresHeightFieldPlaneFitResult plane;
            try
            {
                plane = referenceDatum == null ? new LeastSquaresHeightFieldPlaneFitTool().Execute(referenceSamples, cancellationToken) : referenceDatum.Plane;
            }
            catch (ArgumentException exception)
            {
                return Complete(Error("Reference surface fit failed: " + exception.Message, referenceCount, measurementCount, referenceDatum), cancellationToken);
            }
            catch (InvalidOperationException exception)
            {
                return Complete(Error("Reference surface fit failed: " + exception.Message, referenceCount, measurementCount, referenceDatum), cancellationToken);
            }

            List<double> values = new List<double>(measurementSamples.Count);
            foreach (HeightFieldPlaneFitSample sample in measurementSamples)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (sample == null || sample.Position == null)
                {
                    continue;
                }

                if (!plane.TryEvaluateRawHeight(sample.Position.X, sample.Position.Z, out double referenceHeight)) continue;
                double value = sample.RawHeight - referenceHeight;
                if (IsFinite(value))
                {
                    values.Add(value);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (values.Count < minimumValidSamples)
            {
                return Complete(Error(
                    "Measurement ROI contains " + values.Count + " usable height-axis sample(s); "
                    + minimumValidSamples + " required.",
                    referenceCount,
                    values.Count,
                    referenceDatum), cancellationToken);
            }

            double sum = 0.0;
            double minimum = double.PositiveInfinity;
            double maximum = double.NegativeInfinity;
            int below = 0;
            int above = 0;
            foreach (double value in values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sum += value;
                if (value < minimum) minimum = value;
                if (value > maximum) maximum = value;
                if (value < minimumThickness) below++;
                if (value > maximumThickness) above++;
            }

            cancellationToken.ThrowIfCancellationRequested();
            double mean = sum / values.Count;
            double squaredSpreadSum = 0.0;
            foreach (double value in values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double difference = value - mean;
                squaredSpreadSum += difference * difference;
            }

            cancellationToken.ThrowIfCancellationRequested();
            double referenceSquaredResidualSum = 0.0;
            foreach (HeightFieldPlaneFitSample sample in referenceSamples)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!plane.TryEvaluateRawHeight(sample.Position.X, sample.Position.Z, out double referenceHeight))
                    return Complete(Error("Reference raw-height plane is unavailable or non-finite.", referenceCount, values.Count, referenceDatum), cancellationToken);
                double residual = sample.RawHeight - referenceHeight;
                referenceSquaredResidualSum += residual * residual;
            }

            cancellationToken.ThrowIfCancellationRequested();
            double range = maximum - minimum;
            double rootMeanSquareSpread = Math.Sqrt(squaredSpreadSum / values.Count);
            double referenceFitHeightRootMeanSquare = Math.Sqrt(referenceSquaredResidualSum / referenceSamples.Count);
            if (!IsFinite(sum) || !IsFinite(mean) || !IsFinite(minimum) || !IsFinite(maximum)
                || !IsFinite(range) || !IsFinite(rootMeanSquareSpread) || !IsFinite(referenceFitHeightRootMeanSquare))
            {
                return Complete(Error("Thickness aggregation exceeds the finite calculation range.", referenceCount, values.Count, referenceDatum), cancellationToken);
            }

            DualSurfaceThicknessDecision decision = below == 0 && above == 0
                ? DualSurfaceThicknessDecision.Pass
                : DualSurfaceThicknessDecision.Fail;
            return Complete(new DualSurfaceThicknessInspectionResult(
                decision,
                decision == DualSurfaceThicknessDecision.Pass
                    ? "All measured H-axis separations from the fitted reference surface are within limits."
                    : "One or more measured H-axis separations from the fitted reference surface exceed the limits.",
                plane,
                mean,
                minimum,
                maximum,
                range,
                rootMeanSquareSpread,
                referenceFitHeightRootMeanSquare,
                referenceSamples.Count,
                values.Count,
                below,
                above,
                referenceDatum), cancellationToken);
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
