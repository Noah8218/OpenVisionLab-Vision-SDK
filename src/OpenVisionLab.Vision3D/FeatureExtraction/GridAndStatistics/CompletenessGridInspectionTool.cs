using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    public sealed class CompletenessGridProfile
    {
        public int Rows { get; set; }
        public int Columns { get; set; }
        public int XPitchColumns { get; set; }
        public int ZPitchRows { get; set; }
        public int CellWidthColumns { get; set; }
        public int CellHeightRows { get; set; }
    }

    public sealed class CompletenessPresencePolicy
    {
        public double MinimumFiniteCoverageRatio { get; set; }
        public double MinimumReferenceRelativeMeanHeight { get; set; }
        public double MaximumReferenceRelativeMeanHeight { get; set; }
    }

    public enum CompletenessCellDecision
    {
        NotEvaluated = 0,
        Pass = 1,
        Fail = 2
    }

    public enum CompletenessCoverageDisposition
    {
        NotEvaluated = 0,
        Accepted = 1,
        BelowMinimum = 2
    }

    public enum CompletenessHeightDisposition
    {
        NotEvaluated = 0,
        Accepted = 1,
        Missing = 2,
        BelowMinimum = 3,
        AboveMaximum = 4
    }

    public sealed class CompletenessGridCellResult
    {
        internal CompletenessGridCellResult(
            int gridRow,
            int gridColumn,
            HeightGridRegion region,
            HeightMapRegionStatisticsResult statistics,
            double referenceMean,
            double? relativeMean,
            CompletenessCellDecision decision,
            CompletenessCoverageDisposition coverageDisposition,
            CompletenessHeightDisposition heightDisposition)
        {
            GridRow = gridRow;
            GridColumn = gridColumn;
            Region = region;
            TotalCellCount = statistics.TotalCellCount;
            FiniteCellCount = statistics.FiniteCellCount;
            MissingCellCount = statistics.MissingCellCount;
            FiniteCoverageRatio = statistics.FiniteCoverageRatio;
            MeanHeight = statistics.HasFiniteSamples
                ? (double?)statistics.Mean
                : null;
            ReferenceMeanHeight = referenceMean;
            ReferenceRelativeMeanHeight = relativeMean;
            Decision = decision;
            CoverageDisposition = coverageDisposition;
            HeightDisposition = heightDisposition;
        }

        public int GridRow { get; }
        public int GridColumn { get; }
        public HeightGridRegion Region { get; }
        public int TotalCellCount { get; }
        public int FiniteCellCount { get; }
        public int MissingCellCount { get; }
        public double FiniteCoverageRatio { get; }
        public double? MeanHeight { get; }
        public double ReferenceMeanHeight { get; }
        public double? ReferenceRelativeMeanHeight { get; }
        public CompletenessCellDecision Decision { get; }
        public CompletenessCoverageDisposition CoverageDisposition { get; }
        public CompletenessHeightDisposition HeightDisposition { get; }
    }

    public sealed class CompletenessGridInspectionResult
    {
        internal CompletenessGridInspectionResult(
            bool success,
            string message,
            int referenceFiniteCellCount,
            double referenceMeanHeight,
            IReadOnlyList<CompletenessGridCellResult> cells,
            int passedCellCount,
            int failedCellCount,
            CompletenessCellDecision aggregateDecision)
        {
            Success = success;
            Message = message ?? string.Empty;
            ReferenceFiniteCellCount = referenceFiniteCellCount;
            ReferenceMeanHeight = referenceMeanHeight;
            Cells = cells ?? Array.Empty<CompletenessGridCellResult>();
            PassedCellCount = passedCellCount;
            FailedCellCount = failedCellCount;
            AggregateDecision = aggregateDecision;
        }

        public bool Success { get; }
        public string Message { get; }
        public int ReferenceFiniteCellCount { get; }
        public double ReferenceMeanHeight { get; }
        public IReadOnlyList<CompletenessGridCellResult> Cells { get; }
        public int PassedCellCount { get; }
        public int FailedCellCount { get; }
        public CompletenessCellDecision AggregateDecision { get; }
    }

    /// <summary>
    /// Computes a deterministic completeness grid from explicit reference and
    /// inspection regions, with an optional exact inspection mask. The result
    /// contains numerical and typed decision evidence but no Studio identity,
    /// recipe, or UI state.
    /// </summary>
    public sealed class CompletenessGridInspectionTool
    {
        private readonly HeightMapRegionStatisticsTool regionStatisticsTool =
            new HeightMapRegionStatisticsTool();

        public CompletenessGridInspectionResult Execute(
            int rowCount,
            int columnCount,
            IReadOnlyList<double> values,
            HeightGridRegion referenceRegion,
            HeightGridRegion inspectionRegion,
            CompletenessGridProfile profile,
            CompletenessPresencePolicy policy = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return ExecuteCore(
                rowCount,
                columnCount,
                values,
                referenceRegion,
                inspectionRegion,
                null,
                false,
                profile,
                policy,
                cancellationToken);
        }

        /// <summary>
        /// Computes Completeness using the exact foreground cells of an
        /// inspection mask. The reference region remains rectangular; only
        /// inspection-cell statistics are restricted by the mask.
        /// </summary>
        public CompletenessGridInspectionResult ExecuteMaskAware(
            int rowCount,
            int columnCount,
            IReadOnlyList<double> values,
            HeightGridRegion referenceRegion,
            HeightGridRegion inspectionRegion,
            HeightGridMask inspectionMask,
            CompletenessGridProfile profile,
            CompletenessPresencePolicy policy = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return ExecuteCore(
                rowCount,
                columnCount,
                values,
                referenceRegion,
                inspectionRegion,
                inspectionMask,
                true,
                profile,
                policy,
                cancellationToken);
        }

        private CompletenessGridInspectionResult ExecuteCore(
            int rowCount,
            int columnCount,
            IReadOnlyList<double> values,
            HeightGridRegion referenceRegion,
            HeightGridRegion inspectionRegion,
            HeightGridMask inspectionMask,
            bool requireMask,
            CompletenessGridProfile profile,
            CompletenessPresencePolicy policy,
            CancellationToken cancellationToken)
        {
            try
            {
                Validate(
                    rowCount,
                    columnCount,
                    values,
                    referenceRegion,
                    inspectionRegion,
                    inspectionMask,
                    requireMask,
                    profile,
                    policy);
                HeightMapRegionStatisticsResult reference =
                    regionStatisticsTool.Execute(
                        rowCount,
                        columnCount,
                        values,
                        referenceRegion,
                        cancellationToken);
                if (!reference.Success)
                {
                    throw new InvalidDataException(reference.Message);
                }

                if (!reference.HasFiniteSamples)
                {
                    throw new InvalidDataException(
                        "Completeness Grid v1 requires at least one finite cell in the explicit Reference ROI.");
                }

                double referenceMean = reference.Mean;
                List<CompletenessGridCellResult> cells =
                    new List<CompletenessGridCellResult>(
                        checked(profile.Rows * profile.Columns));
                int passedCellCount = 0;
                int failedCellCount = 0;
                for (int gridRow = 0; gridRow < profile.Rows; gridRow++)
                {
                    for (int gridColumn = 0;
                         gridColumn < profile.Columns;
                         gridColumn++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        HeightGridRegion region = new HeightGridRegion(
                            inspectionRegion.Row
                                + gridRow * profile.ZPitchRows,
                            inspectionRegion.Column
                                + gridColumn * profile.XPitchColumns,
                            profile.CellHeightRows,
                            profile.CellWidthColumns);
                        HeightMapRegionStatisticsResult statistics =
                            inspectionMask == null
                                ? regionStatisticsTool.Execute(
                                    rowCount,
                                    columnCount,
                                    values,
                                    region,
                                    cancellationToken)
                                : regionStatisticsTool.ExecuteMasked(
                                    rowCount,
                                    columnCount,
                                    values,
                                    region,
                                    inspectionMask,
                                    cancellationToken);
                        if (!statistics.Success)
                        {
                            throw new InvalidDataException(statistics.Message);
                        }

                        double? relative = statistics.HasFiniteSamples
                            ? (double?)(statistics.Mean - referenceMean)
                            : null;
                        CompletenessCellDecision decision =
                            CompletenessCellDecision.NotEvaluated;
                        CompletenessCoverageDisposition coverageDisposition =
                            CompletenessCoverageDisposition.NotEvaluated;
                        CompletenessHeightDisposition heightDisposition =
                            CompletenessHeightDisposition.NotEvaluated;
                        if (policy != null)
                        {
                            coverageDisposition = statistics.FiniteCoverageRatio
                                >= policy.MinimumFiniteCoverageRatio
                                    ? CompletenessCoverageDisposition.Accepted
                                    : CompletenessCoverageDisposition.BelowMinimum;
                            heightDisposition = !relative.HasValue
                                ? CompletenessHeightDisposition.Missing
                                : relative.Value
                                    < policy.MinimumReferenceRelativeMeanHeight
                                        ? CompletenessHeightDisposition.BelowMinimum
                                        : relative.Value
                                            > policy.MaximumReferenceRelativeMeanHeight
                                                ? CompletenessHeightDisposition.AboveMaximum
                                                : CompletenessHeightDisposition.Accepted;
                            decision = coverageDisposition
                                    == CompletenessCoverageDisposition.Accepted
                                && heightDisposition
                                    == CompletenessHeightDisposition.Accepted
                                    ? CompletenessCellDecision.Pass
                                    : CompletenessCellDecision.Fail;
                            if (decision == CompletenessCellDecision.Pass)
                            {
                                passedCellCount++;
                            }
                            else
                            {
                                failedCellCount++;
                            }
                        }

                        cells.Add(new CompletenessGridCellResult(
                            gridRow,
                            gridColumn,
                            region,
                            statistics,
                            referenceMean,
                            relative,
                            decision,
                            coverageDisposition,
                            heightDisposition));
                    }
                }

                CompletenessCellDecision aggregateDecision = policy == null
                    ? CompletenessCellDecision.NotEvaluated
                    : failedCellCount == 0
                        ? CompletenessCellDecision.Pass
                        : CompletenessCellDecision.Fail;
                return new CompletenessGridInspectionResult(
                    true,
                    "Completed deterministic completeness-grid inspection.",
                    reference.FiniteCellCount,
                    referenceMean,
                    Array.AsReadOnly(cells.ToArray()),
                    passedCellCount,
                    failedCellCount,
                    aggregateDecision);
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is InvalidDataException
                || exception is OverflowException)
            {
                return new CompletenessGridInspectionResult(
                    false,
                    exception.Message,
                    0,
                    double.NaN,
                    Array.Empty<CompletenessGridCellResult>(),
                    0,
                    0,
                    CompletenessCellDecision.NotEvaluated);
            }
        }

        private static void Validate(
            int rowCount,
            int columnCount,
            IReadOnlyList<double> values,
            HeightGridRegion referenceRegion,
            HeightGridRegion inspectionRegion,
            HeightGridMask inspectionMask,
            bool requireMask,
            CompletenessGridProfile profile,
            CompletenessPresencePolicy policy)
        {
            HeightMapRegionStatisticsTool.Validate(
                rowCount,
                columnCount,
                values,
                referenceRegion);
            HeightMapRegionStatisticsTool.Validate(
                rowCount,
                columnCount,
                values,
                inspectionRegion);
            if (requireMask && inspectionMask == null)
            {
                throw new ArgumentNullException(nameof(inspectionMask));
            }

            if (inspectionMask != null)
            {
                ValidateInspectionMask(
                    rowCount,
                    columnCount,
                    inspectionRegion,
                    inspectionMask);
            }

            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            if (profile.Rows < 1
                || profile.Columns < 1
                || profile.XPitchColumns < profile.CellWidthColumns
                || profile.ZPitchRows < profile.CellHeightRows
                || profile.CellWidthColumns < 1
                || profile.CellHeightRows < 1)
            {
                throw new InvalidDataException(
                    "Completeness Grid profile requires positive non-overlapping rows, columns, pitch, and cell size.");
            }

            int requiredRows = checked(
                (profile.Rows - 1) * profile.ZPitchRows
                + profile.CellHeightRows);
            int requiredColumns = checked(
                (profile.Columns - 1) * profile.XPitchColumns
                + profile.CellWidthColumns);
            if (requiredRows > inspectionRegion.RowCount
                || requiredColumns > inspectionRegion.ColumnCount)
            {
                throw new InvalidDataException(
                    "Completeness Grid extent "
                    + requiredColumns
                    + " x "
                    + requiredRows
                    + " cells does not fit inside the authored Inspection Grid ROI "
                    + inspectionRegion.ColumnCount
                    + " x "
                    + inspectionRegion.RowCount
                    + ".");
            }

            if (policy != null
                && (!IsFinite(policy.MinimumFiniteCoverageRatio)
                    || !IsFinite(policy.MinimumReferenceRelativeMeanHeight)
                    || !IsFinite(policy.MaximumReferenceRelativeMeanHeight)
                    || policy.MinimumFiniteCoverageRatio < 0.0
                    || policy.MinimumFiniteCoverageRatio > 1.0
                    || policy.MinimumReferenceRelativeMeanHeight
                        > policy.MaximumReferenceRelativeMeanHeight))
            {
                throw new InvalidDataException(
                    "Completeness Grid presence policy requires finite ordered bounds and coverage within [0, 1].");
            }
        }

        private static void ValidateInspectionMask(
            int rowCount,
            int columnCount,
            HeightGridRegion inspectionRegion,
            HeightGridMask inspectionMask)
        {
            HeightMapRegionStatisticsTool.ValidateMask(
                rowCount,
                columnCount,
                inspectionMask);

            int selectedCellCount = 0;
            for (int row = 0; row < rowCount; row++)
            {
                for (int column = 0; column < columnCount; column++)
                {
                    int index = checked(row * columnCount + column);
                    if (!inspectionMask.Foreground[index])
                    {
                        continue;
                    }

                    if (row < inspectionRegion.Row
                        || row >= inspectionRegion.Row + inspectionRegion.RowCount
                        || column < inspectionRegion.Column
                        || column >= inspectionRegion.Column + inspectionRegion.ColumnCount)
                    {
                        throw new InvalidDataException(
                            "Completeness Grid inspection mask contains a selected cell outside the authored Inspection Grid ROI.");
                    }

                    selectedCellCount++;
                }
            }

            if (selectedCellCount == 0)
            {
                throw new InvalidDataException(
                    "Completeness Grid inspection mask requires at least one selected cell inside the authored Inspection Grid ROI.");
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
