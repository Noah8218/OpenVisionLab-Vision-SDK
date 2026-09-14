using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    public enum LabeledEvidenceRole
    {
        Good = 0,
        Bad = 1,
        HeldOut = 2
    }

    public sealed class LabeledEvidenceStatisticsObservation
    {
        public LabeledEvidenceStatisticsObservation(
            string sampleIdentity,
            LabeledEvidenceRole role,
            double value)
        {
            SampleIdentity = sampleIdentity ?? string.Empty;
            Role = role;
            Value = value;
        }

        public string SampleIdentity { get; }

        public LabeledEvidenceRole Role { get; }

        public double Value { get; }
    }

    public sealed class LabeledEvidenceRoleStatistics
    {
        internal LabeledEvidenceRoleStatistics(
            LabeledEvidenceRole role,
            int sampleCount,
            int valueCount,
            double? minimum,
            double? maximum,
            double? mean,
            double? populationStandardDeviation)
        {
            Role = role;
            SampleCount = sampleCount;
            ValueCount = valueCount;
            Minimum = minimum;
            Maximum = maximum;
            Mean = mean;
            PopulationStandardDeviation = populationStandardDeviation;
        }

        public LabeledEvidenceRole Role { get; }

        public int SampleCount { get; }

        public int ValueCount { get; }

        public double? Minimum { get; }

        public double? Maximum { get; }

        public double? Mean { get; }

        public double? PopulationStandardDeviation { get; }
    }

    public sealed class LabeledEvidenceStatisticsResult
    {
        internal LabeledEvidenceStatisticsResult(
            bool success,
            string message,
            IReadOnlyList<LabeledEvidenceRoleStatistics> roleStatistics)
        {
            Success = success;
            Message = message;
            RoleStatistics = roleStatistics;
        }

        public bool Success { get; }

        public string Message { get; }

        public IReadOnlyList<LabeledEvidenceRoleStatistics> RoleStatistics { get; }
    }

    /// <summary>
    /// Calculates source-neutral descriptive statistics for labeled scalar
    /// observations. Sample identities are opaque keys used only for distinct
    /// counts. The tool does not own product roles, source routing, metric
    /// identity, development policy, warnings, or report composition.
    /// </summary>
    public sealed class LabeledEvidenceStatisticsTool
    {
        public LabeledEvidenceStatisticsResult Execute(
            IReadOnlyList<LabeledEvidenceStatisticsObservation> observations)
        {
            if (observations == null)
            {
                return Error("Labeled evidence observations are required.");
            }

            for (int index = 0; index < observations.Count; index++)
            {
                LabeledEvidenceStatisticsObservation observation = observations[index];
                if (observation == null)
                {
                    return Error("Labeled evidence observations cannot contain null entries.");
                }

                if (!IsSupportedRole(observation.Role))
                {
                    return Error("Labeled evidence role is not supported.");
                }

                if (!IsFinite(observation.Value))
                {
                    return Error("Labeled evidence values must be finite.");
                }
            }

            LabeledEvidenceRole[] roles =
            {
                LabeledEvidenceRole.Good,
                LabeledEvidenceRole.Bad,
                LabeledEvidenceRole.HeldOut
            };
            LabeledEvidenceRoleStatistics[] statistics = roles
                .Select(role => Calculate(role, observations))
                .ToArray();
            if (statistics.Any(item => item.ValueCount > 0
                && (!item.Minimum.HasValue
                    || !item.Maximum.HasValue
                    || !item.Mean.HasValue
                    || !item.PopulationStandardDeviation.HasValue
                    || !IsFinite(item.Minimum.Value)
                    || !IsFinite(item.Maximum.Value)
                    || !IsFinite(item.Mean.Value)
                    || !IsFinite(item.PopulationStandardDeviation.Value))))
            {
                return Error("Labeled evidence statistics produced a non-finite value or overflow.");
            }

            return new LabeledEvidenceStatisticsResult(
                true,
                "Labeled evidence statistics calculated.",
                statistics);
        }

        private static LabeledEvidenceRoleStatistics Calculate(
            LabeledEvidenceRole role,
            IReadOnlyList<LabeledEvidenceStatisticsObservation> observations)
        {
            LabeledEvidenceStatisticsObservation[] selected = observations
                .Where(item => item.Role == role)
                .ToArray();
            double[] values = selected.Select(item => item.Value).ToArray();
            if (values.Length == 0)
            {
                return new LabeledEvidenceRoleStatistics(
                    role,
                    0,
                    0,
                    null,
                    null,
                    null,
                    null);
            }

            double mean = values.Average();
            double variance = values.Sum(value => (value - mean) * (value - mean))
                / values.Length;
            return new LabeledEvidenceRoleStatistics(
                role,
                selected.Select(item => item.SampleIdentity)
                    .Distinct(StringComparer.Ordinal)
                    .Count(),
                values.Length,
                values.Min(),
                values.Max(),
                mean,
                Math.Sqrt(variance));
        }

        private static bool IsSupportedRole(LabeledEvidenceRole role)
        {
            return role == LabeledEvidenceRole.Good
                || role == LabeledEvidenceRole.Bad
                || role == LabeledEvidenceRole.HeldOut;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static LabeledEvidenceStatisticsResult Error(string message)
        {
            return new LabeledEvidenceStatisticsResult(
                false,
                message,
                Array.Empty<LabeledEvidenceRoleStatistics>());
        }
    }
}
