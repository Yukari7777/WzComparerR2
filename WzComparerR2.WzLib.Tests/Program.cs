using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using WzComparerR2.WzLib;

namespace WzComparerR2.WzLib.Tests
{
    internal static class Program
    {
        private static readonly List<string> Failures = new List<string>();

        private static int Main()
        {
            Run(nameof(ResolvesCaseInsensitiveSlashNormalizedPaths), ResolvesCaseInsensitiveSlashNormalizedPaths);
            Run(nameof(RejectsPathTraversal), RejectsPathTraversal);
            Run(nameof(ResolvesSourceInlinkOutlinkAndUolChains), ResolvesSourceInlinkOutlinkAndUolChains);
            Run(nameof(ReportsMissingAndCyclicLinks), ReportsMissingAndCyclicLinks);
            Run(nameof(ReportsResourceTypeMismatch), ReportsResourceTypeMismatch);
            Run(nameof(DistinguishesLinkStubsFromUnlinkedOneByOneResources), DistinguishesLinkStubsFromUnlinkedOneByOneResources);
            Run(nameof(ValidatesOptionCombinations), ValidatesOptionCombinations);
            Run(nameof(WritesJsonAndXmlToCanonicalPaths), WritesJsonAndXmlToCanonicalPaths);
            Run(nameof(EncodesPortableOutputPaths), EncodesPortableOutputPaths);
            Run(nameof(PreservesExistingOutputWhenPlanningFails), PreservesExistingOutputWhenPlanningFails);

            if (Failures.Count == 0)
            {
                Console.WriteLine("All WzLib export tests passed.");
                return 0;
            }

            Console.Error.WriteLine(string.Join(Environment.NewLine, Failures));
            return 1;
        }

        private static void ResolvesCaseInsensitiveSlashNormalizedPaths()
        {
            Wz_Structure structure = CreateStructure(out Wz_Node document);
            document.Nodes.Add(new Wz_Node("Level"));
            var resolver = new WzNodeResolver(structure);
            Assert(resolver.TryResolveExactPath("doc.IMG/level", out var resolved, out var failure), failure?.Reason);
            using (resolved)
            {
                Assert(resolved.Node.Text == "Level", "Resolved the wrong node.");
            }
        }

        private static void RejectsPathTraversal()
        {
            Wz_Structure structure = CreateStructure(out _);
            var resolver = new WzNodeResolver(structure);
            Assert(!resolver.TryResolveExactPath("../Doc.img", out var resolved, out var failure), "Traversal path unexpectedly resolved.");
            resolved?.Dispose();
            Assert(failure != null, "Traversal rejection did not include a failure.");
        }

        private static void ResolvesSourceInlinkOutlinkAndUolChains()
        {
            foreach (string linkType in new[] { "source", "_outlink" })
            {
                Wz_Structure structure = CreateStructure(out Wz_Node document);
                AddAbsoluteTarget(structure, "Target", "value", 7);
                Wz_Node link = new Wz_Node(linkType) { Value = "Target/value" };
                document.Nodes.Add(new Wz_Node("linked") { Nodes = { link } });
                AssertExportSuccess(structure, document, ExternalOptions());
            }

            {
                Wz_Structure structure = CreateImageStructure(out Wz_Node document);
                document.Nodes.Add(new Wz_Node("target") { Value = 7 });
                document.Nodes.Add(new Wz_Node("linked")
                {
                    Nodes = { new Wz_Node("_inlink") { Value = "target" } },
                });
                AssertExportSuccess(structure, document, ExternalOptions());
            }

            {
                Wz_Structure structure = CreateStructure(out Wz_Node document);
                document.Nodes.Add(new Wz_Node("target") { Value = 7 });
                document.Nodes.Add(new Wz_Node("middle") { Value = new Wz_Uol("target") });
                document.Nodes.Add(new Wz_Node("linked") { Value = new Wz_Uol("middle") });
                AssertExportSuccess(structure, document, ExternalOptions());
            }
        }

        private static void ReportsMissingAndCyclicLinks()
        {
            {
                Wz_Structure structure = CreateStructure(out Wz_Node document);
                document.Nodes.Add(new Wz_Node("linked")
                {
                    Nodes = { new Wz_Node("_outlink") { Value = "Missing/asset" } },
                });
                WzExportResult metadataResult = Export(structure, document, DumpingOptions.CreateDefaults());
                Assert(metadataResult.Success, "Metadata-only export should not resolve external links.");

                WzExportResult externalResult = Export(structure, document, ExternalOptions());
                Assert(!externalResult.Success, "Missing external link unexpectedly succeeded.");
                Assert(externalResult.Failures.Any(item => item.Code == "path_not_found"), "Missing link failure code was not reported.");
            }

            {
                Wz_Structure structure = CreateStructure(out Wz_Node document);
                document.Nodes.Add(new Wz_Node("a") { Value = new Wz_Uol("b") });
                document.Nodes.Add(new Wz_Node("b") { Value = new Wz_Uol("a") });
                WzExportResult result = Export(structure, document, ExternalOptions());
                Assert(!result.Success, "UOL cycle unexpectedly succeeded.");
                Assert(result.Failures.Any(item => item.Code == "link_cycle"), "UOL cycle failure code was not reported.");
            }
        }

