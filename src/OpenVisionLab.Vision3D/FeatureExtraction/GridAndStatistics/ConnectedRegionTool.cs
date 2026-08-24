using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    public enum ConnectedRegionConnectivity
    {
        Four = 4,
        Eight = 8
    }

    public struct HeightGridCell
    {
        public HeightGridCell(int row, int column)
        {
            Row = row;
            Column = column;
        }

        public int Row { get; }
        public int Column { get; }
    }

    /// <summary>
    /// Source-neutral row-major binary mask. The mask does not own source
    /// identity, units, frame, or acceptance policy.
    /// </summary>
    public sealed class HeightGridMask
    {
        public HeightGridMask(
            int rowCount,
            int columnCount,
            IReadOnlyList<bool> foreground)
        {
            RowCount = rowCount;
            ColumnCount = columnCount;
            Foreground = foreground;
        }

        public int RowCount { get; }
        public int ColumnCount { get; }
        public IReadOnlyList<bool> Foreground { get; }
    }

    public sealed class ConnectedRegionOptions
    {
        public ConnectedRegionConnectivity Connectivity { get; set; } = ConnectedRegionConnectivity.Four;
    }

    public sealed class ConnectedRegion
    {
        internal ConnectedRegion(
            int index,
            int seedRow,
            int seedColumn,
            IReadOnlyList<HeightGridCell> cells,
            int minimumRow,
            int minimumColumn,
            int maximumRow,
            int maximumColumn)
        {
            Index = index;
            SeedRow = seedRow;
            SeedColumn = seedColumn;
            Cells = cells;
            MinimumRow = minimumRow;
            MinimumColumn = minimumColumn;
            MaximumRow = maximumRow;
            MaximumColumn = maximumColumn;
        }

        public int Index { get; }
        public int SeedRow { get; }
        public int SeedColumn { get; }
        public IReadOnlyList<HeightGridCell> Cells { get; }
        public int CellCount => Cells.Count;
        public int MinimumRow { get; }
        public int MinimumColumn { get; }
        public int MaximumRow { get; }
        public int MaximumColumn { get; }
    }

    public sealed class ConnectedRegionResult
    {
        private ConnectedRegionResult(
            bool success,
            string message,
            IReadOnlyList<ConnectedRegion> regions,
            int foregroundCellCount,
            int visitedCellCount)
        {
            Success = success;
            Message = message ?? string.Empty;
            Regions = regions ?? new ConnectedRegion[0];
            ForegroundCellCount = foregroundCellCount;
            VisitedCellCount = visitedCellCount;
        }

        public bool Success { get; }
        public string Message { get; }
        public IReadOnlyList<ConnectedRegion> Regions { get; }
        public int RegionCount => Regions.Count;
        public int ForegroundCellCount { get; }
        public int VisitedCellCount { get; }

        internal static ConnectedRegionResult Completed(
            IReadOnlyList<ConnectedRegion> regions,
            int foregroundCellCount)
        {
            return new ConnectedRegionResult(
                true,
                "Completed deterministic connected-region labeling.",
                regions,
                foregroundCellCount,
                foregroundCellCount);
        }

        internal static ConnectedRegionResult Failed(string message)
        {
            return new ConnectedRegionResult(
                false,
                message,
                new ConnectedRegion[0],
                0,
                0);
        }
    }

    /// <summary>
    /// Labels foreground cells in a binary height-grid mask. Regions are
    /// discovered by row-major seed order and their cells are returned in
    /// row-major order. The tool owns no source identity, geometry artifact,
    /// recipe, UI, or acceptance semantics.
    /// </summary>
    public sealed class ConnectedRegionTool
    {
        private static readonly int[,] FourNeighborOffsets =
        {
            { -1, 0 },
            { 0, -1 },
            { 0, 1 },
            { 1, 0 }
        };

        private static readonly int[,] EightNeighborOffsets =
        {
            { -1, -1 },
            { -1, 0 },
            { -1, 1 },
            { 0, -1 },
            { 0, 1 },
            { 1, -1 },
            { 1, 0 },
            { 1, 1 }
        };

        public ConnectedRegionResult Execute(
            HeightGridMask mask,
            ConnectedRegionOptions options = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                Validate(mask, options);
                ConnectedRegionOptions resolvedOptions = options ?? new ConnectedRegionOptions();
                int cellCount = checked(mask.RowCount * mask.ColumnCount);
                bool[] visited = new bool[cellCount];
                List<ConnectedRegion> regions = new List<ConnectedRegion>();
                int foregroundCellCount = 0;

                for (int row = 0; row < mask.RowCount; row++)
                {
                    for (int column = 0; column < mask.ColumnCount; column++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int seedIndex = checked(row * mask.ColumnCount + column);
                        if (!mask.Foreground[seedIndex])
                        {
                            continue;
                        }

                        foregroundCellCount++;
                        if (visited[seedIndex])
                        {
                            continue;
                        }

                        regions.Add(LabelRegion(
                            mask,
                            visited,
                            regions.Count,
                            row,
                            column,
                            resolvedOptions.Connectivity,
                            cancellationToken));
                    }
                }

                return ConnectedRegionResult.Completed(
                    Array.AsReadOnly(regions.ToArray()),
                    foregroundCellCount);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ArgumentException exception)
            {
                return ConnectedRegionResult.Failed(exception.Message);
            }
            catch (InvalidDataException exception)
            {
                return ConnectedRegionResult.Failed(exception.Message);
            }
            catch (OverflowException exception)
            {
                return ConnectedRegionResult.Failed(exception.Message);
            }
        }

        private static ConnectedRegion LabelRegion(
            HeightGridMask mask,
            bool[] visited,
            int regionIndex,
            int seedRow,
            int seedColumn,
            ConnectedRegionConnectivity connectivity,
            CancellationToken cancellationToken)
        {
            int[,] offsets = connectivity == ConnectedRegionConnectivity.Four
                ? FourNeighborOffsets
                : EightNeighborOffsets;
            Queue<HeightGridCell> pending = new Queue<HeightGridCell>();
            List<HeightGridCell> cells = new List<HeightGridCell>();
            pending.Enqueue(new HeightGridCell(seedRow, seedColumn));
            visited[(seedRow * mask.ColumnCount) + seedColumn] = true;

            int minimumRow = seedRow;
            int minimumColumn = seedColumn;
            int maximumRow = seedRow;
            int maximumColumn = seedColumn;

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                HeightGridCell current = pending.Dequeue();
                cells.Add(current);
                minimumRow = Math.Min(minimumRow, current.Row);
                minimumColumn = Math.Min(minimumColumn, current.Column);
                maximumRow = Math.Max(maximumRow, current.Row);
                maximumColumn = Math.Max(maximumColumn, current.Column);

                for (int offsetIndex = 0; offsetIndex < offsets.GetLength(0); offsetIndex++)
                {
                    int row = current.Row + offsets[offsetIndex, 0];
                    int column = current.Column + offsets[offsetIndex, 1];
                    if (row < 0 || row >= mask.RowCount
                        || column < 0 || column >= mask.ColumnCount)
                    {
                        continue;
                    }

                    int index = (row * mask.ColumnCount) + column;
                    if (visited[index] || !mask.Foreground[index])
                    {
                        continue;
                    }

                    visited[index] = true;
                    pending.Enqueue(new HeightGridCell(row, column));
                }
            }

            cells.Sort(CompareCells);
            return new ConnectedRegion(
                regionIndex,
                seedRow,
                seedColumn,
                Array.AsReadOnly(cells.ToArray()),
                minimumRow,
                minimumColumn,
                maximumRow,
                maximumColumn);
        }

        private static int CompareCells(HeightGridCell left, HeightGridCell right)
        {
            int rowComparison = left.Row.CompareTo(right.Row);
            return rowComparison != 0
                ? rowComparison
                : left.Column.CompareTo(right.Column);
        }

        private static void Validate(
            HeightGridMask mask,
            ConnectedRegionOptions options)
        {
            if (mask == null)
            {
                throw new ArgumentNullException(nameof(mask));
            }

            if (mask.RowCount <= 0 || mask.ColumnCount <= 0)
            {
                throw new InvalidDataException(
                    "Connected-region mask dimensions must be greater than zero.");
            }

            int expectedCount = checked(mask.RowCount * mask.ColumnCount);
            if (mask.Foreground == null || mask.Foreground.Count != expectedCount)
            {
                throw new InvalidDataException(
                    "Connected-region mask values must match the declared dimensions.");
            }

            if (options != null
                && options.Connectivity != ConnectedRegionConnectivity.Four
                && options.Connectivity != ConnectedRegionConnectivity.Eight)
            {
                throw new InvalidDataException(
                    "Connected-region connectivity must be Four or Eight.");
            }
        }
    }
}
