using OpenVisionLab.Vision3D.FeatureExtraction;
using System;
using System.Collections.Generic;
using System.Threading;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class ThreeDToolExecutionSmokeSuite
    {
        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("3D adapter report preserves heterogeneous typed results", TestTypedResults);
            yield return new SmokeCase("3D adapter report separates execution from domain decision", TestDomainFailureIsCompletedExecution);
            yield return new SmokeCase("3D adapter runner retains later evidence after a fault", TestFaultAndContinue);
            yield return new SmokeCase("3D adapter runner rejects a null typed result and continues", TestNullResultAndContinue);
            yield return new SmokeCase("3D adapter runner records cooperative cancellation and stops", TestCooperativeCancellation);
            yield return new SmokeCase("3D adapter runner honors cancellation before the first call", TestPreCanceled);
            yield return new SmokeCase("3D adapter runner retains a completed noncooperative result on cancellation", TestNoncooperativeCancellation);
            yield return new SmokeCase("3D adapter runner fails an empty configuration", TestEmptyConfiguration);
        }

        private static void TestTypedResults()
        {
            TwoPointLineTool lineTool = new TwoPointLineTool();
            TwoPointLineInput lineInput = new TwoPointLineInput(
                new ThreeDPoint(1.0, 2.0, 3.0),
                new ThreeDPoint(4.0, 6.0, 3.0));
            RepeatabilityStatisticsTool statisticsTool = new RepeatabilityStatisticsTool();
            double[] repeatabilityValues = { 1.0, 1.5, 2.0 };
            ThreeDToolAdapter<TwoPointLineResult> lineAdapter = new ThreeDToolAdapter<TwoPointLineResult>(
                "Two-point line",
                token => lineTool.Execute(lineInput, token));
            ThreeDToolAdapter<RepeatabilityStatisticsResult> statisticsAdapter =
                new ThreeDToolAdapter<RepeatabilityStatisticsResult>(
                    "Repeatability",
                    () => statisticsTool.Execute(repeatabilityValues));

            ThreeDToolExecutionReport report = ThreeDToolExecutionRunner.Run(
                new IThreeDToolAdapter[] { lineAdapter, statisticsAdapter });

            Require(report.Status == ThreeDToolExecutionStatus.Completed, "Both typed adapters must complete.");
            Require(report.Steps.Count == 2 && report.CompletedStepCount == 2, "The report did not retain both ordered steps.");
            Require(lineAdapter.SupportsCancellation, "The token delegate must declare cooperative cancellation.");
            Require(!statisticsAdapter.SupportsCancellation, "The tokenless delegate must not claim cooperative cancellation.");

            ThreeDToolExecutionResult<TwoPointLineResult> lineStep =
                report.Steps[0] as ThreeDToolExecutionResult<TwoPointLineResult>;
            ThreeDToolExecutionResult<RepeatabilityStatisticsResult> statisticsStep =
                report.Steps[1] as ThreeDToolExecutionResult<RepeatabilityStatisticsResult>;
            Require(lineStep?.Result != null && lineStep.Result.Success, "The exact line result type was not retained.");
            Require(lineStep.Result.SegmentLength == 5.0, "The typed line result changed through the adapter.");
            Require(ReferenceEquals(lineStep.Result, lineStep.UntypedResult), "The common and typed result views diverged.");
            Require(statisticsStep?.Result != null && statisticsStep.Result.Success, "The exact statistics result type was not retained.");
            Require(statisticsStep.ResultType == typeof(RepeatabilityStatisticsResult), "The report lost the declared result type.");
        }

        private static void TestDomainFailureIsCompletedExecution()
        {
            TwoPointLineResult domainFailure = null;
            ThreeDToolExecutionReport report = ThreeDToolExecutionRunner.Run(
                new IThreeDToolAdapter[]
                {
                    new ThreeDToolAdapter<TwoPointLineResult>(
                        "Degenerate line",
                        token =>
                        {
                            domainFailure = new TwoPointLineTool().Execute(
                                new TwoPointLineInput(new ThreeDPoint(1.0, 1.0, 1.0), new ThreeDPoint(1.0, 1.0, 1.0)),
                                token);
                            return domainFailure;
                        })
                });

            Require(domainFailure != null && !domainFailure.Success, "The fixture must produce a controlled domain failure.");
            Require(report.Status == ThreeDToolExecutionStatus.Completed, "A returned domain failure is still completed execution.");
            Require(report.FaultedStepCount == 0, "The report must not reinterpret a domain decision as an execution fault.");
        }

        private static void TestFaultAndContinue()
        {
            int laterExecutions = 0;
            ThreeDToolExecutionReport report = ThreeDToolExecutionRunner.Run(
                new IThreeDToolAdapter[]
                {
                    new ThreeDToolAdapter<string>("Throwing adapter", () => throw new InvalidOperationException("fixture")),
                    new ThreeDToolAdapter<int>("Later adapter", () =>
                    {
                        laterExecutions++;
                        return 42;
                    })
                });

            Require(report.Status == ThreeDToolExecutionStatus.Faulted, "One throwing adapter must fault the report.");
            Require(report.Steps.Count == 2 && report.FaultedStepCount == 1, "The fault report did not retain both steps.");
            Require(laterExecutions == 1, "A fault must not suppress later independent evidence.");
            Require(report.Steps[0].Exception is InvalidOperationException, "The original adapter exception was not retained.");
            Require(((ThreeDToolExecutionResult<int>)report.Steps[1]).Result == 42, "The later typed result was not retained.");
        }

        private static void TestCooperativeCancellation()
        {
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                int laterExecutions = 0;
                ThreeDToolExecutionReport report = ThreeDToolExecutionRunner.Run(
                    new IThreeDToolAdapter[]
                    {
                        new ThreeDToolAdapter<int>("Cancelable adapter", token =>
                        {
                            cancellation.Cancel();
                            token.ThrowIfCancellationRequested();
                            return 1;
                        }),
                        new ThreeDToolAdapter<int>("Suppressed adapter", () => ++laterExecutions)
                    },
                    cancellation.Token);

                Require(report.Status == ThreeDToolExecutionStatus.Canceled, "Cooperative cancellation must cancel the report.");
                Require(report.Steps.Count == 1 && report.CanceledStepCount == 1, "The canceled adapter evidence was not retained.");
                Require(report.Steps[0].Exception is OperationCanceledException, "The cancellation exception was not retained.");
                Require(laterExecutions == 0, "No adapter may start after cooperative cancellation.");
            }
        }

        private static void TestNullResultAndContinue()
        {
            int laterExecutions = 0;
            ThreeDToolExecutionReport report = ThreeDToolExecutionRunner.Run(
                new IThreeDToolAdapter[]
                {
                    new ThreeDToolAdapter<string>("Null adapter", () => null),
                    new ThreeDToolAdapter<int>("Later adapter", () => ++laterExecutions)
                });

            Require(report.Status == ThreeDToolExecutionStatus.Faulted, "A null typed result must fault the report.");
            Require(report.Steps.Count == 2 && report.FaultedStepCount == 1, "The null result did not produce one faulted step.");
            Require(report.Steps[0].Exception is InvalidOperationException, "The null-result contract did not retain its exception.");
            Require(laterExecutions == 1, "A null-result fault must not suppress later evidence.");
        }

        private static void TestPreCanceled()
        {
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                int executions = 0;
                cancellation.Cancel();
                ThreeDToolExecutionReport report = ThreeDToolExecutionRunner.Run(
                    new IThreeDToolAdapter[]
                    {
                        new ThreeDToolAdapter<int>("Suppressed adapter", token =>
                        {
                            executions++;
                            return 1;
                        })
                    },
                    cancellation.Token);

                Require(report.Status == ThreeDToolExecutionStatus.Canceled, "A pre-canceled run must report cancellation.");
                Require(report.Steps.Count == 0 && executions == 0, "A pre-canceled run must not start an adapter.");
            }
        }

        private static void TestNoncooperativeCancellation()
        {
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                int laterExecutions = 0;
                ThreeDToolExecutionReport report = ThreeDToolExecutionRunner.Run(
                    new IThreeDToolAdapter[]
                    {
                        new ThreeDToolAdapter<string>("Tokenless adapter", () =>
                        {
                            cancellation.Cancel();
                            return "retained";
                        }),
                        new ThreeDToolAdapter<int>("Suppressed adapter", () => ++laterExecutions)
                    },
                    cancellation.Token);

                ThreeDToolExecutionResult<string> first = report.Steps[0] as ThreeDToolExecutionResult<string>;
                Require(report.Status == ThreeDToolExecutionStatus.Canceled, "Cancellation observed after a tokenless call must stop the report.");
                Require(first?.Status == ThreeDToolExecutionStatus.Completed && first.Result == "retained", "A completed tokenless result must not be discarded or relabeled.");
                Require(!first.SupportsCancellation, "The tokenless adapter must report its real capability.");
                Require(laterExecutions == 0, "No later adapter may start after cancellation is observed.");
            }
        }

        private static void TestEmptyConfiguration()
        {
            ThreeDToolExecutionReport report = ThreeDToolExecutionRunner.Run(Array.Empty<IThreeDToolAdapter>());

            Require(report.Status == ThreeDToolExecutionStatus.Faulted, "An empty adapter configuration must fail closed.");
            Require(report.Steps.Count == 0, "An empty configuration must not invent a Tool result.");
        }
    }
}