        private static void ReportsResourceTypeMismatch()
        {
            Wz_Structure structure = CreateStructure(out Wz_Node document);
            AddAbsoluteTarget(structure, "Target", "asset", new Wz_RawData(0, 0, null));
            document.Nodes.Add(new Wz_Node("linked")
            {
                Value = new Wz_Png(1, 1, 0, default, 0, 0, 0, 0, null),
                Nodes = { new Wz_Node("_outlink") { Value = "Target/asset" } },
            });

            WzExportResult result = Export(structure, document, ExternalOptions());
            Assert(!result.Success, "Resource type mismatch unexpectedly succeeded.");
            Assert(result.Failures.Any(item => item.Code == "resource_type_mismatch"), "Resource type mismatch code was not reported.");
            Assert(result.Failures.All(item => item.SourceWasLinkStub), "The linked 1x1 source was not identified as a link stub.");
        }

        private static void DistinguishesLinkStubsFromUnlinkedOneByOneResources()
        {
            Wz_Structure structure = CreateStructure(out Wz_Node document);
            document.Nodes.Add(new Wz_Node("oneByOne")
            {
                Value = new Wz_Png(1, 1, 0, Wz_TextureFormat.ARGB8888, 0, 0, 0, 0, null),
            });

            WzExportResult result = Export(structure, document, ExternalOptions());
            Assert(!result.Success, "Synthetic PNG without backing data unexpectedly decoded.");
            Assert(result.Failures.Any(item => item.Code == "resource_decode_failed"), "Decode failure code was not reported.");
            Assert(result.Failures.All(item => !item.SourceWasLinkStub), "An unlinked 1x1 resource was incorrectly classified as a link stub.");
        }

        private static void ValidatesOptionCombinations()
        {
            Wz_Structure structure = CreateStructure(out Wz_Node document);
            WzExportResult rawAndExternal = Export(structure, document, new DumpingOptions
            {
                DumpRaw = true,
                DumpExternal = true,
            });
            Assert(!rawAndExternal.Success, "dumpRaw and dumpExternal unexpectedly succeeded together.");

            WzExportResult referenceOnly = Export(structure, document, new DumpingOptions
            {
                LeaveReference = true,
            });
            Assert(!referenceOnly.Success, "leaveReference unexpectedly succeeded without dumpExternal.");

            string outputRoot = CreateTempDirectory();
            try
            {
                var resolver = new WzNodeResolver(structure);
                Assert(resolver.TryResolveExactPath("Doc.img", out var resolved, out var failure), failure?.Reason);
                using (resolved)
                {
                    WzExportResult invalidFormat = WzDumpExporter.Export(document, outputRoot, (WzDumpFormat)99, DumpingOptions.CreateDefaults(), resolved);
                    Assert(!invalidFormat.Success, "Unknown dump format unexpectedly succeeded.");
                    Assert(invalidFormat.Failures.Any(item => item.Code == "invalid_format"), "Unknown format failure code was not reported.");
                }
            }
            finally
            {
                Directory.Delete(outputRoot, true);
            }
        }

        private static void WritesJsonAndXmlToCanonicalPaths()
        {
            Wz_Structure structure = CreateStructure(out Wz_Node document);
            document.Nodes.Add(new Wz_Node("value") { Value = 7 });
            string outputRoot = CreateTempDirectory();
            try
            {
                var resolver = new WzNodeResolver(structure);
                Assert(resolver.TryResolveExactPath("Doc.img", out var resolved, out var failure), failure?.Reason);
                using (resolved)
                {
                    WzExportResult json = WzDumpExporter.Export(document, outputRoot, WzDumpFormat.Json, DumpingOptions.CreateDefaults(), resolved);
                    WzExportResult xml = WzDumpExporter.Export(document, outputRoot, WzDumpFormat.Xml, DumpingOptions.CreateDefaults(), resolved);
                    Assert(json.Success && xml.Success, "JSON or XML export failed.");
                    Assert(json.DocumentWritten && xml.DocumentWritten, "Document result was not recorded.");
                    Assert(File.Exists(Path.Combine(outputRoot, "Doc.img.json")), "Canonical JSON path was not written.");
                    Assert(File.Exists(Path.Combine(outputRoot, "Doc.img.xml")), "Canonical XML path was not written.");
                }
            }
            finally
            {
                Directory.Delete(outputRoot, true);
            }
        }

