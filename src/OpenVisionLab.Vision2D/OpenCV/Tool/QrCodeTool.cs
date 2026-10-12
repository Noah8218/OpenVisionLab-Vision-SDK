using OpenCvSharp;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Result;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;

namespace OpenVisionLab.Vision2D.Tool
{
    /// <summary>Observes all detected QR candidates and reads non-empty text using the bundled OpenCV backend.</summary>
    /// <remarks>Single-instance configuration/execution/disposal must be serialized. Native calls cannot be forcibly canceled.</remarks>
    public sealed class QrCodeTool : OpenCvAlgorithmBase, ICancellableVisionTool
    {
        public QrCodeToolProperty property = new QrCodeToolProperty();

        /// <summary>The last completed managed observation; cleared before each run and on cancellation or infrastructure failure.</summary>
        public QrCodeReadingEvidence LatestReading { get; private set; }

        public void SetProperty(QrCodeToolProperty property) => this.property = property;

        public override VisionToolResult Execute(Mat source) => Execute(source, CancellationToken.None);

        public VisionToolResult Execute(Mat source, CancellationToken cancellationToken)
        {
            ResetReading();
            try
            {
                VisionToolResult result = ExecuteWithCancellation(source, cancellationToken);
                if (result.Exception != null) ResetReading();
                return result;
            }
            catch
            {
                ResetReading();
                throw;
            }
        }

        public override void Run()
        {
            ResetReading();
            try
            {
                ExecutionCancellationToken.ThrowIfCancellationRequested();
                if (!TryValidateBeforeRun(out _, out string message)) throw new InvalidOperationException(message);
                Rect roi = property.USE_ROI ? property.CvROI : new Rect(0, 0, imageSource.Width, imageSource.Height);
                QrCodeCandidate[] candidates;
                using (Mat region = new Mat(imageSource, roi))
                using (Mat gray = region.Clone())
                {
                    // This reader accepts BGR; the legacy common preprocessing uses RGB weights.
                    if (gray.Channels() == 3) Cv2.CvtColor(gray, gray, ColorConversionCodes.BGR2GRAY);
                    using (InputArray input = gray)
                    using (QRCodeDetector detector = new QRCodeDetector())
                    {
                        ExecutionCancellationToken.ThrowIfCancellationRequested();
                        bool detected = detector.DetectMulti(input, out Point2f[] points);
                        ExecutionCancellationToken.ThrowIfCancellationRequested();
                        if (!detected)
                        {
                            if (points != null && points.Length != 0) throw new InvalidOperationException("QR backend returned points without detection.");
                            candidates = Array.Empty<QrCodeCandidate>();
                        }
                        else
                        {
                            ValidatePoints(points);
                            ExecutionCancellationToken.ThrowIfCancellationRequested();
                            detector.DecodeMulti(input, points, out string[] texts);
                            ExecutionCancellationToken.ThrowIfCancellationRequested();
                            if (texts == null || texts.Length != points.Length / 4) throw new InvalidOperationException("QR backend returned inconsistent candidate and text counts.");
                            candidates = new QrCodeCandidate[texts.Length];
                            for (int index = 0; index < candidates.Length; index++)
                            {
                                Point2f[] corners = new Point2f[4];
                                for (int corner = 0; corner < corners.Length; corner++)
                                {
                                    Point2f point = points[index * 4 + corner];
                                    corners[corner] = new Point2f(point.X + roi.X, point.Y + roi.Y);
                                }
                                // Embedded NUL and empty payloads are outside the text contract; preserve the observed text.
                                string text = texts[index] ?? string.Empty;
                                if (text.IndexOf('\0') >= 0) throw new InvalidOperationException("QR payload contains an unsupported NUL character.");
                                candidates[index] = new QrCodeCandidate(index, text, corners);
                            }
                        }
                    }
                }
                ExecutionCancellationToken.ThrowIfCancellationRequested();
                ReplaceResultImage(imageSource.Clone());
                LatestReading = new QrCodeReadingEvidence(imageSource.Size(), roi, property.ExpectedCodeCount, candidates);
                ExecutionCancellationToken.ThrowIfCancellationRequested();
            }
            catch
            {
                ResetReading();
                throw;
            }
        }

