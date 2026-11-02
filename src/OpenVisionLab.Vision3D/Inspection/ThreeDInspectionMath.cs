using System.Threading;
using System;
using OpenVisionLab.Vision3D.Geometry;

namespace OpenVisionLab.Vision3D.Inspection
{
    internal readonly struct HeightMapSampleSummary
    {
        public HeightMapSampleSummary(HeightMapRoi roi, long totalSampleCount, long validSampleCount, long missingSampleCount)
        {
            Roi = roi;
            TotalSampleCount = totalSampleCount;
            ValidSampleCount = validSampleCount;
            MissingSampleCount = missingSampleCount;
        }

        public HeightMapRoi Roi { get; }

        public long TotalSampleCount { get; }

        public long ValidSampleCount { get; }

        public long MissingSampleCount { get; }

        public double ValidCoverageRatio => TotalSampleCount == 0 ? 0.0 : (double)ValidSampleCount / TotalSampleCount;
    }

    internal static class ThreeDInspectionMath
    {
        public static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        public static HeightMapRoi ResolveRoi(HeightMap3D source, HeightMapRoi? configuredRoi)
        {
            return configuredRoi ?? HeightMapRoi.Full(source);
        }

        public static bool TryPrepareHeightMap(
            HeightMap3D source,
            HeightMapRoi? configuredRoi,
            HeightMapInputRequirements requirements,
            int minimumValidSamples,
            double minimumValidCoverageRatio,
            bool requireMatchingPlanarAndHeightUnits,
            out HeightMapSampleSummary summary,
            out ThreeDInspectionErrorCode errorCode,
            out string message, CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool Complete(bool value) { cancellationToken.ThrowIfCancellationRequested(); return value; }
            summary = default(HeightMapSampleSummary);
            errorCode = ThreeDInspectionErrorCode.None;
            message = string.Empty;

            if (source == null)
            {
                errorCode = ThreeDInspectionErrorCode.InputHeightMapInvalid;
                message = "A height map is required.";
                return Complete(false);
            }

            if (requirements != null
                && (!string.Equals(source.PlanarUnit, requirements.PlanarUnit, StringComparison.Ordinal)
                    || !string.Equals(source.HeightUnit, requirements.HeightUnit, StringComparison.Ordinal)
                    || !string.Equals(source.FrameId, requirements.FrameId, StringComparison.Ordinal)))
            {
                errorCode = ThreeDInspectionErrorCode.InputContractMismatch;
                message = "Height-map input contract mismatch. Expected planar unit '"
                    + requirements.PlanarUnit
                    + "', height unit '"
                    + requirements.HeightUnit
                    + "', and frame '"
                    + requirements.FrameId
                    + "'; actual planar unit '"
                    + source.PlanarUnit
                    + "', height unit '"
                    + source.HeightUnit
                    + "', and frame '"
                    + source.FrameId
                    + "'.";
                return Complete(false);
            }

            if (requireMatchingPlanarAndHeightUnits
                && !string.Equals(source.PlanarUnit, source.HeightUnit, StringComparison.Ordinal))
            {
                errorCode = ThreeDInspectionErrorCode.InputContractMismatch;
                message = "This inspection requires identical planar and height units because it evaluates one Euclidean plane equation. Actual planar unit is '"
                    + source.PlanarUnit
                    + "' and height unit is '"
                    + source.HeightUnit
                    + "'.";
                return Complete(false);
            }

            HeightMapRoi roi = ResolveRoi(source, configuredRoi);
            if (!roi.IsValidFor(source))
            {
                errorCode = ThreeDInspectionErrorCode.InvalidRoi;
                message = "The inspection ROI is outside the height map.";
                return Complete(false);
            }

            long totalSampleCount = (long)roi.RowCount * roi.ColumnCount;
            long validSampleCount = 0;
            long missingSampleCount = 0;
            for (int row = roi.Row; row < roi.Row + roi.RowCount; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int column = roi.Column; column < roi.Column + roi.ColumnCount; column++)
                {
                cancellationToken.ThrowIfCancellationRequested();
                    double value = source.GetHeight(row, column);
                    if (double.IsNaN(value))
                    {
                        missingSampleCount++;
                    }
                    else if (double.IsInfinity(value))
                    {
                        errorCode = ThreeDInspectionErrorCode.InputHeightMapInvalid;
                        message = "The height map contains an infinite sample.";
                        return Complete(false);
                    }
                    else
                    {
                        validSampleCount++;
                    }
                }
            }

            summary = new HeightMapSampleSummary(roi, totalSampleCount, validSampleCount, missingSampleCount);
            if (validSampleCount < minimumValidSamples)
            {
                errorCode = ThreeDInspectionErrorCode.InsufficientValidSamples;
                message = "The inspection ROI does not contain enough finite samples.";
                return Complete(false);
            }

            if (summary.ValidCoverageRatio < minimumValidCoverageRatio)
            {
                errorCode = ThreeDInspectionErrorCode.InsufficientValidCoverage;
                message = "The inspection ROI does not meet the minimum valid coverage ratio.";
                return Complete(false);
            }

            return Complete(true);
        }

        public static void ApplySampleSummary(
            ThreeDInspectionResult result,
            HeightMapSampleSummary summary,
            int minimumValidSamples,
            double minimumValidCoverageRatio)
        {
            result.TotalSampleCount = summary.TotalSampleCount;
            result.ValidSampleCount = summary.ValidSampleCount;
            result.MissingSampleCount = summary.MissingSampleCount;
            result.ValidCoverageRatio = summary.ValidCoverageRatio;
            result.MinimumValidSamples = minimumValidSamples;
            result.MinimumValidCoverageRatio = minimumValidCoverageRatio;
            result.Metrics[ThreeDInspectionMetricNames.Quality.TotalSampleCount] = summary.TotalSampleCount;
            result.Metrics[ThreeDInspectionMetricNames.Quality.ValidSampleCount] = summary.ValidSampleCount;
            result.Metrics[ThreeDInspectionMetricNames.Quality.MissingSampleCount] = summary.MissingSampleCount;
            result.Metrics[ThreeDInspectionMetricNames.Quality.ValidCoverageRatio] = summary.ValidCoverageRatio;
            result.Metrics[ThreeDInspectionMetricNames.Quality.MinimumValidSamples] = minimumValidSamples;
            result.Metrics[ThreeDInspectionMetricNames.Quality.MinimumValidCoverageRatio] = minimumValidCoverageRatio;
            result.MetricUnits[ThreeDInspectionMetricNames.Quality.TotalSampleCount] = "count";
            result.MetricUnits[ThreeDInspectionMetricNames.Quality.ValidSampleCount] = "count";
            result.MetricUnits[ThreeDInspectionMetricNames.Quality.MissingSampleCount] = "count";
            result.MetricUnits[ThreeDInspectionMetricNames.Quality.ValidCoverageRatio] = "ratio";
            result.MetricUnits[ThreeDInspectionMetricNames.Quality.MinimumValidSamples] = "count";
            result.MetricUnits[ThreeDInspectionMetricNames.Quality.MinimumValidCoverageRatio] = "ratio";
        }
    }
}
