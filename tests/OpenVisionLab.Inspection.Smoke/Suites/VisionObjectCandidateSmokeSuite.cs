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
            yield return new SmokeCase("Blob preserves masked candidates and multi-ROI source coordinates", TestBlobMaskAndMultiRoiCandidates);
            yield return new SmokeCase("Contour preserves multi-ROI candidate identity and source coordinates", TestContourMultiRoiCandidates);
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

        private static void TestBlobMaskAndMultiRoiCandidates()
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
                    MIN_WIDTH = 0,
                    MAX_WIDTH = 1000,
                    MIN_HEIGHT = 0,
                    MAX_HEIGHT = 1000,
                    CvMASKS = new List<Rect> { new Rect(80, 20, 52, 24) }
                });

                using (VisionToolResult maskedRun = tool.Execute(source))
                {
                    Require(maskedRun.Success, "Blob mask fixture must execute successfully with unmasked candidates.");
                    Require(tool.candidates.Count == 5, "Blob mask fixture must retain the masked candidate row.");
                    Require(tool.candidates.Count(candidate => candidate.RejectReasonCode == VisionObjectCandidateRejectReasonCode.Masked) == 1,
                        "Blob mask fixture must expose exactly one Masked candidate.");
                    Require(tool.results.Count == 4,
                        "Blob legacy results must exclude the masked candidate while candidates retain it.");
                    VisionObjectCandidate masked = tool.candidates.Single(candidate =>
                        candidate.RejectReasonCode == VisionObjectCandidateRejectReasonCode.Masked);
                    Require(masked.Bounding.X == 80 && masked.Bounding.Width == 52
                        && masked.Drawing != null
                        && masked.Drawing.Kind == VisionToolOverlayKind.Rectangle,
                        "Blob masked candidate must retain source-coordinate rectangle drawing.");
                }

                tool.SetProperty(new BlobToolProperty
                {
                    USE_THRESHOLD = true,
                    THRESHOLD = 100,
                    MIN_AREA = 20,
                    MAX_AREA = 10000,
                    USE_MULTI_ROI = true,
                    CvROIS = new List<Rect>
                    {
                        new Rect(0, 0, 180, 140),
                        new Rect(180, 0, 180, 140)
                    }
                });

                using (VisionToolResult multiRun = tool.Execute(source))
                {
                    Require(multiRun.Success, "Blob multi-ROI fixture must execute successfully.");
                    AssertMultiRoiCandidates(tool.candidates, VisionObjectCandidateGenerationStage.BlobLabeling);
                }
            }
        }

        private static void TestContourMultiRoiCandidates()
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
                    USE_MULTI_ROI = true,
                    CvROIS = new List<Rect>
                    {
                        new Rect(0, 0, 180, 140),
                        new Rect(180, 0, 180, 140)
                    }
                });

                using (VisionToolResult multiRun = tool.Execute(source))
                {
                    Require(multiRun.Success, "Contour multi-ROI fixture must execute successfully.");
                    AssertMultiRoiCandidates(tool.candidates, VisionObjectCandidateGenerationStage.ContourExtraction);
                    Require(tool.candidates.All(candidate => candidate.Drawing != null && candidate.Drawing.Points.Count > 0),
                        "Contour multi-ROI candidates must retain source-coordinate drawing points.");
                }
            }
        }

        private static void AssertMultiRoiCandidates(
            IReadOnlyList<VisionObjectCandidate> candidates,
            VisionObjectCandidateGenerationStage stage)
        {
            Require(candidates != null && candidates.Count == 5,
                "The multi-ROI fixture must publish five candidates.");
            Require(candidates.All(candidate => candidate.GenerationStage == stage),
                "Multi-ROI candidate generation stage changed.");
            Require(candidates.Select(candidate => candidate.CandidateId).Distinct(StringComparer.Ordinal).Count() == candidates.Count,
                "Multi-ROI candidate IDs must be unique.");
            Require(candidates.Select(candidate => candidate.RegionIndex).Distinct().OrderBy(index => index).SequenceEqual(new[] { 0, 1 }),
                "Multi-ROI candidate region indexes must identify both source ROIs.");
            Require(candidates.Any(candidate => candidate.RegionIndex == 0 && candidate.Bounding.X < 180)
                && candidates.Any(candidate => candidate.RegionIndex == 1 && candidate.Bounding.X >= 180),
                "Multi-ROI candidate geometry must remain in source-image coordinates.");
            Require(candidates.All(candidate => candidate.CandidateId.StartsWith(stage + ":", StringComparison.Ordinal)),
                "Multi-ROI candidate IDs must include their generation stage.");
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
