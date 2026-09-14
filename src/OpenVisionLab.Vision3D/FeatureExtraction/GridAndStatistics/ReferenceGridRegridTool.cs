using System;
using System.Collections.Generic;
using System.Threading;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    /// <summary>
    /// Caller-authored reference grid for deterministic point-cloud projection.
    /// The caller owns physical calibration, unit meaning, and persistence.
    /// </summary>
    public sealed class ReferenceGridProfile
    {
        public ReferenceGridProfile(
            string referenceFrameId,
            string referenceUnit,
            string referenceProvenance,
            string referenceRevision,
            double originX,
            double originY,
            double originZ,
            double uAxisX,
            double uAxisY,
            double uAxisZ,
            double vAxisX,
            double vAxisY,
            double vAxisZ,
            double hAxisX,
            double hAxisY,
            double hAxisZ,
            double pitchU,
            double pitchV,
            int rowCount,
            int columnCount,
            double minimumCoverageRatio)
        {
            ReferenceFrameId = referenceFrameId;
            ReferenceUnit = referenceUnit;
            ReferenceProvenance = referenceProvenance;
            ReferenceRevision = referenceRevision;
            OriginX = originX;
            OriginY = originY;
            OriginZ = originZ;
            UAxisX = uAxisX;
            UAxisY = uAxisY;
            UAxisZ = uAxisZ;
            VAxisX = vAxisX;
            VAxisY = vAxisY;
            VAxisZ = vAxisZ;
            HAxisX = hAxisX;
            HAxisY = hAxisY;
            HAxisZ = hAxisZ;
            PitchU = pitchU;
            PitchV = pitchV;
            RowCount = rowCount;
            ColumnCount = columnCount;
            MinimumCoverageRatio = minimumCoverageRatio;
        }

        public string ReferenceFrameId { get; }
        public string ReferenceUnit { get; }
        public string ReferenceProvenance { get; }
        public string ReferenceRevision { get; }
        public double OriginX { get; }
        public double OriginY { get; }
        public double OriginZ { get; }
        public double UAxisX { get; }
        public double UAxisY { get; }
        public double UAxisZ { get; }
        public double VAxisX { get; }
        public double VAxisY { get; }
        public double VAxisZ { get; }
        public double HAxisX { get; }
        public double HAxisY { get; }
        public double HAxisZ { get; }
        public double PitchU { get; }
        public double PitchV { get; }
        public int RowCount { get; }
        public int ColumnCount { get; }
        public double MinimumCoverageRatio { get; }
    }

    /// <summary>
    /// One transformed source point. Row/column are only stable tie-break and
    /// provenance locators; X/Y/Z are the reference-frame coordinates.
    /// </summary>
    public readonly struct ReferenceGridInputPoint
    {
        public ReferenceGridInputPoint(int row, int column, double x, double y, double z)
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

    /// <summary>
    /// One row-major output cell. A missing cell preserves NaN height and
    /// negative source locator; no interpolation or fill is implied.
    /// </summary>
    public readonly struct ReferenceGridHeightCell
    {
        public ReferenceGridHeightCell(int row, int column, double height, int sourceRow, int sourceColumn, double planarDistanceSquared)
        {
            Row = row;
            Column = column;
            Height = height;
            SourceRow = sourceRow;
            SourceColumn = sourceColumn;
            PlanarDistanceSquared = planarDistanceSquared;
        }

        public int Row { get; }
        public int Column { get; }
        public double Height { get; }
        public int SourceRow { get; }
        public int SourceColumn { get; }
        public double PlanarDistanceSquared { get; }
        public bool HasValue => !double.IsNaN(Height);
    }

    public sealed class ReferenceGridRegridResult
    {
        private ReferenceGridRegridResult(
            bool success,
            string message,
            IReadOnlyList<ReferenceGridHeightCell> cells,
            int inputPointCount,
            int populatedCellCount,
            int collisionCount,
            double coverageRatio,
            bool meetsMinimumCoverage)
        {
            Success = success;
            Message = message ?? string.Empty;
            Cells = cells ?? Array.Empty<ReferenceGridHeightCell>();
            InputPointCount = inputPointCount;
            PopulatedCellCount = populatedCellCount;
            CollisionCount = collisionCount;
            CoverageRatio = coverageRatio;
            MeetsMinimumCoverage = meetsMinimumCoverage;
        }

        public bool Success { get; }
        public string Message { get; }
        public IReadOnlyList<ReferenceGridHeightCell> Cells { get; }
        public int InputPointCount { get; }
        public int PopulatedCellCount { get; }
        public int CollisionCount { get; }
        public double CoverageRatio { get; }
        public bool MeetsMinimumCoverage { get; }

        internal static ReferenceGridRegridResult Completed(
            IReadOnlyList<ReferenceGridHeightCell> cells,
            int inputPointCount,
            int populatedCellCount,
            int collisionCount,
            double coverageRatio,
            bool meetsMinimumCoverage)
        {
            string message = meetsMinimumCoverage
                ? "Completed deterministic planar-nearest reference-grid re-sampling."
                : "Completed deterministic planar-nearest reference-grid re-sampling, but coverage is below the authored Publish minimum.";
            return new ReferenceGridRegridResult(true, message, cells, inputPointCount, populatedCellCount, collisionCount, coverageRatio, meetsMinimumCoverage);
        }

        internal static ReferenceGridRegridResult Failed(string message)
        {
            return new ReferenceGridRegridResult(false, message, Array.Empty<ReferenceGridHeightCell>(), 0, 0, 0, 0.0, false);
        }
    }

    /// <summary>
    /// Deterministically projects finite reference-frame XYZ points into one
    /// explicit right-handed U/V/H grid. It never interpolates, averages,
    /// smooths, triangulates, reads files, or asserts physical calibration.
    /// </summary>
    public sealed class ReferenceGridRegridTool
    {
        public const int MaximumOutputCellCount = 4194304;
        private const double AxisTolerance = 1e-9;

        public ReferenceGridRegridResult Execute(
            IReadOnlyList<ReferenceGridInputPoint> points,
            ReferenceGridProfile profile,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                if (points == null) throw new ArgumentNullException(nameof(points));
                ValidateProfile(profile);

                int cellCount = checked(profile.RowCount * profile.ColumnCount);
                bool[] populated = new bool[cellCount];
                double[] heights = new double[cellCount];
                double[] planarDistances = new double[cellCount];
                int[] sourceRows = new int[cellCount];
                int[] sourceColumns = new int[cellCount];
                HashSet<long> locators = new HashSet<long>();
                int collisionCount = 0;
                int populatedCellCount = 0;
                double extentU = profile.ColumnCount * profile.PitchU;
                double extentV = profile.RowCount * profile.PitchV;

                for (int index = 0; index < points.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ReferenceGridInputPoint point = points[index];
                    ValidatePoint(point, locators);
                    double dx = point.X - profile.OriginX;
                    double dy = point.Y - profile.OriginY;
                    double dz = point.Z - profile.OriginZ;
                    double u = Dot(dx, dy, dz, profile.UAxisX, profile.UAxisY, profile.UAxisZ);
                    double v = Dot(dx, dy, dz, profile.VAxisX, profile.VAxisY, profile.VAxisZ);
                    double h = Dot(dx, dy, dz, profile.HAxisX, profile.HAxisY, profile.HAxisZ);
                    if (u < 0.0 || u >= extentU || v < 0.0 || v >= extentV)
                    {
                        return ReferenceGridRegridResult.Failed("Reference-grid re-sampling rejected a transformed point outside the authored half-open U/V bounds.");
                    }

                    int column = (int)Math.Floor(u / profile.PitchU);
                    int row = (int)Math.Floor(v / profile.PitchV);
                    int cellIndex = checked(row * profile.ColumnCount + column);
                    double centerU = (column + 0.5) * profile.PitchU;
                    double centerV = (row + 0.5) * profile.PitchV;
                    double planarDistance = ((u - centerU) * (u - centerU)) + ((v - centerV) * (v - centerV));
                    if (!populated[cellIndex])
                    {
                        populated[cellIndex] = true;
                        heights[cellIndex] = h;
                        planarDistances[cellIndex] = planarDistance;
                        sourceRows[cellIndex] = point.Row;
                        sourceColumns[cellIndex] = point.Column;
                        populatedCellCount++;
                    }
                    else
                    {
                        collisionCount++;
                        if (IsBetterCandidate(planarDistance, point.Row, point.Column, planarDistances[cellIndex], sourceRows[cellIndex], sourceColumns[cellIndex]))
                        {
                            heights[cellIndex] = h;
                            planarDistances[cellIndex] = planarDistance;
                            sourceRows[cellIndex] = point.Row;
                            sourceColumns[cellIndex] = point.Column;
                        }
                    }
                }

                ReferenceGridHeightCell[] cells = new ReferenceGridHeightCell[cellCount];
                for (int row = 0; row < profile.RowCount; row++)
                {
                    for (int column = 0; column < profile.ColumnCount; column++)
                    {
                        int cellIndex = checked(row * profile.ColumnCount + column);
                        cells[cellIndex] = populated[cellIndex]
                            ? new ReferenceGridHeightCell(row, column, heights[cellIndex], sourceRows[cellIndex], sourceColumns[cellIndex], planarDistances[cellIndex])
                            : new ReferenceGridHeightCell(row, column, double.NaN, -1, -1, double.NaN);
                    }
                }
                double coverageRatio = (double)populatedCellCount / cellCount;
                return ReferenceGridRegridResult.Completed(cells, points.Count, populatedCellCount, collisionCount, coverageRatio, coverageRatio >= profile.MinimumCoverageRatio);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return ReferenceGridRegridResult.Failed("Reference-grid re-sampling failed: " + exception.Message);
            }
        }

        private static void ValidateProfile(ReferenceGridProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (string.IsNullOrWhiteSpace(profile.ReferenceFrameId) || string.IsNullOrWhiteSpace(profile.ReferenceUnit)
                || string.IsNullOrWhiteSpace(profile.ReferenceProvenance) || string.IsNullOrWhiteSpace(profile.ReferenceRevision))
            {
                throw new ArgumentException("Reference-grid re-sampling requires non-empty reference identity and provenance.");
            }
            if (profile.RowCount <= 0 || profile.ColumnCount <= 0 || checked(profile.RowCount * profile.ColumnCount) > MaximumOutputCellCount)
            {
                throw new ArgumentOutOfRangeException(nameof(profile), "Reference-grid dimensions must be positive and within the deterministic output-cell limit.");
            }
            if (!IsFinite(profile.PitchU) || !IsFinite(profile.PitchV) || profile.PitchU <= 0.0 || profile.PitchV <= 0.0
                || !IsFinite(profile.MinimumCoverageRatio) || profile.MinimumCoverageRatio < 0.0 || profile.MinimumCoverageRatio > 1.0)
            {
                throw new ArgumentException("Reference-grid pitches must be finite positive values and the minimum coverage ratio must be within [0, 1].");
            }
            double[] values =
            {
                profile.OriginX, profile.OriginY, profile.OriginZ,
                profile.UAxisX, profile.UAxisY, profile.UAxisZ,
                profile.VAxisX, profile.VAxisY, profile.VAxisZ,
                profile.HAxisX, profile.HAxisY, profile.HAxisZ
            };
            for (int index = 0; index < values.Length; index++)
            {
                if (!IsFinite(values[index])) throw new ArgumentException("Reference-grid origin and axes must be finite.");
            }
            double uLength = Math.Sqrt(Dot(profile.UAxisX, profile.UAxisY, profile.UAxisZ, profile.UAxisX, profile.UAxisY, profile.UAxisZ));
            double vLength = Math.Sqrt(Dot(profile.VAxisX, profile.VAxisY, profile.VAxisZ, profile.VAxisX, profile.VAxisY, profile.VAxisZ));
            double hLength = Math.Sqrt(Dot(profile.HAxisX, profile.HAxisY, profile.HAxisZ, profile.HAxisX, profile.HAxisY, profile.HAxisZ));
            if (Math.Abs(uLength - 1.0) > AxisTolerance || Math.Abs(vLength - 1.0) > AxisTolerance || Math.Abs(hLength - 1.0) > AxisTolerance
                || Math.Abs(Dot(profile.UAxisX, profile.UAxisY, profile.UAxisZ, profile.VAxisX, profile.VAxisY, profile.VAxisZ)) > AxisTolerance
                || Math.Abs(Dot(profile.UAxisX, profile.UAxisY, profile.UAxisZ, profile.HAxisX, profile.HAxisY, profile.HAxisZ)) > AxisTolerance
                || Math.Abs(Dot(profile.VAxisX, profile.VAxisY, profile.VAxisZ, profile.HAxisX, profile.HAxisY, profile.HAxisZ)) > AxisTolerance)
            {
                throw new ArgumentException("Reference-grid U/V/H axes must be orthonormal.");
            }
            double crossDotH = (profile.UAxisY * profile.VAxisZ - profile.UAxisZ * profile.VAxisY) * profile.HAxisX
                + (profile.UAxisZ * profile.VAxisX - profile.UAxisX * profile.VAxisZ) * profile.HAxisY
                + (profile.UAxisX * profile.VAxisY - profile.UAxisY * profile.VAxisX) * profile.HAxisZ;
            if (crossDotH < 1.0 - AxisTolerance)
            {
                throw new ArgumentException("Reference-grid U/V/H axes must be right-handed.");
            }
        }

        private static void ValidatePoint(ReferenceGridInputPoint point, HashSet<long> locators)
        {
            if (point.Row < 0 || point.Column < 0 || !IsFinite(point.X) || !IsFinite(point.Y) || !IsFinite(point.Z))
            {
                throw new ArgumentException("Reference-grid re-sampling requires finite points with non-negative unique source locators.");
            }
            long locator = ((long)point.Row << 32) | (uint)point.Column;
            if (!locators.Add(locator)) throw new ArgumentException("Reference-grid re-sampling requires unique source locators.");
        }

        private static bool IsBetterCandidate(double distance, int row, int column, double currentDistance, int currentRow, int currentColumn)
        {
            return distance < currentDistance
                || (distance == currentDistance && (row < currentRow || (row == currentRow && column < currentColumn)));
        }

        private static double Dot(double ax, double ay, double az, double bx, double by, double bz)
        {
            return (ax * bx) + (ay * by) + (az * bz);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
