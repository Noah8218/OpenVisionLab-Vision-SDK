using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    public enum HeightDifferenceEdgeComparisonAxis
    {
        AcrossColumns,
        AcrossRows
    }

    public enum HeightDifferenceEdgePolarity
    {
        Rising,
        Falling,
        Absolute
    }

    /// <summary>
    /// Source-neutral, zero-based rectangular grid selection.
    /// </summary>
    public sealed class HeightDifferenceEdgeSelection
    {
        public HeightDifferenceEdgeSelection(int row, int column, int rowCount, int columnCount)
        {
            Row = row;
            Column = column;
            RowCount = rowCount;
            ColumnCount = columnCount;
        }

        public int Row { get; }
        public int Column { get; }
        public int RowCount { get; }
        public int ColumnCount { get; }
    }

    public sealed class HeightDifferenceEdgeOptions
    {
        public HeightDifferenceEdgeSelection Selection { get; set; }
        public HeightDifferenceEdgeComparisonAxis ComparisonAxis { get; set; }
        public HeightDifferenceEdgePolarity Polarity { get; set; }
        public double MinimumDelta { get; set; }
    }

    public sealed class HeightDifferenceEdgePoint
    {
        public HeightDifferenceEdgePoint(
            int scanlineIndex,
            int firstRow,
            int firstColumn,
            double firstHeight,
            int secondRow,
            int secondColumn,
            double secondHeight,
            double signedDelta,
            double magnitude)
        {
            ScanlineIndex = scanlineIndex;
            FirstRow = firstRow;
            FirstColumn = firstColumn;
            FirstHeight = firstHeight;
            SecondRow = secondRow;
            SecondColumn = secondColumn;
            SecondHeight = secondHeight;
            SignedDelta = signedDelta;
            Magnitude = magnitude;
        }

        public int ScanlineIndex { get; }
        public int FirstRow { get; }
        public int FirstColumn { get; }
        public double FirstHeight { get; }
        public int SecondRow { get; }
        public int SecondColumn { get; }
        public double SecondHeight { get; }
        public double SignedDelta { get; }
        public double Magnitude { get; }
    }

    public sealed class HeightDifferenceEdgeDiagnostics
    {
        public HeightDifferenceEdgeDiagnostics(
            int scanlineCount,
            long eligiblePairCount,
            long skippedMissingPairCount,
            int acceptedScanlineCount,
            int noCandidateScanlineCount,
            double acceptedMagnitudeMinimum,
            double acceptedMagnitudeMaximum,
            double acceptedMagnitudeMean)
        {
            ScanlineCount = scanlineCount;
            EligiblePairCount = eligiblePairCount;
            SkippedMissingPairCount = skippedMissingPairCount;
            AcceptedScanlineCount = acceptedScanlineCount;
            NoCandidateScanlineCount = noCandidateScanlineCount;
            AcceptedMagnitudeMinimum = acceptedMagnitudeMinimum;
            AcceptedMagnitudeMaximum = acceptedMagnitudeMaximum;
            AcceptedMagnitudeMean = acceptedMagnitudeMean;
        }

        public int ScanlineCount { get; }
        public long EligiblePairCount { get; }
        public long SkippedMissingPairCount { get; }
        public int AcceptedScanlineCount { get; }
        public int NoCandidateScanlineCount { get; }
        public double AcceptedMagnitudeMinimum { get; }
        public double AcceptedMagnitudeMaximum { get; }
        public double AcceptedMagnitudeMean { get; }
    }

    public sealed class HeightDifferenceEdgeResult
    {
        private HeightDifferenceEdgeResult(
            bool success,
            string message,
            IReadOnlyList<HeightDifferenceEdgePoint> points,
            HeightDifferenceEdgeDiagnostics diagnostics)
        {
            Success = success;
            Message = message ?? string.Empty;
            Points = points ?? new HeightDifferenceEdgePoint[0];
            Diagnostics = diagnostics;
        }

        public bool Success { get; }
        public string Message { get; }
        public IReadOnlyList<HeightDifferenceEdgePoint> Points { get; }
        public HeightDifferenceEdgeDiagnostics Diagnostics { get; }

        internal static HeightDifferenceEdgeResult Completed(
            IReadOnlyList<HeightDifferenceEdgePoint> points,
            HeightDifferenceEdgeDiagnostics diagnostics)
        {
            return new HeightDifferenceEdgeResult(
                true,
                "Completed deterministic height-difference edge extraction.",
                points,
                diagnostics);
        }

        internal static HeightDifferenceEdgeResult Failed(string message)
        {
            return new HeightDifferenceEdgeResult(false, message, new HeightDifferenceEdgePoint[0], null);
        }
    }

    /// <summary>
    /// Pure adjacent-height-difference scan and deterministic strongest-per-
    /// scanline selection. It owns no C3D, recipe, UI, calibration, or
    /// acceptance semantics.
    /// </summary>
    public sealed class DeterministicHeightDifferenceEdgeTool
    {
        public HeightDifferenceEdgeResult Execute(
            int rowCount,
            int columnCount,
            IReadOnlyList<double> values,
            HeightDifferenceEdgeOptions options,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                Validate(rowCount, columnCount, values, options);
                HeightDifferenceEdgeSelection selection = options.Selection;
                int scanlineCount = options.ComparisonAxis == HeightDifferenceEdgeComparisonAxis.AcrossColumns
                    ? selection.RowCount
                    : selection.ColumnCount;
                int pairCountPerScanline = options.ComparisonAxis == HeightDifferenceEdgeComparisonAxis.AcrossColumns
                    ? selection.ColumnCount - 1
                    : selection.RowCount - 1;
                List<HeightDifferenceEdgePoint> points = new List<HeightDifferenceEdgePoint>(scanlineCount);
                long eligiblePairCount = 0;
                long skippedMissingPairCount = 0;

                for (int scanlineOffset = 0; scanlineOffset < scanlineCount; scanlineOffset++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Candidate winner = null;
                    for (int pairOffset = 0; pairOffset < pairCountPerScanline; pairOffset++)
                    {
                        int firstRow = options.ComparisonAxis == HeightDifferenceEdgeComparisonAxis.AcrossColumns
                            ? selection.Row + scanlineOffset
                            : selection.Row + pairOffset;
                        int firstColumn = options.ComparisonAxis == HeightDifferenceEdgeComparisonAxis.AcrossColumns
                            ? selection.Column + pairOffset
                            : selection.Column + scanlineOffset;
                        int secondRow = firstRow + (options.ComparisonAxis == HeightDifferenceEdgeComparisonAxis.AcrossRows ? 1 : 0);
                        int secondColumn = firstColumn + (options.ComparisonAxis == HeightDifferenceEdgeComparisonAxis.AcrossColumns ? 1 : 0);
                        double firstHeight = values[(firstRow * columnCount) + firstColumn];
                        double secondHeight = values[(secondRow * columnCount) + secondColumn];
                        if (!IsFinite(firstHeight) || !IsFinite(secondHeight))
                        {
                            skippedMissingPairCount++;
                            continue;
                        }

                        eligiblePairCount++;
                        double delta = secondHeight - firstHeight;
                        double magnitude = Math.Abs(delta);
                        if (!Passes(options.Polarity, delta, options.MinimumDelta)
                            || winner != null && magnitude <= winner.Magnitude)
                        {
                            continue;
                        }

                        winner = new Candidate(
                            firstRow,
                            firstColumn,
                            firstHeight,
                            secondRow,
                            secondColumn,
                            secondHeight,
                            delta,
                            magnitude);
                    }

                    if (winner != null)
                    {
                        points.Add(new HeightDifferenceEdgePoint(
                            options.ComparisonAxis == HeightDifferenceEdgeComparisonAxis.AcrossColumns
                                ? selection.Row + scanlineOffset
                                : selection.Column + scanlineOffset,
                            winner.FirstRow,
                            winner.FirstColumn,
                            winner.FirstHeight,
                            winner.SecondRow,
                            winner.SecondColumn,
                            winner.SecondHeight,
                            winner.SignedDelta,
                            winner.Magnitude));
                    }
                }

                if (points.Count < 2)
                {
                    return HeightDifferenceEdgeResult.Failed(
                        "Height Difference Edge requires at least two accepted scanlines; accepted " + points.Count + " of " + scanlineCount + ".");
                }

                HeightDifferenceEdgeDiagnostics diagnostics = new HeightDifferenceEdgeDiagnostics(
                    scanlineCount,
                    eligiblePairCount,
                    skippedMissingPairCount,
                    points.Count,
                    scanlineCount - points.Count,
                    MinimumMagnitude(points),
                    MaximumMagnitude(points),
                    MeanMagnitude(points));
                return HeightDifferenceEdgeResult.Completed(points, diagnostics);
            }
            catch (ArgumentException exception)
            {
                return HeightDifferenceEdgeResult.Failed(exception.Message);
            }
            catch (InvalidDataException exception)
            {
                return HeightDifferenceEdgeResult.Failed(exception.Message);
            }
            catch (OverflowException exception)
            {
                return HeightDifferenceEdgeResult.Failed(exception.Message);
            }
        }

        private static void Validate(
            int rowCount,
            int columnCount,
            IReadOnlyList<double> values,
            HeightDifferenceEdgeOptions options)
        {
            if (rowCount <= 0 || columnCount <= 0)
            {
                throw new InvalidDataException("Height Difference Edge grid dimensions must be greater than zero.");
            }

            if (values == null || values.Count != checked(rowCount * columnCount))
            {
                throw new InvalidDataException("Height Difference Edge grid values must match the declared dimensions.");
            }

            if (options == null || options.Selection == null)
            {
                throw new ArgumentNullException(nameof(options), "Height Difference Edge options and selection are required.");
            }

            if (!IsFinite(options.MinimumDelta) || options.MinimumDelta <= 0.0)
            {
                throw new InvalidDataException("Height Difference Edge MinimumDelta must be finite and greater than zero.");
            }

            if (options.ComparisonAxis != HeightDifferenceEdgeComparisonAxis.AcrossColumns
                && options.ComparisonAxis != HeightDifferenceEdgeComparisonAxis.AcrossRows)
            {
                throw new InvalidDataException("Unsupported Height Difference Edge comparison axis: " + options.ComparisonAxis + ".");
            }

            if (options.Polarity != HeightDifferenceEdgePolarity.Rising
                && options.Polarity != HeightDifferenceEdgePolarity.Falling
                && options.Polarity != HeightDifferenceEdgePolarity.Absolute)
            {
                throw new InvalidDataException("Unsupported Height Difference Edge polarity: " + options.Polarity + ".");
            }

            HeightDifferenceEdgeSelection selection = options.Selection;
            if (selection.Row < 0 || selection.Column < 0
                || selection.RowCount <= 0 || selection.ColumnCount <= 0
                || selection.Row > rowCount - selection.RowCount
                || selection.Column > columnCount - selection.ColumnCount)
            {
                throw new InvalidDataException("Height Difference Edge selection is outside the input grid.");
            }

            if (options.ComparisonAxis == HeightDifferenceEdgeComparisonAxis.AcrossColumns
                && selection.ColumnCount < 2)
            {
                throw new InvalidDataException("AcrossColumns requires at least two selected columns.");
            }

            if (options.ComparisonAxis == HeightDifferenceEdgeComparisonAxis.AcrossRows
                && selection.RowCount < 2)
            {
                throw new InvalidDataException("AcrossRows requires at least two selected rows.");
            }

            int scanlines = options.ComparisonAxis == HeightDifferenceEdgeComparisonAxis.AcrossColumns
                ? selection.RowCount
                : selection.ColumnCount;
            if (scanlines < 2)
            {
                throw new InvalidDataException("Height Difference Edge requires at least two scanlines in the selection.");
            }
        }

        private static bool Passes(HeightDifferenceEdgePolarity polarity, double delta, double minimumDelta)
        {
            return polarity == HeightDifferenceEdgePolarity.Rising
                ? delta >= minimumDelta
                : polarity == HeightDifferenceEdgePolarity.Falling
                    ? delta <= -minimumDelta
                    : Math.Abs(delta) >= minimumDelta;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static double MinimumMagnitude(IReadOnlyList<HeightDifferenceEdgePoint> points)
        {
            double result = double.PositiveInfinity;
            for (int index = 0; index < points.Count; index++) result = Math.Min(result, points[index].Magnitude);
            return result;
        }

        private static double MaximumMagnitude(IReadOnlyList<HeightDifferenceEdgePoint> points)
        {
            double result = double.NegativeInfinity;
            for (int index = 0; index < points.Count; index++) result = Math.Max(result, points[index].Magnitude);
            return result;
        }

        private static double MeanMagnitude(IReadOnlyList<HeightDifferenceEdgePoint> points)
        {
            double sum = 0.0;
            for (int index = 0; index < points.Count; index++) sum += points[index].Magnitude;
            return sum / points.Count;
        }

        private sealed class Candidate
        {
            public Candidate(
                int firstRow,
                int firstColumn,
                double firstHeight,
                int secondRow,
                int secondColumn,
                double secondHeight,
                double signedDelta,
                double magnitude)
            {
                FirstRow = firstRow;
                FirstColumn = firstColumn;
                FirstHeight = firstHeight;
                SecondRow = secondRow;
                SecondColumn = secondColumn;
                SecondHeight = secondHeight;
                SignedDelta = signedDelta;
                Magnitude = magnitude;
            }

            public int FirstRow { get; }
            public int FirstColumn { get; }
            public double FirstHeight { get; }
            public int SecondRow { get; }
            public int SecondColumn { get; }
            public double SecondHeight { get; }
            public double SignedDelta { get; }
            public double Magnitude { get; }
        }
    }
}
