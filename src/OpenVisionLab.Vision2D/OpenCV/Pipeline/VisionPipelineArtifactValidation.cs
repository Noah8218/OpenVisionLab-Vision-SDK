using System;

namespace OpenVisionLab.Vision2D.Pipeline
{
    internal static class VisionPipelineArtifactValidation
    {
        public static void ValidatePipeline(VisionPipeline pipeline)
        {
            if (pipeline == null)
            {
                throw new ArgumentNullException(nameof(pipeline));
            }

            if (pipeline.SchemaVersion < 1 || pipeline.SchemaVersion > VisionPipeline.CurrentSchemaVersion)
            {
                throw new NotSupportedException(
                    $"Vision pipeline schema version '{pipeline.SchemaVersion}' is not supported. Supported versions: 1..{VisionPipeline.CurrentSchemaVersion}.");
            }

            foreach (VisionPipelineStep step in pipeline.Steps)
            {
                if (step == null)
                {
                    continue;
                }

                if (pipeline.SchemaVersion == 1 && step.Artifacts.Count > 0)
                {
                    throw new NotSupportedException("Vision pipeline schema version 1 cannot contain artifact references.");
                }

                foreach (VisionPipelineArtifactReference artifact in step.Artifacts)
                {
                    ValidateReference(artifact, nameof(pipeline));
                }
            }
        }

        public static void ValidateReference(VisionPipelineArtifactReference artifact, string parameterName)
        {
            if (artifact == null)
            {
                throw new ArgumentException("Vision pipeline artifact references cannot contain null.", parameterName);
            }

            if (string.IsNullOrWhiteSpace(artifact.Role))
            {
                throw new ArgumentException("Vision pipeline artifact role is required.", parameterName);
            }

            if (string.IsNullOrWhiteSpace(artifact.Id))
            {
                throw new ArgumentException("Vision pipeline artifact ID is required.", parameterName);
            }

            if (string.IsNullOrWhiteSpace(artifact.Format))
            {
                throw new ArgumentException("Vision pipeline artifact format is required.", parameterName);
            }

            if (artifact.FormatVersion <= 0)
            {
                throw new ArgumentException("Vision pipeline artifact format version must be positive.", parameterName);
            }

            if (!IsSha256(artifact.Sha256))
            {
                throw new ArgumentException("Vision pipeline artifact SHA-256 must contain exactly 64 hexadecimal characters.", parameterName);
            }
        }

        private static bool IsSha256(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
            {
                return false;
            }

            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool hexadecimal = (character >= '0' && character <= '9')
                    || (character >= 'a' && character <= 'f')
                    || (character >= 'A' && character <= 'F');
                if (!hexadecimal)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
