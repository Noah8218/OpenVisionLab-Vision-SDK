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

    /// <summary>
    /// One polarity- and threshold-eligible adjacent pair considered on a
    /// scanline. PairStartIndex is the zero-based pair offset inside the
    /// selected comparison axis; the endpoint coordinates remain absolute
    /// input-grid coordinates.
    /// </summary>
    public sealed class HeightDifferenceEdgeCandidate
    {
        public HeightDifferenceEdgeCandidate(
            int scanlineIndex,
            int pairStartIndex,
            int firstRow,
            int firstColumn,
            double firstHeight,
            int secondRow,
            int secondColumn,
            double secondHeight,
            double signedDelta,
            double magnitude)
        {
            if (scanlineIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scanlineIndex));
            }
            if (pairStartIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(pairStartIndex));
            }
            if (firstRow < 0 || firstColumn < 0 || secondRow < 0 || secondColumn < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(firstRow), "Height Difference Edge candidate coordinates must be non-negative.");
            }
            bool adjacentAcrossColumns = firstRow == secondRow && secondColumn > firstColumn && secondColumn - firstColumn == 1;
            bool adjacentAcrossRows = firstColumn == secondColumn && secondRow > firstRow && secondRow - firstRow == 1;
            if (!adjacentAcrossColumns && !adjacentAcrossRows)
            {
                throw new ArgumentException("Height Difference Edge candidate endpoints must be one adjacent grid pair.", nameof(secondRow));
            }
            if (scanlineIndex != (adjacentAcrossColumns ? firstRow : firstColumn))
            {
                throw new ArgumentException("Height Difference Edge candidate scanline must match its absolute grid coordinates.", nameof(scanlineIndex));
            }
            if (!IsFinite(firstHeight) || !IsFinite(secondHeight) || !IsFinite(signedDelta) || !IsFinite(magnitude)
                || signedDelta != secondHeight - firstHeight || magnitude < 0.0 || magnitude != Math.Abs(signedDelta))
            {
                throw new ArgumentException("Height Difference Edge candidate values must be finite and internally consistent.", nameof(magnitude));
            }

            ScanlineIndex = scanlineIndex;
            PairStartIndex = pairStartIndex;
            FirstRow = firstRow;
            FirstColumn = firstColumn;
            FirstHeight = firstHeight;
            SecondRow = secondRow;
            SecondColumn = secondColumn;
            SecondHeight = secondHeight;
            SignedDelta = signedDelta;
            Magnitude = magnitude;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        public int ScanlineIndex { get; }
        public int PairStartIndex { get; }
        public int FirstRow { get; }
        public int FirstColumn { get; }
        public double FirstHeight { get; }
        public int SecondRow { get; }
        public int SecondColumn { get; }
        public double SecondHeight { get; }
        public double SignedDelta { get; }
        public double Magnitude { get; }
    }

    /// <summary>
    /// Bounded per-scanline evidence for the complete adjacent-pair search.
    /// A complete scanline with one eligible candidate has no competitor;
    /// null candidates on an incomplete search are not treated as proof.
    /// </summary>
    public sealed class HeightDifferenceEdgeScanlineDiagnostics
    {
        public HeightDifferenceEdgeScanlineDiagnostics(
            int scanlineIndex,
            bool searchComplete,
            int eligibleCandidateCount,
            HeightDifferenceEdgeCandidate topCandidate,
            HeightDifferenceEdgeCandidate secondCandidate)
        {
            if (scanlineIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scanlineIndex));
            }
            if (eligibleCandidateCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(eligibleCandidateCount));
            }
            if (!searchComplete && topCandidate != null)
            {
                throw new ArgumentException("Incomplete scanline evidence cannot expose a top candidate.", nameof(topCandidate));
            }
            if (secondCandidate != null && topCandidate == null)
            {
                throw new ArgumentException("A second candidate requires a top candidate.", nameof(secondCandidate));
            }
            if (secondCandidate != null && secondCandidate.PairStartIndex == topCandidate.PairStartIndex)
            {
                throw new ArgumentException("Top and second candidates must be distinct adjacent pairs.", nameof(secondCandidate));
            }
            if (topCandidate != null && topCandidate.ScanlineIndex != scanlineIndex)
            {
                throw new ArgumentException("Top candidate scanline identity does not match the diagnostics.", nameof(topCandidate));
            }
            if (secondCandidate != null && secondCandidate.ScanlineIndex != scanlineIndex)
            {
                throw new ArgumentException("Second candidate scanline identity does not match the diagnostics.", nameof(secondCandidate));
            }
            if (secondCandidate != null && topCandidate.Magnitude < secondCandidate.Magnitude)
            {
                throw new ArgumentException("Top candidate magnitude must not be below the second candidate magnitude.", nameof(secondCandidate));
            }
            if (topCandidate != null && eligibleCandidateCount == 0)
            {
                throw new ArgumentException("A top candidate requires an eligible candidate count.", nameof(topCandidate));
            }
            if (secondCandidate != null && eligibleCandidateCount < 2)
            {
                throw new ArgumentException("A second candidate requires at least two eligible candidates.", nameof(secondCandidate));
            }
            if (searchComplete && eligibleCandidateCount > 0 && topCandidate == null)
            {
                throw new ArgumentException("An eligible candidate count requires a top candidate.", nameof(topCandidate));
            }
            if (searchComplete && eligibleCandidateCount > 1 && secondCandidate == null)
            {
                throw new ArgumentException("A complete scanline with multiple candidates requires a second candidate.", nameof(secondCandidate));
            }
            if (topCandidate != null && secondCandidate != null
                && topCandidate.Magnitude == secondCandidate.Magnitude
                && topCandidate.PairStartIndex > secondCandidate.PairStartIndex)
            {
                throw new ArgumentException("Equal-score candidates must be ordered by ascending pair start index.", nameof(secondCandidate));
            }

            ScanlineIndex = scanlineIndex;
            SearchComplete = searchComplete;
            EligibleCandidateCount = eligibleCandidateCount;
            TopCandidate = topCandidate;
            SecondCandidate = secondCandidate;
        }

        public int ScanlineIndex { get; }
        public bool SearchComplete { get; }
        public int EligibleCandidateCount { get; }
        public HeightDifferenceEdgeCandidate TopCandidate { get; }
        public HeightDifferenceEdgeCandidate SecondCandidate { get; }
        public bool HasNumericScoreDifference => SearchComplete && TopCandidate != null && SecondCandidate != null;
        public double ScoreDifference => HasNumericScoreDifference ? TopCandidate.Magnitude - SecondCandidate.Magnitude : double.NaN;
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
            Scanlines = Array.Empty<HeightDifferenceEdgeScanlineDiagnostics>();
        }

        public HeightDifferenceEdgeDiagnostics(
            int scanlineCount,
            long eligiblePairCount,
            long skippedMissingPairCount,
            int acceptedScanlineCount,
            int noCandidateScanlineCount,
            double acceptedMagnitudeMinimum,
            double acceptedMagnitudeMaximum,
            double acceptedMagnitudeMean,
            IReadOnlyList<HeightDifferenceEdgeScanlineDiagnostics> scanlines)
            : this(
                scanlineCount,
                eligiblePairCount,
                skippedMissingPairCount,
                acceptedScanlineCount,
                noCandidateScanlineCount,
                acceptedMagnitudeMinimum,
                acceptedMagnitudeMaximum,
                acceptedMagnitudeMean)
        {
            if (scanlineCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scanlineCount));
            }
            if (eligiblePairCount < 0 || skippedMissingPairCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(eligiblePairCount));
            }
            if (acceptedScanlineCount < 0 || noCandidateScanlineCount < 0 || (long)acceptedScanlineCount + noCandidateScanlineCount != scanlineCount)
            {
                throw new ArgumentOutOfRangeException(nameof(acceptedScanlineCount), "Accepted and no-candidate scanline counts must partition the scanline count.");
            }
            if (scanlines == null)
            {
                throw new ArgumentNullException(nameof(scanlines));
            }
            if (scanlines.Count != 0 && scanlines.Count != scanlineCount)
            {
                throw new ArgumentException("Per-scanline diagnostics must cover every scanline or be omitted for legacy construction.", nameof(scanlines));
            }

            Scanlines = CopyScanlines(scanlines);
            if (Scanlines.Count > 0)
            {
                int acceptedCount = 0;
                long candidateCount = 0;
                for (int index = 0; index < Scanlines.Count; index++)
                {
                    if (Scanlines[index].TopCandidate != null) acceptedCount++;
                    candidateCount += Scanlines[index].EligibleCandidateCount;
                }
                if (acceptedCount != acceptedScanlineCount || candidateCount > eligiblePairCount)
                {
                    throw new ArgumentException("Per-scanline evidence must agree with the aggregate accepted and eligible counts.", nameof(scanlines));
                }
            }
        }

        public int ScanlineCount { get; }
        public long EligiblePairCount { get; }
        public long SkippedMissingPairCount { get; }
        public int AcceptedScanlineCount { get; }
        public int NoCandidateScanlineCount { get; }
        public double AcceptedMagnitudeMinimum { get; }
        public double AcceptedMagnitudeMaximum { get; }
        public double AcceptedMagnitudeMean { get; }
        public IReadOnlyList<HeightDifferenceEdgeScanlineDiagnostics> Scanlines { get; }

        private static IReadOnlyList<HeightDifferenceEdgeScanlineDiagnostics> CopyScanlines(
            IReadOnlyList<HeightDifferenceEdgeScanlineDiagnostics> source)
        {
            if (source.Count == 0)
            {
                return Array.Empty<HeightDifferenceEdgeScanlineDiagnostics>();
            }

            HeightDifferenceEdgeScanlineDiagnostics[] copy = new HeightDifferenceEdgeScanlineDiagnostics[source.Count];
            for (int index = 0; index < source.Count; index++)
            {
                copy[index] = source[index] ?? throw new ArgumentException("Per-scanline diagnostics cannot contain null entries.", nameof(source));
                if (index > 0 && copy[index].ScanlineIndex <= copy[index - 1].ScanlineIndex)
                {
                    throw new ArgumentException("Per-scanline diagnostics must be strictly ordered by scanline index.", nameof(source));
                }
            }

            return Array.AsReadOnly(copy);
        }
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
            Points = points ?? Array.Empty<HeightDifferenceEdgePoint>();
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
            return new HeightDifferenceEdgeResult(false, message, Array.Empty<HeightDifferenceEdgePoint>(), null);
        }
    }

    /// <summary>
    /// Pure adjacent-height-difference scan with deterministic strongest-per-
    /// scanline selection and bounded top-two candidate evidence. It owns no
    /// C3D, recipe, UI, calibration, or acceptance semantics.
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
                List<HeightDifferenceEdgeScanlineDiagnostics> scanlines = new List<HeightDifferenceEdgeScanlineDiagnostics>(scanlineCount);
                long eligiblePairCount = 0;
                long skippedMissingPairCount = 0;

                for (int scanlineOffset = 0; scanlineOffset < scanlineCount; scanlineOffset++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Candidate winner = null;
                    Candidate second = null;
                    int eligibleCandidateCount = 0;
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
                        if (!IsFinite(delta) || !IsFinite(magnitude))
                        {
                            throw new InvalidDataException("Height Difference Edge candidate score must be finite.");
                        }
                        if (!Passes(options.Polarity, delta, options.MinimumDelta))
                        {
                            continue;
                        }

                        eligibleCandidateCount++;
                        Candidate candidate = new Candidate(
                            options.ComparisonAxis == HeightDifferenceEdgeComparisonAxis.AcrossColumns
                                ? selection.Row + scanlineOffset
                                : selection.Column + scanlineOffset,
                            pairOffset,
                            firstRow,
                            firstColumn,
                            firstHeight,
                            secondRow,
                            secondColumn,
                            secondHeight,
                            delta,
                            magnitude);
                        if (IsBetter(candidate, winner))
                        {
                            second = winner;
                            winner = candidate;
                        }
                        else if (IsBetter(candidate, second))
                        {
                            second = candidate;
                        }
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

                    int scanlineIndex = options.ComparisonAxis == HeightDifferenceEdgeComparisonAxis.AcrossColumns
                        ? selection.Row + scanlineOffset
                        : selection.Column + scanlineOffset;
                    scanlines.Add(new HeightDifferenceEdgeScanlineDiagnostics(
                        scanlineIndex,
                        true,
                        eligibleCandidateCount,
                        winner == null ? null : ToCandidate(winner),
                        second == null ? null : ToCandidate(second)));
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
                    MeanMagnitude(points),
                    scanlines);
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

        private static bool IsBetter(Candidate candidate, Candidate current)
        {
            return current == null
                || candidate.Magnitude > current.Magnitude
                || candidate.Magnitude == current.Magnitude && candidate.PairStartIndex < current.PairStartIndex;
        }

        private static HeightDifferenceEdgeCandidate ToCandidate(Candidate candidate)
        {
            return new HeightDifferenceEdgeCandidate(
                candidate.ScanlineIndex,
                candidate.PairStartIndex,
                candidate.FirstRow,
                candidate.FirstColumn,
                candidate.FirstHeight,
                candidate.SecondRow,
                candidate.SecondColumn,
                candidate.SecondHeight,
                candidate.SignedDelta,
                candidate.Magnitude);
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
            if (IsFinite(sum)) return sum / points.Count;

            double mean = points[0].Magnitude;
            for (int index = 1; index < points.Count; index++) mean += (points[index].Magnitude - mean) / (index + 1);
            return mean;
        }

        private sealed class Candidate
        {
            public Candidate(
                int scanlineIndex,
                int pairStartIndex,
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
                PairStartIndex = pairStartIndex;
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
            public int PairStartIndex { get; }
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
