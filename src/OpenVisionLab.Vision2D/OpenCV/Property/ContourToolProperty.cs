using OpenCvSharp;
using System.Drawing;

namespace OpenVisionLab.Vision2D.Property
{
    /// <summary>Provides a ready-to-use configuration for ContourTool and CornerTool.</summary>
    public sealed class ContourToolProperty : OpenCvToolPropertyBase, IOpenCVPropertyContour, IVisionObjectFilterProperty
    {
        public ContourToolProperty() : base("Contour") { }

        public bool USE_APPROXPOLYDP { get; set; }
        public bool USE_DRAW_IMAGE { get; set; }
        public ContourApproximationModes ApproximationModes { get; set; } = ContourApproximationModes.ApproxSimple;
        public RetrievalModes DetectMode { get; set; } = RetrievalModes.External;
        public double EPSILON { get; set; } = 0.01d;
        public int MIN_AREA { get; set; } = 200;
        public int MAX_AREA { get; set; } = 1000000;
        public int MIN_WIDTH { get; set; } = 0;
        public int MAX_WIDTH { get; set; } = 1000000;
        public int MIN_HEIGHT { get; set; } = 0;
        public int MAX_HEIGHT { get; set; } = 1000000;
        public Color DrawColor { get; set; } = Color.Aquamarine;
        public int DrawThickness { get; set; } = 2;
        public string ClrGridHtml { get; set; } = "#7FFFD4";
    }
}
