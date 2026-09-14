using OpenVisionLab.Vision2D.Tool;
using OpenCvSharp;
using System;
using System.Diagnostics;
using System.Threading;

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
            return RunCore(pipeline, context, false, false, CancellationToken.None);
        }

        /// <summary>
        /// Runs the configured steps with cooperative cancellation. Cancellation is propagated to the caller.
        /// </summary>
        public VisionPipelineRunResult Run(
            VisionPipeline pipeline,
            VisionPipelineContext context,
            CancellationToken cancellationToken)
        {
            return RunCore(pipeline, context, false, true, cancellationToken);
        }

        /// <summary>
        /// Runs the configured steps and converts missing layers, factory failures, and thrown or null Tool results
        /// into failed step results. Invalid Pipeline definitions are still rejected before execution.
        /// </summary>
        public VisionPipelineRunResult RunWithFailureResults(VisionPipeline pipeline, VisionPipelineContext context)
        {
            return RunCore(pipeline, context, true, false, CancellationToken.None);
        }

        /// <summary>
        /// Runs the configured steps with cooperative cancellation and records cancellation as one failed step result.
        /// </summary>
        public VisionPipelineRunResult RunWithFailureResults(
            VisionPipeline pipeline,
            VisionPipelineContext context,
            CancellationToken cancellationToken)
        {
            return RunCore(pipeline, context, true, true, cancellationToken);
        }

        private VisionPipelineRunResult RunCore(
            VisionPipeline pipeline,
            VisionPipelineContext context,
            bool captureStepFailures,
            bool cancellationEnabled,
            CancellationToken cancellationToken)
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

                    if (StopForCancellation(
                        runResult,
                        step,
                        captureStepFailures,
                        cancellationEnabled,
                        TimeSpan.Zero,
                        cancellationToken))
                    {
                        break;
                    }

                    IVisionTool tool = null;
                    Mat input = null;

                    try
                    {
                        if (captureStepFailures)
                        {
                            input = context.GetLayer(step.InputLayer);
                            if (StopForCancellation(
                                runResult,
                                step,
                                captureStepFailures,
                                cancellationEnabled,
                                TimeSpan.Zero,
                                cancellationToken))
                            {
                                break;
                            }

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
                        catch (OperationCanceledException exception)
                            when (captureStepFailures
                                && cancellationEnabled
                                && cancellationToken.IsCancellationRequested)
                        {
                            AddCancellationResult(runResult, step, TimeSpan.Zero, exception);
                            break;
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

                        if (StopForCancellation(
                            runResult,
                            step,
                            captureStepFailures,
                            cancellationEnabled,
                            TimeSpan.Zero,
                            cancellationToken))
                        {
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
                            if (StopForCancellation(
                                runResult,
                                step,
                                captureStepFailures,
                                cancellationEnabled,
                                TimeSpan.Zero,
                                cancellationToken))
                            {
                                break;
                            }
                        }

                        VisionToolResult toolResult = null;
                        Stopwatch executionStopwatch = Stopwatch.StartNew();
                        try
                        {
                            toolResult = cancellationEnabled && tool is ICancellableVisionTool cancellableTool
                                ? cancellableTool.Execute(input, cancellationToken)
                                : tool.Execute(input);
                            executionStopwatch.Stop();

                            if (cancellationEnabled && cancellationToken.IsCancellationRequested)
                            {
                                toolResult?.Dispose();
                                toolResult = null;
                                cancellationToken.ThrowIfCancellationRequested();
                            }
                        }
                        catch (OperationCanceledException exception)
                            when (cancellationEnabled && cancellationToken.IsCancellationRequested)
                        {
                            executionStopwatch.Stop();
                            toolResult?.Dispose();
                            if (captureStepFailures)
                            {
                                AddCancellationResult(runResult, step, executionStopwatch.Elapsed, exception);
                                break;
                            }

                            throw;
                        }
                        catch (Exception exception) when (captureStepFailures)
                        {
                            executionStopwatch.Stop();
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

        private static bool StopForCancellation(
            VisionPipelineRunResult runResult,
            VisionPipelineStep step,
            bool captureStepFailures,
            bool cancellationEnabled,
            TimeSpan elapsed,
            CancellationToken cancellationToken)
        {
            if (!cancellationEnabled || !cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            OperationCanceledException exception = new OperationCanceledException(cancellationToken);
            if (!captureStepFailures)
            {
                throw exception;
            }

            AddCancellationResult(runResult, step, elapsed, exception);
            return true;
        }

        private static void AddCancellationResult(
            VisionPipelineRunResult runResult,
            VisionPipelineStep step,
            TimeSpan elapsed,
            OperationCanceledException exception)
        {
            string message = $"Pipeline step '{step.Name}' was canceled.";
            runResult.StepResults.Add(new VisionPipelineStepResult
            {
                Step = step,
                ToolResult = VisionToolResult.Failed(
                    VisionToolErrorCode.StepCanceled,
                    message,
                    elapsed,
                    exception),
                AcceptancePassed = false,
                AcceptanceMessage = message
            });
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
