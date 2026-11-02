using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    public sealed class ReferenceGridVector
    {
        public ReferenceGridVector(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }
    }

    public sealed class ReferenceGridDefinition
    {
        public ReferenceGridVector Origin { get; set; }
        public ReferenceGridVector UAxis { get; set; }
        public ReferenceGridVector VAxis { get; set; }
        public ReferenceGridVector HAxis { get; set; }
        public double PitchU { get; set; }
        public double PitchV { get; set; }
    }

    public enum ReferenceGridCoordinateMode
    {
        DeclaredFrame = 0,
        ReferenceAxes = 1
    }

    public sealed class ReferenceGridPointReconstructionOptions
    {
        public ReferenceGridCoordinateMode CoordinateMode { get; set; }
        public double MinimumSupportedCoordinate { get; set; } = -float.MaxValue;
        public double MaximumSupportedCoordinate { get; set; } = float.MaxValue;
    }

    public sealed class ReferenceGridPointSample
    {
        internal ReferenceGridPointSample(
            int row,
            int column,
            double height,
            double u,
            double v,
            double x,
            double y,
            double z)
        {
            Row = row;
            Column = column;
            Height = height;
            U = u;
            V = v;
            X = x;
            Y = y;
            Z = z;
        }

        public int Row { get; }
        public int Column { get; }
        public double Height { get; }
        public double U { get; }
        public double V { get; }
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
    }

    public sealed class ReferenceGridPointReconstructionResult
    {
        internal ReferenceGridPointReconstructionResult(
            bool success,
            string message,
            IReadOnlyList<ReferenceGridPointSample> samples)
        {
            Success = success;
            Message = message ?? string.Empty;
            Samples = samples ?? Array.Empty<ReferenceGridPointSample>();
        }

        public bool Success { get; }
        public string Message { get; }
        public IReadOnlyList<ReferenceGridPointSample> Samples { get; }
    }

    /// <summary>
    /// Reconstructs finite row-major height cells into declared reference-
    /// frame XYZ and reference-axis U/H/V coordinates. The caller selects
    /// which coordinate triplet must satisfy its supported range.
    /// </summary>
    public sealed class ReferenceGridPointReconstructionTool
    {
        public ReferenceGridPointReconstructionResult Execute(
            int rowCount,
            int columnCount,
            IReadOnlyList<double> values,
            HeightGridRegion region,
            ReferenceGridDefinition definition,
            ReferenceGridPointReconstructionOptions options,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                HeightMapRegionStatisticsTool.Validate(
                    rowCount,
                    columnCount,
                    values,
                    region);
                Validate(definition, options);
                List<ReferenceGridPointSample> samples =
                    new List<ReferenceGridPointSample>();
                for (int row = region.Row;
                     row < region.Row + region.RowCount;
                     row++)
                {
                    for (int column = region.Column;
                         column < region.Column + region.ColumnCount;
                         column++)
                    {
                        int index = checked(row * columnCount + column);
                        // A narrow, offset ROI may never visit a flat-index checkpoint.
                        cancellationToken.ThrowIfCancellationRequested();

                        double height = values[index];
                        if (!HeightMapRegionStatisticsTool.IsFinite(height))
                        {
                            continue;
                        }

                        double u = (column + 0.5) * definition.PitchU;
                        double v = (row + 0.5) * definition.PitchV;
                        double x = definition.Origin.X
                            + definition.UAxis.X * u
                            + definition.VAxis.X * v
                            + definition.HAxis.X * height;
                        double y = definition.Origin.Y
                            + definition.UAxis.Y * u
                            + definition.VAxis.Y * v
                            + definition.HAxis.Y * height;
                        double z = definition.Origin.Z
                            + definition.UAxis.Z * u
                            + definition.VAxis.Z * v
                            + definition.HAxis.Z * height;
                        bool supported = options.CoordinateMode
                            == ReferenceGridCoordinateMode.DeclaredFrame
                                ? InRange(x, options)
                                  && InRange(y, options)
                                  && InRange(z, options)
                                : InRange(u, options)
                                  && InRange(height, options)
                                  && InRange(v, options);
                        if (!supported)
                        {
                            throw new InvalidDataException(
                                "Reference-grid reconstructed coordinate exceeds the supported range.");
                        }

                        samples.Add(new ReferenceGridPointSample(
                            row,
                            column,
                            height,
                            u,
                            v,
                            x,
                            y,
                            z));
                    }
                }

                var result = new ReferenceGridPointReconstructionResult(
                    true,
                    "Completed deterministic reference-grid point reconstruction.",
                    Array.AsReadOnly(samples.ToArray()));
                cancellationToken.ThrowIfCancellationRequested();
                return result;
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is InvalidDataException
                || exception is OverflowException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ReferenceGridPointReconstructionResult(
                    false,
                    exception.Message,
                    Array.Empty<ReferenceGridPointSample>());
            }
        }

        private static void Validate(
            ReferenceGridDefinition definition,
            ReferenceGridPointReconstructionOptions options)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (!IsFinite(definition.Origin)
                || !IsFinite(definition.UAxis)
                || !IsFinite(definition.VAxis)
                || !IsFinite(definition.HAxis)
                || !HeightMapRegionStatisticsTool.IsFinite(definition.PitchU)
                || !HeightMapRegionStatisticsTool.IsFinite(definition.PitchV)
                || definition.PitchU <= 0.0
                || definition.PitchV <= 0.0)
            {
                throw new InvalidDataException(
                    "Reference-grid reconstruction requires finite vectors and positive finite pitches.");
            }

            if (!HeightMapRegionStatisticsTool.IsFinite(
                    options.MinimumSupportedCoordinate)
                || !HeightMapRegionStatisticsTool.IsFinite(
                    options.MaximumSupportedCoordinate)
                || options.MinimumSupportedCoordinate
                    > options.MaximumSupportedCoordinate)
            {
                throw new InvalidDataException(
                    "Reference-grid reconstruction requires finite ordered coordinate limits.");
            }
        }

        private static bool IsFinite(ReferenceGridVector vector)
        {
            return vector != null
                && HeightMapRegionStatisticsTool.IsFinite(vector.X)
                && HeightMapRegionStatisticsTool.IsFinite(vector.Y)
                && HeightMapRegionStatisticsTool.IsFinite(vector.Z);
        }

        private static bool InRange(
            double value,
            ReferenceGridPointReconstructionOptions options)
        {
            return HeightMapRegionStatisticsTool.IsFinite(value)
                && value >= options.MinimumSupportedCoordinate
                && value <= options.MaximumSupportedCoordinate;
        }
    }
}
