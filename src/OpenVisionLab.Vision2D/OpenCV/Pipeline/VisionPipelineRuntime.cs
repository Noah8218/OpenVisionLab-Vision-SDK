using OpenVisionLab.Vision2D.Tool;
using OpenCvSharp;
using System;

namespace OpenVisionLab.Vision2D.Pipeline
{
    /// <summary>Executes an ordered, layer-based 2D vision pipeline.</summary>
    public class VisionPipelineRuntime
    {
        private readonly Func<VisionPipelineStep, IVisionTool> toolFactory;
        private readonly bool disposeCreatedTools;

        public VisionPipelineRuntime()
            : this(VisionPipelineToolFactory.Create, true)
        {
        }

        /// <summary>
        /// Uses caller-owned tools. The runtime does not dispose tools returned by this factory.
        /// </summary>
        public VisionPipelineRuntime(Func<VisionPipelineStep, IVisionTool> toolFactory)
            : this(toolFactory, false)
        {
        }

        /// <summary>
        /// Configures whether the runtime owns and disposes tools returned by the factory.
        /// </summary>
        public VisionPipelineRuntime(Func<VisionPipelineStep, IVisionTool> toolFactory, bool disposeCreatedTools)
        {
            this.toolFactory = toolFactory ?? throw new ArgumentNullException(nameof(toolFactory));
            this.disposeCreatedTools = disposeCreatedTools;
        }

        /// <summary>
        /// Runs the configured steps and rejects invalid or non-terminal expected-failure contracts before execution.
        /// </summary>
        public VisionPipelineRunResult Run(VisionPipeline pipeline, VisionPipelineContext context)
        {
            if (pipeline == null)
            {
                throw new ArgumentNullException(nameof(pipeline));
            }

            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            ValidatePipeline(pipeline);

            VisionPipelineRunResult runResult = new VisionPipelineRunResult();

            try
            {
                foreach (VisionPipelineStep step in pipeline.Steps)
                {
                    if (!step.Enabled)
                    {
                        runResult.StepResults.Add(new VisionPipelineStepResult
                        {
                            Step = step,
                            Skipped = true,
                            AcceptancePassed = true,
                            AcceptanceMessage = "Step is disabled."
                        });
                        continue;
                    }

                    IVisionTool tool = toolFactory(step);
                    if (tool == null)
                    {
                        throw new InvalidOperationException($"Vision tool factory returned null for step '{step?.Name}'.");
                    }

                    try
                    {
                        using (Mat input = context.GetLayer(step.InputLayer))
                        {
                            VisionToolResult toolResult = tool.Execute(input);
                            VisionPipelineAcceptanceResult acceptance = VisionPipelineAcceptanceEvaluator.Evaluate(step, toolResult);

                            VisionPipelineStepResult stepResult = new VisionPipelineStepResult
                            {
                                Step = step,
                                ToolResult = toolResult,
                                AcceptancePassed = acceptance.Passed,
                                AcceptanceMessage = acceptance.Message
                            };
                            runResult.StepResults.Add(stepResult);

                            if (!stepResult.Success)
                            {
                                break;
                            }

                            if (toolResult.ResultImage != null
                                && !string.IsNullOrWhiteSpace(step.OutputLayer))
                            {
                                context.SetLayer(step.OutputLayer, toolResult.ResultImage);
                            }
                        }
                    }
                    finally
                    {
                        if (disposeCreatedTools && tool is IDisposable disposableTool)
                        {
                            disposableTool.Dispose();
                        }
                    }
                }
            }
            catch
            {
                runResult.Dispose();
                throw;
            }

            return runResult;
        }

        private static void ValidatePipeline(VisionPipeline pipeline)
        {
            bool expectedFailureMustBeTerminal = false;
            foreach (VisionPipelineStep step in pipeline.Steps)
            {
                if (step == null)
                {
                    throw new InvalidOperationException("Vision pipeline steps cannot contain null.");
                }

                if (!step.Enabled)
                {
                    continue;
                }

                if (expectedFailureMustBeTerminal)
                {
                    throw new InvalidOperationException(
                        "A step with ExpectedSuccess=false must be the final enabled pipeline step.");
                }

                expectedFailureMustBeTerminal = step.UseAcceptance && !step.ExpectedSuccess;
            }
        }
    }
}
