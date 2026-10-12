namespace OpenVisionLab.Vision2D.Property
{
    /// <summary>QR text settings for an entire 8-bit gray/BGR image or one rectangular ROI.</summary>
    /// <remarks>Threshold, inversion, adaptive preprocessing, masks and multiple ROIs are not supported by this Tool.</remarks>
    public sealed class QrCodeToolProperty : OpenCvToolPropertyBase
    {
        public QrCodeToolProperty() : base("QR") { }

        /// <summary>Required observed candidate count, at least one. Every candidate must decode to nonempty text.</summary>
        public int ExpectedCodeCount { get; set; } = 1;
    }
}
