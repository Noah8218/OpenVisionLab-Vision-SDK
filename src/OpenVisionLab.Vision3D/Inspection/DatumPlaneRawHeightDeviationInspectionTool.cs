using OpenVisionLab.Vision3D.Geometry;
using System;
using System.Diagnostics;

namespace OpenVisionLab.Vision3D.Inspection
{
    /// <summary>
    /// Evaluates finite height-map samples against one explicitly supplied
    /// datum plane. It never fits a plane, transforms a map, or infers a
    /// physical unit, datum, calibration, or metrology claim.
    /// </summary>
    public sealed class DatumPlaneRawHeightDeviationInspectionTool : IThreeDInspectionTool
    {
        public DatumPlaneRawHeightDeviationInspectionTool()
            : this(new DatumPlaneRawHeightDeviationInspectionOptions())
        {
        }

        public DatumPlaneRawHeightDeviationInspectionTool(DatumPlaneRawHeightDeviationInspectionOptions options)
        {
            Options = options;
        }

        public string Name => "Datum Plane Raw-Height Deviation";

        public DatumPlaneRawHeightDeviationInspectionOptions Options { get; }

        public ThreeDInspectionResult Execute(HeightMap3D source)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                if (source == null)
                {
                    return Failure(ThreeDInspectionErrorCode.InputHeightMapInvalid, "A height map is required.", stopwatch);
                }

                if (Options == null)
                {
                    return Failure(ThreeDInspectionErrorCode.InvalidParameter, "Datum-plane options are required.", stopwatch, source);
                }

                if (!TryNormalizePlane(out double normalX, out double normalY, out double normalZ, out double offset, out string planeError))
                {
                    return Failure(ThreeDInspectionErrorCode.InvalidParameter, planeError, stopwatch, source);
                }

                if (Math.Abs(normalY) < Options.MinimumAbsoluteNormalY)
                {
                    return Failure(
                        ThreeDInspectionErrorCode.DegenerateGeometry,
                        "Datum plane cannot be represented as raw height over the grid because its normalized Y component is below the configured minimum.",
                        stopwatch,
                        source);
                }

                if (!ThreeDInspectionMath.TryPrepareHeightMap(
                    source,
                    Options.Roi,
                    Options.InputRequirements,
                    Options.MinimumValidSamples,
                    Options.MinimumValidCoverageRatio,
                    true,
                    out HeightMapSampleSummary summary,
                    out ThreeDInspectionErrorCode inputErrorCode,
                    out string inputErrorMessage))
                {
                    HeightMapRoi? failureRoi = summary.TotalSampleCount > 0 ? summary.Roi : Options.Roi;
                    ThreeDInspectionResult failure = Failure(inputErrorCode, inputErrorMessage, stopwatch, source, failureRoi);
                    if (summary.TotalSampleCount > 0)
                    {
                        ThreeDInspectionMath.ApplySampleSummary(failure, summary, Options.MinimumValidSamples, Options.MinimumValidCoverageRatio);
                    }

                    return failure;
                }

                HeightMapRoi roi = summary.Roi;
                long validSampleCount = 0;
                double minimumResidual = double.PositiveInfinity;
                double maximumResidual = double.NegativeInfinity;
                int minimumResidualRow = -1;
                int minimumResidualColumn = -1;
                int maximumResidualRow = -1;
                int maximumResidualColumn = -1;
                double rmsScale = 0.0;
                double rmsSum = 0.0;

                for (int row = roi.Row; row < roi.Row + roi.RowCount; row++)
                {
                    for (int column = roi.Column; column < roi.Column + roi.ColumnCount; column++)
                    {
                        double rawHeight = source.GetHeight(row, column);
                        if (double.IsNaN(rawHeight))
                        {
                            continue;
                        }

                        double gridX = source.GetX(column);
                        double gridY = source.GetY(row);
                        if (!ThreeDInspectionMath.IsFinite(gridX)
                            || !ThreeDInspectionMath.IsFinite(gridY)
                            || !ThreeDInspectionMath.IsFinite(rawHeight))
                        {
                            return Failure(ThreeDInspectionErrorCode.InputHeightMapInvalid, "The height map contains a non-finite coordinate or sample.", stopwatch, source, roi);
                        }

                        if (!TryCalculateRawHeightResidual(
                            normalX,
                            normalY,
                            normalZ,
                            offset,
                            gridX,
                            gridY,
                            rawHeight,
                            out double residual))
                        {
                            return Failure(ThreeDInspectionErrorCode.DegenerateGeometry, "The datum-plane raw-height residual cannot be represented as a finite value.", stopwatch, source, roi);
                        }

                        validSampleCount++;
                        if (residual < minimumResidual)
                        {
                            minimumResidual = residual;
                            minimumResidualRow = row;
                            minimumResidualColumn = column;
                        }
                        if (residual > maximumResidual)
                        {
                            maximumResidual = residual;
                            maximumResidualRow = row;
                            maximumResidualColumn = column;
                        }
                        AccumulateScaledSquare(Math.Abs(residual), ref rmsScale, ref rmsSum);
                    }
                }

