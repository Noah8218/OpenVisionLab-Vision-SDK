#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        public string MachineDecision { get; } = "NotProvided";
        public string? FailureStage { get; private set; }
        public string? ErrorCode { get; private set; }
        public string? ErrorType { get; private set; }
        public string? ErrorMessage { get; private set; }

        internal HostOutcome WithFailure(string stage, string? code, string? type, string? message)
        {
            return new HostOutcome(Execution, MeasurementEstablished, Decision) { FailureStage = stage, ErrorCode = code, ErrorType = type, ErrorMessage = message };
        }

        internal static HostOutcome Frame(string execution, bool measured, string decision) => new HostOutcome(execution, measured, decision);
        internal static HostOutcome Faulted(string stage, Exception error) => Faulted().WithFailure(stage, error.GetBaseException().GetType().Name, error.GetBaseException().GetType().FullName, error.GetBaseException().Message);
        internal static int ExitCode(HostOutcome outcome) => outcome.Execution == "Faulted" || outcome.Execution == "AcquisitionTimeout" ? 3
            : outcome.Execution == "Canceled" ? 130 : outcome.Decision == "Pass" ? 0 : 2;

        internal static HostOutcome Summarize(IEnumerable<HostOutcome> outcomes)
        {
            HostOutcome[] values = outcomes.ToArray();
            return values.FirstOrDefault(o => ExitCode(o) == 3) ?? values.FirstOrDefault(o => ExitCode(o) == 130)
                ?? (values.Length == 0 ? NotMeasured() : Completed(values.All(o => o.MeasurementEstablished), values.All(o => ExitCode(o) == 0)));
        }

        // Additive envelope v2; existing status and evidence fields remain intact.
        internal static void Write(object report, string commandId, string? shotId, JsonSerializerOptions options)
        {
            Console.Error.WriteLine("CommandTerminal|CommandId=" + commandId + (shotId == null ? "" : "|ShotId=" + shotId));
            Console.WriteLine(Serialize(report, commandId, shotId, options));
        }

        internal static string Serialize(object report, string commandId, string? shotId, JsonSerializerOptions options)
        {
            JsonObject json = JsonSerializer.SerializeToNode(report, options)!.AsObject();
            json["SchemaVersion"] = 2;
            json["CommandId"] = commandId;
            if (!json.ContainsKey("ShotId")) json["ShotId"] = shotId;
            if (!json.ContainsKey("Outcome")) json["Outcome"] = JsonSerializer.SerializeToNode(NotMeasured(), options);
            json[nameof(MachineDecision)] = "NotProvided";
            return json.ToJsonString(options);
        }

        internal static HostOutcome Completed(bool measured, bool accepted) => new HostOutcome("Completed", measured, accepted ? "Pass" : "Fail");
        internal static HostOutcome NotMeasured() => new HostOutcome("Completed", false, "NotEvaluated");
        internal static HostOutcome Faulted() => new HostOutcome("Faulted", false, "NotEvaluated");
        internal static HostOutcome Canceled() => new HostOutcome("Canceled", false, "NotEvaluated").WithFailure("Cancellation", "Canceled", null, "Cancellation requested.");

        internal static HostOutcome Matching(VisionToolResult result)
        {
            if (result == null) return Faulted();
            if (result.ErrorCode == VisionToolErrorCode.MatchingNoResult || result.ErrorCode == VisionToolErrorCode.MatchingAmbiguous) return Detail(Completed(false, false), result, "Matching");
            if (!result.Success) return Detail(Faulted(), result, "Matching");
            return Metric(result, "ResultCount", out double count) && count >= 1 ? Completed(true, true) : NotMeasured();
        }

        internal static HostOutcome Qr(VisionToolResult result)
        {
            if (result == null) return Faulted();
            if (result.ErrorCode == VisionToolErrorCode.QrCodeNotDetected || result.ErrorCode == VisionToolErrorCode.QrCodeDecodeFailed || result.ErrorCode == VisionToolErrorCode.QrCodeCountMismatch) return Detail(Completed(false, false), result, "QrReading");
            if (!result.Success) return Detail(Faulted(), result, "QrReading");
            if (!Metric(result, "QrExpectedCount", out double expected) || !Metric(result, "QrDecodedCount", out double decoded) || expected < 1) return NotMeasured();
            return Completed(decoded > 0, decoded == expected);
        }

        internal static HostOutcome Blob(VisionToolResult result, int acceptedCandidateCount)
        {
            if (result == null) return Faulted();
            if (!result.Success && result.ErrorCode != VisionToolErrorCode.BlobNoResult) return Detail(Faulted(), result, "Blob");
            if (acceptedCandidateCount < 0) return NotMeasured();
            return result.Success ? Completed(true, acceptedCandidateCount > 0) : Detail(Completed(false, false), result, "Blob");
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
            if (result == null) return Faulted();
            if (!result.Success) return Detail(result.ErrorCode == VisionToolErrorCode.CaliperNoEdge || result.ErrorCode == VisionToolErrorCode.CaliperGeometryDegenerate ? NotMeasured() : Faulted(), result, "Caliper");
            if (evidence == null || !evidence.MeasurementEstablished) return NotMeasured();
            if (evidence.Acceptance == CaliperAcceptance.NotEvaluated) return new HostOutcome("Completed", true, "NotEvaluated");
            return Completed(true, evidence.Acceptance == CaliperAcceptance.Accepted);
        }

        private static bool Metric(VisionToolResult result, string name, out double value) => result.Metrics.TryGetValue(name, out value) && Finite(value);
        private static HostOutcome Detail(HostOutcome outcome, VisionToolResult result, string stage) => outcome.WithFailure(stage, result.ErrorName, result.Exception?.GetType().FullName, result.Message);
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
