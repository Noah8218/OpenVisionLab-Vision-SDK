using System;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class SmokeAssert
    {
        internal static TException RequireThrows<TException>(Action action) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException exception)
            {
                return exception;
            }

            throw new InvalidOperationException("Expected " + typeof(TException).Name + " was not thrown.");
        }

        internal static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        internal static void RequireApproximately(double actual, double expected, double tolerance, string message)
        {
            if (!double.IsFinite(actual) || !double.IsFinite(expected) || !double.IsFinite(tolerance)
                || tolerance < 0.0 || Math.Abs(actual - expected) > tolerance)
            {
                throw new InvalidOperationException(message + " Expected=" + expected + ", Actual=" + actual + ".");
            }
        }
    }
}
