using System;
using System.Collections.Generic;

namespace OpenVisionLab.Inspection
{
    public enum ThreeDToolExecutionStatus
    {
        Completed = 0,
        Canceled = 1,
        Faulted = 2
    }

    /// <summary>
    /// Describes whether an adapter call completed, was canceled, or faulted. It
    /// does not interpret an algorithm result as an inspection pass or failure.
    /// </summary>
    public abstract class ThreeDToolExecutionResult
    {
        internal ThreeDToolExecutionResult(
            string toolName,
            Type resultType,
            bool supportsCancellation,
            ThreeDToolExecutionStatus status,
            TimeSpan elapsed,
            string message,
            object untypedResult,
            Exception exception)
        {
            ToolName = toolName ?? string.Empty;
            ResultType = resultType ?? typeof(object);
            SupportsCancellation = supportsCancellation;
            Status = status;
            Elapsed = elapsed;
            Message = message ?? string.Empty;
            UntypedResult = untypedResult;
            Exception = exception;
        }

        public string ToolName { get; }

        public Type ResultType { get; }

        public bool SupportsCancellation { get; }

        public ThreeDToolExecutionStatus Status { get; }

        public TimeSpan Elapsed { get; }

        public string Message { get; }

        public object UntypedResult { get; }

        public Exception Exception { get; }

        internal static ThreeDToolExecutionResult Faulted(
            string toolName,
            Type resultType,
            bool supportsCancellation,
            TimeSpan elapsed,
            string message,
            Exception exception)
        {
            return new UntypedThreeDToolExecutionResult(
                toolName,
                resultType,
                supportsCancellation,
                ThreeDToolExecutionStatus.Faulted,
                elapsed,
                message,
                null,
                exception);
        }

        internal static ThreeDToolExecutionResult Canceled(
            string toolName,
            Type resultType,
            bool supportsCancellation,
            TimeSpan elapsed,
            OperationCanceledException exception)
        {
            return new UntypedThreeDToolExecutionResult(
                toolName,
                resultType,
                supportsCancellation,
                ThreeDToolExecutionStatus.Canceled,
                elapsed,
                "3D tool adapter execution was canceled.",
                null,
                exception);
        }

        private sealed class UntypedThreeDToolExecutionResult : ThreeDToolExecutionResult
        {
            internal UntypedThreeDToolExecutionResult(
                string toolName,
                Type resultType,
                bool supportsCancellation,
                ThreeDToolExecutionStatus status,
                TimeSpan elapsed,
                string message,
                object untypedResult,
                Exception exception)
                : base(
                    toolName,
                    resultType,
                    supportsCancellation,
                    status,
                    elapsed,
                    message,
                    untypedResult,
                    exception)
            {
            }
        }
    }

    /// <summary>
    /// Retains the exact result type returned by one 3D Tool adapter.
    /// </summary>
    public sealed class ThreeDToolExecutionResult<TResult> : ThreeDToolExecutionResult
    {
        internal ThreeDToolExecutionResult(
            string toolName,
            bool supportsCancellation,
            ThreeDToolExecutionStatus status,
            TimeSpan elapsed,
            string message,
            TResult result,
            Exception exception)
            : base(
                toolName,
                typeof(TResult),
                supportsCancellation,
                status,
                elapsed,
                message,
                result,
                exception)
        {
            Result = result;
        }

        public TResult Result { get; }
    }

    /// <summary>
    /// Ordered execution evidence. The caller owns every input captured by an
    /// adapter and every returned result object.
    /// </summary>
    public sealed class ThreeDToolExecutionReport
    {
        internal ThreeDToolExecutionReport(
            ThreeDToolExecutionStatus status,
            TimeSpan elapsed,
            string message,
            List<ThreeDToolExecutionResult> steps)
        {
            Status = status;
            Elapsed = elapsed;
            Message = message ?? string.Empty;
            Steps = steps.AsReadOnly();
        }

        public ThreeDToolExecutionStatus Status { get; }

        public TimeSpan Elapsed { get; }

        public string Message { get; }

        public IReadOnlyList<ThreeDToolExecutionResult> Steps { get; }

        public int CompletedStepCount => Count(ThreeDToolExecutionStatus.Completed);

        public int CanceledStepCount => Count(ThreeDToolExecutionStatus.Canceled);

        public int FaultedStepCount => Count(ThreeDToolExecutionStatus.Faulted);

        private int Count(ThreeDToolExecutionStatus status)
        {
            int count = 0;
            foreach (ThreeDToolExecutionResult step in Steps)
            {
                if (step.Status == status)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
