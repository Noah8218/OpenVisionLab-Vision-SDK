using System;
using System.Collections.Generic;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    public struct GridCoordinateSample
    {
        public GridCoordinateSample(
            int row,
            int column,
            double x,
            double y,
            double z)
        {
            Row = row;
            Column = column;
            X = x;
            Y = y;
            Z = z;
        }

        public int Row { get; }

        public int Column { get; }

        public double X { get; }

        public double Y { get; }

        public double Z { get; }
    }

    public enum GridDiagnosticCode
    {
        Topology = 0,
        LocatorMonotonicity = 1,
        DuplicateLocator = 2,
        CoordinateFiniteness = 3
    }

    public enum GridDiagnosticState
    {
        Pass = 0,
        Error = 1
    }

    public sealed class GridDiagnosticCheck
    {
        internal GridDiagnosticCheck(
            GridDiagnosticCode code,
            GridDiagnosticState state,
            long affectedCount,
            long? firstSampleOrdinal,
            int? firstRow,
            int? firstColumn,
            string firstComponent,
            string message)
        {
            Code = code;
            State = state;
            AffectedCount = affectedCount;
            FirstSampleOrdinal = firstSampleOrdinal;
            FirstRow = firstRow;
            FirstColumn = firstColumn;
            FirstComponent = firstComponent;
            Message = message ?? string.Empty;
        }

        public GridDiagnosticCode Code { get; }

        public GridDiagnosticState State { get; }

        public long AffectedCount { get; }

        public long? FirstSampleOrdinal { get; }

        public int? FirstRow { get; }

        public int? FirstColumn { get; }

        public string FirstComponent { get; }

        public string Message { get; }
    }

    public sealed class GridDiagnosticsResult
    {
        internal GridDiagnosticsResult(
            GridDiagnosticState state,
            long declaredCellCount,
            long observedSampleCount,
            long uniqueLocatorCount,
            IReadOnlyList<GridDiagnosticCheck> checks)
        {
            State = state;
            DeclaredCellCount = declaredCellCount;
            ObservedSampleCount = observedSampleCount;
            UniqueLocatorCount = uniqueLocatorCount;
            Checks = checks;
        }

        public GridDiagnosticState State { get; }

        public long DeclaredCellCount { get; }

        public long ObservedSampleCount { get; }

        public long UniqueLocatorCount { get; }

        public IReadOnlyList<GridDiagnosticCheck> Checks { get; }
    }

    /// <summary>
    /// Produces deterministic topology, locator-order, duplicate-locator, and
    /// coordinate-finiteness evidence for implicit or explicit organized grids.
    /// </summary>
    public sealed class GridDiagnosticsTool
    {
        public GridDiagnosticsResult Execute(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(width),
                    "Implicit grid dimensions must be positive.");
            }

            long cellCount = checked((long)width * height);
            return Create(
                cellCount,
                cellCount,
                cellCount,
                Pass(
                    GridDiagnosticCode.Topology,
                    "Grid topology matches its declared dimensions."),
                Pass(
                    GridDiagnosticCode.LocatorMonotonicity,
                    "Grid locators are monotonic in row-major order."),
                Pass(
                    GridDiagnosticCode.DuplicateLocator,
                    "Grid locators are unique."),
                Pass(
                    GridDiagnosticCode.CoordinateFiniteness,
                    "Implicit row-major grid coordinates are finite."));
        }

        public GridDiagnosticsResult Execute(
            int width,
            int height,
            IReadOnlyList<GridCoordinateSample> samples)
        {
            if (samples == null)
            {
                throw new ArgumentNullException(nameof(samples));
            }

            bool dimensionsValid = width > 0 && height > 0;
            long declaredCellCount = dimensionsValid
                ? checked((long)width * height)
                : 0L;
            int observedSampleCount = samples.Count;
            HashSet<long> locators = new HashSet<long>();
            HashSet<long> inRangeLocators = new HashSet<long>();

            long descendingCount = 0;
            long duplicateCount = 0;
            long nonFiniteCount = 0;
            GridCoordinateSample? firstDescending = null;
            long? firstDescendingOrdinal = null;
            GridCoordinateSample? firstDuplicate = null;
            long? firstDuplicateOrdinal = null;
            GridCoordinateSample? firstNonFinite = null;
            long? firstNonFiniteOrdinal = null;
            string firstNonFiniteComponent = null;
            GridCoordinateSample? previous = null;
            long outOfRangeCount = 0;

            for (int index = 0; index < samples.Count; index++)
            {
                GridCoordinateSample sample = samples[index];
                if (previous.HasValue
                    && CompareLocator(sample, previous.Value) < 0)
                {
                    descendingCount++;
                    if (!firstDescending.HasValue)
                    {
                        firstDescending = sample;
                        firstDescendingOrdinal = index;
                    }
                }

                previous = sample;
                if (!locators.Add(LocatorKey(sample)))
                {
                    duplicateCount++;
                    if (!firstDuplicate.HasValue)
                    {
                        firstDuplicate = sample;
                        firstDuplicateOrdinal = index;
                    }
                }

                bool inRange = dimensionsValid
                    && sample.Row >= 0
                    && sample.Row < height
                    && sample.Column >= 0
                    && sample.Column < width;
                if (inRange)
                {
                    inRangeLocators.Add(LocatorKey(sample));
                }
                else if (dimensionsValid)
                {
                    outOfRangeCount++;
                }

                CountNonFinite(sample.X, "X", sample, index,
                    ref nonFiniteCount,
                    ref firstNonFinite,
                    ref firstNonFiniteOrdinal,
                    ref firstNonFiniteComponent);
                CountNonFinite(sample.Y, "Y", sample, index,
                    ref nonFiniteCount,
                    ref firstNonFinite,
                    ref firstNonFiniteOrdinal,
                    ref firstNonFiniteComponent);
                CountNonFinite(sample.Z, "Z", sample, index,
                    ref nonFiniteCount,
                    ref firstNonFinite,
                    ref firstNonFiniteOrdinal,
                    ref firstNonFiniteComponent);
            }

            long topologyAffectedCount = dimensionsValid
                ? Math.Abs(observedSampleCount - declaredCellCount)
                    + Math.Abs(inRangeLocators.Count - declaredCellCount)
                    + outOfRangeCount
                : 1L;
            long? topologyFirstOrdinal = FirstTopologyOrdinal(
                dimensionsValid,
                width,
                height,
                declaredCellCount,
                samples,
                firstDuplicateOrdinal);
            GridCoordinateSample? topologyFirst = topologyFirstOrdinal.HasValue
                && topologyFirstOrdinal.Value >= 0
                && topologyFirstOrdinal.Value < samples.Count
                    ? samples[(int)topologyFirstOrdinal.Value]
                    : (GridCoordinateSample?)null;

            return Create(
                declaredCellCount,
                observedSampleCount,
                locators.Count,
                topologyAffectedCount == 0
                    ? Pass(
                        GridDiagnosticCode.Topology,
                        "Grid topology matches its declared dimensions.")
                    : Error(
                        GridDiagnosticCode.Topology,
                        topologyAffectedCount,
                        topologyFirstOrdinal,
                        topologyFirst,
                        topologyFirst.HasValue ? "Locator" : null,
                        "Grid topology has " + topologyAffectedCount + " mismatch(es)."),
                descendingCount == 0
                    ? Pass(
                        GridDiagnosticCode.LocatorMonotonicity,
                        "Grid locators are monotonic in row-major order.")
                    : Error(
                        GridDiagnosticCode.LocatorMonotonicity,
                        descendingCount,
                        firstDescendingOrdinal,
                        firstDescending,
                        "Locator",
                        "Grid has " + descendingCount + " descending locator transition(s)."),
                duplicateCount == 0
                    ? Pass(
                        GridDiagnosticCode.DuplicateLocator,
                        "Grid locators are unique.")
                    : Error(
                        GridDiagnosticCode.DuplicateLocator,
                        duplicateCount,
                        firstDuplicateOrdinal,
                        firstDuplicate,
                        "Locator",
                        "Grid has " + duplicateCount + " duplicate locator occurrence(s)."),
                nonFiniteCount == 0
                    ? Pass(
                        GridDiagnosticCode.CoordinateFiniteness,
                        "Grid coordinates are finite.")
                    : Error(
                        GridDiagnosticCode.CoordinateFiniteness,
                        nonFiniteCount,
                        firstNonFiniteOrdinal,
                        firstNonFinite,
                        firstNonFiniteComponent,
                        "Grid has " + nonFiniteCount + " non-finite coordinate component(s)."));
        }

        private static GridDiagnosticsResult Create(
            long declaredCellCount,
            long observedSampleCount,
            long uniqueLocatorCount,
            params GridDiagnosticCheck[] checks)
        {
            GridDiagnosticState state = GridDiagnosticState.Pass;
            for (int index = 0; index < checks.Length; index++)
            {
                if (checks[index].State == GridDiagnosticState.Error)
                {
                    state = GridDiagnosticState.Error;
                    break;
                }
            }

            return new GridDiagnosticsResult(
                state,
                declaredCellCount,
                observedSampleCount,
                uniqueLocatorCount,
                Array.AsReadOnly(checks));
        }

        private static GridDiagnosticCheck Pass(
            GridDiagnosticCode code,
            string message)
        {
            return new GridDiagnosticCheck(
                code,
                GridDiagnosticState.Pass,
                0,
                null,
                null,
                null,
                null,
                message);
        }

        private static GridDiagnosticCheck Error(
            GridDiagnosticCode code,
            long affectedCount,
            long? ordinal,
            GridCoordinateSample? sample,
            string component,
            string message)
        {
            return new GridDiagnosticCheck(
                code,
                GridDiagnosticState.Error,
                affectedCount,
                ordinal,
                sample.HasValue ? (int?)sample.Value.Row : null,
                sample.HasValue ? (int?)sample.Value.Column : null,
                component,
                message);
        }

        private static int CompareLocator(
            GridCoordinateSample current,
            GridCoordinateSample previous)
        {
            int row = current.Row.CompareTo(previous.Row);
            return row != 0 ? row : current.Column.CompareTo(previous.Column);
        }

        private static long LocatorKey(GridCoordinateSample sample)
        {
            return ((long)sample.Row << 32) | (uint)sample.Column;
        }

        private static void CountNonFinite(
            double value,
            string component,
            GridCoordinateSample sample,
            int ordinal,
            ref long count,
            ref GridCoordinateSample? first,
            ref long? firstOrdinal,
            ref string firstComponent)
        {
            if (!double.IsNaN(value) && !double.IsInfinity(value))
            {
                return;
            }

            count++;
            if (!first.HasValue)
            {
                first = sample;
                firstOrdinal = ordinal;
                firstComponent = component;
            }
        }

        private static long? FirstTopologyOrdinal(
            bool dimensionsValid,
            int width,
            int height,
            long declaredCellCount,
            IReadOnlyList<GridCoordinateSample> samples,
            long? firstDuplicateOrdinal)
        {
            if (!dimensionsValid)
            {
                return null;
            }

            for (int index = 0; index < samples.Count; index++)
            {
                GridCoordinateSample sample = samples[index];
                if (sample.Row < 0
                    || sample.Row >= height
                    || sample.Column < 0
                    || sample.Column >= width)
                {
                    return index;
                }
            }

            if (samples.Count > declaredCellCount)
            {
                return declaredCellCount;
            }

            if (samples.Count < declaredCellCount)
            {
                return null;
            }

            return firstDuplicateOrdinal;
        }
    }
}
