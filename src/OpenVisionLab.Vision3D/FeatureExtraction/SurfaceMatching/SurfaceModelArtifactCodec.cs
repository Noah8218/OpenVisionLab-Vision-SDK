using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    /// <summary>Immutable learned-model snapshot. Source identity, unit and frame are caller declarations, not calibration approval.</summary>
    public sealed class SurfaceModelArtifact
    {
        public SurfaceModelArtifact(string sourceId, string unit, string coordinateFrame,
            IReadOnlyList<PreparedSurfaceModelSample> samples, IReadOnlyList<ExtractedModelKeyPoint> keyPoints,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            SurfaceModelArtifactCodec.ValidateMetadata(sourceId, nameof(sourceId));
            SurfaceModelArtifactCodec.ValidateMetadata(unit, nameof(unit));
            SurfaceModelArtifactCodec.ValidateMetadata(coordinateFrame, nameof(coordinateFrame));
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (keyPoints == null) throw new ArgumentNullException(nameof(keyPoints));
            if (samples.Count < 1 || samples.Count > SurfaceModelArtifactCodec.MaximumSampleCount
                || keyPoints.Count > samples.Count || keyPoints.Count > SurfaceModelArtifactCodec.MaximumSampleCount)
                throw new ArgumentException("Learned-model record counts exceed the supported limits.", nameof(samples));
            PreparedSurfaceModelSample[] sampleSnapshot = new PreparedSurfaceModelSample[samples.Count];
            Dictionary<int, PreparedSurfaceModelSample> sourceOrders = new Dictionary<int, PreparedSurfaceModelSample>(samples.Count);
            for (int i = 0; i < sampleSnapshot.Length; i++)
            {
                if ((i & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                PreparedSurfaceModelSample sample = samples[i];
                if (sample == null || sample.Order < 0 || sourceOrders.ContainsKey(sample.Order) || sample.SourceTriangleIndex < 0
                    || !IsPoint(sample.Position) || !IsUnitNormal(sample.Normal))
                    throw new ArgumentException("Model samples require unique nonnegative IDs, finite positions and unit normals.", nameof(samples));
                sampleSnapshot[i] = sample;
                sourceOrders.Add(sample.Order, sample);
            }
            ExtractedModelKeyPoint[] keySnapshot = new ExtractedModelKeyPoint[keyPoints.Count];
            HashSet<int> keyOrders = new HashSet<int>();
            for (int i = 0; i < keySnapshot.Length; i++)
            {
                if ((i & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                ExtractedModelKeyPoint keyPoint = keyPoints[i];
                if (keyPoint == null || keyPoint.Order != i || !keyOrders.Add(keyPoint.SourceSampleOrder)
                    || !sourceOrders.TryGetValue(keyPoint.SourceSampleOrder, out PreparedSurfaceModelSample source)
                    || !SamePoint(keyPoint.Position, source.Position) || !SamePoint(keyPoint.Normal, source.Normal)
                    || !SurfaceMatchingContractValidation.IsFinite(keyPoint.NearestSelectedDistance) || keyPoint.NearestSelectedDistance < 0D)
                    throw new ArgumentException("Key points must preserve sequential order, unique source correspondence and finite nonnegative distances.", nameof(keyPoints));
                keySnapshot[i] = keyPoint;
            }
            cancellationToken.ThrowIfCancellationRequested();
            PreparationSemantics = DeterministicSurfaceModelPreparationTool.Semantics;
            KeyPointSemantics = DeterministicModelKeyPointExtractionTool.Semantics;
            SourceId = sourceId;
            Unit = unit;
            CoordinateFrame = coordinateFrame;
            Samples = Array.AsReadOnly(sampleSnapshot);
            KeyPoints = Array.AsReadOnly(keySnapshot);
        }

        public string SourceId { get; }
        public string Unit { get; }
        public string CoordinateFrame { get; }
        public string PreparationSemantics { get; }
        public string KeyPointSemantics { get; }
        public IReadOnlyList<PreparedSurfaceModelSample> Samples { get; }
        public IReadOnlyList<ExtractedModelKeyPoint> KeyPoints { get; }

        private static bool IsPoint(ThreeDPoint point) => point != null && point.IsFinite;
        private static bool SamePoint(ThreeDPoint first, ThreeDPoint second) => first != null && second != null
            && first.X == second.X && first.Y == second.Y && first.Z == second.Z;
        private static bool IsUnitNormal(ThreeDPoint point) => IsPoint(point)
            && Math.Abs(Math.Sqrt(point.X * point.X + point.Y * point.Y + point.Z * point.Z) - 1D) <= 0.000001D;
    }

    /// <summary>Bounded format v1 byte codec with SHA-256 corruption detection. Owns no files and never executes matching.</summary>
    public static class SurfaceModelArtifactCodec
    {
        public const int SchemaVersion = 1;
        public const int MaximumSampleCount = 1000000;
        public const int MaximumMetadataByteCount = 4096;
        public const int MaximumArtifactByteCount = 64 * 1024 * 1024;
        private const int Magic = 0x4D53564F; // OVSM, little endian.
        private const int DigestLength = 32;
        private const int SampleRecordLength = 56;
        private const int KeyPointRecordLength = 64;
        private static readonly UTF8Encoding TextEncoding = new UTF8Encoding(false, true);

        public static byte[] Encode(SurfaceModelArtifact artifact, CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (artifact == null) throw new ArgumentNullException(nameof(artifact));
            string[] metadata = { artifact.SourceId, artifact.Unit, artifact.CoordinateFrame, artifact.PreparationSemantics, artifact.KeyPointSemantics };
            long length = 16L + metadata.Length * 4L + artifact.Samples.Count * (long)SampleRecordLength
                + artifact.KeyPoints.Count * (long)KeyPointRecordLength + DigestLength;
            foreach (string value in metadata) length += TextEncoding.GetByteCount(value);
            if (length > MaximumArtifactByteCount) throw new ArgumentException("Learned-model artifact exceeds the byte limit.", nameof(artifact));
            using (MemoryStream stream = new MemoryStream((int)length))
            using (BinaryWriter writer = new BinaryWriter(stream, TextEncoding, true))
            {
                writer.Write(Magic);
                writer.Write(SchemaVersion);
                foreach (string value in metadata) WriteText(writer, value);
                writer.Write(artifact.Samples.Count);
                writer.Write(artifact.KeyPoints.Count);
                for (int i = 0; i < artifact.Samples.Count; i++)
                {
                    if ((i & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                    PreparedSurfaceModelSample sample = artifact.Samples[i];
                    writer.Write(sample.Order);
                    writer.Write(sample.SourceTriangleIndex);
                    WritePoint(writer, sample.Position);
                    WritePoint(writer, sample.Normal);
                }
                for (int i = 0; i < artifact.KeyPoints.Count; i++)
                {
                    if ((i & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                    ExtractedModelKeyPoint keyPoint = artifact.KeyPoints[i];
                    writer.Write(keyPoint.Order);
                    writer.Write(keyPoint.SourceSampleOrder);
                    WritePoint(writer, keyPoint.Position);
                    WritePoint(writer, keyPoint.Normal);
                    writer.Write(keyPoint.NearestSelectedDistance);
                }
                cancellationToken.ThrowIfCancellationRequested();
                using (SHA256 hash = SHA256.Create()) writer.Write(hash.ComputeHash(stream.GetBuffer(), 0, (int)stream.Length));
                cancellationToken.ThrowIfCancellationRequested();
                byte[] bytes = stream.ToArray();
                cancellationToken.ThrowIfCancellationRequested();
                return bytes;
            }
        }

        public static SurfaceModelArtifact Decode(byte[] bytes, CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length < 68 || bytes.Length > MaximumArtifactByteCount) throw new FormatException("Learned-model artifact length is invalid.");
            byte[] snapshot = (byte[])bytes.Clone();
            cancellationToken.ThrowIfCancellationRequested();
            int payloadLength = snapshot.Length - DigestLength;
            using (SHA256 hash = SHA256.Create())
            {
                byte[] actual = hash.ComputeHash(snapshot, 0, payloadLength);
                cancellationToken.ThrowIfCancellationRequested();
                int difference = 0;
                for (int i = 0; i < DigestLength; i++) difference |= actual[i] ^ snapshot[payloadLength + i];
                if (difference != 0) throw new FormatException("Learned-model artifact SHA-256 mismatch.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using (MemoryStream stream = new MemoryStream(snapshot, 0, payloadLength, false))
                using (BinaryReader reader = new BinaryReader(stream, TextEncoding, true))
                {
                    if (reader.ReadInt32() != Magic) throw new FormatException("Learned-model artifact magic is invalid.");
                    if (reader.ReadInt32() != SchemaVersion) throw new FormatException("Learned-model artifact schema is unsupported.");
                    string sourceId = ReadText(reader);
                    string unit = ReadText(reader);
                    string frame = ReadText(reader);
                    if (ReadText(reader) != DeterministicSurfaceModelPreparationTool.Semantics
                        || ReadText(reader) != DeterministicModelKeyPointExtractionTool.Semantics)
                        throw new FormatException("Learned-model preparation or key-point semantics are unsupported.");
                    int sampleCount = reader.ReadInt32();
                    int keyPointCount = reader.ReadInt32();
                    if (sampleCount < 1 || sampleCount > MaximumSampleCount || keyPointCount < 0 || keyPointCount > sampleCount
                        || sampleCount * (long)SampleRecordLength + keyPointCount * (long)KeyPointRecordLength != stream.Length - stream.Position)
                        throw new FormatException("Learned-model record count or exact payload length is invalid.");
                    PreparedSurfaceModelSample[] samples = new PreparedSurfaceModelSample[sampleCount];
                    for (int i = 0; i < sampleCount; i++)
                    {
                        if ((i & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                        samples[i] = new PreparedSurfaceModelSample(reader.ReadInt32(), reader.ReadInt32(), ReadPoint(reader), ReadPoint(reader));
                    }
                    ExtractedModelKeyPoint[] keyPoints = new ExtractedModelKeyPoint[keyPointCount];
                    for (int i = 0; i < keyPointCount; i++)
                    {
                        if ((i & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                        keyPoints[i] = new ExtractedModelKeyPoint(reader.ReadInt32(), reader.ReadInt32(), ReadPoint(reader), ReadPoint(reader), reader.ReadDouble());
                    }
                    return new SurfaceModelArtifact(sourceId, unit, frame, samples, keyPoints, cancellationToken);
                }
            }
            catch (EndOfStreamException exception) { throw new FormatException("Learned-model artifact is truncated.", exception); }
            catch (ArgumentException exception) { throw new FormatException("Learned-model artifact records are invalid.", exception); }
        }

        internal static void ValidateMetadata(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) || TextEncoding.GetByteCount(value) > MaximumMetadataByteCount)
                throw new ArgumentException("Learned-model metadata must be nonblank strict UTF-8 within the byte limit.", parameterName);
        }

        private static void WriteText(BinaryWriter writer, string value)
        {
            byte[] bytes = TextEncoding.GetBytes(value);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static string ReadText(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length < 1 || length > MaximumMetadataByteCount || length > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new FormatException("Learned-model metadata length is invalid.");
            return TextEncoding.GetString(reader.ReadBytes(length));
        }

        private static void WritePoint(BinaryWriter writer, ThreeDPoint point)
        {
            writer.Write(point.X);
            writer.Write(point.Y);
            writer.Write(point.Z);
        }

        private static ThreeDPoint ReadPoint(BinaryReader reader) => new ThreeDPoint(reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble());
    }
}
