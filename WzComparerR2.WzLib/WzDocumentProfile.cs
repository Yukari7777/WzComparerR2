using System;
using System.Collections.Generic;

namespace WzComparerR2.WzLib
{
    public sealed class WzDocumentProfile
    {
        public string Root { get; set; }

        public bool SplitImageChildren { get; set; }

        public IReadOnlyList<string> AdditionalSplitNodes { get; set; } = Array.Empty<string>();

        public IReadOnlyList<string> ExcludedSubtrees { get; set; } = Array.Empty<string>();
    }
}
