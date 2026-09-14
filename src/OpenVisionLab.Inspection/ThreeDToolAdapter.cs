using System;
using System.Diagnostics;
using System.Threading;

namespace OpenVisionLab.Inspection
{
    public interface IThreeDToolAdapter
    {
        string Name { get; }

        Type ResultType { get; }

        bool SupportsCancellation { get; }

        ThreeDToolExecutionResult Execute(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Adapts one host-authored typed 3D Tool invocation without changing its input
    /// or result contract.
    /// </summary>
    public sealed class ThreeDToolAdapter<TResult> : IThreeDToolAdapter
    {
        private readonly Func<TResult> execute;
        private readonly Func<CancellationToken, TResult> cancellableExecute;

        public ThreeDToolAdapter(string name, Func<TResult> execute)
        {
            Name = ValidateName(name);
            this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
        }

        public ThreeDToolAdapter(string name, Func<CancellationToken, TResult> execute)
        {
            Name = ValidateName(name);
            cancellableExecute = execute ?? throw new ArgumentNullException(nameof(execute));
        }

        public string Name { get; }

        public Type ResultType => typeof(TResult);

        public bool SupportsCancellation => cancellableExecute != null;

        public ThreeDToolExecutionResult<TResult> Execute(CancellationToken cancellationToken = default(CancellationToken))
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                TResult result = SupportsCancellation
                    ? cancellableExecute(cancellationToken)
                    : execute();
                stopwatch.Stop();

                if (ReferenceEquals(result, null))
                {
                    InvalidOperationException exception = new InvalidOperationException("The 3D tool adapter returned no result.");
                    return new ThreeDToolExecutionResult<TResult>(
                        Name,
                        SupportsCancellation,
                        ThreeDToolExecutionStatus.Faulted,
                        stopwatch.Elapsed,
                        exception.Message,
                        default(TResult),
                        exception);
                }

                return new ThreeDToolExecutionResult<TResult>(
                    Name,
                    SupportsCancellation,
                    ThreeDToolExecutionStatus.Completed,
                    stopwatch.Elapsed,
                    "3D tool adapter execution completed.",
                    result,
                    null);
            }
            catch (OperationCanceledException exception)
            {
                stopwatch.Stop();
                return new ThreeDToolExecutionResult<TResult>(
                    Name,
                    SupportsCancellation,
                    ThreeDToolExecutionStatus.Canceled,
                    stopwatch.Elapsed,
                    "3D tool adapter execution was canceled.",
                    default(TResult),
                    exception);
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                return new ThreeDToolExecutionResult<TResult>(
                    Name,
                    SupportsCancellation,
                    ThreeDToolExecutionStatus.Faulted,
                    stopwatch.Elapsed,
                    "3D tool adapter execution faulted.",
                    default(TResult),
                    exception);
            }
        }

        ThreeDToolExecutionResult IThreeDToolAdapter.Execute(CancellationToken cancellationToken)
        {
            return Execute(cancellationToken);
        }

        private static string ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A 3D tool adapter name is required.", nameof(name));
            }

            return name.Trim();
        }
    }
}
