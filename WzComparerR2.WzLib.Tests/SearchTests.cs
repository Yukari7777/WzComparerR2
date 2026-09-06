using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using WzComparerR2.WzLib;

namespace WzComparerR2.WzLib.Tests
{
    internal static partial class Program
    {
        private static void RunSearchTests()
        {
            Run(nameof(SearchReportsMatchesBeforeCompletion), SearchReportsMatchesBeforeCompletion);
            Run(nameof(SearchesNamesPathsAndMetadata), SearchesNamesPathsAndMetadata);
            Run(nameof(SearchesAllRootsWithoutDuplicateRows), SearchesAllRootsWithoutDuplicateRows);
            Run(nameof(SearchLimitsCancellationAndValidation), SearchLimitsCancellationAndValidation);
            Run(nameof(SearchPreservesExtractedImagesAndNavigates), SearchPreservesExtractedImagesAndNavigates);
            Run(nameof(SearchReportsUnreadableImagesAndContinues), SearchReportsUnreadableImagesAndContinues);
        }

        private static void SearchReportsMatchesBeforeCompletion()
        {
            var root = new Wz_Node("Root");
            root.Nodes.Add(new Wz_Node("first") { Value = "림보" });
            root.Nodes.Add(new Wz_Node("second") { Value = "림보" });
            using var cancellation = new CancellationTokenSource();
            WzSearchMatch streamed = null;
            var result = WzSearch.Search(new[] { root }, new WzSearchOptions { Query = "림보" },
                cancellation.Token, onMatch: match =>
                {
                    Assert(streamed == null, "A match arrived after the callback cancelled traversal.");
                    streamed = match;
                    cancellation.Cancel();
                });
            Assert(result.Cancelled && result.VisitedNodes == 2, "Matches must be reported during traversal, not after it finishes.");
            Assert(result.Matches.Count == 1 && ReferenceEquals(streamed, result.Matches[0]), "Streamed and final results must agree without duplicates.");
        }

        private static void SearchesNamesPathsAndMetadata()
        {
            var root = new Wz_Node("Mob");
            var doc = new Wz_Node("8881300.img");
            root.Nodes.Add(doc);
            doc.Nodes.Add(new Wz_Node("name") { Value = "림보" });
            doc.Nodes.Add(new Wz_Node("category") { Value = 1010 });
            doc.Nodes.Add(new Wz_Node("offset") { Value = new Wz_Vector(-20, 30) });
            doc.Nodes.Add(new Wz_Node("link") { Value = new Wz_Uol("../8881350.img") });
            doc.Nodes.Add(new Wz_Node("asset") { Value = new Wz_Png(1, 1, 0, (Wz_TextureFormat)0, 0, 0, 0, 0, null) });
            foreach (var query in new[] { "림보", "1010", "-20,30", "8881350" })
            {
                var found = WzSearch.Search(new[] { root }, new WzSearchOptions { Query = query, Field = "value" });
                Assert(found.Matches.Count == 1, "Metadata query did not match exactly one node: " + query);
                Assert(found.Matches[0].ResolveNode()?.Value != null, "Search result navigation failed.");
            }
            var named = WzSearch.Search(new[] { root }, new WzSearchOptions { Query = "NAME", Field = "name", Mode = "exact" });
            Assert(named.Matches.Single().Value == "림보", "Exact name match should ignore case.");
            var path = WzSearch.Search(new[] { root }, new WzSearchOptions { Query = @"^Mob/8881300\.img/name$", Field = "path", Mode = "regex" });
            Assert(path.Matches.Count == 1, "Regex path search failed.");
            var asset = WzSearch.Search(new[] { root }, new WzSearchOptions { Query = "asset", Field = "name" });
            Assert(asset.Matches.Count == 1 && asset.Matches[0].Value == null, "Canvas payload must not be decoded.");
        }

        private static void SearchesAllRootsWithoutDuplicateRows()
        {
            var root = new Wz_Node("String");
            var child = new Wz_Node("Mob.img");
            child.Nodes.Add(new Wz_Node("8881300") { Value = "림보" });
            root.Nodes.Add(child);
            var second = new Wz_Node("Second") { Value = "림보" };
            var result = WzSearch.Search(new[] { child, root, second, root }, new WzSearchOptions { Query = "림보" });
            Assert(result.Matches.Count == 2, "All roots must be searched once, including nested roots.");
        }

        private static void SearchLimitsCancellationAndValidation()
        {
            var root = new Wz_Node("Root");
            for (int i = 0; i < 10; i++) root.Nodes.Add(new Wz_Node(i.ToString()) { Value = "hit" });
            var result = WzSearch.Search(new[] { root }, new WzSearchOptions { Query = "hit", Limit = 3 });
            Assert(result.Truncated && result.Matches.Count == 3, "Result limit was not enforced.");
            var exactLimit = WzSearch.Search(new[] { root }, new WzSearchOptions { Query = "hit", Limit = 10 });
            Assert(!exactLimit.Truncated, "Exactly enough matches must not report truncation.");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var cancelled = WzSearch.Search(new[] { root }, new WzSearchOptions { Query = "hit" }, cancellation.Token);
            Assert(cancelled.Cancelled && cancelled.VisitedNodes == 0, "Cancellation did not stop traversal.");
            foreach (var options in new[] {
                new WzSearchOptions { Query = "" },
                new WzSearchOptions { Query = "[", Mode = "regex" },
                new WzSearchOptions { Query = "hit", Limit = 0 },
                new WzSearchOptions { Query = "hit", Field = "invalid" },
                new WzSearchOptions { Query = "hit", Mode = "invalid" },
            })
            {
                bool rejected = false;
                try { WzSearch.Search(new[] { root }, options); }
                catch (ArgumentException) { rejected = true; }
                Assert(rejected, "Invalid search options were accepted.");
            }
        }

        private static void SearchPreservesExtractedImagesAndNavigates()
        {
            var structure = new Wz_Structure();
            var image = new Wz_Image("Mob.img", 0, 0, 0, 0, new SyntheticMapleStoryFile(structure));
            var owner = new Wz_Node("Mob.img") { Value = image };
            image.OwnerNode = owner;
            typeof(Wz_Image).GetField("extr", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(image, true);
            image.Node.Nodes.Add(new Wz_Node("name") { Value = "림보" });
            var result = WzSearch.Search(new[] { owner }, new WzSearchOptions { Query = "림보" });
            Assert(image.Extracted, "Already extracted image must remain available to the viewer.");
            Assert(result.VisitedImages == 1 && result.Matches.Single().ResolveNode() == image.Node.Nodes["name"], "Image-local navigation failed.");
        }

        private static void SearchReportsUnreadableImagesAndContinues()
        {
            var structure = new Wz_Structure();
            var broken = new Wz_Image("Broken.img", 0, 0, 0, 0, new SyntheticMapleStoryFile(structure));
            var root = new Wz_Node("Mob");
            root.Nodes.Add(new Wz_Node("Broken.img") { Value = broken });
            root.Nodes.Add(new Wz_Node("valid") { Value = "림보" });
            var result = WzSearch.Search(new[] { root }, new WzSearchOptions { Query = "림보" });
            Assert(result.ErrorCount == 1 && result.Matches.Count == 1, "Unreadable images must report errors and allow remaining roots to be searched.");
            Assert(!broken.Extracted, "Search must release failed image extractions.");
        }
    }
}