                double peakToValley = maximumResidual - minimumResidual;
                double rms = rmsScale == 0.0 ? 0.0 : rmsScale * Math.Sqrt(rmsSum / validSampleCount);
                if (!ThreeDInspectionMath.IsFinite(peakToValley) || !ThreeDInspectionMath.IsFinite(rms))
                {
                    return Failure(ThreeDInspectionErrorCode.DegenerateGeometry, "The datum-plane raw-height metrics cannot be represented as finite values.", stopwatch, source, roi);
                }

                stopwatch.Stop();
                ThreeDInspectionResult result = ThreeDInspectionResult.CreateMeasurement(source, roi, stopwatch.Elapsed);
                ThreeDInspectionMath.ApplySampleSummary(result, summary, Options.MinimumValidSamples, Options.MinimumValidCoverageRatio);
                result.Metrics[ThreeDInspectionMetricNames.DatumPlaneRawHeightDeviation.MinimumRawHeightResidual] = minimumResidual;
                result.Metrics[ThreeDInspectionMetricNames.DatumPlaneRawHeightDeviation.MaximumRawHeightResidual] = maximumResidual;
                result.Metrics[ThreeDInspectionMetricNames.DatumPlaneRawHeightDeviation.MinimumResidualRow] = minimumResidualRow;
                result.Metrics[ThreeDInspectionMetricNames.DatumPlaneRawHeightDeviation.MinimumResidualColumn] = minimumResidualColumn;
                result.Metrics[ThreeDInspectionMetricNames.DatumPlaneRawHeightDeviation.MaximumResidualRow] = maximumResidualRow;
                result.Metrics[ThreeDInspectionMetricNames.DatumPlaneRawHeightDeviation.MaximumResidualColumn] = maximumResidualColumn;
                result.Metrics[ThreeDInspectionMetricNames.DatumPlaneRawHeightDeviation.PeakToValleyRawHeight] = peakToValley;
                result.Metrics[ThreeDInspectionMetricNames.DatumPlaneRawHeightDeviation.RmsRawHeightResidual] = rms;
                result.Metrics[ThreeDInspectionMetricNames.DatumPlaneRawHeightDeviation.MaximumPeakToValleyRawHeight] = Options.MaximumPeakToValleyRawHeight;
                result.Metrics[ThreeDInspectionMetricNames.DatumPlaneRawHeightDeviation.MinimumAbsoluteNormalY] = Options.MinimumAbsoluteNormalY;
                result.Metrics[ThreeDInspectionMetricNames.DatumPlaneRawHeightDeviation.PlaneNormalX] = normalX;
                result.Metrics[ThreeDInspectionMetricNames.DatumPlaneRawHeightDeviation.PlaneNormalY] = normalY;
                result.Metrics[ThreeDInspectionMetricNames.DatumPlaneRawHeightDeviation.PlaneNormalZ] = normalZ;
                result.Metrics[ThreeDInspectionMetricNames.DatumPlaneRawHeightDeviation.PlaneOffset] = offset;
                result.MetricUnits[ThreeDInspectionMetricNames.DatumPlaneRawHeightDeviation.MinimumRawHeightResidual] = source.HeightUnit;
                result.MetricUnits[ThreeDInspectionMetricNames.DatumPlaneRawHeightDeviation.MaximumRawHeightResidual] = source.HeightUnit;
                result.MetricUnits[ThreeDInspectionMetricNames.DatumPlaneRawHeightDeviation.MinimumResidualRow] = "count";
                result.MetricUnits[ThreeDInspectionMetricNames.DatumPlaneRawHeightDeviation.MinimumResidualColumn] = "count";
                result.MetricUnits[ThreeDInspectionMetricNames.DatumPlaneRawHeightDeviation.MaximumResidualRow] = "count";
                result.MetricUnits[ThreeDInspectionMetricNames.DatumPlaneRawHeightDeviation.MaximumResidualColumn] = "count";
                result.MetricUnits[ThreeDInspectionMetricNames.DatumPlaneRawHeightDeviation.PeakToValleyRawHeight] = source.HeightUnit;
                result.MetricUnits[ThreeDInspectionMetricNames.DatumPlaneRawHeightDeviation.RmsRawHeightResidual] = source.HeightUnit;
                result.MetricUnits[ThreeDInspectionMetricNames.DatumPlaneRawHeightDeviation.MaximumPeakToValleyRawHeight] = source.HeightUnit;
                result.MetricUnits[ThreeDInspectionMetricNames.DatumPlaneRawHeightDeviation.MinimumAbsoluteNormalY] = "ratio";
                result.MetricUnits[ThreeDInspectionMetricNames.DatumPlaneRawHeightDeviation.PlaneNormalX] = "ratio";
                result.MetricUnits[ThreeDInspectionMetricNames.DatumPlaneRawHeightDeviation.PlaneNormalY] = "ratio";
                result.MetricUnits[ThreeDInspectionMetricNames.DatumPlaneRawHeightDeviation.PlaneNormalZ] = "ratio";
                result.MetricUnits[ThreeDInspectionMetricNames.DatumPlaneRawHeightDeviation.PlaneOffset] = source.HeightUnit;
                bool passed = peakToValley <= Options.MaximumPeakToValleyRawHeight;
                result.Success = passed;
                result.ResultStatus = passed ? ThreeDInspectionResultStatus.Passed : ThreeDInspectionResultStatus.Failed;
                result.Message = passed
                    ? "Datum-plane raw-height residuals are within the local limit."
                    : "Datum-plane raw-height residuals exceed the local limit.";
                return result;
            }
            catch (Exception exception)
            {
                return Failure(ThreeDInspectionErrorCode.ToolExecutionException, "Datum-plane raw-height deviation failed with an unexpected exception.", stopwatch, source, null, exception);
            }
        }

        /// <summary>
        /// Reuses the same raw-height residual equation for read-only display
        /// sampling. The caller supplies normalized, already-validated plane
        /// coefficients from a completed inspection result.
        /// </summary>
        public static bool TryCalculateRawHeightResidual(
            double normalX,
            double normalY,
            double normalZ,
            double planeOffset,
            double gridX,
            double gridY,
            double rawHeight,
            out double residual)
        {
            residual = double.NaN;
            if (!ThreeDInspectionMath.IsFinite(normalX)
                || !ThreeDInspectionMath.IsFinite(normalY)
                || !ThreeDInspectionMath.IsFinite(normalZ)
                || !ThreeDInspectionMath.IsFinite(planeOffset)
                || !ThreeDInspectionMath.IsFinite(gridX)
                || !ThreeDInspectionMath.IsFinite(gridY)
                || !ThreeDInspectionMath.IsFinite(rawHeight)
                || normalY == 0.0)
            {
                return false;
            }

            // Cancel the common plane normalization before multiplying grid coordinates.
            double expectedRawHeight = -(((normalX / normalY) * gridX) + ((normalZ / normalY) * gridY) + (planeOffset / normalY));
            if (!ThreeDInspectionMath.IsFinite(expectedRawHeight))
            {
                expectedRawHeight = -((normalX * gridX) + (normalZ * gridY) + planeOffset) / normalY;
            }
            residual = rawHeight - expectedRawHeight;
            return ThreeDInspectionMath.IsFinite(expectedRawHeight) && ThreeDInspectionMath.IsFinite(residual);
        }

        private bool TryNormalizePlane(out double normalX, out double normalY, out double normalZ, out double offset, out string message)
        {
            normalX = double.NaN;
            normalY = double.NaN;
            normalZ = double.NaN;
            offset = double.NaN;
            message = string.Empty;
            if (!ThreeDInspectionMath.IsFinite(Options.PlaneNormalX)
                || !ThreeDInspectionMath.IsFinite(Options.PlaneNormalY)
                || !ThreeDInspectionMath.IsFinite(Options.PlaneNormalZ)
                || !ThreeDInspectionMath.IsFinite(Options.PlaneOffset)
                || !ThreeDInspectionMath.IsFinite(Options.MaximumPeakToValleyRawHeight)
                || Options.MaximumPeakToValleyRawHeight <= 0.0
                || Options.MinimumValidSamples < 3
                || !ThreeDInspectionMath.IsFinite(Options.MinimumValidCoverageRatio)
                || Options.MinimumValidCoverageRatio < 0.0
                || Options.MinimumValidCoverageRatio > 1.0
                || !ThreeDInspectionMath.IsFinite(Options.MinimumAbsoluteNormalY)
                || Options.MinimumAbsoluteNormalY <= 0.0
                || Options.MinimumAbsoluteNormalY > 1.0)
            {
                message = "Datum-plane coefficients, limits, or minimum sample policy are invalid.";
                return false;
            }

            double length = Math.Sqrt(
                (Options.PlaneNormalX * Options.PlaneNormalX)
                + (Options.PlaneNormalY * Options.PlaneNormalY)
                + (Options.PlaneNormalZ * Options.PlaneNormalZ));
            if (!ThreeDInspectionMath.IsFinite(length) || length <= 0.0)
            {
                message = "Datum-plane normal must have finite non-zero length.";
                return false;
            }

            normalX = Options.PlaneNormalX / length;
            normalY = Options.PlaneNormalY / length;
            normalZ = Options.PlaneNormalZ / length;
            offset = Options.PlaneOffset / length;
            if (!ThreeDInspectionMath.IsFinite(normalX)
                || !ThreeDInspectionMath.IsFinite(normalY)
                || !ThreeDInspectionMath.IsFinite(normalZ)
                || !ThreeDInspectionMath.IsFinite(offset))
            {
                message = "Datum-plane normalization produced non-finite coefficients.";
                return false;
            }

            return true;
        }

        private static void AccumulateScaledSquare(double absoluteValue, ref double scale, ref double sum)
        {
            if (absoluteValue == 0.0) return;
            if (scale < absoluteValue)
            {
                double ratio = scale / absoluteValue;
                sum = 1.0 + (sum * ratio * ratio);
                scale = absoluteValue;
                return;
            }

            double existingRatio = absoluteValue / scale;
            sum += existingRatio * existingRatio;
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
