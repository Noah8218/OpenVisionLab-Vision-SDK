using OpenCvSharp;
using System;
using System.Collections.Generic;

namespace OpenVisionLab.Vision2D.Pipeline
{
    public class VisionPipelineContext : IDisposable
    {
        private readonly Dictionary<string, Mat> layers = new Dictionary<string, Mat>(StringComparer.OrdinalIgnoreCase);

        public void SetLayer(string name, Mat image)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Layer name is required.", nameof(name));
            }

            if (layers.TryGetValue(name, out Mat existing))
            {
                existing?.Dispose();
            }

            layers[name] = image?.Clone();
        }

        public Mat GetLayer(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            return layers.TryGetValue(name, out Mat image) ? image?.Clone() : null;
        }

        public void Dispose()
        {
            foreach (Mat image in layers.Values)
            {
                image?.Dispose();
            }

            layers.Clear();
            GC.SuppressFinalize(this);
        }
    }
}
