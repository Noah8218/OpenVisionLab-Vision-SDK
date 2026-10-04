using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace OpenVisionLab.Vision2D.Tool
{
    /// <summary>One immutable learned edge with its original pixel coordinate and signed unit gradient.</summary>
    public sealed class EdgeTemplateModelPoint
    {
        internal EdgeTemplateModelPoint(int x, int y, double unitDx, double unitDy, double inverseMagnitude)
        {
            X = x;
            Y = y;
            UnitDx = unitDx;
            UnitDy = unitDy;
            InverseMagnitude = inverseMagnitude;
        }

        public int X { get; }
        public int Y { get; }
        public double UnitDx { get; }
        public double UnitDy { get; }
        public double InverseMagnitude { get; }
    }

    /// <summary>Immutable base-scale model snapshot. Source identity is caller-declared. Owns no native image or file.</summary>
    public sealed class EdgeTemplateModelArtifact
    {
        internal EdgeTemplateModelArtifact(string sourceId, string learningParameters, int width, int height, int sourceChannels,
            double centerX, double centerY, int rawPointCount, EdgeTemplateModelPoint[] points,
            byte[] sourcePixels, byte[] preparedPixels, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EdgeTemplateModelArtifactCodec.ValidateText(sourceId, nameof(sourceId));
            EdgeTemplateModelArtifactCodec.ValidateText(learningParameters, nameof(learningParameters));
            long pixelCount = (long)width * height;
            if (width < 1 || height < 1 || pixelCount > EdgeTemplateModelArtifactCodec.MaximumPixelCount
                || (sourceChannels != 1 && sourceChannels != 3 && sourceChannels != 4))
                throw new ArgumentException("Edge model dimensions or channels are unsupported.");
            if (points == null || points.Length > EdgeTemplateModelArtifactCodec.MaximumPointCount
                || rawPointCount < points.Length || rawPointCount > pixelCount || (points.Length == 0 && rawPointCount != 0)
                || sourcePixels == null || sourcePixels.LongLength != pixelCount * sourceChannels
                || preparedPixels == null || preparedPixels.LongLength != pixelCount
                || !Finite(centerX) || !Finite(centerY))
                throw new ArgumentException("Edge model record counts, raster lengths or center are invalid.");
            long totalLength = 88L + EdgeTemplateModelArtifactCodec.TextByteCount(EdgeTemplateModelArtifactCodec.LearningSemantics)
                + EdgeTemplateModelArtifactCodec.TextByteCount(sourceId) + EdgeTemplateModelArtifactCodec.TextByteCount(learningParameters)
                + sourcePixels.Length + preparedPixels.Length + points.Length * 32L;
            if (totalLength > EdgeTemplateModelArtifactCodec.MaximumArtifactByteCount)
                throw new ArgumentException("Edge model exceeds the artifact byte limit.");
            HashSet<long> coordinates = new HashSet<long>();
            long sumX = 0, sumY = 0;
            foreach (EdgeTemplateModelPoint point in points)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (point == null || point.X < 0 || point.X >= width || point.Y < 0 || point.Y >= height
                    || !coordinates.Add(((long)point.Y << 32) | (uint)point.X)
                    || !Finite(point.UnitDx) || !Finite(point.UnitDy) || !Finite(point.InverseMagnitude) || point.InverseMagnitude <= 0D
                    || Math.Abs(point.UnitDx * point.UnitDx + point.UnitDy * point.UnitDy - 1D) > 0.00001D)
                    throw new ArgumentException("Edge model points require unique in-frame pixels, unit gradients and positive finite inverse magnitudes.");
                sumX += point.X;
                sumY += point.Y;
            }
            if (centerX != (points.Length == 0 ? 0D : sumX / (double)points.Length)
                || centerY != (points.Length == 0 ? 0D : sumY / (double)points.Length))
                throw new ArgumentException("Edge model center does not match its learned points.");
            SourceId = sourceId;
            LearningParameters = learningParameters;
            Width = width;
            Height = height;
            SourceChannels = sourceChannels;
            CenterX = centerX;
            CenterY = centerY;
            RawPointCount = rawPointCount;
            Points = Array.AsReadOnly((EdgeTemplateModelPoint[])points.Clone());
            SourceBytes = (byte[])sourcePixels.Clone();
            PreparedBytes = (byte[])preparedPixels.Clone();
            SourcePixels = Array.AsReadOnly(SourceBytes);
            PreparedPixels = Array.AsReadOnly(PreparedBytes);
            cancellationToken.ThrowIfCancellationRequested();
        }

        public string SourceId { get; }
        public string LearningParameters { get; }
        public string LearningSemantics { get; } = EdgeTemplateModelArtifactCodec.LearningSemantics;
        public int Width { get; }
        public int Height { get; }
        public int SourceChannels { get; }
        public double CenterX { get; }
        public double CenterY { get; }
        public int RawPointCount { get; }
        public IReadOnlyList<EdgeTemplateModelPoint> Points { get; }
        public IReadOnlyList<byte> SourcePixels { get; }
        public IReadOnlyList<byte> PreparedPixels { get; }
        internal byte[] SourceBytes { get; }
        internal byte[] PreparedBytes { get; }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    /// <summary>Bounded v1 memory codec with SHA-256 corruption detection. Does not authenticate the producer or execute matching.</summary>
    public static class EdgeTemplateModelArtifactCodec
    {
        public const string ArtifactFormat = "edge-template-model";
        public const string LearningSemantics = "gray-canny-sobel-sampled-edges-v1";
        public const int SchemaVersion = 1;
        public const int MaximumPixelCount = 16 * 1024 * 1024;
        public const int MaximumPointCount = 1000000;
        public const int MaximumTextByteCount = 4096;
        public const int MaximumArtifactByteCount = 64 * 1024 * 1024;
        private const int Magic = 0x4D45564F; // OVEM, little endian.
        private const int DigestLength = 32;
        private static readonly UTF8Encoding TextEncoding = new UTF8Encoding(false, true);

        public static byte[] Encode(EdgeTemplateModelArtifact artifact, CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (artifact == null) throw new ArgumentNullException(nameof(artifact));
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, TextEncoding, true))
            {
                writer.Write(Magic);
                writer.Write(SchemaVersion);
                WriteText(writer, LearningSemantics);
                WriteText(writer, artifact.SourceId);
                WriteText(writer, artifact.LearningParameters);
                writer.Write(artifact.Width);
                writer.Write(artifact.Height);
                writer.Write(artifact.SourceChannels);
                writer.Write(artifact.CenterX);
                writer.Write(artifact.CenterY);
                writer.Write(artifact.RawPointCount);
                writer.Write(artifact.Points.Count);
                writer.Write(artifact.SourceBytes);
                writer.Write(artifact.PreparedBytes);
                foreach (EdgeTemplateModelPoint point in artifact.Points)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    writer.Write(point.X);
                    writer.Write(point.Y);
                    writer.Write(point.UnitDx);
                    writer.Write(point.UnitDy);
                    writer.Write(point.InverseMagnitude);
                }
                writer.Flush();
                cancellationToken.ThrowIfCancellationRequested();
                using (SHA256 hash = SHA256.Create())
                    writer.Write(hash.ComputeHash(stream.GetBuffer(), 0, (int)stream.Length));
                cancellationToken.ThrowIfCancellationRequested();
                return stream.ToArray();
            }
        }

        public static EdgeTemplateModelArtifact Decode(byte[] bytes, CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length < 88 || bytes.Length > MaximumArtifactByteCount)
                throw new FormatException("Edge model artifact length is invalid.");
            byte[] snapshot = (byte[])bytes.Clone();
            int payloadLength = snapshot.Length - DigestLength;
            using (SHA256 hash = SHA256.Create())
            {
                byte[] digest = hash.ComputeHash(snapshot, 0, payloadLength);
                cancellationToken.ThrowIfCancellationRequested();
                int difference = 0;
                for (int i = 0; i < DigestLength; i++) difference |= digest[i] ^ snapshot[payloadLength + i];
                if (difference != 0) throw new FormatException("Edge model artifact SHA-256 mismatch.");
            }
            try
            {
                using (MemoryStream stream = new MemoryStream(snapshot, 0, payloadLength, false))
                using (BinaryReader reader = new BinaryReader(stream, TextEncoding, true))
                {
                    if (reader.ReadInt32() != Magic || reader.ReadInt32() != SchemaVersion)
                        throw new FormatException("Edge model artifact magic or schema is unsupported.");
                    if (ReadText(reader) != LearningSemantics) throw new FormatException("Edge learning semantics are unsupported.");
                    string sourceId = ReadText(reader), parameters = ReadText(reader);
                    int width = reader.ReadInt32(), height = reader.ReadInt32(), channels = reader.ReadInt32();
                    double centerX = reader.ReadDouble(), centerY = reader.ReadDouble();
                    int rawCount = reader.ReadInt32(), count = reader.ReadInt32();
                    long pixels = (long)width * height;
                    if (width < 1 || height < 1 || pixels > MaximumPixelCount || (channels != 1 && channels != 3 && channels != 4)
                        || count < 0 || count > MaximumPointCount || rawCount < count || rawCount > pixels
                        || pixels * (channels + 1L) + count * 32L != stream.Length - stream.Position)
                        throw new FormatException("Edge model dimensions, record count or exact payload length is invalid.");
                    cancellationToken.ThrowIfCancellationRequested();
                    byte[] source = reader.ReadBytes((int)(pixels * channels));
                    byte[] prepared = reader.ReadBytes((int)pixels);
                    EdgeTemplateModelPoint[] points = new EdgeTemplateModelPoint[count];
                    for (int i = 0; i < count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        points[i] = new EdgeTemplateModelPoint(reader.ReadInt32(), reader.ReadInt32(),
                            reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble());
                    }
                    return new EdgeTemplateModelArtifact(sourceId, parameters, width, height, channels,
                        centerX, centerY, rawCount, points, source, prepared, cancellationToken);
                }
            }
            catch (EndOfStreamException exception) { throw new FormatException("Edge model artifact is truncated.", exception); }
            catch (ArgumentException exception) { throw new FormatException("Edge model artifact records are invalid.", exception); }
        }

        internal static int TextByteCount(string value) => TextEncoding.GetByteCount(value);
        internal static void ValidateText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) || TextByteCount(value) > MaximumTextByteCount)
                throw new ArgumentException("Edge model text must be nonblank strict UTF-8 within the byte limit.", parameterName);
        }
        private static void WriteText(BinaryWriter writer, string value)
        {
            byte[] bytes = TextEncoding.GetBytes(value);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }
        private static string ReadText(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count < 1 || count > MaximumTextByteCount || count > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new FormatException("Edge model text length is invalid.");
            return TextEncoding.GetString(reader.ReadBytes(count));
        }
    }
}
