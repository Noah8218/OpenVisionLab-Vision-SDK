using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace OpenVisionLab.Vision2D.Pipeline
{
    /// <summary>Serializes and validates the supported Vision Pipeline XML schema.</summary>
    public static class VisionPipelineSerializer
    {
        private const int SupportedSchemaVersion = 1;
        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(VisionPipeline));

        /// <summary>Serializes a supported pipeline to indented XML text.</summary>
        public static string Serialize(VisionPipeline pipeline)
        {
            if (pipeline == null)
            {
                throw new ArgumentNullException(nameof(pipeline));
            }

            EnsureSupportedSchemaVersion(pipeline.SchemaVersion);

            StringBuilder output = new StringBuilder();
            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                NewLineChars = "\n",
                NewLineHandling = NewLineHandling.Replace,
                OmitXmlDeclaration = true
            };
            XmlSerializerNamespaces namespaces = new XmlSerializerNamespaces();
            namespaces.Add(string.Empty, string.Empty);

            using (XmlWriter writer = XmlWriter.Create(output, settings))
            {
                Serializer.Serialize(writer, pipeline, namespaces);
            }

            return output.ToString();
        }

        /// <summary>
        /// Deserializes Pipeline XML, rejects DTD processing, and accepts only the current schema version.
        /// XML without a schemaVersion attribute is treated as the original version 1 contract.
        /// </summary>
        public static VisionPipeline Deserialize(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
            {
                throw new ArgumentException("Serialized vision pipeline XML is required.", nameof(xml));
            }

            XmlReaderSettings settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };

            VisionPipeline pipeline;
            using (StringReader input = new StringReader(xml))
            using (XmlReader reader = XmlReader.Create(input, settings))
            {
                pipeline = Serializer.Deserialize(reader) as VisionPipeline;
            }

            if (pipeline == null)
            {
                throw new InvalidOperationException("Serialized XML did not contain a VisionPipeline.");
            }

            EnsureSupportedSchemaVersion(pipeline.SchemaVersion);
            return pipeline;
        }

        private static void EnsureSupportedSchemaVersion(int schemaVersion)
        {
            if (schemaVersion != SupportedSchemaVersion)
            {
                throw new NotSupportedException(
                    $"Vision pipeline schema version '{schemaVersion}' is not supported. Supported version: {SupportedSchemaVersion}.");
            }
        }
    }
}
