using OpenVisionLab.Vision2D.Tool;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenVisionLab.Vision2D.Pipeline
{
    public sealed class VisionPipelineAcceptanceResult
    {
        public bool Passed { get; set; } = true;
        public string Message { get; set; } = string.Empty;
    }

    public static class VisionPipelineAcceptanceEvaluator
    {
        public static VisionPipelineAcceptanceResult Evaluate(VisionPipelineStep step, VisionToolResult toolResult)
        {
            if (step == null || !step.UseAcceptance)
            {
                return new VisionPipelineAcceptanceResult();
            }

            List<string> failures = new List<string>();

            bool actualSuccess = toolResult != null && toolResult.Success;
            if (actualSuccess != step.ExpectedSuccess)
            {
                failures.Add($"ExpectedSuccess={step.ExpectedSuccess}, ActualSuccess={actualSuccess}");
            }

            if (!IsFinite(step.MaxElapsedMilliseconds))
            {
                failures.Add("MaxElapsedMilliseconds must be finite.");
            }
            else if (step.MaxElapsedMilliseconds > 0 && toolResult != null
                && toolResult.Elapsed.TotalMilliseconds > step.MaxElapsedMilliseconds)
            {
                failures.Add($"Elapsed {toolResult.Elapsed.TotalMilliseconds:0.0} ms > {step.MaxElapsedMilliseconds:0.0} ms");
            }

            if (!string.IsNullOrWhiteSpace(step.RequiredMessageText))
            {
                string message = toolResult?.Message ?? string.Empty;
                if (message.IndexOf(step.RequiredMessageText, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    failures.Add($"Message does not contain '{step.RequiredMessageText}'");
                }
            }

            if (step.UseAcceptanceMetricMinimum || step.UseAcceptanceMetricMaximum)
            {
                if (string.IsNullOrWhiteSpace(step.AcceptanceMetricName))
                {
                    failures.Add("Acceptance metric name is required when metric limits are enabled.");
                }
                else if (toolResult == null || !toolResult.Metrics.TryGetValue(step.AcceptanceMetricName, out double metricValue))
                {
                    failures.Add($"Metric '{step.AcceptanceMetricName}' was not produced");
                }
                else
                {
                    if (!IsFinite(metricValue))
                    {
                        failures.Add($"Metric '{step.AcceptanceMetricName}' must be finite.");
                    }

                    if (step.UseAcceptanceMetricMinimum && !IsFinite(step.AcceptanceMetricMinimum))
                    {
                        failures.Add("Acceptance metric minimum must be finite.");
                    }
                    else if (step.UseAcceptanceMetricMinimum && metricValue < step.AcceptanceMetricMinimum)
                    {
                        failures.Add($"{step.AcceptanceMetricName} {metricValue:0.###} < {step.AcceptanceMetricMinimum:0.###}");
                    }

                    if (step.UseAcceptanceMetricMaximum && !IsFinite(step.AcceptanceMetricMaximum))
                    {
                        failures.Add("Acceptance metric maximum must be finite.");
                    }
                    else if (step.UseAcceptanceMetricMaximum && metricValue > step.AcceptanceMetricMaximum)
                    {
                        failures.Add($"{step.AcceptanceMetricName} {metricValue:0.###} > {step.AcceptanceMetricMaximum:0.###}");
                    }
                }
            }

            if (failures.Count == 0)
            {
                return new VisionPipelineAcceptanceResult
                {
                    Passed = true,
                    Message = "Acceptance passed."
                };
            }

            return new VisionPipelineAcceptanceResult
            {
                Passed = false,
                Message = string.Join("; ", failures.Distinct())
            };
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
