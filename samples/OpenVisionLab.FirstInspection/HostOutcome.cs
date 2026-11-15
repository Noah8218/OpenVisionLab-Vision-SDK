using System;
using OpenVisionLab.Vision2D.Tool;
using OpenVisionLab.Vision2D.Result;
using OpenVisionLab.Vision3D.FeatureExtraction;
using OpenVisionLab.Vision3D.Inspection;

namespace OpenVisionLab.FirstInspection
{
    // Host policy example, not a universal SDK Success-to-machine-OK conversion.
    internal sealed class HostOutcome
    {
        private HostOutcome(string execution, bool measured, string decision)
        {
            Execution = execution;
            MeasurementEstablished = measured;
            Decision = decision;
        }

        public string Execution { get; }
        public bool MeasurementEstablished { get; }
        public string Decision { get; }

        internal static HostOutcome Completed(bool measured, bool accepted) => new HostOutcome("Completed", measured, accepted ? "Pass" : "Fail");
        internal static HostOutcome NotMeasured() => new HostOutcome("Completed", false, "NotEvaluated");
        internal static HostOutcome Faulted() => new HostOutcome("Faulted", false, "NotEvaluated");
        internal static HostOutcome Canceled() => new HostOutcome("Canceled", false, "NotEvaluated");

        internal static HostOutcome Matching(VisionToolResult result)
        {
            if (result == null) return Faulted();
            if (result.ErrorCode == VisionToolErrorCode.MatchingNoResult || result.ErrorCode == VisionToolErrorCode.MatchingAmbiguous) return Completed(false, false);
            if (!result.Success) return Faulted();
            return Metric(result, "ResultCount", out double count) && count >= 1 ? Completed(true, true) : NotMeasured();
        }

        internal static HostOutcome Qr(VisionToolResult result)
        {
            if (result == null) return Faulted();
            if (result.ErrorCode == VisionToolErrorCode.QrCodeNotDetected || result.ErrorCode == VisionToolErrorCode.QrCodeDecodeFailed || result.ErrorCode == VisionToolErrorCode.QrCodeCountMismatch) return Completed(false, false);
            if (!result.Success) return Faulted();
            if (!Metric(result, "QrExpectedCount", out double expected) || !Metric(result, "QrDecodedCount", out double decoded) || expected < 1) return NotMeasured();
            return Completed(decoded > 0, decoded == expected);
        }

        internal static HostOutcome Blob(VisionToolResult result, int acceptedCandidateCount)
        {
            if (result == null || (!result.Success && result.ErrorCode != VisionToolErrorCode.BlobNoResult)) return Faulted();
            if (acceptedCandidateCount < 0) return NotMeasured();
            return Completed(result.Success, result.Success && acceptedCandidateCount > 0);
        }

        internal static HostOutcome ThreeD(ThreeDInspectionResult result, string metric, string expectedUnit)
        {
            if (result == null) return Faulted();
            if (result.MeasurementOutcome == ThreeDMeasurementOutcome.NotMeasured)
                return result.ErrorCode == ThreeDInspectionErrorCode.InsufficientValidSamples || result.ErrorCode == ThreeDInspectionErrorCode.InsufficientValidCoverage ? NotMeasured() : Faulted();
            if (!result.TryGetMetric(metric, out double value, out string unit) || !Finite(value) || string.IsNullOrWhiteSpace(expectedUnit) || unit != expectedUnit) return NotMeasured();
            return Completed(true, result.MeasurementOutcome == ThreeDMeasurementOutcome.Passed);
        }

        internal static HostOutcome DualSurface(DualSurfaceThicknessInspectionResult result)
        {
            if (result == null || !result.Success) return Faulted();
            if (!Finite(result.Mean) || !Finite(result.RootMeanSquareSpread)) return NotMeasured();
            return Completed(true, result.Decision == DualSurfaceThicknessDecision.Pass);
        }

        internal static HostOutcome Caliper(VisionToolResult result, CaliperMeasurementEvidence evidence)
        {
            if (result == null || !result.Success) return result != null && (result.ErrorCode == VisionToolErrorCode.CaliperNoEdge || result.ErrorCode == VisionToolErrorCode.CaliperGeometryDegenerate) ? NotMeasured() : Faulted();
            if (evidence == null || !evidence.MeasurementEstablished) return NotMeasured();
            if (evidence.Acceptance == CaliperAcceptance.NotEvaluated) return new HostOutcome("Completed", true, "NotEvaluated");
            return Completed(true, evidence.Acceptance == CaliperAcceptance.Accepted);
        }

        private static bool Metric(VisionToolResult result, string name, out double value) => result.Metrics.TryGetValue(name, out value) && Finite(value);
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
