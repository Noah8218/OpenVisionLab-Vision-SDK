namespace OpenVisionLab.Vision2D.Property
{
    /// <summary>Optional bounding-box limits used by the one-pass object candidate contract.</summary>
    public interface IVisionObjectFilterProperty
    {
        int MIN_WIDTH { get; set; }
        int MAX_WIDTH { get; set; }
        int MIN_HEIGHT { get; set; }
        int MAX_HEIGHT { get; set; }
    }
}
