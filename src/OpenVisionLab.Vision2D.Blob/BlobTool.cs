using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Result;
using OpenVisionLab.Vision2D.Tool;
using OpenCvSharp;
using OpenCvSharp.Blob;

namespace OpenVisionLab.Vision2D.Blob
{
    /// <summary>
    /// Labels thresholded blobs and publishes deterministic, source-coordinate results.
    /// </summary>
    public partial class BlobTool : OpenCvAlgorithmBase
    {
        /// <summary>The active preprocessing, ROI, and area-filter configuration.</summary>
        public IOpenCVPropertyBlob property;

        /// <summary>The detected blobs in stable one-based index order after a successful execution.</summary>
        public List<BlobResult> results = new List<BlobResult>();

        /// <summary>All source-coordinate candidates produced by the last execution, including rejected candidates.</summary>
        public List<VisionObjectCandidate> candidates = new List<VisionObjectCandidate>();

        public BlobTool() { }

        public void SetProperty(IOpenCVPropertyBlob propertyBase) => property = propertyBase;

        protected override bool TryValidateBeforeRun(out VisionToolErrorCode errorCode, out string message)
        {
            if (!base.TryValidateBeforeRun(out errorCode, out message))
            {
                return false;
            }

            if (!TryValidateAreaRange(
                property.MIN_AREA,
                property.MAX_AREA,
                VisionToolErrorCode.BlobInvalidAreaRange,
                "Blob",
                out errorCode,
                out message))
            {
                return false;
            }

            if (!TryValidateAdaptiveThreshold(
                property,
                VisionToolErrorCode.BlobInvalidAdaptiveBlockSize,
                out errorCode,
                out message))
            {
                return false;
            }

            if (!TryValidateRoiSet(
                property,
                property.USE_ROI,
                true,
                VisionToolErrorCode.BlobRoiInvalid,
                "Blob",
                out errorCode,
                out message))
            {
                return false;
            }

            errorCode = VisionToolErrorCode.None;
            message = string.Empty;
            return true;
        }

        protected override bool TryValidateAfterRun(out VisionToolErrorCode errorCode, out string message)
        {
            if (results == null || results.Count == 0)
            {
                errorCode = VisionToolErrorCode.BlobNoResult;
                message = $"Blob found no result. Area={property.MIN_AREA}..{property.MAX_AREA}, ROI={FormatBlobRoi()}";
                return false;
            }

            errorCode = VisionToolErrorCode.None;
            message = string.Empty;
            return true;
        }

        protected override VisionToolErrorCode ResolveExecutionErrorCode(System.Exception exception)
        {
            VisionToolErrorCode baseCode = base.ResolveExecutionErrorCode(exception);
            return baseCode == VisionToolErrorCode.OpenCvExecutionFailed
                ? VisionToolErrorCode.BlobLabelingFailed
                : baseCode;
        }

        public override void Run()
        {
            if (property.USE_MULTI_ROI)
            {
                MultiRun();
            }
            else
            {
                SingleRun();
            }
        }

        protected bool SingleRun()
        {
            swTaktTimems.Restart();
            results.Clear();
            candidates.Clear();

            if (!PrepareSourceImage())
            {
                return false;
            }

            Rect roi = NormalizeSingleRoi();
            BlobDetectionBatch batch = RunBlobLabeling(roi, property.USE_ROI, 0);
            results = ReindexBlobs(batch.Results.OrderBy(result => result.Index))
                .ToList();
            candidates = OrderCandidates(batch.Candidates).ToList();

            swTaktTimems.Stop();
            return true;
        }

        protected bool MultiRun()
        {
            swTaktTimems.Restart();
            results.Clear();
            candidates.Clear();

            if (!PrepareSourceImage())
            {
                return false;
            }

            for (int i = 0; i < property.CvROIS.Count; i++)
            {
                Rect roi = NormalizeMultiRoi(i);
                BlobDetectionBatch batch = RunBlobLabeling(roi, true, i);
                results.AddRange(batch.Results.OrderBy(result => result.Index));
                candidates.AddRange(batch.Candidates);
            }

            results = ReindexBlobs(results).ToList();
            candidates = OrderCandidates(candidates).ToList();
            swTaktTimems.Stop();
            return true;
        }

        private bool PrepareSourceImage()
        {
            if (OpenCvHelper.IsImageEmpty(imageSource))
            {
                return false;
            }

            return true;
        }

        private Rect NormalizeSingleRoi()
        {
            return NormalizeBlobRoi(property.CvROI);
        }

        private Rect NormalizeMultiRoi(int index)
        {
            return NormalizeBlobRoi(property.CvROIS[index]);
        }

        private Rect NormalizeBlobRoi(Rect roi)
        {
            return roi.Width == 0 || roi.Height == 0
                ? new Rect(0, 0, imageSource.Width, imageSource.Height)
                : roi;
        }

