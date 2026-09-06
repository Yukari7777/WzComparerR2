using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace WzComparerR2.WzLib
{
    public sealed class WzSearchOptions
    {
        public const int DefaultLimit = 1000;
        public string Query { get; set; }
        public string Mode { get; set; } = "contains";
        public string Field { get; set; } = "all";
        public int Limit { get; set; } = DefaultLimit;

        internal Func<string, bool> CreateMatcher()
        {
            if (string.IsNullOrWhiteSpace(Query)) throw new ArgumentException("Search query must not be empty.");
            if (Limit < 1) throw new ArgumentException("Search limit must be positive.");
            if (!new[] { "all", "name", "path", "value" }.Contains(Field))
                throw new ArgumentException("Search field must be all, name, path, or value.");
            if (Mode == "regex")
            {
                var regex = new Regex(Query, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                return value => value != null && regex.IsMatch(value);
            }
            if (Mode == "exact") return value => string.Equals(value, Query, StringComparison.OrdinalIgnoreCase);
            if (Mode != "contains") throw new ArgumentException("Search mode must be contains, exact, or regex.");
            return value => value != null && value.IndexOf(Query, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    public sealed class WzSearchMatch
    {
        public string Source { get; internal set; }
        public string Path { get; internal set; }
        public string Name { get; internal set; }
        public string Value { get; internal set; }
        public string Field { get; internal set; }
        internal Wz_Image Image;
        internal Wz_Node Anchor;
        internal string[] Segments;

        public Wz_Node ResolveNode()
        {
            var node = Anchor;
            if (Image != null)
            {
                if (!Image.TryExtract(out var error)) throw new InvalidOperationException("Cannot reopen search result: " + Path, error);
                node = Image.Node;
            }
            foreach (var segment in Segments) node = node?.Nodes[segment];
            return node;
        }
    }

    public sealed class WzSearchError
    {
        public string Path { get; set; }
        public string Message { get; set; }
    }

    public sealed class WzSearchResult
    {
        public List<WzSearchMatch> Matches { get; } = new List<WzSearchMatch>();
        public List<WzSearchError> Errors { get; } = new List<WzSearchError>();
        public int ErrorCount { get; internal set; }
        public long VisitedNodes { get; internal set; }
        public int VisitedImages { get; internal set; }
        public bool Truncated { get; internal set; }
        public bool Cancelled { get; internal set; }
    }

    public static class WzSearch
    {
        public static IEnumerable<Wz_Node> OpenRoots(IEnumerable<Wz_Structure> structures)
        {
            return structures.SelectMany(s => new[] { s.WzNode }.Concat(s.wz_files.Select(f => f.Node)))
                .Where(n => n != null).Distinct().ToArray();
        }

        public static WzSearchResult Search(IEnumerable<Wz_Node> roots, WzSearchOptions options,
            CancellationToken cancellationToken = default, Action<long, int> progress = null,
            Action<WzSearchMatch> onMatch = null)
        {
            var match = options.CreateMatcher();
            var result = new WzSearchResult();
            var visitedImages = new HashSet<Wz_Image>();
            var rootSet = new HashSet<Wz_Node>(roots.Where(n => n != null));
            bool Stopped() => result.Truncated || cancellationToken.IsCancellationRequested;

            void Visit(Wz_Node node, string path, Wz_Node anchor, Wz_Image image, List<string> segments)
            {
                if (Stopped()) return;
                if (node.Value is Wz_File || node.Value is Wz_Image)
                    path = node.FullPathToFile.Replace('\\', '/');
                result.VisitedNodes++;
                if (result.VisitedNodes % 256 == 0) progress?.Invoke(result.VisitedNodes, result.Matches.Count);
                string value = FormatValue(node.Value);
                string field = null;
                if ((options.Field == "all" || options.Field == "name") && match(node.Text)) field = "name";
                else if ((options.Field == "all" || options.Field == "path") && match(path)) field = "path";
                else if ((options.Field == "all" || options.Field == "value") && match(value)) field = "value";
                if (field != null)
                {
                    if (result.Matches.Count == options.Limit) { result.Truncated = true; return; }
                    var file = (image ?? node.GetNodeWzImage())?.WzFile ?? node.Value as IMapleStoryFile;
                    var found = new WzSearchMatch
                    {
                        Source = file is Wz_File wz ? wz.Header?.FileName : file is Ms_File ms ? ms.Header?.FullFileName :
                            file is Ms_FileV2 ms2 ? ms2.Header?.FullFileName : null,
                        Path = path, Name = node.Text, Value = value, Field = field,
                        Anchor = anchor, Image = image, Segments = segments.ToArray(),
                    };
                    result.Matches.Add(found);
                    onMatch?.Invoke(found);
                }

                if (node.Value is Wz_Image img)
                {
                    if (!visitedImages.Add(img)) return;
                    result.VisitedImages++;
                    bool wasExtracted = img.Extracted;
                    try
                    {
                        bool extracted;
                        Exception error;
                        try { extracted = img.TryExtract(out error); }
                        catch (Exception ex) { extracted = false; error = ex; }
                        if (!extracted)
                        {
                            result.ErrorCount++;
                            if (result.Errors.Count < 100) result.Errors.Add(new WzSearchError { Path = path, Message = error?.Message ?? "Image extraction failed." });
                            return;
                        }
                        var childSegments = new List<string>();
                        foreach (var child in img.Node.Nodes)
                        {
                            if (Stopped()) break;
                            childSegments.Add(child.Text);
                            Visit(child, path + "/" + child.Text, null, img, childSegments);
                            childSegments.RemoveAt(childSegments.Count - 1);
                        }
                    }
                    finally
                    {
                        if (!wasExtracted) img.Unextract();
                    }
                    return;
                }
                foreach (var child in node.Nodes)
                {
                    if (Stopped()) break;
                    segments.Add(child.Text);
                    Visit(child, path.Length == 0 ? child.Text : path + "/" + child.Text, anchor, image, segments);
                    segments.RemoveAt(segments.Count - 1);
                }
            }

            foreach (var root in rootSet)
            {
                bool nested = false;
                for (var parent = root.ParentNode; parent != null; parent = parent.ParentNode)
                    if (rootSet.Contains(parent)) { nested = true; break; }
                if (!nested) Visit(root, root.FullPathToFile.Replace('\\', '/'), root, null, new List<string>());
                if (Stopped()) break;
            }
            result.Cancelled = cancellationToken.IsCancellationRequested;
            return result;
        }

        private static string FormatValue(object value)
        {
            if (value is string text) return text;
            if (value is Wz_Uol uol) return uol.Uol;
            if (value is Wz_Vector vector) return vector.X.ToString(CultureInfo.InvariantCulture) + "," + vector.Y.ToString(CultureInfo.InvariantCulture);
            if (value is byte || value is short || value is int || value is long || value is float || value is double || value is bool)
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            return null;
        }
    }
}
