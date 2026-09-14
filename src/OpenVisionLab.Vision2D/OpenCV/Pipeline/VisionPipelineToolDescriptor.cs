using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace OpenVisionLab.Vision2D.Pipeline
{
    /// <summary>Identifies the invariant text representation used by a Pipeline parameter.</summary>
    public enum VisionPipelineParameterValueKind
    {
        Text,
        Boolean,
        WholeNumber,
        Number,
        Enum,
        Rectangle,
        RectangleList,
        Color
    }

    /// <summary>Describes one supported Pipeline parameter without inspecting a property type at runtime.</summary>
    public sealed class VisionPipelineParameterDescriptor
    {
        internal VisionPipelineParameterDescriptor(
            string name,
            VisionPipelineParameterValueKind valueKind,
            string valueType,
            string defaultValue,
            bool required)
        {
            Name = name;
            ValueKind = valueKind;
            ValueType = valueType;
            DefaultValue = defaultValue;
            Required = required;
        }

        public string Name { get; }
        public VisionPipelineParameterValueKind ValueKind { get; }
        public string ValueType { get; }
        public string DefaultValue { get; }
        public bool Required { get; }
    }

    /// <summary>Describes an external artifact that must be supplied to reconstruct a Tool.</summary>
    public sealed class VisionPipelineArtifactDescriptor
    {
        internal VisionPipelineArtifactDescriptor(string role, string format, int formatVersion, bool required)
        {
            Role = role;
            Format = format;
            FormatVersion = formatVersion;
            Required = required;
        }

        public string Role { get; }
        public string Format { get; }
        public int FormatVersion { get; }
        public bool Required { get; }
    }

    /// <summary>Describes a Tool ID, aliases, owning package, settings, and reconstruction artifacts.</summary>
    public sealed class VisionPipelineToolDescriptor
    {
        internal VisionPipelineToolDescriptor(
            string toolType,
            string toolTypeName,
            string propertyTypeName,
            string packageId,
            IEnumerable<string> aliases,
            IEnumerable<VisionPipelineParameterDescriptor> parameters,
            IEnumerable<VisionPipelineArtifactDescriptor> artifacts)
        {
            ToolType = toolType;
            ToolTypeName = toolTypeName;
            PropertyTypeName = propertyTypeName;
            PackageId = packageId;
            Aliases = ReadOnly(aliases);
            Parameters = ReadOnly(parameters);
            Artifacts = ReadOnly(artifacts);
        }

        public string ToolType { get; }
        public string ToolTypeName { get; }
        public string PropertyTypeName { get; }
        public string PackageId { get; }
        public IReadOnlyList<string> Aliases { get; }
        public IReadOnlyList<VisionPipelineParameterDescriptor> Parameters { get; }
        public IReadOnlyList<VisionPipelineArtifactDescriptor> Artifacts { get; }

        private static ReadOnlyCollection<T> ReadOnly<T>(IEnumerable<T> values)
        {
            return new ReadOnlyCollection<T>((values ?? Enumerable.Empty<T>()).ToArray());
        }
    }

    internal static class VisionPipelineDescriptorParameter
    {
        public static VisionPipelineParameterDescriptor String(string name, string defaultValue, bool required = false)
        {
            return Create(name, VisionPipelineParameterValueKind.Text, "System.String", defaultValue, required);
        }

        public static VisionPipelineParameterDescriptor Boolean(string name, bool defaultValue, bool required = false)
        {
            return Create(name, VisionPipelineParameterValueKind.Boolean, "System.Boolean", defaultValue ? "true" : "false", required);
        }

        public static VisionPipelineParameterDescriptor Integer(string name, int defaultValue, bool required = false)
        {
            return Create(name, VisionPipelineParameterValueKind.WholeNumber, "System.Int32", defaultValue.ToString(System.Globalization.CultureInfo.InvariantCulture), required);
        }

        public static VisionPipelineParameterDescriptor Number(string name, double defaultValue, bool required = false)
        {
            return Create(name, VisionPipelineParameterValueKind.Number, "System.Double", defaultValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture), required);
        }

        public static VisionPipelineParameterDescriptor Enum<TEnum>(string name, TEnum defaultValue, bool required = false)
            where TEnum : struct, System.Enum
        {
            return Create(name, VisionPipelineParameterValueKind.Enum, typeof(TEnum).FullName, defaultValue.ToString(), required);
        }

        public static VisionPipelineParameterDescriptor Rectangle(string name, string defaultValue, bool required = false)
        {
            return Create(name, VisionPipelineParameterValueKind.Rectangle, "OpenCvSharp.Rect", defaultValue, required);
        }

        public static VisionPipelineParameterDescriptor RectangleList(string name, string defaultValue = "", bool required = false)
        {
            return Create(name, VisionPipelineParameterValueKind.RectangleList, "System.Collections.Generic.List<OpenCvSharp.Rect>", defaultValue, required);
        }

        public static VisionPipelineParameterDescriptor Color(string name, string defaultValue, bool required = false)
        {
            return Create(name, VisionPipelineParameterValueKind.Color, "System.Drawing.Color", defaultValue, required);
        }

        private static VisionPipelineParameterDescriptor Create(
            string name,
            VisionPipelineParameterValueKind valueKind,
            string valueType,
            string defaultValue,
            bool required)
        {
            return new VisionPipelineParameterDescriptor(name, valueKind, valueType, defaultValue ?? string.Empty, required);
        }
    }
}
