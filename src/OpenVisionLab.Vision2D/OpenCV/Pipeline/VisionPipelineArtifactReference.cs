using System;
using System.Xml.Serialization;

namespace OpenVisionLab.Vision2D.Pipeline
{
    /// <summary>Identifies host-owned bytes used to reconstruct a Pipeline Tool.</summary>
    public sealed class VisionPipelineArtifactReference
    {
        public VisionPipelineArtifactReference()
        {
        }

        public VisionPipelineArtifactReference(string role, string id, string format, int formatVersion, string sha256)
        {
            Role = role ?? throw new ArgumentNullException(nameof(role));
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Format = format ?? throw new ArgumentNullException(nameof(format));
            FormatVersion = formatVersion;
            Sha256 = sha256 ?? throw new ArgumentNullException(nameof(sha256));
        }

        [XmlAttribute("role")]
        public string Role { get; set; } = string.Empty;

        [XmlAttribute("id")]
        public string Id { get; set; } = string.Empty;

        [XmlAttribute("format")]
        public string Format { get; set; } = string.Empty;

        [XmlAttribute("formatVersion")]
        public int FormatVersion { get; set; } = 1;

        [XmlAttribute("sha256")]
        public string Sha256 { get; set; } = string.Empty;
    }
}
