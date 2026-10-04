using OpenVisionLab.Core.Geometry2D;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenVisionLab.Vision2D.Result
{
    /// <summary>Immutable pixel-domain evidence from the opt-in gray-gradient measurement. Orthogonal RMSE is descriptive fit error, not calibrated uncertainty.</summary>
    public sealed class LineGaugeSubpixelEvidence
    {
        internal LineGaugeSubpixelEvidence(List<OpenCvSharp.Point2d> points, int rejectedPointCount, bool projectsX,
            bool hasFit, double slope, double intercept, double orthogonalRmse)
        {
            Points = Array.AsReadOnly(points.ToArray());
            RejectedPointCount = rejectedPointCount;
            ProjectsX = projectsX;
            HasFit = hasFit;
            Slope = slope;
            Intercept = intercept;
            OrthogonalRmse = orthogonalRmse;
        }
        public IReadOnlyList<OpenCvSharp.Point2d> Points { get; }
        public int SampleCount => Points.Count;
        public int RejectedPointCount { get; }
        /// <summary>True means x=Slope*y+Intercept; false means y=Slope*x+Intercept.</summary>
        public bool ProjectsX { get; }
        public bool HasFit { get; }
        public double Slope { get; }
        public double Intercept { get; }
        /// <summary>Pixel orthogonal RMSE; NaN when HasFit is false.</summary>
        public double OrthogonalRmse { get; }
    }

    public class LineGaugeResult
    {
        public int Index { get; set; } = 0;
        public LineGaugeSubpixelEvidence SubpixelEvidence { get; }
        public List<LineGaugeEdge> Results_List { get; set; } = new List<LineGaugeEdge>();
        public LineSegment2D FitLine { get; set; } = new LineSegment2D(); // 라인 핏팅 
        public List<OpenCvSharp.Point> edgeList { get; set; } = new List<OpenCvSharp.Point>(); // 엣지 리스트
        public int EdgeCount => Results_List?.Count ?? 0;
        public int EdgePointCount => edgeList?.Count ?? 0;

        internal LineGaugeResult(List<LineGaugeEdge> edges, LineSegment2D fitLine, int index, LineGaugeSubpixelEvidence subpixelEvidence)
            : this(edges, fitLine, index)
        {
            SubpixelEvidence = subpixelEvidence;
        }

        public LineGaugeResult(List<LineGaugeEdge> Results_List, LineSegment2D fitLine, int index = 0)
        {
            Index = index;
            this.Results_List = Results_List.ConvertAll(s => new LineGaugeEdge(s.NO, s.MeasPos, s.UseEdge));
            this.FitLine = fitLine;
            edgeList = this.Results_List.Select(item => item.MeasPos).ToList();
        }
    }

    public class LineGaugeVerticalLines
    {
        public int index { get; set; } = 0;

        public List<double> intersectionLengths { get; set; } = new List<double>();
        public List<LineSegment2D> cLines { get; set; } = new List<LineSegment2D>();
    }
}
