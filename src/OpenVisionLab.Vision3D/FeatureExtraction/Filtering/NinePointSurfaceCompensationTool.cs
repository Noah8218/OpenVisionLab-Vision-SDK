using OpenVisionLab.Vision3D.Geometry;
using System;
using System.Collections.Generic;
using System.Threading;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    public enum NinePointSurfaceCompensationFailureReason
    {
        None,
        InvalidInput,
        InvalidReferenceGrid,
        MissingReferenceSample,
        NumericalFailure
    }

    public sealed class NinePointSurfaceCompensationOptions
    {
        public IReadOnlyList<int> ReferenceRows { get; set; }
        public IReadOnlyList<int> ReferenceColumns { get; set; }
        public double TargetHeight { get; set; }
        public string OutputFrameId { get; set; }
        public string OutputSourceId { get; set; }
    }

    public sealed class NinePointSurfaceControlEvidence
    {
        internal NinePointSurfaceControlEvidence(int row, int column, double x, double y, double height,
            double referencePlaneHeight, double planeResidual, double compensatedHeight)
        {
            Row = row;
            Column = column;
            X = x;
            Y = y;
            Height = height;
            ReferencePlaneHeight = referencePlaneHeight;
            PlaneResidual = planeResidual;
            CompensatedHeight = compensatedHeight;
        }

        public int Row { get; }
        public int Column { get; }
        public double X { get; }
        public double Y { get; }
        public double Height { get; }
        public double ReferencePlaneHeight { get; }
        public double PlaneResidual { get; }
        public double CompensatedHeight { get; }
    }

    public sealed class NinePointSurfaceCompensationResult
    {
        internal NinePointSurfaceCompensationResult(NinePointSurfaceCompensationFailureReason failureReason, string message,
            HeightMap3D input, HeightMap3D output = null, double[] backgroundHeights = null,
            NinePointSurfaceControlEvidence[] controlPoints = null, double planeSlopeX = double.NaN,
            double planeSlopeY = double.NaN, double planeHeightAtOrigin = double.NaN,
            double referencePeakToValley = double.NaN, double referenceRms = double.NaN)
        {
            FailureReason = failureReason;
            Message = message;
            Input = input;
            Output = output;
            BackgroundHeights = Array.AsReadOnly(backgroundHeights ?? Array.Empty<double>());
            ControlPoints = Array.AsReadOnly(controlPoints ?? Array.Empty<NinePointSurfaceControlEvidence>());
            PlaneSlopeX = planeSlopeX;
            PlaneSlopeY = planeSlopeY;
            PlaneHeightAtOrigin = planeHeightAtOrigin;
            ReferencePeakToValley = referencePeakToValley;
            ReferenceRms = referenceRms;
        }

        public bool Success => FailureReason == NinePointSurfaceCompensationFailureReason.None;
        public NinePointSurfaceCompensationFailureReason FailureReason { get; }
        public string Message { get; }
        public HeightMap3D Input { get; }
        public HeightMap3D Output { get; }
        /// <summary>Modeled background in input height units, in the input row-major grid. This is not a second observation.</summary>
        public IReadOnlyList<double> BackgroundHeights { get; }
        /// <summary>Nine measured cells in reference-row then reference-column order. Their corrected heights are interpolation constraints, not independent accuracy evidence.</summary>
        public IReadOnlyList<NinePointSurfaceControlEvidence> ControlPoints { get; }
        public double PlaneSlopeX { get; }
        public double PlaneSlopeY { get; }
        public double PlaneHeightAtOrigin { get; }
        /// <summary>Peak-to-valley of the nine input heights relative to their least-squares height plane.</summary>
        public double ReferencePeakToValley { get; }
        /// <summary>RMS of the nine input heights relative to their least-squares height plane.</summary>
        public double ReferenceRms { get; }
    }

    /// <summary>
    /// Interpolates a tensor biquadratic background through nine substrate cells on a three-row by three-column grid,
    /// then subtracts that background vertically and adds TargetHeight into a separate height map.
    /// The outer references must cover the complete input grid; each middle reference must lie within 25-75% of its span.
    /// There is no extrapolation, resampling, XY deformation, texture processing, or automatic reference selection.
    /// Local features away from the references remain relative to the modeled background. A feature on a reference
    /// is indistinguishable from substrate warp with nine measurements and will influence the background.
    /// Units are declared metadata, not calibration evidence. Callers must not mutate options during Execute.
    /// OutputFrameId is explicit and may retain the input grid frame because XY is unchanged; OutputSourceId must be distinct.
    /// Cancellation throws OperationCanceledException and never exposes a partial output.
    /// </summary>
    public sealed class NinePointSurfaceCompensationTool
    {
        public const string Semantics = "nine-point-tensor-biquadratic-vertical-compensation-v1";

        public NinePointSurfaceCompensationResult Execute(HeightMap3D source, NinePointSurfaceCompensationOptions options,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (source == null || options == null || !IsFinite(options.TargetHeight)
                || string.IsNullOrWhiteSpace(options.OutputFrameId) || string.IsNullOrWhiteSpace(options.OutputSourceId))
            {
                return Failed(source, NinePointSurfaceCompensationFailureReason.InvalidInput,
                    "A height map, finite target height, explicit output frame, and distinct output source ID are required.");
            }

            string outputFrameId = options.OutputFrameId.Trim();
            string outputSourceId = options.OutputSourceId.Trim();
            double targetHeight = options.TargetHeight;
            if (string.Equals(source.SourceId.Trim(), outputSourceId, StringComparison.Ordinal))
            {
                return Failed(source, NinePointSurfaceCompensationFailureReason.InvalidInput,
                    "Compensated data must have a source ID distinct from the input.");
            }

            if (!TryReferenceAxis(options.ReferenceRows, source.Rows, out int[] rows)
                || !TryReferenceAxis(options.ReferenceColumns, source.Columns, out int[] columns)
                || source.GetX(1) <= source.GetX(0) || source.GetX(source.Columns - 1) <= source.GetX(source.Columns - 2)
                || source.GetY(1) <= source.GetY(0) || source.GetY(source.Rows - 1) <= source.GetY(source.Rows - 2))
            {
                return Failed(source, NinePointSurfaceCompensationFailureReason.InvalidReferenceGrid,
                    "Use three ordered rows and columns spanning the input, with each middle index at 25-75% of its span and distinct coordinates.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            double originHeight = source.GetHeight(rows[1], columns[1]);
            if (!IsFinite(originHeight))
            {
                return Failed(source, NinePointSurfaceCompensationFailureReason.MissingReferenceSample,
                    "All nine substrate reference cells must have finite heights.");
            }

            double[] referenceHeights = new double[9];
            HeightFieldPlaneFitSample[] samples = new HeightFieldPlaneFitSample[9];
            for (int rowIndex = 0; rowIndex < 3; rowIndex++)
            {
                for (int columnIndex = 0; columnIndex < 3; columnIndex++)
                {
                    int index = rowIndex * 3 + columnIndex;
                    double height = source.GetHeight(rows[rowIndex], columns[columnIndex]);
                    if (!IsFinite(height))
                    {
                        return Failed(source, NinePointSurfaceCompensationFailureReason.MissingReferenceSample,
                            "All nine substrate reference cells must have finite heights.");
                    }

                    double relativeHeight = height - originHeight;
                    if (!IsFinite(relativeHeight))
                    {
                        return Failed(source, NinePointSurfaceCompensationFailureReason.NumericalFailure,
                            "Reference height differences exceed the numeric range.");
                    }

                    referenceHeights[index] = relativeHeight;
                    samples[index] = new HeightFieldPlaneFitSample(new ThreeDPoint(
                        (double)columns[columnIndex] / (source.Columns - 1), relativeHeight,
                        (double)rows[rowIndex] / (source.Rows - 1)), relativeHeight);
                }
            }

            try
            {
                LeastSquaresHeightFieldPlaneFitResult plane = new LeastSquaresHeightFieldPlaneFitTool().Execute(samples);
                double planeSlopeX = plane.SlopeX / ((source.Columns - 1) * source.ColumnPitch);
                double planeSlopeY = plane.SlopeZ / ((source.Rows - 1) * source.RowPitch);
                double planeHeightAtOrigin = originHeight + plane.EvaluateY(0.0, 0.0);
                double[] residuals = new double[9];
                double minimumResidual = double.PositiveInfinity;
                double maximumResidual = double.NegativeInfinity;
                double residualScale = 0.0;
                for (int index = 0; index < 9; index++)
                {
                    residuals[index] = referenceHeights[index] - plane.EvaluateY(samples[index].Position.X, samples[index].Position.Z);
                    minimumResidual = Math.Min(minimumResidual, residuals[index]);
                    maximumResidual = Math.Max(maximumResidual, residuals[index]);
                    residualScale = Math.Max(residualScale, Math.Abs(residuals[index]));
                }

                double normalizedSquareSum = 0.0;
                if (residualScale > 0.0)
                {
                    for (int index = 0; index < 9; index++)
                    {
                        double normalizedResidual = residuals[index] / residualScale;
                        normalizedSquareSum += normalizedResidual * normalizedResidual;
                    }
                }

                double peakToValley = maximumResidual - minimumResidual;
                double rms = residualScale * Math.Sqrt(normalizedSquareSum / 9.0);
                if (!IsFinite(planeSlopeX) || !IsFinite(planeSlopeY) || !IsFinite(planeHeightAtOrigin)
                    || !IsFinite(peakToValley) || !IsFinite(rms))
                {
                    return Failed(source, NinePointSurfaceCompensationFailureReason.NumericalFailure,
                        "The reference plane or height statistics exceed the numeric range.");
                }

                double[,] columnWeights = new double[source.Columns, 3];
                double middleColumn = (double)columns[1] / (source.Columns - 1);
                for (int column = 0; column < source.Columns; column++)
                {
                    if ((column & 0x3fff) == 0) cancellationToken.ThrowIfCancellationRequested();
                    double coordinate = (double)column / (source.Columns - 1);
                    columnWeights[column, 0] = (coordinate - middleColumn) * (coordinate - 1.0) / middleColumn;
                    columnWeights[column, 1] = coordinate * (coordinate - 1.0) / (middleColumn * (middleColumn - 1.0));
                    columnWeights[column, 2] = coordinate * (coordinate - middleColumn) / (1.0 - middleColumn);
                }

                double[] backgroundHeights = new double[checked(source.Rows * source.Columns)];
                double[] outputHeights = new double[backgroundHeights.Length];
                double middleRow = (double)rows[1] / (source.Rows - 1);
                double[] rowWeights = new double[3];
                for (int row = 0; row < source.Rows; row++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    double coordinate = (double)row / (source.Rows - 1);
                    rowWeights[0] = (coordinate - middleRow) * (coordinate - 1.0) / middleRow;
                    rowWeights[1] = coordinate * (coordinate - 1.0) / (middleRow * (middleRow - 1.0));
                    rowWeights[2] = coordinate * (coordinate - middleRow) / (1.0 - middleRow);
                    for (int column = 0; column < source.Columns; column++)
                    {
                        if ((column & 0x3fff) == 0) cancellationToken.ThrowIfCancellationRequested();
                        double relativeBackground = 0.0;
                        for (int rowIndex = 0; rowIndex < 3; rowIndex++)
                        {
                            for (int columnIndex = 0; columnIndex < 3; columnIndex++)
                            {
                                relativeBackground += rowWeights[rowIndex] * columnWeights[column, columnIndex]
                                    * referenceHeights[rowIndex * 3 + columnIndex];
                            }
                        }

                        int index = row * source.Columns + column;
                        double height = source.GetHeight(row, column);
                        backgroundHeights[index] = originHeight + relativeBackground;
                        // Center before subtraction so a large common height does not erase a representable local feature.
                        outputHeights[index] = double.IsNaN(height) ? double.NaN : (height - originHeight) - relativeBackground + targetHeight;
                        if (!IsFinite(backgroundHeights[index]) || (!double.IsNaN(height) && !IsFinite(outputHeights[index])))
                        {
                            return Failed(source, NinePointSurfaceCompensationFailureReason.NumericalFailure,
                                "The curved background or compensated height exceeds the numeric range.");
                        }
                    }
                }

                NinePointSurfaceControlEvidence[] evidence = new NinePointSurfaceControlEvidence[9];
                for (int index = 0; index < 9; index++)
                {
                    int row = rows[index / 3];
                    int column = columns[index % 3];
                    evidence[index] = new NinePointSurfaceControlEvidence(row, column, source.GetX(column), source.GetY(row),
                        source.GetHeight(row, column), originHeight + plane.EvaluateY(samples[index].Position.X, samples[index].Position.Z),
                        residuals[index], outputHeights[row * source.Columns + column]);
                    if (!IsFinite(evidence[index].ReferencePlaneHeight))
                    {
                        return Failed(source, NinePointSurfaceCompensationFailureReason.NumericalFailure,
                            "The reference plane height exceeds the numeric range.");
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                HeightMap3D output = new HeightMap3D(source.Rows, source.Columns, source.OriginX, source.OriginY,
                    source.ColumnPitch, source.RowPitch, outputHeights, source.PlanarUnit, source.HeightUnit, outputFrameId, outputSourceId);
                cancellationToken.ThrowIfCancellationRequested();
                return new NinePointSurfaceCompensationResult(NinePointSurfaceCompensationFailureReason.None,
                    "Compensated the modeled nine-point background into a separate height map.", source, output,
                    backgroundHeights, evidence, planeSlopeX, planeSlopeY, planeHeightAtOrigin, peakToValley, rms);
            }
            catch (ArgumentException exception)
            {
                return Failed(source, NinePointSurfaceCompensationFailureReason.NumericalFailure, exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                return Failed(source, NinePointSurfaceCompensationFailureReason.NumericalFailure, exception.Message);
            }
            catch (OverflowException exception)
            {
                return Failed(source, NinePointSurfaceCompensationFailureReason.NumericalFailure, exception.Message);
            }
        }

        private static bool TryReferenceAxis(IReadOnlyList<int> reference, int count, out int[] indices)
        {
            indices = null;
            if (reference == null || reference.Count != 3 || count < 3) return false;
            indices = new[] { reference[0], reference[1], reference[2] };
            return indices[0] == 0 && indices[2] == count - 1
                && (double)indices[1] / (count - 1) >= 0.25 && (double)indices[1] / (count - 1) <= 0.75;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static NinePointSurfaceCompensationResult Failed(HeightMap3D source,
            NinePointSurfaceCompensationFailureReason reason, string message)
        {
            return new NinePointSurfaceCompensationResult(reason, message, source);
        }
    }
}
