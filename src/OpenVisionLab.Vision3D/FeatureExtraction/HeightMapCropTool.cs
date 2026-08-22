using System;
using System.Threading;
using OpenVisionLab.Vision3D.Geometry;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    /// <summary>
    /// Controlled result from extracting one rectangular height-map region.
    /// </summary>
    public sealed class HeightMapCropResult
    {
        internal HeightMapCropResult(
            bool success,
            string message,
            HeightMapRoi sourceRoi,
            HeightMap3D output,
            int validSampleCount,
            int missingSampleCount)
        {
            Success = success;
            Message = message ?? string.Empty;
            SourceRoi = sourceRoi;
            Output = output;
            ValidSampleCount = validSampleCount;
            MissingSampleCount = missingSampleCount;
        }

        public bool Success { get; }

        public string Message { get; }

        public HeightMapRoi SourceRoi { get; }

        public HeightMap3D Output { get; }

        public int ValidSampleCount { get; }

        public int MissingSampleCount { get; }
    }

    /// <summary>
    /// Extracts one inclusive-start, exclusive-extent rectangular region from an immutable
    /// height map. Values and missing samples are copied in row-major order. The output keeps
    /// the source frame, units, pitches, and source identity while advancing its planar origin.
    /// </summary>
    public sealed class HeightMapCropTool
    {
        public HeightMapCropResult Execute(
            HeightMap3D source,
            HeightMapRoi roi,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (source == null)
            {
                return Error(roi, "A source height map is required.");
            }

            if (!roi.IsValidFor(source))
            {
                return Error(roi, "The crop ROI must have positive dimensions and remain inside the source height map.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            double[] values = new double[checked(roi.RowCount * roi.ColumnCount)];
            int valid = 0;
            int missing = 0;
            for (int outputRow = 0; outputRow < roi.RowCount; outputRow++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int sourceRow = roi.Row + outputRow;
                for (int outputColumn = 0; outputColumn < roi.ColumnCount; outputColumn++)
                {
                    double value = source.GetHeight(sourceRow, roi.Column + outputColumn);
                    values[(outputRow * roi.ColumnCount) + outputColumn] = value;
                    if (double.IsNaN(value))
                    {
                        missing++;
                    }
                    else
                    {
                        valid++;
                    }
                }
            }

            HeightMap3D output = new HeightMap3D(
                roi.RowCount,
                roi.ColumnCount,
                source.GetX(roi.Column),
                source.GetY(roi.Row),
                source.ColumnPitch,
                source.RowPitch,
                values,
                source.PlanarUnit,
                source.HeightUnit,
                source.FrameId,
                source.SourceId);
            return new HeightMapCropResult(
                true,
                "The selected height-map region was copied without interpolation or missing-value replacement.",
                roi,
                output,
                valid,
                missing);
        }

        private static HeightMapCropResult Error(HeightMapRoi roi, string message)
        {
            return new HeightMapCropResult(false, message, roi, null, 0, 0);
        }
    }
}
