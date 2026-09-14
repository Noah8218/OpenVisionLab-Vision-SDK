using System.Collections.Generic;
using System.Xml.Serialization;

namespace OpenVisionLab.Vision2D.Pipeline
{
    [XmlRoot("VisionPipeline")]
    public class VisionPipeline
    {
        public const int CurrentSchemaVersion = 2;

        /// <summary>Gets or sets the serialized pipeline schema version.</summary>
        [XmlAttribute("schemaVersion")]
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        public string Name { get; set; } = string.Empty;

        [XmlArray("Steps")]
        [XmlArrayItem("Step")]
        public List<VisionPipelineStep> Steps { get; } = new List<VisionPipelineStep>();
    }
}
