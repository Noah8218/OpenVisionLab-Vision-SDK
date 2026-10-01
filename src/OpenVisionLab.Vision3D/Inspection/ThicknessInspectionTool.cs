using OpenVisionLab.Vision3D.Geometry;
using System;
using System.Diagnostics;

namespace OpenVisionLab.Vision3D.Inspection
{
    /// <summary>
    /// Evaluates declared height-map scalar values as thickness. The caller owns the data meaning,
    /// calibration, unit, and reference-plane definition.
    /// </summary>
    public sealed class ThicknessInspectionTool : IThreeDInspectionTool
    {
        public ThicknessInspectionTool()
            : this(new ThicknessInspectionOptions())
        {
        }

        public ThicknessInspectionTool(ThicknessInspectionOptions options)
        {
            Options = options;
        }

        public string Name => "Thickness";

        public ThicknessInspectionOptions Options { get; }

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
                    return Failure(ThreeDInspectionErrorCode.InvalidParameter, "Thickness options are required.", stopwatch, source);
                }

                if (!ThreeDInspectionMath.IsFinite(Options.MinimumThickness)
                    || !ThreeDInspectionMath.IsFinite(Options.MaximumThickness)
                    || Options.MinimumThickness > Options.MaximumThickness
                    || Options.MinimumValidSamples <= 0
                    || !ThreeDInspectionMath.IsFinite(Options.MinimumValidCoverageRatio)
                    || Options.MinimumValidCoverageRatio < 0.0
                    || Options.MinimumValidCoverageRatio > 1.0)
                {
                    return Failure(ThreeDInspectionErrorCode.InvalidParameter, "Thickness limits or valid-sample policy are invalid.", stopwatch, source);
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
                long belowLowerLimitCount = 0;
                long aboveUpperLimitCount = 0;
                double minimum = double.PositiveInfinity;
                double maximum = double.NegativeInfinity;
                double mean = 0.0;

                for (int row = roi.Row; row < roi.Row + roi.RowCount; row++)
                {
                    for (int column = roi.Column; column < roi.Column + roi.ColumnCount; column++)
                    {
                        double value = source.GetHeight(row, column);
                        if (double.IsNaN(value))
                        {
                            continue;
                        }

                        if (!ThreeDInspectionMath.IsFinite(value))
                        {
                            return Failure(ThreeDInspectionErrorCode.InputHeightMapInvalid, "The height map contains a non-finite sample.", stopwatch, source, roi);
                        }

                        validSampleCount++;
                        mean += (value - mean) / validSampleCount;
                        minimum = Math.Min(minimum, value);
                        maximum = Math.Max(maximum, value);

                        if (value < Options.MinimumThickness)
                        {
                            belowLowerLimitCount++;
                        }

                        if (value > Options.MaximumThickness)
                        {
                            aboveUpperLimitCount++;
                        }
                    }
                }

                double range = maximum - minimum;
                if (!ThreeDInspectionMath.IsFinite(mean) || !ThreeDInspectionMath.IsFinite(range))
                {
                    ThreeDInspectionResult failure = Failure(ThreeDInspectionErrorCode.DegenerateGeometry, "The thickness metrics cannot be represented as finite values.", stopwatch, source, roi);
                    ThreeDInspectionMath.ApplySampleSummary(failure, summary, Options.MinimumValidSamples, Options.MinimumValidCoverageRatio);
                    return failure;
                }

                stopwatch.Stop();
                ThreeDInspectionResult result = ThreeDInspectionResult.CreateMeasurement(source, roi, stopwatch.Elapsed);
                ThreeDInspectionMath.ApplySampleSummary(result, summary, Options.MinimumValidSamples, Options.MinimumValidCoverageRatio);
                result.Metrics[ThreeDInspectionMetricNames.Thickness.Minimum] = minimum;
                result.Metrics[ThreeDInspectionMetricNames.Thickness.Maximum] = maximum;
                result.Metrics[ThreeDInspectionMetricNames.Thickness.Mean] = mean;
                result.Metrics[ThreeDInspectionMetricNames.Thickness.Range] = range;
                result.Metrics[ThreeDInspectionMetricNames.Thickness.LowerLimit] = Options.MinimumThickness;
                result.Metrics[ThreeDInspectionMetricNames.Thickness.UpperLimit] = Options.MaximumThickness;
                result.Metrics[ThreeDInspectionMetricNames.Thickness.BelowLowerLimitCount] = belowLowerLimitCount;
                result.Metrics[ThreeDInspectionMetricNames.Thickness.AboveUpperLimitCount] = aboveUpperLimitCount;
                result.MetricUnits[ThreeDInspectionMetricNames.Thickness.Minimum] = source.HeightUnit;
                result.MetricUnits[ThreeDInspectionMetricNames.Thickness.Maximum] = source.HeightUnit;
                result.MetricUnits[ThreeDInspectionMetricNames.Thickness.Mean] = source.HeightUnit;
                result.MetricUnits[ThreeDInspectionMetricNames.Thickness.Range] = source.HeightUnit;
                result.MetricUnits[ThreeDInspectionMetricNames.Thickness.LowerLimit] = source.HeightUnit;
                result.MetricUnits[ThreeDInspectionMetricNames.Thickness.UpperLimit] = source.HeightUnit;
                result.MetricUnits[ThreeDInspectionMetricNames.Thickness.BelowLowerLimitCount] = "count";
                result.MetricUnits[ThreeDInspectionMetricNames.Thickness.AboveUpperLimitCount] = "count";

                if (belowLowerLimitCount == 0 && aboveUpperLimitCount == 0)
                {
                    result.Success = true;
                    result.ResultStatus = ThreeDInspectionResultStatus.Passed;
                    result.Message = "All valid thickness samples are within tolerance.";
                }
                else
                {
                    result.Success = false;
                    result.ResultStatus = ThreeDInspectionResultStatus.Failed;
                    result.Message = "One or more valid thickness samples are outside tolerance.";
                }

                return result;
            }
            catch (Exception exception)
            {
                return Failure(
                    ThreeDInspectionErrorCode.ToolExecutionException,
                    "Thickness inspection failed with an unexpected exception.",
                    stopwatch,
                    source,
                    null,
                    exception);
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
