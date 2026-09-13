using System;
using System.Collections.Generic;

namespace OpenVisionLab.Inspection.Smoke
{
    internal sealed class SmokeRunner
    {
        private readonly string filter;
        private readonly bool listOnly;

        internal SmokeRunner(string filter, bool listOnly)
        {
            this.filter = filter;
            this.listOnly = listOnly;
        }

        internal int Passed { get; private set; }

        internal int Total { get; private set; }

        internal void Run(IEnumerable<SmokeCase> cases)
        {
            foreach (SmokeCase smokeCase in cases)
            {
                if (filter != null && !smokeCase.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Total++;
                if (listOnly)
                {
                    Console.WriteLine(smokeCase.Name);
                    continue;
                }

                smokeCase.Execute();
                Passed++;
                Console.WriteLine("PASS | " + smokeCase.Name);
            }
        }
    }
}
