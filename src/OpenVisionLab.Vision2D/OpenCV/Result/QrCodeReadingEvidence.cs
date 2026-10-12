using OpenCvSharp;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace OpenVisionLab.Vision2D.Result
{
    /// <summary>An immutable managed snapshot of one QR observation; owns no image or decoder.</summary>
    /// <remarks>DetectedCount is the number observed by the detector, not a guarantee that every code was found.</remarks>
    public sealed class QrCodeReadingEvidence
    {
        internal QrCodeReadingEvidence(Size inputImageSize, Rect appliedRoi, int expectedCodeCount, IEnumerable<QrCodeCandidate> candidates)
        {
            InputImageSize = inputImageSize;
            AppliedRoi = appliedRoi;
            ExpectedCodeCount = expectedCodeCount;
            QrCodeCandidate[] snapshot = candidates.ToArray();
            Candidates = new ReadOnlyCollection<QrCodeCandidate>(snapshot);
            DecodedCount = snapshot.Count(candidate => candidate.Decoded);
        }

        public Size InputImageSize { get; }
        /// <summary>The inspected rectangle in input Mat pixels; the full frame when ROI is disabled.</summary>
        public Rect AppliedRoi { get; }
        public int ExpectedCodeCount { get; }
        public int DetectedCount => Candidates.Count;
        public int DecodedCount { get; }
        public IReadOnlyList<QrCodeCandidate> Candidates { get; }
    }
}
