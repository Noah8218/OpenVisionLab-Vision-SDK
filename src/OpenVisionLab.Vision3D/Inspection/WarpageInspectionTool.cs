using System.Threading;
using OpenVisionLab.Vision3D.Geometry;
using System;
using System.Diagnostics;

namespace OpenVisionLab.Vision3D.Inspection
{
    /// <summary>
    /// Fits z = ax + by + c to the finite ROI samples and evaluates residual peak-to-valley and RMS.
    /// This is a numeric planarity calculation in the declared map frame, not a metrology certification.
    /// </summary>
    public sealed class WarpageInspectionTool : ICancellableThreeDInspectionTool
    {
        public WarpageInspectionTool()
            : this(new WarpageInspectionOptions())
        {
        }

        public WarpageInspectionTool(WarpageInspectionOptions options)
        {
            Options = options;
        }

        public string Name => "Warpage";

        public WarpageInspectionOptions Options { get; }

        public ThreeDInspectionResult Execute(HeightMap3D source)
        { return Execute(source, CancellationToken.None); }

        public ThreeDInspectionResult Execute(HeightMap3D source, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThreeDInspectionResult Complete(ThreeDInspectionResult value) { cancellationToken.ThrowIfCancellationRequested(); return value; }
            Stopwatch stopwatch = Stopwatch.StartNew();

            try
            {
                if (source == null)
                {
                    return Complete(Failure(ThreeDInspectionErrorCode.InputHeightMapInvalid, "A height map is required.", stopwatch));
                }

                if (Options == null)
                {
                    return Complete(Failure(ThreeDInspectionErrorCode.InvalidParameter, "Warpage options are required.", stopwatch, source));
                }

                if (!ThreeDInspectionMath.IsFinite(Options.MaximumPeakToValley)
                    || Options.MaximumPeakToValley < 0.0
                    || Options.MinimumValidSamples < 3
                    || !ThreeDInspectionMath.IsFinite(Options.MinimumValidCoverageRatio)
                    || Options.MinimumValidCoverageRatio < 0.0
                    || Options.MinimumValidCoverageRatio > 1.0
                    || (Options.MaximumRms.HasValue
                        && (!ThreeDInspectionMath.IsFinite(Options.MaximumRms.Value) || Options.MaximumRms.Value < 0.0)))
                {
                    return Complete(Failure(ThreeDInspectionErrorCode.InvalidParameter, "Warpage limits or valid-sample policy are invalid.", stopwatch, source));
                }

                if (!ThreeDInspectionMath.TryPrepareHeightMap(
                    source,
                    Options.Roi,
                    Options.InputRequirements,
                    Options.MinimumValidSamples,
                    Options.MinimumValidCoverageRatio,
                    false,
                    out HeightMapSampleSummary summary,
                    out ThreeDInspectionErrorCode inputErrorCode,
                    out string inputErrorMessage, cancellationToken))
                {
                    HeightMapRoi? failureRoi = summary.TotalSampleCount > 0 ? summary.Roi : Options.Roi;
                    ThreeDInspectionResult failure = Failure(inputErrorCode, inputErrorMessage, stopwatch, source, failureRoi);
                    if (summary.TotalSampleCount > 0)
                    {
                        ThreeDInspectionMath.ApplySampleSummary(failure, summary, Options.MinimumValidSamples, Options.MinimumValidCoverageRatio);
                    }

                    return Complete(failure);
                }

                HeightMapRoi roi = summary.Roi;
                long validSampleCount = 0;
                double meanX = 0.0;
                double meanY = 0.0;
                double meanZ = 0.0;

                for (int row = roi.Row; row < roi.Row + roi.RowCount; row++)
                {
                cancellationToken.ThrowIfCancellationRequested();
                    for (int column = roi.Column; column < roi.Column + roi.ColumnCount; column++)
                    {
                cancellationToken.ThrowIfCancellationRequested();
                        double z = source.GetHeight(row, column);
                        if (double.IsNaN(z))
                        {
                            continue;
                        }

                        double x = source.GetX(column);
                        double y = source.GetY(row);
                        if (!ThreeDInspectionMath.IsFinite(x) || !ThreeDInspectionMath.IsFinite(y) || !ThreeDInspectionMath.IsFinite(z))
                        {
                            return Complete(Failure(ThreeDInspectionErrorCode.InputHeightMapInvalid, "The height map contains a non-finite coordinate or sample.", stopwatch, source, roi));
                        }

                        validSampleCount++;
                        meanX += (x - meanX) / validSampleCount;
                        meanY += (y - meanY) / validSampleCount;
                        meanZ += (z - meanZ) / validSampleCount;
                    }
                }

                double sumXX = 0.0;
                double sumXY = 0.0;
                double sumYY = 0.0;
                double sumXZ = 0.0;
                double sumYZ = 0.0;

                for (int row = roi.Row; row < roi.Row + roi.RowCount; row++)
                {
                cancellationToken.ThrowIfCancellationRequested();
                    for (int column = roi.Column; column < roi.Column + roi.ColumnCount; column++)
                    {
                cancellationToken.ThrowIfCancellationRequested();
                        double z = source.GetHeight(row, column);
                        if (double.IsNaN(z))
                        {
                            continue;
                        }

                        double dx = source.GetX(column) - meanX;
                        double dy = source.GetY(row) - meanY;
                        double dz = z - meanZ;
                        sumXX += dx * dx;
                        sumXY += dx * dy;
                        sumYY += dy * dy;
                        sumXZ += dx * dz;
                        sumYZ += dy * dz;
                    }
                }

                if (!ThreeDInspectionMath.IsFinite(sumXX)
                    || !ThreeDInspectionMath.IsFinite(sumXY)
                    || !ThreeDInspectionMath.IsFinite(sumYY)
                    || !ThreeDInspectionMath.IsFinite(sumXZ)
                    || !ThreeDInspectionMath.IsFinite(sumYZ))
                {
                    return Complete(Failure(ThreeDInspectionErrorCode.InputHeightMapInvalid, "The height-map scale cannot be represented by the plane fit.", stopwatch, source, roi));
                }

                double normalScale = Math.Max(sumXX, sumYY);
                if (normalScale <= 0.0)
                {
                    return Complete(Failure(ThreeDInspectionErrorCode.DegenerateGeometry, "The warpage ROI does not span two independent map directions.", stopwatch, source, roi));
                }

                double normalizedXX = sumXX / normalScale;
                double normalizedXY = sumXY / normalScale;
                double normalizedYY = sumYY / normalScale;
                double normalizedXZ = sumXZ / normalScale;
                double normalizedYZ = sumYZ / normalScale;
                double determinant = (normalizedXX * normalizedYY) - (normalizedXY * normalizedXY);

                if (!ThreeDInspectionMath.IsFinite(determinant) || Math.Abs(determinant) <= 1e-12)
                {
                    return Complete(Failure(ThreeDInspectionErrorCode.DegenerateGeometry, "The warpage ROI does not support a stable plane fit.", stopwatch, source, roi));
                }

                double slopeX = ((normalizedXZ * normalizedYY) - (normalizedYZ * normalizedXY)) / determinant;
                double slopeY = ((normalizedYZ * normalizedXX) - (normalizedXZ * normalizedXY)) / determinant;
                double intercept = meanZ - (slopeX * meanX) - (slopeY * meanY);
                if (!ThreeDInspectionMath.IsFinite(slopeX)
                    || !ThreeDInspectionMath.IsFinite(slopeY)
                    || !ThreeDInspectionMath.IsFinite(intercept))
                {
                    return Complete(Failure(ThreeDInspectionErrorCode.DegenerateGeometry, "The warpage plane fit is non-finite.", stopwatch, source, roi));
                }

                double minimumResidual = double.PositiveInfinity;
                double maximumResidual = double.NegativeInfinity;
                double rmsScale = 0.0;
                double rmsSum = 0.0;

                for (int row = roi.Row; row < roi.Row + roi.RowCount; row++)
                {
                cancellationToken.ThrowIfCancellationRequested();
                    for (int column = roi.Column; column < roi.Column + roi.ColumnCount; column++)
                    {
                cancellationToken.ThrowIfCancellationRequested();
                        double z = source.GetHeight(row, column);
                        if (double.IsNaN(z))
                        {
                            continue;
                        }

                        double predicted = (slopeX * source.GetX(column)) + (slopeY * source.GetY(row)) + intercept;
                        double residual = z - predicted;
                        if (!ThreeDInspectionMath.IsFinite(predicted) || !ThreeDInspectionMath.IsFinite(residual))
                        {
                            return Complete(Failure(ThreeDInspectionErrorCode.DegenerateGeometry, "The warpage residual cannot be represented as a finite value.", stopwatch, source, roi));
                        }

                        minimumResidual = Math.Min(minimumResidual, residual);
                        maximumResidual = Math.Max(maximumResidual, residual);
                        AccumulateScaledSquare(Math.Abs(residual), ref rmsScale, ref rmsSum);
                    }
                }

                double peakToValley = maximumResidual - minimumResidual;
                double rms = rmsScale == 0.0
                    ? 0.0
                    : rmsScale * Math.Sqrt(rmsSum / validSampleCount);
                if (!ThreeDInspectionMath.IsFinite(peakToValley) || !ThreeDInspectionMath.IsFinite(rms))
                {
                    return Complete(Failure(ThreeDInspectionErrorCode.DegenerateGeometry, "The warpage metrics cannot be represented as finite values.", stopwatch, source, roi));
                }

                stopwatch.Stop();
                ThreeDInspectionResult result = ThreeDInspectionResult.CreateMeasurement(source, roi, stopwatch.Elapsed);
                result.PlaneFit = new ThreeDPlaneFit(slopeX, slopeY, intercept);
                ThreeDInspectionMath.ApplySampleSummary(result, summary, Options.MinimumValidSamples, Options.MinimumValidCoverageRatio);
                result.Metrics[ThreeDInspectionMetricNames.Warpage.PeakToValley] = peakToValley;
                result.Metrics[ThreeDInspectionMetricNames.Warpage.Rms] = rms;
                result.Metrics[ThreeDInspectionMetricNames.Warpage.MinimumResidual] = minimumResidual;
                result.Metrics[ThreeDInspectionMetricNames.Warpage.MaximumResidual] = maximumResidual;
                result.Metrics[ThreeDInspectionMetricNames.Warpage.MaximumPeakToValley] = Options.MaximumPeakToValley;
                result.Metrics[ThreeDInspectionMetricNames.Warpage.PlaneSlopeX] = slopeX;
                result.Metrics[ThreeDInspectionMetricNames.Warpage.PlaneSlopeY] = slopeY;
                result.Metrics[ThreeDInspectionMetricNames.Warpage.PlaneIntercept] = intercept;
                if (Options.MaximumRms.HasValue)
                {
                    result.Metrics[ThreeDInspectionMetricNames.Warpage.MaximumRms] = Options.MaximumRms.Value;
                }

                result.MetricUnits[ThreeDInspectionMetricNames.Warpage.PeakToValley] = source.HeightUnit;
                result.MetricUnits[ThreeDInspectionMetricNames.Warpage.Rms] = source.HeightUnit;
                result.MetricUnits[ThreeDInspectionMetricNames.Warpage.MinimumResidual] = source.HeightUnit;
                result.MetricUnits[ThreeDInspectionMetricNames.Warpage.MaximumResidual] = source.HeightUnit;
                result.MetricUnits[ThreeDInspectionMetricNames.Warpage.MaximumPeakToValley] = source.HeightUnit;
                result.MetricUnits[ThreeDInspectionMetricNames.Warpage.PlaneSlopeX] = source.HeightUnit + "/" + source.PlanarUnit;
                result.MetricUnits[ThreeDInspectionMetricNames.Warpage.PlaneSlopeY] = source.HeightUnit + "/" + source.PlanarUnit;
                result.MetricUnits[ThreeDInspectionMetricNames.Warpage.PlaneIntercept] = source.HeightUnit;
                if (Options.MaximumRms.HasValue)
                {
                    result.MetricUnits[ThreeDInspectionMetricNames.Warpage.MaximumRms] = source.HeightUnit;
                }

                bool passed = peakToValley <= Options.MaximumPeakToValley
                    && (!Options.MaximumRms.HasValue || rms <= Options.MaximumRms.Value);
                result.Success = passed;
                result.ResultStatus = passed ? ThreeDInspectionResultStatus.Passed : ThreeDInspectionResultStatus.Failed;
                result.Message = passed
                    ? "Warpage residuals are within tolerance."
                    : "Warpage residuals are outside tolerance.";
                return Complete(result);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                return Complete(Failure(
                    ThreeDInspectionErrorCode.ToolExecutionException,
                    "Warpage inspection failed with an unexpected exception.",
                    stopwatch,
                    source,
                    null,
                    exception));
            }
        }

        private static void AccumulateScaledSquare(double absoluteValue, ref double scale, ref double sum)
        {
            if (absoluteValue == 0.0)
            {
                return;
            }

            if (scale < absoluteValue)
            {
                double ratio = scale / absoluteValue;
                sum = 1.0 + (sum * ratio * ratio);
                scale = absoluteValue;
            }
            else
            {
                double ratio = absoluteValue / scale;
                sum += ratio * ratio;
            }
        }

        private static ThreeDInspectionResult Failure(
            ThreeDInspectionErrorCode errorCode,
            string message,
            Stopwatch stopwatch,
            HeightMap3D source = null,
            HeightMapRoi? roi = null,
            Exception exception = null)
        {
            stopwatch.Stop();
            return ThreeDInspectionResult.Failed(errorCode, message, stopwatch.Elapsed, source, roi, exception);
        }
    }
}