        private string FormatBlobRoi()
        {
            if (property.USE_MULTI_ROI)
            {
                return $"Multi({property.CvROIS?.Count ?? 0})";
            }

            Rect roi = NormalizeBlobRoi(property.CvROI);
            return $"{roi.X},{roi.Y},{roi.Width},{roi.Height}";
        }

        private static IEnumerable<BlobResult> ReindexBlobs(IEnumerable<BlobResult> source)
        {
            int index = 1;
            foreach (BlobResult result in source)
            {
                result.Index = index++;
                yield return result;
            }
        }

        private BlobDetectionBatch RunBlobLabeling(Rect roi, bool useRoi, int regionIndex)
        {
            using (Mat imageBlob = CreatePreprocessedImage(roi, useRoi, property))
            {
                CvBlobs blobs = new CvBlobs();
                blobs.Label(imageBlob);

                ConcurrentBag<BlobResult> detectedBlobs = new ConcurrentBag<BlobResult>();
                ConcurrentBag<VisionObjectCandidate> detectedCandidates = new ConcurrentBag<VisionObjectCandidate>();
                VisionObjectCandidateLimits limits = ResolveCandidateLimits();
                Parallel.ForEach(blobs, (item, state, index) =>
                {
                    CvBlob blob = item.Value;
                    Rect bounds = useRoi
                        ? new Rect(blob.Rect.X + roi.X, blob.Rect.Y + roi.Y, blob.Rect.Width, blob.Rect.Height)
                        : blob.Rect;
                    Point2d center = useRoi
                        ? new Point2d(blob.Centroid.X + roi.X, blob.Centroid.Y + roi.Y)
                        : blob.Centroid;

                    bool masked = IsMasked(bounds);
                    VisionObjectCandidateDecision decision = masked
                        ? new VisionObjectCandidateDecision(
                            VisionObjectCandidateRejectReasonCode.Masked,
                            "Candidate is inside a configured mask.")
                        : VisionObjectCandidateEvaluator.Evaluate(
                            blob.Area,
                            bounds.Width,
                            bounds.Height,
                            limits);
                    detectedCandidates.Add(new VisionObjectCandidate
                    {
                        CandidateId = VisionObjectCandidate.CreateCandidateId(
                            VisionObjectCandidateGenerationStage.BlobLabeling,
                            regionIndex,
                            item.Key),
                        RegionIndex = regionIndex,
                        NativeIndex = item.Key,
                        Area = blob.Area,
                        Center = center,
                        Bounding = new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                        Angle = blob.Angle(),
                        Accepted = decision.Accepted,
                        RejectReasonCode = decision.Code,
                        RejectReasonText = decision.Text,
                        AppliedLimits = limits,
                        Drawing = new VisionToolOverlay
                        {
                            Kind = VisionToolOverlayKind.Rectangle,
                            Label = "Blob candidate",
                            Bounds = new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                            Center = new PointF((float)center.X, (float)center.Y),
                            Angle = blob.Angle()
                        },
                        GenerationStage = VisionObjectCandidateGenerationStage.BlobLabeling,
                        CoordinateFrame = VisionObjectCandidateCoordinateFrame.SourceImage
                    });

                    // Keep the legacy results list area-filtered. The application applies
                    // its existing dimension filter to that list after consuming candidates.
                    if (!masked
                        && blob.Area >= property.MIN_AREA
                        && blob.Area <= property.MAX_AREA)
                    {
                        detectedBlobs.Add(new BlobResult((int)index, blob.Area, center, bounds, blob.Angle()));
                    }
                });

                return new BlobDetectionBatch
                {
                    Results = detectedBlobs.ToList(),
                    Candidates = detectedCandidates.ToList()
                };
            }
        }

        private VisionObjectCandidateLimits ResolveCandidateLimits()
        {
            IVisionObjectFilterProperty filter = property as IVisionObjectFilterProperty;
            return new VisionObjectCandidateLimits(
                property.MIN_AREA,
                property.MAX_AREA,
                filter?.MIN_WIDTH ?? 0,
                filter?.MAX_WIDTH ?? int.MaxValue,
                filter?.MIN_HEIGHT ?? 0,
                filter?.MAX_HEIGHT ?? int.MaxValue);
        }

        private static IEnumerable<VisionObjectCandidate> OrderCandidates(
            IEnumerable<VisionObjectCandidate> source)
        {
            return (source ?? Enumerable.Empty<VisionObjectCandidate>())
                .Where(candidate => candidate != null)
                .OrderBy(candidate => candidate.RegionIndex)
                .ThenBy(candidate => candidate.NativeIndex);
        }

        private sealed class BlobDetectionBatch
        {
            public List<BlobResult> Results { get; set; } = new List<BlobResult>();
            public List<VisionObjectCandidate> Candidates { get; set; } = new List<VisionObjectCandidate>();
        }

        private bool IsMasked(Rect bounds)
        {
            if (property.CvMASKS == null || property.CvMASKS.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < property.CvMASKS.Count; i++)
            {
                if (property.CvMASKS[i].Contains(bounds))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
