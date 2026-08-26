using OpenVisionLab.Vision2D.Blob;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Result;
using OpenVisionLab.Vision2D.Tool;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class VisionObjectCandidateSmokeSuite
    {
        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("Blob publishes stable one-pass accepted and rejected candidates", TestBlobCandidates);
            yield return new SmokeCase("Contour publishes stable one-pass accepted and rejected candidates", TestContourCandidates);
        }

        private static void TestBlobCandidates()
        {
            using (Mat source = CreateCandidateSource())
            using (BlobTool tool = new BlobTool())
            {
                tool.SetProperty(new BlobToolProperty
                {
                    USE_THRESHOLD = true,
                    THRESHOLD = 100,
                    MIN_AREA = 20,
                    MAX_AREA = 10000,
                    MIN_WIDTH = 15,
                    MAX_WIDTH = 30,
                    MIN_HEIGHT = 15,
                    MAX_HEIGHT = 40
                });

                using (VisionToolResult first = tool.Execute(source))
                {
                    Require(first.Success, "Blob candidate fixture must execute successfully.");
                    AssertCandidates(tool.candidates, VisionObjectCandidateGenerationStage.BlobLabeling, 1);
                    Require(tool.results.Count == tool.candidates.Count,
                        "Blob legacy results must still contain every area-valid candidate for app-side dimension filtering.");
                    Require(tool.candidates.Count(candidate => candidate.Accepted) == 1,
                        "Blob candidate limits should accept exactly one fixture object.");
                }

                string[] firstIds = tool.candidates.Select(candidate => candidate.CandidateId).ToArray();
                using (VisionToolResult second = tool.Execute(source))
                {
                    Require(second.Success, "Repeated Blob candidate execution must succeed.");
                    Require(firstIds.SequenceEqual(tool.candidates.Select(candidate => candidate.CandidateId)),
                        "Blob candidate IDs must remain stable across repeated execution.");
                }
            }
        }

        private static void TestContourCandidates()
        {
            using (Mat source = CreateCandidateSource())
            using (ContourTool tool = new ContourTool())
            {
                tool.SetProperty(new ContourToolProperty
                {
                    USE_THRESHOLD = true,
                    THRESHOLD = 100,
                    MIN_AREA = 20,
                    MAX_AREA = 10000,
                    MIN_WIDTH = 15,
                    MAX_WIDTH = 30,
                    MIN_HEIGHT = 15,
                    MAX_HEIGHT = 40
                });

                using (VisionToolResult first = tool.Execute(source))
                {
                    Require(first.Success, "Contour candidate fixture must execute successfully.");
                    AssertCandidates(tool.candidates, VisionObjectCandidateGenerationStage.ContourExtraction, 1);
                    Require(tool.results.Count == tool.candidates.Count,
                        "Contour legacy results must still contain every area-valid candidate for app-side dimension filtering.");
                    Require(tool.candidates.Count(candidate => candidate.Accepted) == 1,
                        "Contour candidate limits should accept exactly one fixture object.");
                    Require(tool.candidates.All(candidate => candidate.Drawing.Points.Count > 0),
                        "Contour candidates must retain drawing geometry.");
                }

                string[] firstIds = tool.candidates.Select(candidate => candidate.CandidateId).ToArray();
                using (VisionToolResult second = tool.Execute(source))
                {
                    Require(second.Success, "Repeated Contour candidate execution must succeed.");
                    Require(firstIds.SequenceEqual(tool.candidates.Select(candidate => candidate.CandidateId)),
                        "Contour candidate IDs must remain stable across repeated execution.");
                }
            }
        }

        private static void AssertCandidates(
            IReadOnlyList<VisionObjectCandidate> candidates,
            VisionObjectCandidateGenerationStage stage,
            int expectedAccepted)
        {
            Require(candidates != null && candidates.Count == 5,
                "The candidate fixture must publish five candidates.");
            Require(candidates.Select(candidate => candidate.CandidateId).Distinct(StringComparer.Ordinal).Count() == candidates.Count,
                "Candidate IDs must be unique within one execution.");
            Require(candidates.All(candidate => candidate.GenerationStage == stage),
                "Candidate generation stage changed.");
            Require(candidates.All(candidate => candidate.CoordinateFrame == VisionObjectCandidateCoordinateFrame.SourceImage),
                "Candidate coordinate frame must remain SourceImage.");
            Require(candidates.All(candidate => candidate.AppliedLimits != null
                && candidate.AppliedLimits.MinimumArea == 20
                && candidate.AppliedLimits.MaximumArea == 10000
                && candidate.AppliedLimits.MinimumWidth == 15
                && candidate.AppliedLimits.MaximumWidth == 30
                && candidate.AppliedLimits.MinimumHeight == 15
                && candidate.AppliedLimits.MaximumHeight == 40),
                "Candidate applied limits were not retained.");
            Require(candidates.All(candidate => candidate.Drawing != null
                && candidate.Bounding.Width > 0
                && candidate.Bounding.Height > 0),
                "Candidate geometry must be retained for accepted and rejected objects.");
            Require(candidates.Count(candidate => candidate.Accepted) == expectedAccepted,
                "Unexpected accepted candidate count.");
            Require(candidates.Any(candidate => candidate.RejectReasonCode == VisionObjectCandidateRejectReasonCode.WidthAboveMaximum)
                && candidates.Any(candidate => candidate.RejectReasonCode == VisionObjectCandidateRejectReasonCode.WidthBelowMinimum)
                && candidates.Any(candidate => candidate.RejectReasonCode == VisionObjectCandidateRejectReasonCode.HeightAboveMaximum)
                && candidates.Any(candidate => candidate.RejectReasonCode == VisionObjectCandidateRejectReasonCode.HeightBelowMinimum),
                "Candidate rejection codes must identify each dimension rejection in the fixture.");
        }

        private static Mat CreateCandidateSource()
        {
            Mat source = new Mat(new Size(360, 140), MatType.CV_8UC1, Scalar.Black);
            Cv2.Rectangle(source, new Rect(20, 20, 24, 32), Scalar.White, -1);
            Cv2.Rectangle(source, new Rect(80, 20, 52, 24), Scalar.White, -1);
            Cv2.Rectangle(source, new Rect(155, 20, 8, 32), Scalar.White, -1);
            Cv2.Rectangle(source, new Rect(195, 20, 24, 8), Scalar.White, -1);
            Cv2.Rectangle(source, new Rect(250, 20, 24, 60), Scalar.White, -1);
            return source;
        }
    }
}
