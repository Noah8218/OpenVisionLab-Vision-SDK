using OpenVisionLab.Vision2D.Pipeline;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Tool;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Threading;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class Vision2DCancellationSmokeSuite
    {
        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("2D cancellation rejects pre-canceled expensive Tools", TestBuiltInPreCancellation);
            yield return new SmokeCase("2D cancellation observes managed and native boundaries and resets state", TestBaseCancellationCheckpoints);
            yield return new SmokeCase("2D cancellation propagates through Pipeline and releases completed work", TestPipelineCancellationPropagation);
            yield return new SmokeCase("2D cancellation records one Pipeline result and releases noncooperative work", TestPipelineCancellationResult);
            yield return new SmokeCase("2D cancellation leaves legacy Pipeline exception classification unchanged", TestLegacyPipelineCompatibility);
        }

        private static void TestBuiltInPreCancellation()
        {
            using (Mat source = new Mat(8, 8, MatType.CV_8UC1, Scalar.All(7)))
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                ICancellableVisionTool[] tools =
                {
                    new MatchingTool(),
                    new EdgeBasedTemplateMatchingTool(),
                    new AutoMPointTool(),
                    new SiftTool()
                };

                try
                {
                    foreach (ICancellableVisionTool tool in tools)
                    {
                        OperationCanceledException exception = RequireThrows<OperationCanceledException>(
                            () => tool.Execute(source, cancellation.Token));
                        Require(exception.CancellationToken == cancellation.Token,
                            tool.Name + " did not propagate the caller cancellation token.");
                    }
                }
                finally
                {
                    foreach (ICancellableVisionTool tool in tools)
                    {
                        (tool as IDisposable)?.Dispose();
                    }
                }

                Require(!source.IsDisposed, "A pre-canceled Tool released the caller-owned source image.");
            }
        }

        private static void TestBaseCancellationCheckpoints()
        {
            using (Mat source = new Mat(16, 16, MatType.CV_8UC1, Scalar.All(11)))
            {
                VerifyCanceledProbe(source, CancellationProbeMode.ManagedLoop);
                VerifyCanceledProbe(source, CancellationProbeMode.NativeBoundary);
                Require(!source.IsDisposed, "A canceled base execution released the caller-owned source image.");
            }
        }

        private static void VerifyCanceledProbe(Mat source, CancellationProbeMode mode)
        {
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            using (CancellationProbeTool tool = new CancellationProbeTool(cancellation, mode))
            {
                OperationCanceledException exception = RequireThrows<OperationCanceledException>(
                    () => tool.Execute(source, cancellation.Token));
                Require(exception.CancellationToken == cancellation.Token,
                    "The base execution did not retain the cancellation token at the checkpoint.");

                using (VisionToolResult recovered = tool.Execute(source))
                {
                    Require(recovered.Success && tool.RunCount == 2,
                        "A canceled execution left its token active for the next legacy execution.");
                }
            }
        }

        private static void TestPipelineCancellationPropagation()
        {
            VisionPipeline pipeline = new VisionPipeline { Name = "Cancellation propagation" };
            pipeline.Steps.Add(CreateStep("first", "input", "intermediate"));
            pipeline.Steps.Add(CreateStep("cancel", "intermediate", "canceled-output"));
            pipeline.Steps.Add(CreateStep("later", "canceled-output", "unused"));

            using (Mat source = new Mat(8, 8, MatType.CV_8UC1, Scalar.All(13)))
            using (VisionPipelineContext context = new VisionPipelineContext())
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                context.SetLayer("input", source);
                TrackingDisposableVisionTool first = new TrackingDisposableVisionTool();
                CancelingCancellableVisionTool canceling = new CancelingCancellableVisionTool(cancellation);
                PassThroughVisionTool later = new PassThroughVisionTool();
                int factoryCalls = 0;
                VisionPipelineRuntime runtime = new VisionPipelineRuntime(step =>
                {
                    factoryCalls++;
                    if (step.ToolType == "first") { return first; }
                    if (step.ToolType == "cancel") { return canceling; }
                    return later;
                }, true);

                OperationCanceledException exception = RequireThrows<OperationCanceledException>(
                    () => runtime.Run(pipeline, context, cancellation.Token));

                Require(exception.CancellationToken == cancellation.Token,
                    "The Pipeline did not propagate the caller cancellation token.");
                Require(factoryCalls == 2 && canceling.TokenExecutions == 1 && canceling.LegacyExecutions == 0,
                    "The Pipeline did not route through the cancellable Tool contract exactly once.");
                Require(!later.WasExecuted,
                    "The Pipeline executed a later step after cooperative cancellation.");
                Require(first.WasDisposed && canceling.WasDisposed,
                    "The Pipeline did not dispose factory-owned Tools on cancellation.");
                Require(first.ResultSnapshot != null && first.ResultSnapshot.IsDisposed,
                    "The Pipeline did not dispose a completed result while propagating cancellation.");
                Require(first.LastSource != null && first.LastSource.IsDisposed
                    && canceling.LastSource != null && canceling.LastSource.IsDisposed,
                    "The Pipeline did not dispose cloned input layers on cancellation.");
                using (Mat intermediate = context.GetLayer("intermediate"))
                {
                    Require(intermediate != null && !intermediate.Empty(),
                        "Cancellation invalidated the independently owned completed output layer.");
                }
                Require(context.GetLayer("canceled-output") == null,
                    "The canceled step published an output layer.");
                Require(!source.IsDisposed,
                    "Pipeline cancellation released the caller-owned source image.");
            }
        }

        private static void TestPipelineCancellationResult()
        {
            VisionPipeline pipeline = new VisionPipeline { Name = "Cancellation result" };
            pipeline.Steps.Add(CreateStep("cancel", "input", "canceled-output"));
            pipeline.Steps.Add(CreateStep("later", "canceled-output", "unused"));

            using (Mat source = new Mat(8, 8, MatType.CV_8UC1, Scalar.All(17)))
            using (VisionPipelineContext context = new VisionPipelineContext())
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                context.SetLayer("input", source);
                CancelingNoncooperativeVisionTool canceling = new CancelingNoncooperativeVisionTool(cancellation);
                PassThroughVisionTool later = new PassThroughVisionTool();
                int factoryCalls = 0;
                VisionPipelineRuntime runtime = new VisionPipelineRuntime(step =>
                {
                    factoryCalls++;
                    return step.ToolType == "cancel" ? (IVisionTool)canceling : later;
                });

                using (VisionPipelineRunResult result = runtime.RunWithFailureResults(
                    pipeline,
                    context,
                    cancellation.Token))
                {
                    Require(!result.Success && result.StepResults.Count == 1,
                        "Captured cancellation must produce exactly one failed step result.");
                    VisionToolResult canceled = result.StepResults[0].ToolResult;
                    Require(canceled.ErrorCode == VisionToolErrorCode.StepCanceled
                        && canceled.ResultStatus == VisionToolResultStatus.Canceled
                        && canceled.Exception is OperationCanceledException
                        && !result.StepResults[0].AcceptancePassed,
                        "Captured cancellation did not retain its typed status and exception.");
                    Require(canceled.Elapsed >= TimeSpan.Zero,
                        "Captured cancellation reported an invalid elapsed duration.");
                }

                Require(factoryCalls == 1 && !later.WasExecuted,
                    "The Pipeline created or ran a later step after captured cancellation.");
                Require(canceling.ResultSnapshot != null && canceling.ResultSnapshot.IsDisposed,
                    "The Pipeline retained a result returned after noncooperative cancellation.");
                Require(canceling.LastSource != null && canceling.LastSource.IsDisposed,
                    "The Pipeline retained the canceled step's cloned input layer.");
                Require(context.GetLayer("canceled-output") == null,
                    "A result returned after cancellation was published as an output layer.");

                using (CancellationTokenSource preCanceled = new CancellationTokenSource())
                {
                    preCanceled.Cancel();
                    int preCanceledFactoryCalls = 0;
                    VisionPipelineRuntime preCanceledRuntime = new VisionPipelineRuntime(_ =>
                    {
                        preCanceledFactoryCalls++;
                        return new PassThroughVisionTool();
                    });
                    using (VisionPipelineRunResult preCanceledResult = preCanceledRuntime.RunWithFailureResults(
                        pipeline,
                        context,
                        preCanceled.Token))
                    {
                        Require(preCanceledResult.StepResults.Count == 1
                            && preCanceledResult.StepResults[0].ToolResult.ErrorCode == VisionToolErrorCode.StepCanceled
                            && preCanceledFactoryCalls == 0,
                            "A pre-canceled Pipeline must stop before Tool creation and record one cancellation.");
                    }
                }

                using (CancellationTokenSource factoryCancellation = new CancellationTokenSource())
                {
                    VisionPipelineRuntime factoryRuntime = new VisionPipelineRuntime(_ =>
                    {
                        factoryCancellation.Cancel();
                        factoryCancellation.Token.ThrowIfCancellationRequested();
                        throw new InvalidOperationException("Unreachable canceled factory fixture path.");
                    });
                    using (VisionPipelineRunResult factoryResult = factoryRuntime.RunWithFailureResults(
                        pipeline,
                        context,
                        factoryCancellation.Token))
                    {
                        Require(factoryResult.StepResults.Count == 1
                            && factoryResult.StepResults[0].ToolResult.ErrorCode == VisionToolErrorCode.StepCanceled
                            && factoryResult.StepResults[0].ToolResult.Exception is OperationCanceledException,
                            "Cancellation raised by a factory after the caller token was canceled was misclassified.");
                    }
                }
            }
        }

        private static void TestLegacyPipelineCompatibility()
        {
            VisionPipeline pipeline = new VisionPipeline { Name = "Legacy cancellation classification" };
            pipeline.Steps.Add(CreateStep("legacy", "input", "unused"));

            using (Mat source = new Mat(4, 4, MatType.CV_8UC1, Scalar.All(19)))
            using (VisionPipelineContext context = new VisionPipelineContext())
            {
                context.SetLayer("input", source);
                LegacyOperationCanceledVisionTool tool = new LegacyOperationCanceledVisionTool();
                using (VisionPipelineRunResult result = new VisionPipelineRuntime(_ => tool)
                    .RunWithFailureResults(pipeline, context))
                {
                    Require(result.StepResults.Count == 1
                        && result.StepResults[0].ToolResult.ErrorCode == VisionToolErrorCode.ToolExecutionException
                        && result.StepResults[0].ToolResult.ResultStatus == VisionToolResultStatus.Exception
                        && result.StepResults[0].ToolResult.Exception is OperationCanceledException,
                        "The tokenless Pipeline overload changed the legacy OperationCanceledException classification.");
                }

                Require(tool.LegacyExecutions == 1 && tool.TokenExecutions == 0,
                    "The tokenless Pipeline overload called the new cancellable entry point.");

                using (CancellationTokenSource notCanceled = new CancellationTokenSource())
                using (VisionPipelineRunResult result = new VisionPipelineRuntime(_ => tool)
                    .RunWithFailureResults(pipeline, context, notCanceled.Token))
                {
                    Require(result.StepResults.Count == 1
                        && result.StepResults[0].ToolResult.ErrorCode == VisionToolErrorCode.ToolExecutionException
                        && result.StepResults[0].ToolResult.ResultStatus == VisionToolResultStatus.Exception
                        && result.StepResults[0].ToolResult.Exception is OperationCanceledException,
                        "An OperationCanceledException without a requested caller token was misclassified as StepCanceled.");
                }

                Require(tool.TokenExecutions == 1,
                    "The token-aware Pipeline did not call the cancellable Tool entry point.");
            }
        }

        private static VisionPipelineStep CreateStep(string toolType, string inputLayer, string outputLayer)
        {
            return new VisionPipelineStep
            {
                Name = toolType,
                ToolType = toolType,
                InputLayer = inputLayer,
                OutputLayer = outputLayer
            };
        }

        private enum CancellationProbeMode
        {
            ManagedLoop,
            NativeBoundary
        }

        private sealed class CancellationProbeTool : OpenCvAlgorithmBase, ICancellableVisionTool
        {
            private readonly CancellationTokenSource cancellation;
            private readonly CancellationProbeMode mode;
            private bool cancelNextRun = true;

            public object property = new object();

            internal CancellationProbeTool(CancellationTokenSource cancellation, CancellationProbeMode mode)
            {
                this.cancellation = cancellation;
                this.mode = mode;
            }

            internal int RunCount { get; private set; }

            public VisionToolResult Execute(Mat source, CancellationToken cancellationToken)
            {
                return ExecuteWithCancellation(source, cancellationToken);
            }

            public override void Run()
            {
                RunCount++;
                if (!cancelNextRun)
                {
                    return;
                }

                cancelNextRun = false;
                if (mode == CancellationProbeMode.NativeBoundary)
                {
                    Cv2.Mean(imageSource);
                    cancellation.Cancel();
                    return;
                }

                for (int i = 0; i < 8; i++)
                {
                    if (i == 4)
                    {
                        cancellation.Cancel();
                    }

                    ExecutionCancellationToken.ThrowIfCancellationRequested();
                }
            }
        }

        private sealed class CancelingCancellableVisionTool : ICancellableVisionTool, IDisposable
        {
            private readonly CancellationTokenSource cancellation;

            internal CancelingCancellableVisionTool(CancellationTokenSource cancellation)
            {
                this.cancellation = cancellation;
            }

            public string Name => "Canceling cancellable 2D";

            internal int LegacyExecutions { get; private set; }

            internal int TokenExecutions { get; private set; }

            internal Mat LastSource { get; private set; }

            internal bool WasDisposed { get; private set; }

            public VisionToolResult Execute(Mat source)
            {
                LegacyExecutions++;
                throw new InvalidOperationException("The cancellable entry point was not used.");
            }

            public VisionToolResult Execute(Mat source, CancellationToken cancellationToken)
            {
                TokenExecutions++;
                LastSource = source;
                cancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                throw new InvalidOperationException("Unreachable cancellation fixture path.");
            }

            public void Dispose()
            {
                WasDisposed = true;
            }
        }

        private sealed class CancelingNoncooperativeVisionTool : IVisionTool
        {
            private readonly CancellationTokenSource cancellation;

            internal CancelingNoncooperativeVisionTool(CancellationTokenSource cancellation)
            {
                this.cancellation = cancellation;
            }

            public string Name => "Canceling noncooperative 2D";

            internal Mat LastSource { get; private set; }

            internal Mat ResultSnapshot { get; private set; }

            public VisionToolResult Execute(Mat source)
            {
                LastSource = source;
                ResultSnapshot = source?.Clone();
                cancellation.Cancel();
                return VisionToolResult.Passed(ResultSnapshot, TimeSpan.Zero);
            }
        }

        private sealed class LegacyOperationCanceledVisionTool : ICancellableVisionTool
        {
            public string Name => "Legacy OperationCanceledException 2D";

            internal int LegacyExecutions { get; private set; }

            internal int TokenExecutions { get; private set; }

            public VisionToolResult Execute(Mat source)
            {
                LegacyExecutions++;
                throw new OperationCanceledException("Legacy fixture cancellation-shaped exception.");
            }

            public VisionToolResult Execute(Mat source, CancellationToken cancellationToken)
            {
                TokenExecutions++;
                throw new OperationCanceledException("Unrequested fixture cancellation-shaped exception.", cancellationToken);
            }
        }
    }
}
