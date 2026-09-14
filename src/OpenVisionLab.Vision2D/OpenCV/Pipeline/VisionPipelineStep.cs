using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;

namespace OpenVisionLab.Vision2D.Pipeline
{
    public class VisionPipelineParameter
    {
        public VisionPipelineParameter()
        {
        }

        public VisionPipelineParameter(string key, string value)
        {
            Key = key;
            Value = value;
        }

        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    public class VisionPipelineStep
    {
        public string Name { get; set; } = string.Empty;
        public string ToolType { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public string InputLayer { get; set; } = string.Empty;
        public string OutputLayer { get; set; } = string.Empty;
        /// <summary>Gets or sets whether acceptance rules determine the step outcome.</summary>
        public bool UseAcceptance { get; set; }

        /// <summary>
        /// Gets or sets the required tool success state. A false value is valid only on the final enabled step.
        /// </summary>
        public bool ExpectedSuccess { get; set; } = true;
        public double MaxElapsedMilliseconds { get; set; }
        public string RequiredMessageText { get; set; } = string.Empty;
        public string AcceptanceMetricName { get; set; } = string.Empty;
        public bool UseAcceptanceMetricMinimum { get; set; }
        public double AcceptanceMetricMinimum { get; set; }
        public bool UseAcceptanceMetricMaximum { get; set; }
        public double AcceptanceMetricMaximum { get; set; }

        /// <summary>Gets the case-insensitive built-in tool parameter values.</summary>
        [XmlIgnore]
        public Dictionary<string, string> Parameters { get; } = new Dictionary<string, string>();

        /// <summary>Gets the host-resolved artifact identities required to reconstruct this Tool.</summary>
        [XmlArray("Artifacts")]
        [XmlArrayItem("Artifact")]
        public List<VisionPipelineArtifactReference> Artifacts { get; } = new List<VisionPipelineArtifactReference>();

        [XmlArray("Parameters")]
        [XmlArrayItem("Parameter")]
        public VisionPipelineParameter[] XmlParameters
        {
            get => Parameters
                .Select(parameter => new VisionPipelineParameter(parameter.Key, parameter.Value))
                .ToArray();
            set
            {
                Dictionary<string, string> parsedParameters = new Dictionary<string, string>();
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (value == null)
                {
                    Parameters.Clear();
                    return;
                }

                foreach (VisionPipelineParameter parameter in value)
                {
                    if (parameter == null || string.IsNullOrWhiteSpace(parameter.Key))
                    {
                        throw new ArgumentException("Vision pipeline parameters cannot be null or have an empty key.", nameof(value));
                    }

                    if (!seen.Add(parameter.Key))
                    {
                        throw new ArgumentException(
                            $"Vision pipeline parameter '{parameter.Key}' is duplicated.",
                            nameof(value));
                    }

                    parsedParameters.Add(parameter.Key, parameter.Value ?? string.Empty);
                }

                Parameters.Clear();
                foreach (KeyValuePair<string, string> parameter in parsedParameters)
                {
                    Parameters.Add(parameter.Key, parameter.Value);
                }
            }
        }
    }
}
