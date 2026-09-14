using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    public enum ThresholdObservationClass
    {
        Accepted = 0,
        Rejected = 1
    }

    public enum ThresholdCandidateLimitKind
    {
        Minimum = 0,
        Maximum = 1,
        Range = 2
    }

    public enum ThresholdCandidateDecisionKind
    {
        CorrectAccepted = 0,
        FalseReject = 1,
        CorrectRejected = 2,
        FalseAccept = 3
    }

    public sealed class ThresholdCandidateObservation
    {
        public ThresholdCandidateObservation(
            int observationIndex,
            ThresholdObservationClass expectedClass,
            double value)
        {
            ObservationIndex = observationIndex;
            ExpectedClass = expectedClass;
            Value = value;
        }

        public int ObservationIndex { get; }

        public ThresholdObservationClass ExpectedClass { get; }

        public double Value { get; }
    }

    public sealed class ThresholdCandidateDecision
    {
        internal ThresholdCandidateDecision(
            int observationIndex,
            ThresholdObservationClass expectedClass,
            ThresholdObservationClass predictedClass,
            ThresholdCandidateDecisionKind decision,
            double value)
        {
            ObservationIndex = observationIndex;
            ExpectedClass = expectedClass;
            PredictedClass = predictedClass;
            Decision = decision;
            Value = value;
        }

        public int ObservationIndex { get; }

        public ThresholdObservationClass ExpectedClass { get; }

        public ThresholdObservationClass PredictedClass { get; }

        public ThresholdCandidateDecisionKind Decision { get; }

        public double Value { get; }
    }

    public sealed class ThresholdCandidateAnalysisCandidate
    {
        internal ThresholdCandidateAnalysisCandidate(
            ThresholdCandidateLimitKind limitKind,
            double? minimum,
            double? maximum,
            int acceptedAcceptedCount,
            int acceptedRejectedCount,
            int rejectedRejectedCount,
            int rejectedAcceptedCount,
            IReadOnlyList<ThresholdCandidateDecision> decisions)
        {
            LimitKind = limitKind;
            Minimum = minimum;
            Maximum = maximum;
            AcceptedAcceptedCount = acceptedAcceptedCount;
            AcceptedRejectedCount = acceptedRejectedCount;
            RejectedRejectedCount = rejectedRejectedCount;
            RejectedAcceptedCount = rejectedAcceptedCount;
            Decisions = decisions;
        }

        public ThresholdCandidateLimitKind LimitKind { get; }

        public double? Minimum { get; }

        public double? Maximum { get; }

        public int AcceptedAcceptedCount { get; }

        public int AcceptedRejectedCount { get; }

        public int RejectedRejectedCount { get; }

        public int RejectedAcceptedCount { get; }

        public IReadOnlyList<ThresholdCandidateDecision> Decisions { get; }

        public int ErrorCount => AcceptedRejectedCount + RejectedAcceptedCount;
    }

    public sealed class ThresholdCandidateAnalysisResult
    {
        internal ThresholdCandidateAnalysisResult(
            bool success,
            string message,
            IReadOnlyList<ThresholdCandidateAnalysisCandidate> candidates)
        {
            Success = success;
            Message = message;
            Candidates = candidates;
        }

        public bool Success { get; }

        public string Message { get; }

        public IReadOnlyList<ThresholdCandidateAnalysisCandidate> Candidates { get; }
    }

    /// <summary>
    /// Generates and evaluates deterministic Minimum, Maximum, and Range
    /// candidates for source-neutral accepted/rejected scalar observations.
    /// The tool does not own metric/source identity, candidate hashes,
    /// development/Held-out routing, warnings, or product report composition.
    /// </summary>
    public sealed class ThresholdCandidateAnalysisTool
    {
        public ThresholdCandidateAnalysisResult Execute(
            IReadOnlyList<ThresholdCandidateObservation> observations)
        {
            if (observations == null)
            {
                return Error("Threshold observations are required.");
            }

            if (observations.Count == 0)
            {
                return Error("At least one accepted and one rejected observation are required.");
            }

            HashSet<int> indices = new HashSet<int>();
            bool hasAccepted = false;
            bool hasRejected = false;
            for (int index = 0; index < observations.Count; index++)
            {
                ThresholdCandidateObservation observation = observations[index];
                if (observation == null)
                {
                    return Error("Threshold observations cannot contain null entries.");
                }

                if (!indices.Add(observation.ObservationIndex))
                {
                    return Error("Threshold observation indices must be unique.");
                }

                if (!IsFinite(observation.Value))
                {
                    return Error("Threshold observation values must be finite.");
                }

                if (observation.ExpectedClass == ThresholdObservationClass.Accepted)
                {
                    hasAccepted = true;
                }
                else if (observation.ExpectedClass == ThresholdObservationClass.Rejected)
                {
                    hasRejected = true;
                }
                else
                {
                    return Error("Threshold observation class is not supported.");
                }
            }

            if (!hasAccepted || !hasRejected)
            {
                return Error("At least one accepted and one rejected observation are required.");
            }

            double[] values = observations
                .Select(observation => observation.Value)
                .Distinct()
                .OrderBy(value => value)
                .ToArray();
            ThresholdCandidateAnalysisCandidate[] candidates =
            {
                SelectBest(ThresholdCandidateLimitKind.Minimum, values, observations),
                SelectBest(ThresholdCandidateLimitKind.Maximum, values, observations),
                SelectBest(ThresholdCandidateLimitKind.Range, values, observations)
            };
            return new ThresholdCandidateAnalysisResult(
                true,
                "Threshold candidates calculated.",
                candidates);
        }

        private static ThresholdCandidateAnalysisCandidate SelectBest(
            ThresholdCandidateLimitKind kind,
            IReadOnlyList<double> values,
            IReadOnlyList<ThresholdCandidateObservation> observations)
        {
            IEnumerable<ThresholdCandidateAnalysisCandidate> evaluated;
            switch (kind)
            {
                case ThresholdCandidateLimitKind.Minimum:
                    evaluated = MinimumCandidates(values)
                        .Select(minimum => Evaluate(kind, minimum, null, observations));
                    break;
                case ThresholdCandidateLimitKind.Maximum:
                    evaluated = MaximumCandidates(values)
                        .Select(maximum => Evaluate(kind, null, maximum, observations));
                    break;
                case ThresholdCandidateLimitKind.Range:
                    evaluated = values.SelectMany(minimum => values
                        .Where(maximum => maximum >= minimum)
                        .Select(maximum => Evaluate(kind, minimum, maximum, observations)));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }

            IOrderedEnumerable<ThresholdCandidateAnalysisCandidate> ordered = evaluated
                .OrderBy(candidate => candidate.ErrorCount)
                .ThenBy(candidate => candidate.RejectedAcceptedCount)
                .ThenBy(candidate => candidate.AcceptedRejectedCount);
            switch (kind)
            {
                case ThresholdCandidateLimitKind.Minimum:
                    return ordered.ThenByDescending(candidate => candidate.Minimum).First();
                case ThresholdCandidateLimitKind.Maximum:
                    return ordered.ThenBy(candidate => candidate.Maximum).First();
                case ThresholdCandidateLimitKind.Range:
                    return ordered
                        .ThenBy(candidate => candidate.Maximum.Value - candidate.Minimum.Value)
                        .ThenByDescending(candidate => candidate.Minimum)
                        .ThenBy(candidate => candidate.Maximum)
                        .First();
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private static IEnumerable<double> MinimumCandidates(IReadOnlyList<double> values)
        {
            for (int index = 0; index < values.Count; index++)
            {
                yield return values[index];
            }

            double rejectAll = NextUp(values[values.Count - 1]);
            if (IsFinite(rejectAll))
            {
                yield return rejectAll;
            }
        }

        private static IEnumerable<double> MaximumCandidates(IReadOnlyList<double> values)
        {
            double rejectAll = NextDown(values[0]);
            if (IsFinite(rejectAll))
            {
                yield return rejectAll;
            }

            for (int index = 0; index < values.Count; index++)
            {
                yield return values[index];
            }
        }

        private static ThresholdCandidateAnalysisCandidate Evaluate(
            ThresholdCandidateLimitKind kind,
            double? minimum,
            double? maximum,
            IReadOnlyList<ThresholdCandidateObservation> observations)
        {
            ThresholdCandidateDecision[] decisions = observations
                .Select(observation =>
                {
                    bool accepted;
                    switch (kind)
                    {
                        case ThresholdCandidateLimitKind.Minimum:
                            accepted = observation.Value >= minimum.Value;
                            break;
                        case ThresholdCandidateLimitKind.Maximum:
                            accepted = observation.Value <= maximum.Value;
                            break;
                        case ThresholdCandidateLimitKind.Range:
                            accepted = observation.Value >= minimum.Value
                                && observation.Value <= maximum.Value;
                            break;
                        default:
                            accepted = false;
                            break;
                    }

                    ThresholdObservationClass predicted = accepted
                        ? ThresholdObservationClass.Accepted
                        : ThresholdObservationClass.Rejected;
                    ThresholdCandidateDecisionKind decision;
                    if (observation.ExpectedClass == ThresholdObservationClass.Accepted
                        && predicted == ThresholdObservationClass.Accepted)
                    {
                        decision = ThresholdCandidateDecisionKind.CorrectAccepted;
                    }
                    else if (observation.ExpectedClass == ThresholdObservationClass.Accepted)
                    {
                        decision = ThresholdCandidateDecisionKind.FalseReject;
                    }
                    else if (predicted == ThresholdObservationClass.Rejected)
                    {
                        decision = ThresholdCandidateDecisionKind.CorrectRejected;
                    }
                    else
                    {
                        decision = ThresholdCandidateDecisionKind.FalseAccept;
                    }

                    return new ThresholdCandidateDecision(
                        observation.ObservationIndex,
                        observation.ExpectedClass,
                        predicted,
                        decision,
                        observation.Value);
                })
                .ToArray();

            return new ThresholdCandidateAnalysisCandidate(
                kind,
                minimum,
                maximum,
                decisions.Count(decision =>
                    decision.Decision == ThresholdCandidateDecisionKind.CorrectAccepted),
                decisions.Count(decision =>
                    decision.Decision == ThresholdCandidateDecisionKind.FalseReject),
                decisions.Count(decision =>
                    decision.Decision == ThresholdCandidateDecisionKind.CorrectRejected),
                decisions.Count(decision =>
                    decision.Decision == ThresholdCandidateDecisionKind.FalseAccept),
                decisions);
        }

        private static double NextUp(double value)
        {
            if (double.IsNaN(value) || value == double.PositiveInfinity)
            {
                return value;
            }

            if (value == 0.0)
            {
                return double.Epsilon;
            }

            long bits = BitConverter.DoubleToInt64Bits(value);
            bits += value > 0.0 ? 1 : -1;
            return BitConverter.Int64BitsToDouble(bits);
        }

        private static double NextDown(double value)
        {
            if (double.IsNaN(value) || value == double.NegativeInfinity)
            {
                return value;
            }

            if (value == 0.0)
            {
                return -double.Epsilon;
            }

            long bits = BitConverter.DoubleToInt64Bits(value);
            bits += value > 0.0 ? -1 : 1;
            return BitConverter.Int64BitsToDouble(bits);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static ThresholdCandidateAnalysisResult Error(string message)
        {
            return new ThresholdCandidateAnalysisResult(
                false,
                message,
                Array.Empty<ThresholdCandidateAnalysisCandidate>());
        }
    }
}
