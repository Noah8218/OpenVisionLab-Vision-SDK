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
            return RunCore(pipeline, context, false);
        }

        /// <summary>
        /// Runs the configured steps and converts missing layers, factory failures, and thrown or null Tool results
        /// into failed step results. Invalid Pipeline definitions are still rejected before execution.
        /// </summary>
        public VisionPipelineRunResult RunWithFailureResults(VisionPipeline pipeline, VisionPipelineContext context)
        {
            return RunCore(pipeline, context, true);
        }

        private VisionPipelineRunResult RunCore(VisionPipeline pipeline, VisionPipelineContext context, bool captureStepFailures)
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

                    IVisionTool tool = null;
                    Mat input = null;

                    try
                    {
                        if (captureStepFailures)
                        {
                            input = context.GetLayer(step.InputLayer);
                            if (input == null)
                            {
                                AddFailureResult(
                                    runResult,
                                    step,
                                    VisionToolErrorCode.InputLayerMissing,
                                    $"Input layer '{step.InputLayer}' was not found.");
                                break;
                            }
                        }

                        try
                        {
                            tool = toolFactory(step);
                        }
                        catch (Exception exception) when (captureStepFailures)
                        {
                            AddFailureResult(
                                runResult,
                                step,
                                VisionToolErrorCode.ToolFactoryFailed,
                                $"Vision tool creation failed for step '{step.Name}': {exception.Message}",
                                exception);
                            break;
                        }

                        if (tool == null)
                        {
                            string message = $"Vision tool factory returned null for step '{step.Name}'.";
                            if (captureStepFailures)
                            {
                                AddFailureResult(runResult, step, VisionToolErrorCode.ToolFactoryFailed, message);
                                break;
                            }

                            throw new InvalidOperationException(message);
                        }

                        if (!captureStepFailures)
                        {
                            input = context.GetLayer(step.InputLayer);
                        }

                        VisionToolResult toolResult;
                        try
                        {
                            toolResult = tool.Execute(input);
                        }
                        catch (Exception exception) when (captureStepFailures)
                        {
                            AddFailureResult(
                                runResult,
                                step,
                                VisionToolErrorCode.ToolExecutionException,
                                $"Vision tool execution failed for step '{step.Name}': {exception.Message}",
                                exception);
                            break;
                        }

                        if (captureStepFailures && toolResult == null)
                        {
                            AddFailureResult(
                                runResult,
                                step,
                                VisionToolErrorCode.ToolExecutionException,
                                $"Vision tool returned no result for step '{step.Name}'.");
                            break;
                        }

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
                    finally
                    {
                        input?.Dispose();
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

        private static void AddFailureResult(
            VisionPipelineRunResult runResult,
            VisionPipelineStep step,
            VisionToolErrorCode errorCode,
            string message,
            Exception exception = null)
        {
            runResult.StepResults.Add(new VisionPipelineStepResult
            {
                Step = step,
                ToolResult = VisionToolResult.Failed(errorCode, message, TimeSpan.Zero, exception),
                AcceptancePassed = false,
                AcceptanceMessage = message
            });
        }

        private static void ValidatePipeline(VisionPipeline pipeline)
        {
            VisionPipelineArtifactValidation.ValidatePipeline(pipeline);

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
