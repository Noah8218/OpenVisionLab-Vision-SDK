using System.Collections.Generic;
using System.Xml.Serialization;

namespace OpenVisionLab.Vision2D.Pipeline
{
    [XmlRoot("VisionPipeline")]
    public class VisionPipeline
    {
        /// <summary>Gets or sets the serialized pipeline schema version.</summary>
        [XmlAttribute("schemaVersion")]
        public int SchemaVersion { get; set; } = 1;

        public string Name { get; set; } = string.Empty;

        [XmlArray("Steps")]
        [XmlArrayItem("Step")]
        public List<VisionPipelineStep> Steps { get; } = new List<VisionPipelineStep>();
    }
}
