using OpenVisionLab.Vision2D.Property;
using OpenCvSharp;
using System.Collections.Generic;

namespace OpenVisionLab.Vision2D.Blob
{
    /// <summary>
    /// Provides a ready-to-use BlobTool configuration while preserving the customizable IOpenCVPropertyBlob contract.
    /// </summary>
    public sealed class BlobToolProperty : IOpenCVPropertyBlob, IVisionObjectFilterProperty
    {
        public string NAME { get; set; } = "Blob";
        public double PIXELPERMM { get; set; } = 1d;
        public bool USE_THRESHOLD { get; set; } = true;
        public bool USE_BITWISENOT { get; set; }
        public ThresholdTypes THRESHOLD_TYPES { get; set; } = ThresholdTypes.Binary;
        public double THRESHOLD { get; set; } = 120d;
        public bool USE_ADAPTIVE_THRESHOLD { get; set; }
        public double ADAPTIVE_THRESHOLD { get; set; } = 255d;
        public ThresholdTypes ADAPTIVE_THRESHOLD_TYPES { get; set; } = ThresholdTypes.Binary;
        public AdaptiveThresholdTypes ADAPTIVE_THRESHOLD_ALGORITHM { get; set; } = AdaptiveThresholdTypes.MeanC;
        public int BlockSize { get; set; } = 25;
        public int Weight { get; set; } = 5;
        public bool USE_ROI { get; set; }
        public bool USE_MULTI_ROI { get; set; }
        public Rect CvROI { get; set; } = new Rect();
        public List<Rect> CvROIS { get; set; } = new List<Rect>();
        public List<Rect> CvMASKS { get; set; } = new List<Rect>();
        public int MIN_AREA { get; set; } = 20;
        public int MAX_AREA { get; set; } = 100000;
        public int MIN_WIDTH { get; set; } = 0;
        public int MAX_WIDTH { get; set; } = 1000000;
        public int MIN_HEIGHT { get; set; } = 0;
        public int MAX_HEIGHT { get; set; } = 1000000;
    }
}