        private static void PreservesExistingOutputWhenPlanningFails()
        {
            Wz_Structure structure = CreateStructure(out Wz_Node document);
            document.Nodes.Add(new Wz_Node("linked")
            {
                Nodes = { new Wz_Node("_outlink") { Value = "Missing/asset" } },
            });
            string outputRoot = CreateTempDirectory();
            string documentPath = Path.Combine(outputRoot, "Doc.img.json");
            const string originalContent = "existing-output";
            File.WriteAllText(documentPath, originalContent);
            try
            {
                WzExportResult result = Export(structure, document, ExternalOptions(), outputRoot);
                Assert(!result.Success, "Planning failure unexpectedly succeeded.");
                Assert(File.ReadAllText(documentPath) == originalContent, "Failed export changed existing output.");
                Assert(Directory.GetFiles(outputRoot, "*", SearchOption.AllDirectories).Length == 1, "Failed export left partial files.");
            }
            finally
            {
                Directory.Delete(outputRoot, true);
            }
        }

        private static void EncodesPortableOutputPaths()
        {
            MethodInfo method = typeof(WzDumpExporter).GetMethod(
                "EncodeRelativeOutputPath",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert(method != null, "Portable path encoder was not found.");
            Func<string, string> encode = value => (string)method.Invoke(null, new object[] { value });

            Assert(encode("Etc/_Canvas/BossKaring.img/normal/0.png")
                == "Etc/_Canvas/BossKaring.img/normal/0.png", "Safe path was changed.");
            Assert(encode("Etc/_Canvas/BossKaring.img/button:ready/0.png")
                == "Etc/_Canvas/BossKaring.img/button%3Aready/0.png", "Colon was not encoded.");
            Assert(encode("Etc/100%/trail./0.png")
                == "Etc/100%25/trail%2E/0.png", "Percent or trailing dot was not encoded.");
            Assert(encode("Etc/CON/file.png")
                == "Etc/%43ON/file.png", "Reserved Windows file name was not encoded.");
        }

        private static WzExportResult AssertExportSuccess(Wz_Structure structure, Wz_Node document, DumpingOptions options)
        {
            WzExportResult result = Export(structure, document, options);
            Assert(result.Success, result.Failures.FirstOrDefault()?.Reason ?? "Export failed.");
            return result;
        }

        private static WzExportResult Export(Wz_Structure structure, Wz_Node document, DumpingOptions options, string outputRoot = null)
        {
            bool ownsOutputRoot = outputRoot == null;
            outputRoot ??= CreateTempDirectory();
            try
            {
                var resolver = new WzNodeResolver(structure);
                Assert(resolver.TryResolveExactPath("Doc.img", out var resolved, out var failure), failure?.Reason);
                using (resolved)
                {
                    return WzDumpExporter.Export(document, outputRoot, WzDumpFormat.Json, options, resolved);
                }
            }
            finally
            {
                if (ownsOutputRoot)
                {
                    Directory.Delete(outputRoot, true);
                }
            }
        }

        private static Wz_Structure CreateStructure(out Wz_Node document)
        {
            var structure = new Wz_Structure { WzNode = new Wz_Node(string.Empty) };
            document = new Wz_Node("Doc.img");
            structure.WzNode.Nodes.Add(document);
            return structure;
        }

        private static Wz_Structure CreateImageStructure(out Wz_Node document)
        {
            var structure = new Wz_Structure { WzNode = new Wz_Node(string.Empty) };
            var image = new Wz_Image("Doc.img", 0, 0, 0, 0, new SyntheticMapleStoryFile(structure));
            typeof(Wz_Image).GetField("extr", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(image, true);
            document = image.Node;
            structure.WzNode.Nodes.Add(document);
            return structure;
        }

        private static Wz_Node AddAbsoluteTarget(Wz_Structure structure, string directoryName, string nodeName, object value)
        {
            Wz_Node directory = structure.WzNode.Nodes[directoryName];
            if (directory == null)
            {
                directory = new Wz_Node(directoryName);
                structure.WzNode.Nodes.Add(directory);
            }
            var node = new Wz_Node(nodeName) { Value = value };
            directory.Nodes.Add(node);
            return node;
        }

        private static DumpingOptions ExternalOptions() => new DumpingOptions { DumpExternal = true };

        private static string CreateTempDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "wcr2-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception ex)
            {
                Failures.Add($"FAIL {name}: {ex.Message}");
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message ?? "Assertion failed.");
            }
        }

        private sealed class SyntheticMapleStoryFile : IMapleStoryFile
        {
            public SyntheticMapleStoryFile(Wz_Structure structure)
            {
                this.WzStructure = structure;
            }

            public Wz_Structure WzStructure { get; }

            public Stream FileStream => Stream.Null;

            public object ReadLock => this;

            public void Dispose()
            {
            }
        }
    }
}
