using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenVisionLab.Vision2D.Pipeline
{
    /// <summary>
    /// Owns the tool results collected for each pipeline step.
    /// </summary>
    public class VisionPipelineRunResult : IDisposable
    {
        public List<VisionPipelineStepResult> StepResults { get; } = new List<VisionPipelineStepResult>();

        /// <summary>
        /// Gets whether at least one enabled step ran and every recorded step satisfied its contract.
        /// </summary>
        public bool Success => StepResults.Any(result => result != null && !result.Skipped)
            && StepResults.All(result => result != null && result.Success);

        public void Dispose()
        {
            foreach (VisionPipelineStepResult stepResult in StepResults)
            {
                stepResult?.ToolResult?.Dispose();
            }

            GC.SuppressFinalize(this);
        }
    }
}
