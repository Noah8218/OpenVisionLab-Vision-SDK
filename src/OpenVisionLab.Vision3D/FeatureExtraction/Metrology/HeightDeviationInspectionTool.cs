using System;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    public enum HeightDeviationDecision
    {
        Error = 0,
        Pass = 1,
        Fail = 2
    }

    public sealed class HeightDeviationInspectionResult
    {
        internal HeightDeviationInspectionResult(
            HeightDeviationDecision decision,
            string message,
            double lowDeviation,
            double highDeviation,
            double peakDeviation)
        {
            Decision = decision;
            Message = message;
            LowDeviation = lowDeviation;
            HighDeviation = highDeviation;
            PeakDeviation = peakDeviation;
        }

        public HeightDeviationDecision Decision { get; }
        public bool Success => Decision != HeightDeviationDecision.Error;
        public string Message { get; }
        public double LowDeviation { get; }
        public double HighDeviation { get; }
        public double PeakDeviation { get; }
    }

    /// <summary>
    /// Evaluates peak absolute deviation from finite height-grid summary
    /// statistics. Source identity, unit, and presentation remain caller-owned.
    /// </summary>
    public sealed class HeightDeviationInspectionTool
    {
        public HeightDeviationInspectionResult Execute(
            double minimum,
            double maximum,
            double mean,
            int validSampleCount,
            double peakTolerance)
        {
            if (validSampleCount <= 0
                || !IsFinite(minimum)
                || !IsFinite(maximum)
                || !IsFinite(mean)
                || minimum > maximum
                || mean < minimum
                || mean > maximum
                || !IsFinite(peakTolerance)
                || peakTolerance <= 0.0)
            {
                return new HeightDeviationInspectionResult(
                    HeightDeviationDecision.Error,
                    "Invalid height-grid statistics or tolerance.",
                    double.NaN,
                    double.NaN,
                    double.NaN);
            }

            double lowDeviation = Math.Abs(mean - minimum);
            double highDeviation = Math.Abs(maximum - mean);
            double peakDeviation = Math.Max(lowDeviation, highDeviation);
            if (!IsFinite(peakDeviation))
            {
                return new HeightDeviationInspectionResult(
                    HeightDeviationDecision.Error,
                    "Height deviation cannot be represented as a finite value.",
                    double.NaN,
                    double.NaN,
                    double.NaN);
            }

            HeightDeviationDecision decision = peakDeviation <= peakTolerance
                ? HeightDeviationDecision.Pass
                : HeightDeviationDecision.Fail;
            return new HeightDeviationInspectionResult(
                decision,
                decision == HeightDeviationDecision.Pass
                    ? "Peak absolute deviation is within tolerance."
                    : "Peak absolute deviation exceeds tolerance.",
                lowDeviation,
                highDeviation,
                peakDeviation);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