        protected override bool TryValidateBeforeRun(out VisionToolErrorCode errorCode, out string message)
        {
            if (!base.TryValidateBeforeRun(out errorCode, out message)) return false;
            if (imageSource == null || imageSource.IsDisposed || imageSource.Empty() || (imageSource.Type() != MatType.CV_8UC1 && imageSource.Type() != MatType.CV_8UC3))
            {
                errorCode = VisionToolErrorCode.InputImageInvalid;
                message = "QR requires a non-empty 8-bit grayscale or BGR image.";
                return false;
            }
            if (property.ExpectedCodeCount < 1 || property.USE_THRESHOLD || property.USE_ADAPTIVE_THRESHOLD || property.USE_BITWISENOT || property.USE_MULTI_ROI || (property.CvMASKS != null && property.CvMASKS.Count > 0))
            {
                errorCode = VisionToolErrorCode.InvalidParameter;
                message = "QR requires a positive expected count and supports no common threshold, inversion, multi-ROI or mask settings.";
                return false;
            }
            Rect roi = property.CvROI;
            if (property.USE_ROI && (roi.X < 0 || roi.Y < 0 || roi.Width <= 0 || roi.Height <= 0 || (long)roi.X + roi.Width > imageSource.Width || (long)roi.Y + roi.Height > imageSource.Height))
            {
                errorCode = VisionToolErrorCode.InvalidRoi;
                message = "QR ROI must be a positive rectangle wholly inside the current input image.";
                return false;
            }
            errorCode = VisionToolErrorCode.None;
            message = string.Empty;
            return true;
        }

        protected override bool TryValidateAfterRun(out VisionToolErrorCode errorCode, out string message)
        {
            if (LatestReading == null) throw new InvalidOperationException("QR observation was not completed.");
            if (LatestReading.DetectedCount == 0)
            {
                errorCode = VisionToolErrorCode.QrCodeNotDetected;
                message = "No QR candidate was detected.";
            }
            else if (LatestReading.DetectedCount != LatestReading.ExpectedCodeCount)
            {
                errorCode = VisionToolErrorCode.QrCodeCountMismatch;
                message = $"QR detected {LatestReading.DetectedCount} candidates; expected {LatestReading.ExpectedCodeCount}.";
            }
            else if (LatestReading.DecodedCount != LatestReading.DetectedCount)
            {
                errorCode = VisionToolErrorCode.QrCodeDecodeFailed;
                message = $"QR decoded {LatestReading.DecodedCount} of {LatestReading.DetectedCount} candidates.";
            }
            else
            {
                errorCode = VisionToolErrorCode.None;
                message = string.Empty;
            }
            return errorCode == VisionToolErrorCode.None;
        }

        protected override void AttachResultEvidence(VisionToolResult result)
        {
            ExecutionCancellationToken.ThrowIfCancellationRequested();
            result.QrCodeReading = LatestReading;
        }

        protected override IDictionary<string, double> CollectMetrics()
        {
            IDictionary<string, double> metrics = base.CollectMetrics();
            if (LatestReading != null)
            {
                metrics["QrExpectedCount"] = LatestReading.ExpectedCodeCount;
                metrics["QrDetectedCount"] = LatestReading.DetectedCount;
                metrics["QrDecodedCount"] = LatestReading.DecodedCount;
            }
            return metrics;
        }

        protected override IEnumerable<VisionToolOverlay> CollectOverlays()
        {
            if (LatestReading == null) yield break;
            foreach (QrCodeCandidate candidate in LatestReading.Candidates)
            {
                VisionToolOverlay overlay = new VisionToolOverlay { Kind = VisionToolOverlayKind.Points, Label = $"QR {candidate.CandidateIndex}: {(candidate.Decoded ? "Decoded" : "Unreadable")}" };
                foreach (Point2f corner in candidate.Corners) overlay.Points.Add(new PointF(corner.X, corner.Y));
                yield return overlay;
            }
        }

        protected override VisionToolErrorCode ResolveExecutionErrorCode(Exception exception)
        {
            Exception cause = exception.GetBaseException();
            return cause is DllNotFoundException || cause is EntryPointNotFoundException || cause is BadImageFormatException
                ? VisionToolErrorCode.QrCodeBackendUnavailable : base.ResolveExecutionErrorCode(exception);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) LatestReading = null;
            base.Dispose(disposing);
        }

        private void ResetReading()
        {
            LatestReading = null;
            // The instance owns this image. Previously returned results own independent snapshots.
            if (imageResult != null && !imageResult.IsDisposed) imageResult.Dispose();
            imageResult = null;
        }

        private static void ValidatePoints(Point2f[] points)
        {
            if (points == null || points.Length == 0 || points.Length % 4 != 0) throw new InvalidOperationException("QR backend returned invalid corner groups.");
            for (int start = 0; start < points.Length; start += 4)
            {
                double area = 0;
                for (int corner = 0; corner < 4; corner++)
                {
                    Point2f a = points[start + corner], b = points[start + (corner + 1) % 4];
                    if (float.IsNaN(a.X) || float.IsInfinity(a.X) || float.IsNaN(a.Y) || float.IsInfinity(a.Y)) throw new InvalidOperationException("QR backend returned non-finite corners.");
                    area += (double)a.X * b.Y - (double)b.X * a.Y;
                }
                // OpenCV 4.3 silently drops zero-area groups in decodeMulti, which would invalidate text correspondence.
                if (area == 0) throw new InvalidOperationException("QR backend returned a degenerate corner group.");
            }
        }
    }
}
