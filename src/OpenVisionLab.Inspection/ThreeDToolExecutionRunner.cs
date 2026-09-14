using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace OpenVisionLab.Inspection
{
    /// <summary>
    /// Executes heterogeneous typed 3D adapters in order. Faulted adapters do not
    /// prevent later evidence; cancellation stops the remaining adapters.
    /// </summary>
    public static class ThreeDToolExecutionRunner
    {
        public static ThreeDToolExecutionReport Run(IEnumerable<IThreeDToolAdapter> adapters)
        {
            return Run(adapters, CancellationToken.None);
        }

        public static ThreeDToolExecutionReport Run(
            IEnumerable<IThreeDToolAdapter> adapters,
            CancellationToken cancellationToken)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            List<ThreeDToolExecutionResult> steps = new List<ThreeDToolExecutionResult>();
            bool canceled = false;

            if (adapters != null)
            {
                foreach (IThreeDToolAdapter adapter in adapters)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        canceled = true;
                        break;
                    }

                    ThreeDToolExecutionResult step = ExecuteAdapter(adapter, cancellationToken);
                    steps.Add(step);
                    if (step.Status == ThreeDToolExecutionStatus.Canceled || cancellationToken.IsCancellationRequested)
                    {
                        canceled = true;
                        break;
                    }
                }
            }

            stopwatch.Stop();
            if (canceled)
            {
                return new ThreeDToolExecutionReport(
                    ThreeDToolExecutionStatus.Canceled,
                    stopwatch.Elapsed,
                    "3D tool adapter execution was canceled before all configured adapters completed.",
                    steps);
            }

            int faultedStepCount = CountFaultedSteps(steps);
            if (adapters == null || steps.Count == 0)
            {
                return new ThreeDToolExecutionReport(
                    ThreeDToolExecutionStatus.Faulted,
                    stopwatch.Elapsed,
                    "No 3D tool adapters were configured.",
                    steps);
            }

            return new ThreeDToolExecutionReport(
                faultedStepCount == 0
                    ? ThreeDToolExecutionStatus.Completed
                    : ThreeDToolExecutionStatus.Faulted,
                stopwatch.Elapsed,
                faultedStepCount == 0
                    ? "All configured 3D tool adapters completed."
                    : faultedStepCount.ToString(CultureInfo.InvariantCulture) + " configured 3D tool adapter(s) faulted.",
                steps);
        }

        private static ThreeDToolExecutionResult ExecuteAdapter(
            IThreeDToolAdapter adapter,
            CancellationToken cancellationToken)
        {
            if (adapter == null)
            {
                InvalidOperationException exception = new InvalidOperationException("A configured 3D tool adapter was null.");
                return ThreeDToolExecutionResult.Faulted(
                    "Unconfigured 3D adapter",
                    typeof(object),
                    false,
                    TimeSpan.Zero,
                    exception.Message,
                    exception);
            }

            string name = ResolveName(adapter);
            Type resultType = ResolveResultType(adapter);
            bool supportsCancellation = ResolveCancellationCapability(adapter);
            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                ThreeDToolExecutionResult result = adapter.Execute(cancellationToken);
                stopwatch.Stop();
                if (result != null)
                {
                    return result;
                }

                InvalidOperationException exception = new InvalidOperationException("The 3D tool adapter returned no execution result.");
                return ThreeDToolExecutionResult.Faulted(
                    name,
                    resultType,
                    supportsCancellation,
                    stopwatch.Elapsed,
                    exception.Message,
                    exception);
            }
            catch (OperationCanceledException exception)
            {
                stopwatch.Stop();
                return ThreeDToolExecutionResult.Canceled(
                    name,
                    resultType,
                    supportsCancellation,
                    stopwatch.Elapsed,
                    exception);
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                return ThreeDToolExecutionResult.Faulted(
                    name,
                    resultType,
                    supportsCancellation,
                    stopwatch.Elapsed,
                    "The 3D tool adapter threw an exception.",
                    exception);
            }
        }

        private static int CountFaultedSteps(IEnumerable<ThreeDToolExecutionResult> steps)
        {
            int count = 0;
            foreach (ThreeDToolExecutionResult step in steps)
            {
                if (step.Status == ThreeDToolExecutionStatus.Faulted)
                {
                    count++;
                }
            }

            return count;
        }

        private static string ResolveName(IThreeDToolAdapter adapter)
        {
            try
            {
                return string.IsNullOrWhiteSpace(adapter.Name) ? "Unnamed 3D adapter" : adapter.Name.Trim();
            }
            catch (Exception)
            {
                return "Unnamed 3D adapter";
            }
        }

        private static Type ResolveResultType(IThreeDToolAdapter adapter)
        {
            try
            {
                return adapter.ResultType ?? typeof(object);
            }
            catch (Exception)
            {
                return typeof(object);
            }
        }

        private static bool ResolveCancellationCapability(IThreeDToolAdapter adapter)
        {
            try
            {
                return adapter.SupportsCancellation;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
