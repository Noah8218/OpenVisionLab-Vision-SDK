using OpenVisionLab.Vision2D.Tool;
using OpenVisionLab.Vision3D.Inspection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace OpenVisionLab.Inspection
{
    /// <summary>
    /// Executes independent 2D and 3D tool lists without changing VisionPipeline's Mat/layer semantics.
    /// Each configured step runs even if an earlier step fails so the caller retains all evidence.
    /// </summary>
    public sealed class CombinedInspectionRunner
    {
        public CombinedInspectionRunResult Run(
            CombinedInspectionInput input,
            IEnumerable<IVisionTool> twoDTools,
            IEnumerable<IThreeDInspectionTool> threeDTools)
        {
            return Run(input, twoDTools, threeDTools, CancellationToken.None);
        }

        /// <summary>Runs independent steps, stopping and releasing unreturned results on caller cancellation.</summary>
        public CombinedInspectionRunResult Run(
            CombinedInspectionInput input, IEnumerable<IVisionTool> twoDTools,
            IEnumerable<IThreeDInspectionTool> threeDTools, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Stopwatch stopwatch = Stopwatch.StartNew();
            CombinedInspectionRunResult result = new CombinedInspectionRunResult();
            CombinedInspectionInput resolvedInput = input ?? new CombinedInspectionInput();

            try
            {
                ExecuteTwoDTools(result, resolvedInput, twoDTools, cancellationToken);
                ExecuteThreeDTools(result, resolvedInput, threeDTools, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch
            {
                result.Dispose();
                throw;
            }

            stopwatch.Stop();
            result.Elapsed = stopwatch.Elapsed;

            if (result.Steps.Count == 0)
            {
                result.Success = false;
                result.Message = "No 2D or 3D inspection tools were configured.";
                return result;
            }

            result.Success = result.FailedStepCount == 0;
            result.Message = result.Success
                ? "All configured inspection steps passed."
                : result.FailedStepCount + " configured inspection step(s) did not pass.";
            return result;
        }

        private static void ExecuteTwoDTools(
            CombinedInspectionRunResult result,
            CombinedInspectionInput input,
            IEnumerable<IVisionTool> tools, CancellationToken cancellationToken)
        {
            if (tools == null)
            {
                return;
            }

            foreach (IVisionTool tool in tools)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (tool == null)
                {
                    result.AddStep(new CombinedInspectionStepResult
                    {
                        Domain = CombinedInspectionDomain.TwoD,
                        ToolName = "Unconfigured 2D tool",
                        VisionResult = VisionToolResult.Failed(
                            VisionToolErrorCode.ToolPropertyMissing,
                            "A configured 2D tool was null.",
                            TimeSpan.Zero)
                    });
                    continue;
                }

                string toolName = ResolveVisionToolName(tool, "Unnamed 2D tool");
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    result.AddStep(new CombinedInspectionStepResult
                    {
                        Domain = CombinedInspectionDomain.TwoD,
                        ToolName = toolName,
                        VisionResult = (tool is ICancellableVisionTool cancellable ? cancellable.Execute(input.Image, cancellationToken) : tool.Execute(input.Image))
                            ?? VisionToolResult.Failed(
                                VisionToolErrorCode.ToolExecutionException,
                                "The 2D tool returned no result.",
                                TimeSpan.Zero)
                    });
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    result.AddStep(new CombinedInspectionStepResult
                    {
                        Domain = CombinedInspectionDomain.TwoD,
                        ToolName = toolName,
                        VisionResult = VisionToolResult.Failed(
                            VisionToolErrorCode.ToolExecutionException,
                            "The 2D tool threw an exception.",
                            TimeSpan.Zero,
                            exception)
                    });
                }
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        private static void ExecuteThreeDTools(
            CombinedInspectionRunResult result,
            CombinedInspectionInput input,
            IEnumerable<IThreeDInspectionTool> tools, CancellationToken cancellationToken)
        {
            if (tools == null)
            {
                return;
            }

            foreach (IThreeDInspectionTool tool in tools)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (tool == null)
                {
                    result.AddStep(new CombinedInspectionStepResult
                    {
                        Domain = CombinedInspectionDomain.ThreeD,
                        ToolName = "Unconfigured 3D tool",
                        ThreeDResult = ThreeDInspectionResult.Failed(
                            ThreeDInspectionErrorCode.ToolNotConfigured,
                            "A configured 3D tool was null.",
                            TimeSpan.Zero)
                    });
                    continue;
                }

                string toolName = ResolveThreeDToolName(tool, "Unnamed 3D tool");
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    result.AddStep(new CombinedInspectionStepResult
                    {
                        Domain = CombinedInspectionDomain.ThreeD,
                        ToolName = toolName,
                        ThreeDResult = (tool is ICancellableThreeDInspectionTool cancellable ? cancellable.Execute(input.HeightMap, cancellationToken) : tool.Execute(input.HeightMap))
                            ?? ThreeDInspectionResult.Failed(
                                ThreeDInspectionErrorCode.ToolExecutionException,
                                "The 3D tool returned no result.",
                                TimeSpan.Zero,
                                input.HeightMap)
                    });
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    result.AddStep(new CombinedInspectionStepResult
                    {
                        Domain = CombinedInspectionDomain.ThreeD,
                        ToolName = toolName,
                        ThreeDResult = ThreeDInspectionResult.Failed(
                            ThreeDInspectionErrorCode.ToolExecutionException,
                            "The 3D tool threw an exception.",
                            TimeSpan.Zero,
                            input.HeightMap,
                            null,
                            exception)
                    });
                }
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        private static string ResolveVisionToolName(IVisionTool tool, string fallback)
        {
            try
            {
                return ResolveToolName(tool.Name, fallback);
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        private static string ResolveThreeDToolName(IThreeDInspectionTool tool, string fallback)
        {
            try
            {
                return ResolveToolName(tool.Name, fallback);
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        private static string ResolveToolName(string name, string fallback)
        {
            return string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
        }
    }
}
