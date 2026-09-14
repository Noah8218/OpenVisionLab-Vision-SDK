using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace OpenVisionLab.Vision2D.Pipeline
{
    /// <summary>Serializes and validates the supported Vision Pipeline XML schema.</summary>
    public static class VisionPipelineSerializer
    {
        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(VisionPipeline));

        /// <summary>Serializes a supported pipeline to indented XML text.</summary>
        public static string Serialize(VisionPipeline pipeline)
        {
            if (pipeline == null)
            {
                throw new ArgumentNullException(nameof(pipeline));
            }

            VisionPipelineArtifactValidation.ValidatePipeline(pipeline);

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
        /// Deserializes Pipeline XML, rejects DTD processing, and accepts schema versions 1 through the current version.
        /// XML without a schemaVersion attribute is treated as the original version 1 contract. Loading never resolves
        /// an artifact or executes a Tool.
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
                try
                {
                    reader.MoveToContent();
                }
                catch (XmlException exception)
                {
                    throw new InvalidOperationException("Serialized vision pipeline XML could not be read.", exception);
                }

                int declaredSchemaVersion = ReadDeclaredSchemaVersion(reader);
                pipeline = Serializer.Deserialize(reader) as VisionPipeline;
                if (pipeline != null)
                {
                    pipeline.SchemaVersion = declaredSchemaVersion;
                }
            }

            if (pipeline == null)
            {
                throw new InvalidOperationException("Serialized XML did not contain a VisionPipeline.");
            }

            VisionPipelineArtifactValidation.ValidatePipeline(pipeline);
            return pipeline;
        }

        private static int ReadDeclaredSchemaVersion(XmlReader reader)
        {
            string value = reader.GetAttribute("schemaVersion");
            if (value == null)
            {
                return 1;
            }

            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int schemaVersion))
            {
                throw new NotSupportedException($"Vision pipeline schema version '{value}' is not valid.");
            }

            return schemaVersion;
        }
    }
}
