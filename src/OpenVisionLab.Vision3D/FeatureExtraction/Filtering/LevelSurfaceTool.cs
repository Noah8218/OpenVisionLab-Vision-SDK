using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    public sealed class LevelSurfaceRegion
    {
        public LevelSurfaceRegion(
            int row,
            int column,
            int rowCount,
            int columnCount)
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

    public sealed class LevelSurfaceRegionEvidence
    {
        public LevelSurfaceRegionEvidence(
            int regionIndex,
            int validSampleCount)
        {
            RegionIndex = regionIndex;
            ValidSampleCount = validSampleCount;
        }

        public int RegionIndex { get; }
        public int ValidSampleCount { get; }
    }

    public sealed class LevelSurfaceOptions
    {
        public int MinimumValidSampleCount { get; set; }
        public DatumPlaneFitOptions DatumPlaneFitOptions { get; set; }
    }

    public sealed class LevelSurfaceResult
    {
        private LevelSurfaceResult(
            bool success,
            string message,
            IReadOnlyList<double> values,
            double fittedSlopeX,
            double fittedSlopeZ,
            double fittedIntercept,
            double targetHeight,
            int referenceSampleCount,
            double referenceResidualRms,
            double referenceResidualPeakToValley,
            double outputReferenceSlopeX,
            double outputReferenceSlopeZ,
            IReadOnlyList<LevelSurfaceRegionEvidence> regionEvidence,
            DatumPlaneFitResult datumPlaneFitEvidence = null,
            int[] referenceSampleIndices = null)
        {
            Success = success;
            Message = message ?? string.Empty;
            Values = values ?? Array.Empty<double>();
            FittedSlopeX = fittedSlopeX;
            FittedSlopeZ = fittedSlopeZ;
            FittedIntercept = fittedIntercept;
            TargetHeight = targetHeight;
            ReferenceSampleCount = referenceSampleCount;
            ReferenceResidualRms = referenceResidualRms;
            ReferenceResidualPeakToValley = referenceResidualPeakToValley;
            OutputReferenceSlopeX = outputReferenceSlopeX;
            OutputReferenceSlopeZ = outputReferenceSlopeZ;
            RegionEvidence = regionEvidence
                ?? Array.Empty<LevelSurfaceRegionEvidence>();
            DatumPlaneFitEvidence = datumPlaneFitEvidence;
            ReferenceSampleIndices = Array.AsReadOnly(referenceSampleIndices ?? Array.Empty<int>());
        }

        public bool Success { get; }
        public string Message { get; }
        public IReadOnlyList<double> Values { get; }
        public double FittedSlopeX { get; }
        public double FittedSlopeZ { get; }
        public double FittedIntercept { get; }
        public double TargetHeight { get; }
        public int ReferenceSampleCount { get; }
        public double ReferenceResidualRms { get; }
        public double ReferenceResidualPeakToValley { get; }
        public double OutputReferenceSlopeX { get; }
        public double OutputReferenceSlopeZ { get; }
        public IReadOnlyList<LevelSurfaceRegionEvidence> RegionEvidence { get; }
        public DatumPlaneFitResult DatumPlaneFitEvidence { get; }
        public IReadOnlyList<int> ReferenceSampleIndices { get; }

        internal static LevelSurfaceResult Completed(
            IReadOnlyList<double> values,
            LeastSquaresHeightFieldPlaneFitResult fit,
            double targetHeight,
            double referenceResidualRms,
            double referenceResidualPeakToValley,
            double outputReferenceSlopeX,
            double outputReferenceSlopeZ,
            IReadOnlyList<LevelSurfaceRegionEvidence> regionEvidence,
            DatumPlaneFitResult datumEvidence = null,
            int[] referenceSampleIndices = null)
        {
            return new LevelSurfaceResult(
                true,
                "Completed deterministic height-field surface leveling.",
                values,
                fit.SlopeX,
                fit.SlopeZ,
                fit.Intercept,
                targetHeight,
                fit.SampleCount,
                referenceResidualRms,
                referenceResidualPeakToValley,
                outputReferenceSlopeX,
                outputReferenceSlopeZ,
                regionEvidence,
                datumEvidence,
                referenceSampleIndices);
        }

        internal static LevelSurfaceResult Failed(string message, DatumPlaneFitResult datumEvidence = null, int[] referenceSampleIndices = null)
        {
            return new LevelSurfaceResult(
                false,
                message,
                Array.Empty<double>(),
                double.NaN,
                double.NaN,
                double.NaN,
                double.NaN,
                0,
                double.NaN,
                double.NaN,
                double.NaN,
                double.NaN,
                Array.Empty<LevelSurfaceRegionEvidence>(),
                datumEvidence,
                referenceSampleIndices);
        }
    }

    /// <summary>
    /// Fits a least-squares height plane over unique finite cells from one or
    /// more source-neutral grid regions, calculates raw-height residuals, and
    /// detrends the complete grid to the reference mean while preserving NaN
    /// cells and row-major grid positions. Identity and authored acceptance
    /// limits remain caller responsibilities.
    /// </summary>
    public sealed class LevelSurfaceTool
    {
        public const string Semantics =
            "least-squares-height-plane-detrend-to-reference-mean-v1";

        private readonly LeastSquaresHeightFieldPlaneFitTool planeFitTool =
            new LeastSquaresHeightFieldPlaneFitTool();

        public LevelSurfaceResult Execute(
            int rowCount,
            int columnCount,
            IReadOnlyList<double> values,
            IReadOnlyList<LevelSurfaceRegion> referenceRegions,
            LevelSurfaceOptions options,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                Validate(
                    rowCount,
                    columnCount,
                    values,
                    referenceRegions,
                    options);

                bool[] selected = new bool[values.Count];
                List<HeightFieldPlaneFitSample> samples =
                    new List<HeightFieldPlaneFitSample>();
                LevelSurfaceRegionEvidence[] evidence =
                    new LevelSurfaceRegionEvidence[referenceRegions.Count];
                for (int regionIndex = 0;
                     regionIndex < referenceRegions.Count;
                     regionIndex++)
                {
                    LevelSurfaceRegion region = referenceRegions[regionIndex];
                    int validInRegion = 0;
                    for (int row = region.Row;
                         row < region.Row + region.RowCount;
                         row++)
                    {
                        for (int column = region.Column;
                             column < region.Column + region.ColumnCount;
                             column++)
                        {
                            int index = checked(row * columnCount + column);
                            if ((index & 0x3fff) == 0)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                            }

                            double value = values[index];
                            if (!IsFinite(value))
                            {
                                continue;
                            }

                            validInRegion++;
                            if (!selected[index])
                            {
                                selected[index] = true;
                                samples.Add(new HeightFieldPlaneFitSample(
                                    new ThreeDPoint(
                                        column,
                                        value,
                                        row),
                                    value));
                            }
                        }
                    }

                    evidence[regionIndex] = new LevelSurfaceRegionEvidence(
                        regionIndex,
                        validInRegion);
                }

                if (samples.Count < options.MinimumValidSampleCount)
                {
                    return LevelSurfaceResult.Failed(
                        "Level Surface requires at least "
                        + options.MinimumValidSampleCount
                        + " unique finite reference samples; found "
                        + samples.Count
                        + ".");
                }

                DatumPlaneFitResult datum = null;
                int[] referenceIndices = Array.Empty<int>();
                IReadOnlyList<HeightFieldPlaneFitSample> fitSamples = samples;
                if (options.DatumPlaneFitOptions != null)
                {
                    referenceIndices = new int[samples.Count];
                    for (int index = 0; index < samples.Count; index++)
                        referenceIndices[index] = checked((int)samples[index].Position.Z * columnCount + (int)samples[index].Position.X);
                    datum = new DeterministicDatumPlaneFitTool().Execute(samples, options.DatumPlaneFitOptions, cancellationToken);
                    if (!datum.Success)
                        return LevelSurfaceResult.Failed("Reference datum was rejected: " + datum.FailureReason + ". " + datum.Message, datum, referenceIndices);
                    if (datum.InlierSampleCount < options.MinimumValidSampleCount)
                        return LevelSurfaceResult.Failed("Selected datum has fewer samples than Level Surface MinimumValidSampleCount.", datum, referenceIndices);
                    fitSamples = datum.InlierSamples;
                }
                LeastSquaresHeightFieldPlaneFitResult fit = datum == null ? planeFitTool.Execute(samples) : datum.Plane;
                double squaredResidualSum = 0.0;
                double minimumResidual = double.PositiveInfinity;
                double maximumResidual = double.NegativeInfinity;
                double targetHeight = 0.0;
                for (int sampleIndex = 0;
                     sampleIndex < fitSamples.Count;
                     sampleIndex++)
                {
                    HeightFieldPlaneFitSample sample = fitSamples[sampleIndex];
                    double residual = datum == null
                        ? sample.RawHeight - fit.EvaluateY(sample.Position.X, sample.Position.Z)
                        : datum.HeightResiduals[datum.InlierSampleIndices[sampleIndex]];
                    if (datum == null) squaredResidualSum += residual * residual;
                    minimumResidual = Math.Min(minimumResidual, residual);
                    maximumResidual = Math.Max(maximumResidual, residual);
                    if (datum == null) targetHeight += sample.RawHeight;
                }

                double referenceResidualRms = datum == null ? Math.Sqrt(squaredResidualSum / samples.Count) : datum.InlierHeightResidualRms;
                double referenceResidualPeakToValley =
                    maximumResidual - minimumResidual;
                targetHeight = datum == null ? targetHeight / samples.Count : datum.MeanInlierRawHeight;

                double matrixM21 = -fit.SlopeX;
                double matrixM22 = 1.0;
                double matrixM23 = -fit.SlopeZ;
                double matrixM24 = targetHeight - fit.Intercept;
                double[] output = new double[values.Count];
                for (int index = 0; index < values.Count; index++)
                {
                    if ((index & 0x3fff) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    double value = values[index];
                    output[index] = IsFinite(value)
                        ? matrixM21 * (index % columnCount)
                            + matrixM22 * value
                            + matrixM23 * (index / columnCount)
                            + matrixM24
                        : double.NaN;
                }

                HeightFieldPlaneFitSample[] outputSamples =
                    new HeightFieldPlaneFitSample[fitSamples.Count];
                for (int sampleIndex = 0;
                     sampleIndex < fitSamples.Count;
                     sampleIndex++)
                {
                    int row = (int)fitSamples[sampleIndex].Position.Z;
                    int column = (int)fitSamples[sampleIndex].Position.X;
                    double height = output[checked(row * columnCount + column)];
                    outputSamples[sampleIndex] = new HeightFieldPlaneFitSample(
                        new ThreeDPoint(column, height, row),
                        height);
                }

                LeastSquaresHeightFieldPlaneFitResult outputFit =
                    planeFitTool.Execute(outputSamples);
                return LevelSurfaceResult.Completed(
                    output,
                    fit,
                    targetHeight,
                    referenceResidualRms,
                    referenceResidualPeakToValley,
                    outputFit.SlopeX,
                    outputFit.SlopeZ,
                    evidence,
                    datum,
                    referenceIndices);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ArgumentException exception)
            {
                return LevelSurfaceResult.Failed(exception.Message);
            }
            catch (InvalidDataException exception)
            {
                return LevelSurfaceResult.Failed(exception.Message);
            }
            catch (OverflowException exception)
            {
                return LevelSurfaceResult.Failed(exception.Message);
            }
        }

        private static void Validate(
            int rowCount,
            int columnCount,
            IReadOnlyList<double> values,
            IReadOnlyList<LevelSurfaceRegion> referenceRegions,
            LevelSurfaceOptions options)
        {
            if (rowCount <= 0 || columnCount <= 0)
            {
                throw new InvalidDataException(
                    "Level Surface grid dimensions must be positive.");
            }

            if (values == null
                || values.Count != checked(rowCount * columnCount))
            {
                throw new InvalidDataException(
                    "Level Surface values must match the declared grid dimensions.");
            }

            if (referenceRegions == null || referenceRegions.Count == 0)
            {
                throw new InvalidDataException(
                    "Level Surface requires one or more reference regions.");
            }

            if (options == null || options.MinimumValidSampleCount < 3)
            {
                throw new InvalidDataException(
                    "Level Surface MinimumValidSampleCount must be at least three.");
            }

            for (int regionIndex = 0;
                 regionIndex < referenceRegions.Count;
                 regionIndex++)
            {
                LevelSurfaceRegion region = referenceRegions[regionIndex];
                if (region == null
                    || region.Row < 0
                    || region.Column < 0
                    || region.RowCount <= 0
                    || region.ColumnCount <= 0
                    || region.Row > rowCount - region.RowCount
                    || region.Column > columnCount - region.ColumnCount)
                {
                    throw new InvalidDataException(
                        "Level Surface reference regions must be valid source-grid rectangles.");
                }
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
