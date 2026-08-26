using System;
using System.IO;
using System.Threading;
using OpenVisionLab.Vision3D.Geometry;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    /// <summary>
    /// Controlled result from reducing an immutable height map to the
    /// foreground cells of an exact source-grid mask.
    /// </summary>
    public sealed class HeightMapDomainMaskResult
    {
        internal HeightMapDomainMaskResult(
            bool success,
            string message,
            HeightMap3D output,
            int foregroundCellCount,
            int preservedValidSampleCount,
            int preservedMissingSampleCount,
            int reducedToMissingCellCount)
        {
            Success = success;
            Message = message ?? string.Empty;
            Output = output;
            ForegroundCellCount = foregroundCellCount;
            PreservedValidSampleCount = preservedValidSampleCount;
            PreservedMissingSampleCount = preservedMissingSampleCount;
            ReducedToMissingCellCount = reducedToMissingCellCount;
        }

        public bool Success { get; }

        public string Message { get; }

        public HeightMap3D Output { get; }

        public int ForegroundCellCount { get; }

        public int PreservedValidSampleCount { get; }

        public int PreservedMissingSampleCount { get; }

        public int ReducedToMissingCellCount { get; }
    }

    /// <summary>
    /// Keeps source values at foreground cells and reduces background cells to
    /// NaN on an immutable same-grid height map. The tool owns no file format,
    /// source artifact, recipe, or acceptance policy.
    /// </summary>
    public sealed class HeightMapDomainMaskTool
    {
        public HeightMapDomainMaskResult Execute(
            HeightMap3D source,
            HeightGridMask mask,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                Validate(source, mask);
                cancellationToken.ThrowIfCancellationRequested();

                double[] sourceValues = source.CopyValues();
                double[] outputValues = new double[sourceValues.Length];
                int foregroundCellCount = 0;
                int preservedValidSampleCount = 0;
                int preservedMissingSampleCount = 0;
                int reducedToMissingCellCount = 0;
                for (int index = 0; index < sourceValues.Length; index++)
                {
                    if ((index & 0x3fff) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    if (mask.Foreground[index])
                    {
                        double value = sourceValues[index];
                        outputValues[index] = value;
                        foregroundCellCount++;
                        if (double.IsNaN(value))
                        {
                            preservedMissingSampleCount++;
                        }
                        else
                        {
                            preservedValidSampleCount++;
                        }
                    }
                    else
                    {
                        outputValues[index] = double.NaN;
                        if (!double.IsNaN(sourceValues[index]))
                        {
                            reducedToMissingCellCount++;
                        }
                    }
                }

                HeightMap3D output = new HeightMap3D(
                    source.Rows,
                    source.Columns,
                    source.OriginX,
                    source.OriginY,
                    source.ColumnPitch,
                    source.RowPitch,
                    outputValues,
                    source.PlanarUnit,
                    source.HeightUnit,
                    source.FrameId,
                    source.SourceId);
                return new HeightMapDomainMaskResult(
                    true,
                    "The source height map was reduced to the exact foreground domain without interpolation or missing-value replacement.",
                    output,
                    foregroundCellCount,
                    preservedValidSampleCount,
                    preservedMissingSampleCount,
                    reducedToMissingCellCount);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ArgumentException exception)
            {
                return Error(exception.Message);
            }
            catch (InvalidDataException exception)
            {
                return Error(exception.Message);
            }
            catch (OverflowException exception)
            {
                return Error(exception.Message);
            }
        }

        private static void Validate(HeightMap3D source, HeightGridMask mask)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (mask == null)
            {
                throw new ArgumentNullException(nameof(mask));
            }

            if (mask.RowCount != source.Rows || mask.ColumnCount != source.Columns)
            {
                throw new InvalidDataException(
                    "The domain mask dimensions must match the source height map.");
            }

            int expectedCellCount = checked(source.Rows * source.Columns);
            if (mask.Foreground == null || mask.Foreground.Count != expectedCellCount)
            {
                throw new InvalidDataException(
                    "The domain mask values must match the source height-map dimensions.");
            }

            bool hasForeground = false;
            for (int index = 0; index < mask.Foreground.Count; index++)
            {
                if (mask.Foreground[index])
                {
                    hasForeground = true;
                    break;
                }
            }

            if (!hasForeground)
            {
                throw new InvalidDataException(
                    "The domain mask must contain at least one foreground cell.");
            }
        }

        private static HeightMapDomainMaskResult Error(string message)
        {
            return new HeightMapDomainMaskResult(
                false,
                message,
                null,
                0,
                0,
                0,
                0);
        }
    }
}
