using OpenVisionLab.Vision2D.Tool;
using OpenVisionLab.Vision3D.Geometry;
using OpenVisionLab.Vision3D.Inspection;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace OpenVisionLab.Inspection
{
    public enum CombinedInspectionDomain
    {
        TwoD,
        ThreeD
    }

    /// <summary>
    /// The caller retains ownership of Image and HeightMap. The combined runner never disposes them.
    /// </summary>
    public sealed class CombinedInspectionInput
    {
        public Mat Image { get; set; }

        public HeightMap3D HeightMap { get; set; }
    }

    public sealed class CombinedInspectionStepResult
    {
        public CombinedInspectionDomain Domain { get; internal set; }

        public string ToolName { get; internal set; } = string.Empty;

        public VisionToolResult VisionResult { get; internal set; }

        public ThreeDInspectionResult ThreeDResult { get; internal set; }

        public bool Success
        {
            get
            {
                return Domain == CombinedInspectionDomain.TwoD
                    ? VisionResult != null && VisionResult.Success
                    : ThreeDResult != null && ThreeDResult.Success;
            }
        }

        public string Message
        {
            get
            {
                return Domain == CombinedInspectionDomain.TwoD
                    ? VisionResult == null ? string.Empty : VisionResult.Message
                    : ThreeDResult == null ? string.Empty : ThreeDResult.Message;
            }
        }
    }

    /// <summary>
    /// Owns the 2D VisionResult instances collected by the combined runner.
    /// </summary>
    public sealed class CombinedInspectionRunResult : IDisposable
    {
        private readonly HashSet<VisionToolResult> ownedResults = new HashSet<VisionToolResult>(new ResultReferenceComparer());

        internal void AddStep(CombinedInspectionStepResult step)
        {
            if (step.VisionResult != null) ownedResults.Add(step.VisionResult);
            Steps.Add(step);
        }

        public bool Success { get; internal set; }

        public string Message { get; internal set; } = string.Empty;

        public TimeSpan Elapsed { get; internal set; }

        public List<CombinedInspectionStepResult> Steps { get; } = new List<CombinedInspectionStepResult>();

        public int FailedStepCount
        {
            get
            {
                int failedStepCount = 0;
                foreach (CombinedInspectionStepResult step in Steps)
                {
                    if (!step.Success)
                    {
                        failedStepCount++;
                    }
                }

                return failedStepCount;
            }
        }

        public void Dispose()
        {
            foreach (VisionToolResult result in ownedResults) result.Dispose();
            ownedResults.Clear();
        }

        private sealed class ResultReferenceComparer : IEqualityComparer<VisionToolResult>
        {
            public bool Equals(VisionToolResult first, VisionToolResult second) => ReferenceEquals(first, second);
            public int GetHashCode(VisionToolResult result) => RuntimeHelpers.GetHashCode(result);
        }
    }
}
