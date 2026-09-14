using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;

namespace OpenVisionLab.Vision2D.Pipeline
{
    internal sealed class VisionPipelineParameterReader
    {
        private static readonly char[] RectangleListSeparator = { ';' };
        private readonly IDictionary<string, string> parameters;

        public VisionPipelineParameterReader(
            IDictionary<string, string> parameters,
            VisionPipelineToolDescriptor descriptor)
        {
            this.parameters = parameters;
            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            HashSet<string> allowed = new HashSet<string>(
                descriptor.Parameters.Select(parameter => parameter.Name),
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (parameters != null)
            {
                foreach (KeyValuePair<string, string> item in parameters)
                {
                    if (string.IsNullOrWhiteSpace(item.Key))
                    {
                        throw new ArgumentException("Vision pipeline parameter names cannot be empty.", nameof(parameters));
                    }

                    if (!seen.Add(item.Key))
                    {
                        throw new ArgumentException($"Vision pipeline parameter '{item.Key}' is duplicated.", nameof(parameters));
                    }

                    if (!allowed.Contains(item.Key))
                    {
                        throw new ArgumentException(
                            $"Vision pipeline parameter '{item.Key}' is not supported by {descriptor.PropertyTypeName}.",
                            nameof(parameters));
                    }
                }
            }

            foreach (VisionPipelineParameterDescriptor parameter in descriptor.Parameters)
            {
                if (parameter.Required && !TryGetValue(parameter.Name, out _))
                {
                    throw new ArgumentException(
                        $"Vision pipeline parameter '{parameter.Name}' is required by {descriptor.ToolType}.",
                        nameof(parameters));
                }
            }
        }

        public string GetString(string key, string defaultValue)
        {
            return TryGetValue(key, out string value) ? value ?? string.Empty : defaultValue;
        }

        public int GetInt(string key, int defaultValue)
        {
            if (!TryGetValue(key, out string value))
            {
                return defaultValue;
            }

            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
            {
                throw InvalidParameter(key, value, "an invariant-culture integer", parameters);
            }

            return result;
        }

        public double GetDouble(string key, double defaultValue)
        {
            if (!TryGetValue(key, out string value))
            {
                return defaultValue;
            }

            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result)
                || double.IsNaN(result)
                || double.IsInfinity(result))
            {
                throw InvalidParameter(key, value, "an invariant-culture finite number", parameters);
            }

            return result;
        }

        public bool GetBool(string key, bool defaultValue)
        {
            if (!TryGetValue(key, out string value))
            {
                return defaultValue;
            }

            if (!bool.TryParse(value, out bool result))
            {
                throw InvalidParameter(key, value, "true or false", parameters);
            }

            return result;
        }

        public TEnum GetEnum<TEnum>(string key, TEnum defaultValue)
            where TEnum : struct
        {
            if (!TryGetValue(key, out string value))
            {
                return defaultValue;
            }

            if (!Enum.TryParse(value, true, out TEnum result) || !IsSupportedEnumValue(result))
            {
                throw InvalidParameter(key, value, typeof(TEnum).Name, parameters);
            }

            return result;
        }

        public Rect GetRectangle(string key, Rect defaultValue)
        {
            return TryGetValue(key, out string value) ? ParseRectangle(key, value, parameters) : defaultValue;
        }

        public List<Rect> GetRectangleList(string key, IEnumerable<Rect> defaultValue)
        {
            if (!TryGetValue(key, out string value))
            {
                return new List<Rect>(defaultValue ?? Enumerable.Empty<Rect>());
            }

            List<Rect> rectangles = new List<Rect>();
            if (string.IsNullOrWhiteSpace(value))
            {
                return rectangles;
            }

            foreach (string item in value.Split(RectangleListSeparator, StringSplitOptions.None))
            {
                rectangles.Add(ParseRectangle(key, item, parameters));
            }

            return rectangles;
        }

        public Color GetColor(string key, Color defaultValue)
        {
            if (!TryGetValue(key, out string value))
            {
                return defaultValue;
            }

            string hex = (value ?? string.Empty).Trim();
            if (hex.StartsWith("#", StringComparison.Ordinal))
            {
                hex = hex.Substring(1);
            }

            if (hex.Length == 6
                && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            {
                return Color.FromArgb(byte.MaxValue, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
            }

            if (hex.Length == 8
                && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint argb))
            {
                return Color.FromArgb(
                    (int)((argb >> 24) & 255),
                    (int)((argb >> 16) & 255),
                    (int)((argb >> 8) & 255),
                    (int)(argb & 255));
            }

            throw InvalidParameter(key, value, "#RRGGBB or #AARRGGBB", parameters);
        }

        private bool TryGetValue(string key, out string value)
        {
            value = null;
            if (parameters == null || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            foreach (KeyValuePair<string, string> item in parameters)
            {
                if (string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = item.Value;
                    return true;
                }
            }

            return false;
        }

        private static Rect ParseRectangle(
            string key,
            string value,
            IDictionary<string, string> parameters)
        {
            string[] parts = (value ?? string.Empty).Split(',');
            if (parts.Length != 4)
            {
                throw InvalidParameter(key, value, "x,y,width,height", parameters);
            }

            int[] parsed = new int[4];
            for (int index = 0; index < parsed.Length; index++)
            {
                if (!int.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed[index]))
                {
                    throw InvalidParameter(key, value, "x,y,width,height using invariant-culture integers", parameters);
                }
            }

            return new Rect(parsed[0], parsed[1], parsed[2], parsed[3]);
        }

        private static bool IsSupportedEnumValue<TEnum>(TEnum value)
            where TEnum : struct
        {
            Type enumType = typeof(TEnum);
            if (Enum.IsDefined(enumType, value))
            {
                return true;
            }

            if (!enumType.IsDefined(typeof(FlagsAttribute), false))
            {
                return false;
            }

            try
            {
                ulong allowedBits = 0;
                foreach (object definedValue in Enum.GetValues(enumType))
                {
                    allowedBits |= Convert.ToUInt64(definedValue, CultureInfo.InvariantCulture);
                }

                ulong actualBits = Convert.ToUInt64(value, CultureInfo.InvariantCulture);
                return (actualBits & ~allowedBits) == 0;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static ArgumentException InvalidParameter(
            string key,
            string value,
            string expected,
            IDictionary<string, string> parameters)
        {
            return new ArgumentException(
                $"Vision pipeline parameter '{key}' must be {expected}. Value='{value ?? "<null>"}'.",
                nameof(parameters));
        }
    }
}
