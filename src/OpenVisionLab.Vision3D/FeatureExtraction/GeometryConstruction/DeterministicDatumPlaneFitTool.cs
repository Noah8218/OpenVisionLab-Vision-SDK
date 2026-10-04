using System;
using System.Collections.Generic;
using System.Threading;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    public sealed class DatumPlaneFitOptions
    {
        public double MaximumAbsoluteHeightResidual { get; set; }
        public int MinimumInlierCount { get; set; } = 3;
        public double MinimumInlierRatio { get; set; }
    }

    public enum DatumPlaneFitFailureReason
    {
        None,
        InsufficientSupport,
        AmbiguousSupport,
        DegenerateGeometry,
        NumericalFailure,
        RefitDidNotConverge
    }

    /// <summary>
    /// Immutable reference selection. Indices address the original ordered
    /// input; residuals and the threshold use its geometric Y unit. No source,
    /// calibration, probability, or product acceptance is inferred.
    /// </summary>
    public sealed class DatumPlaneFitResult
    {
        internal DatumPlaneFitResult(
            DatumPlaneFitFailureReason failureReason, string message, int inputSampleCount,
            LeastSquaresHeightFieldPlaneFitResult plane, HeightFieldPlaneFitSample[] inlierSamples,
            int[] inlierIndices, int[] rejectedIndices, int[] hypothesisIndices, double[] residuals,
            DatumPlaneFitOptions options, double inlierHeightResidualRms,
            double meanInlierRawHeight, int evaluatedHypothesisCount, int refitIterationCount)
        {
            FailureReason = failureReason;
            Message = message;
            InputSampleCount = inputSampleCount;
            Plane = plane;
            InlierSamples = Array.AsReadOnly(inlierSamples);
            InlierSampleIndices = Array.AsReadOnly(inlierIndices);
            RejectedSampleIndices = Array.AsReadOnly(rejectedIndices);
            HypothesisSampleIndices = Array.AsReadOnly(hypothesisIndices);
            HeightResiduals = Array.AsReadOnly(residuals);
            MaximumAbsoluteHeightResidual = options.MaximumAbsoluteHeightResidual;
            MinimumInlierCount = options.MinimumInlierCount;
            MinimumInlierRatio = options.MinimumInlierRatio;
            InlierHeightResidualRms = inlierHeightResidualRms;
            MeanInlierRawHeight = meanInlierRawHeight;
            EvaluatedHypothesisCount = evaluatedHypothesisCount;
            RefitIterationCount = refitIterationCount;
        }

        public bool Success => FailureReason == DatumPlaneFitFailureReason.None;
        public DatumPlaneFitFailureReason FailureReason { get; }
        public string Message { get; }
        public int InputSampleCount { get; }
        public int InlierSampleCount => InlierSamples.Count;
        public LeastSquaresHeightFieldPlaneFitResult Plane { get; }
        public IReadOnlyList<HeightFieldPlaneFitSample> InlierSamples { get; }
        public IReadOnlyList<int> InlierSampleIndices { get; }
        public IReadOnlyList<int> RejectedSampleIndices { get; }
        public IReadOnlyList<int> HypothesisSampleIndices { get; }
        public IReadOnlyList<double> HeightResiduals { get; }
        public double MaximumAbsoluteHeightResidual { get; }
        public int MinimumInlierCount { get; }
        public double MinimumInlierRatio { get; }
        public double InlierHeightResidualRms { get; }
        public double MeanInlierRawHeight { get; }
        public int EvaluatedHypothesisCount { get; }
        public int RefitIterationCount { get; }
    }

    /// <summary>
    /// Bounded deterministic majority consensus for Y=f(X,Z). Uses all triples
    /// from at most twelve evenly indexed samples, then refits with the existing
    /// centered least-squares solver. Equal-support distinct memberships fail
    /// closed. This finite hypothesis pool is not an exhaustive/global fit.
    /// </summary>
    public sealed class DeterministicDatumPlaneFitTool
    {
        public const string Semantics = "bounded-height-residual-datum-consensus-v1";
        public const int MaximumHypothesisSampleCount = 12;
        public const int MaximumRefitIterations = 10;

        private readonly LeastSquaresHeightFieldPlaneFitTool planeFit = new LeastSquaresHeightFieldPlaneFitTool();

        public DatumPlaneFitResult Execute(
            IReadOnlyList<HeightFieldPlaneFitSample> samples, DatumPlaneFitOptions options,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Validate(samples, options, cancellationToken);
            int poolCount = Math.Min(MaximumHypothesisSampleCount, samples.Count);
            int[] hypothesisIndices = new int[poolCount];
            for (int index = 0; index < poolCount; index++)
                hypothesisIndices[index] = (int)((long)index * (samples.Count - 1) / (poolCount - 1));

            int evaluated = 0;
            int[] best = Array.Empty<int>();
            bool ambiguous = false;
            bool hadGeometry = false;
            bool hadNumericalFailure = false;
            // ponytail: at most 220 hypotheses; a larger/geometry-aware pool needs measured missed-support evidence.
            for (int first = 0; first < poolCount - 2; first++)
            {
                for (int second = first + 1; second < poolCount - 1; second++)
                {
                    for (int third = second + 1; third < poolCount; third++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        evaluated++;
                        LeastSquaresHeightFieldPlaneFitResult candidate;
                        try
                        {
                            candidate = planeFit.Execute(new[]
                            {
                                samples[hypothesisIndices[first]], samples[hypothesisIndices[second]], samples[hypothesisIndices[third]]
                            });
                        }
                        catch (ArgumentException) { continue; }
                        catch (InvalidOperationException) { hadNumericalFailure = true; continue; }
                        hadGeometry = true;
                        if (!TryClassify(samples, candidate, options.MaximumAbsoluteHeightResidual, cancellationToken, out int[] inliers, out _))
                        {
                            hadNumericalFailure = true;
                            continue;
                        }
                        if (inliers.Length > best.Length)
                        {
                            best = inliers;
                            ambiguous = false;
                        }
                        else if (inliers.Length == best.Length && !SameIndices(inliers, best))
                        {
                            ambiguous = true;
                        }
                    }
                }
            }

            if (!hadGeometry)
                return Failed(hadNumericalFailure ? DatumPlaneFitFailureReason.NumericalFailure : DatumPlaneFitFailureReason.DegenerateGeometry,
                    "No representable non-degenerate datum hypothesis was available.", samples.Count, hypothesisIndices, evaluated, options);
            if (!HasSupport(best.Length, samples.Count, options))
                return Failed(DatumPlaneFitFailureReason.InsufficientSupport, "No datum membership meets the declared count and majority ratio.", samples.Count, hypothesisIndices, evaluated, options);
            if (ambiguous)
                return Failed(DatumPlaneFitFailureReason.AmbiguousSupport, "Distinct datum memberships share the maximum support count.", samples.Count, hypothesisIndices, evaluated, options);

            for (int iteration = 1; iteration <= MaximumRefitIterations; iteration++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                HeightFieldPlaneFitSample[] selected = new HeightFieldPlaneFitSample[best.Length];
                for (int index = 0; index < best.Length; index++) selected[index] = samples[best[index]];
                LeastSquaresHeightFieldPlaneFitResult fitted;
                try { fitted = planeFit.Execute(selected); }
                catch (ArgumentException)
                {
                    return Failed(DatumPlaneFitFailureReason.DegenerateGeometry, "The selected datum does not span both horizontal axes.", samples.Count, hypothesisIndices, evaluated, options, iteration);
                }
                catch (InvalidOperationException)
                {
                    return Failed(DatumPlaneFitFailureReason.NumericalFailure, "The selected datum cannot be fitted in the finite calculation range.", samples.Count, hypothesisIndices, evaluated, options, iteration);
                }
                if (!TryClassify(samples, fitted, options.MaximumAbsoluteHeightResidual, cancellationToken, out int[] refined, out double[] residuals))
                    return Failed(DatumPlaneFitFailureReason.NumericalFailure, "Datum height residuals cannot be represented as finite values.", samples.Count, hypothesisIndices, evaluated, options, iteration);
                if (!HasSupport(refined.Length, samples.Count, options))
                    return Failed(DatumPlaneFitFailureReason.InsufficientSupport, "Refitting lost the declared datum support.", samples.Count, hypothesisIndices, evaluated, options, iteration);
                if (!SameIndices(best, refined))
                {
                    best = refined;
                    continue;
                }

                List<int> rejected = new List<int>();
                int inlierIndex = 0;
                double rmsScale = 0.0;
                double scaledSquaredSum = 0.0;
                double rawOrigin = selected[0].RawHeight;
                double rawDeltaSum = 0.0;
                for (int index = 0; index < samples.Count; index++)
                {
                    if ((index & 0x3ff) == 0) cancellationToken.ThrowIfCancellationRequested();
                    if (inlierIndex < best.Length && best[inlierIndex] == index)
                    {
                        double magnitude = Math.Abs(residuals[index]);
                        if (magnitude > rmsScale)
                        {
                            double ratio = rmsScale / magnitude;
                            scaledSquaredSum = 1.0 + scaledSquaredSum * ratio * ratio;
                            rmsScale = magnitude;
                        }
                        else if (magnitude > 0.0)
                        {
                            double ratio = magnitude / rmsScale;
                            scaledSquaredSum += ratio * ratio;
                        }
                        rawDeltaSum += samples[index].RawHeight - rawOrigin;
                        inlierIndex++;
                    }
                    else rejected.Add(index);
                }
                double meanRaw = rawOrigin + rawDeltaSum / selected.Length;
                if (!IsFinite(rawDeltaSum) || !IsFinite(meanRaw))
                    return Failed(DatumPlaneFitFailureReason.NumericalFailure, "The datum raw-height mean exceeds the finite range.", samples.Count, hypothesisIndices, evaluated, options, iteration);
                double rms = rmsScale == 0.0 ? 0.0 : rmsScale * Math.Min(1.0, Math.Sqrt(scaledSquaredSum / selected.Length));
                return new DatumPlaneFitResult(DatumPlaneFitFailureReason.None, "Completed bounded datum consensus and stable least-squares refit.",
                    samples.Count, fitted, selected, best, rejected.ToArray(), hypothesisIndices, residuals,
                    options, rms, meanRaw, evaluated, iteration);
            }
            return Failed(DatumPlaneFitFailureReason.RefitDidNotConverge, "Datum membership did not stabilize within the declared refit limit.",
                samples.Count, hypothesisIndices, evaluated, options, MaximumRefitIterations);
        }

        private static bool TryClassify(
            IReadOnlyList<HeightFieldPlaneFitSample> samples, LeastSquaresHeightFieldPlaneFitResult plane, double threshold,
            CancellationToken cancellationToken, out int[] indices, out double[] residuals)
        {
            List<int> inliers = new List<int>();
            residuals = new double[samples.Count];
            for (int index = 0; index < samples.Count; index++)
            {
                if ((index & 0x3ff) == 0) cancellationToken.ThrowIfCancellationRequested();
                // Convert centered orthogonal distance to Y residual without subtracting rounded global heights.
                double residual = plane.SignedDistance(samples[index].Position) / plane.Normal.Y;
                if (!IsFinite(residual)) { indices = Array.Empty<int>(); return false; }
                residuals[index] = residual;
                if (Math.Abs(residual) <= threshold) inliers.Add(index);
            }
            indices = inliers.ToArray();
            return true;
        }

        private static bool HasSupport(int count, int total, DatumPlaneFitOptions options)
        {
            return count >= options.MinimumInlierCount && count / (double)total >= options.MinimumInlierRatio;
        }

        private static bool SameIndices(int[] first, int[] second)
        {
            if (first.Length != second.Length) return false;
            for (int index = 0; index < first.Length; index++)
                if (first[index] != second[index]) return false;
            return true;
        }

        private static DatumPlaneFitResult Failed(
            DatumPlaneFitFailureReason reason, string message, int count, int[] hypothesisIndices, int evaluated, DatumPlaneFitOptions options, int iterations = 0)
        {
            return new DatumPlaneFitResult(reason, message, count, null, Array.Empty<HeightFieldPlaneFitSample>(),
                Array.Empty<int>(), Array.Empty<int>(), hypothesisIndices, Array.Empty<double>(), options, double.NaN, double.NaN, evaluated, iterations);
        }

        private static void Validate(IReadOnlyList<HeightFieldPlaneFitSample> samples, DatumPlaneFitOptions options, CancellationToken cancellationToken)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (samples.Count < 3) throw new ArgumentException("Datum fitting requires at least three finite samples.", nameof(samples));
            if (!IsFinite(options.MaximumAbsoluteHeightResidual) || options.MaximumAbsoluteHeightResidual <= 0.0
                || options.MinimumInlierCount < 3 || !IsFinite(options.MinimumInlierRatio)
                || options.MinimumInlierRatio <= 0.5 || options.MinimumInlierRatio > 1.0)
                throw new ArgumentException("Datum options require a positive finite Y residual, at least three inliers, and a ratio greater than 0.5 through 1.", nameof(options));
            for (int index = 0; index < samples.Count; index++)
            {
                if ((index & 0x3ff) == 0) cancellationToken.ThrowIfCancellationRequested();
                HeightFieldPlaneFitSample sample = samples[index];
                if (sample == null || sample.Position == null || !sample.Position.IsFinite || !IsFinite(sample.RawHeight))
                    throw new ArgumentException("Datum samples must contain finite positions and raw heights.", nameof(samples));
            }
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
