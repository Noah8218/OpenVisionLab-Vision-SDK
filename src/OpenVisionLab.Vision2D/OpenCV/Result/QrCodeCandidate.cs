using OpenCvSharp;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace OpenVisionLab.Vision2D.Result
{
    /// <summary>An immutable observed QR candidate, in the pixels of the Mat passed to Execute.</summary>
    public sealed class QrCodeCandidate
    {
        internal QrCodeCandidate(int candidateIndex, string text, Point2f[] corners)
        {
            CandidateIndex = candidateIndex;
            Text = text ?? string.Empty;
            Corners = new ReadOnlyCollection<Point2f>((Point2f[])corners.Clone());
        }

        /// <summary>Zero-based observation order, not a persistent object identity.</summary>
        public int CandidateIndex { get; }
        /// <summary>Original decoded text, including whitespace; empty when decoding failed.</summary>
        public string Text { get; }
        public bool Decoded => Text.Length > 0;
        /// <summary>Four native-order corners in input Mat pixels, with any ROI origin already added.</summary>
        public IReadOnlyList<Point2f> Corners { get; }
    }
}
