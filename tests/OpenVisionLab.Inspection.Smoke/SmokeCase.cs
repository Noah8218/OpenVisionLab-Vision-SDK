using System;

namespace OpenVisionLab.Inspection.Smoke
{
    internal sealed class SmokeCase
    {
        internal SmokeCase(string name, Action execute, string scope = "cpu")
        {
            if (scope != "cpu" && scope != "cuda-probe" && scope != "cuda-device" && scope != "native-fixture")
                throw new ArgumentOutOfRangeException(nameof(scope));
            Scope = scope;
            Name = name;
            Execute = execute;
        }

        internal string Name { get; }

        internal Action Execute { get; }

        internal string Scope { get; }
    }
}
