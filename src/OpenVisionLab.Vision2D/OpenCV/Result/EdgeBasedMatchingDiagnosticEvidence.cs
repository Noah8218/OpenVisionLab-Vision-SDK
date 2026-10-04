using System.Collections.Generic;
using System.Drawing;

namespace OpenVisionLab.Vision2D.Result
{
    public sealed class EdgeBasedMatchingCandidateDiagnostic
    {
        public double Score { get; set; }
        public double Angle { get; set; }
        public double Scale { get; set; } = 1D;
        public PointF Center { get; set; }
        public RectangleF Bounds { get; set; }

        public EdgeBasedMatchingCandidateDiagnostic Clone()
        {
            return new EdgeBasedMatchingCandidateDiagnostic
            {
                Score = Score,
                Angle = Angle,
                Scale = Scale,
                Center = Center,
                Bounds = Bounds
            };
        }
    }

    public sealed class EdgeBasedMatchingDiagnosticEvidence
    {
        public string State { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string ErrorCode { get; set; } = string.Empty;
        public RectangleF SearchRoi { get; set; }
        public int TemplateWidth { get; set; }
        public int TemplateHeight { get; set; }
        public PointF ModelCenter { get; set; }
        public List<PointF> ModelPoints { get; } = new List<PointF>();
        public EdgeBasedMatchingCandidateDiagnostic SelectedCandidate { get; set; }
        public EdgeBasedMatchingCandidateDiagnostic StrongestSpatialAlternative { get; set; }
        public EdgeBasedMatchingCandidateDiagnostic StrongestPoseAlternative { get; set; }

        public EdgeBasedMatchingDiagnosticEvidence Clone()
        {
            EdgeBasedMatchingDiagnosticEvidence clone = new EdgeBasedMatchingDiagnosticEvidence
            {
                State = State ?? string.Empty,
                Reason = Reason ?? string.Empty,
                ErrorCode = ErrorCode ?? string.Empty,
                SearchRoi = SearchRoi,
                TemplateWidth = TemplateWidth,
                TemplateHeight = TemplateHeight,
                ModelCenter = ModelCenter,
                SelectedCandidate = SelectedCandidate?.Clone(),
                StrongestSpatialAlternative = StrongestSpatialAlternative?.Clone(),
                StrongestPoseAlternative = StrongestPoseAlternative?.Clone()
            };
            clone.ModelPoints.AddRange(ModelPoints);
            return clone;
        }
    }
}
