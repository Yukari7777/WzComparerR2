using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml;

namespace WzComparerR2.WzLib
{
    public static class WzDumpExporter
    {
        public static bool TryExportImageAsXml(Wz_Image image, string xmlPath, string exportRoot, DumpingOptions dumpOptions, out Exception error)
        {
            error = null;
            try
            {
                if (image == null)
                {
                    throw new ArgumentNullException(nameof(image));
                }

                if (!image.TryExtract(out var extractError))
                {
                    error = extractError;
                    return false;
                }

                return TryExportNodeAsXml(image.Node, xmlPath, exportRoot, dumpOptions, out error);
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        public static bool TryExportNodeAsXml(Wz_Node node, string xmlPath, string exportRoot, DumpingOptions dumpOptions, out Exception error)
        {
            error = null;
            try
            {
                if (node == null)
                {
                    throw new ArgumentNullException(nameof(node));
                }

                dumpOptions ??= DumpingOptions.CreateXmlDefaults();
                exportRoot = NormalizeExportRoot(xmlPath, exportRoot);
                if (ShouldRemoveRedundantPngExports(node, dumpOptions))
                {
                    DeleteRedundantPngExports(exportRoot, node);
                }

                if (ShouldSkipPlaceholderImageDump(node, dumpOptions))
                {
                    DeleteOutputFile(xmlPath);
                    return TryExportNodeAssetsAsXml(node, exportRoot, dumpOptions, out error);
                }

                EnsureOutputDirectory(xmlPath);

                var settings = new XmlWriterSettings()
                {
                    CloseOutput = false,
                    Indent = true,
                    Encoding = Encoding.UTF8,
                    CheckCharacters = true,
                    NewLineChars = Environment.NewLine,
                    NewLineOnAttributes = false,
                };

                using (var fs = new FileStream(xmlPath, FileMode.Create, FileAccess.Write))
                using (var writer = XmlWriter.Create(fs, settings))
                {
                    writer.WriteStartDocument(true);
                    node.DumpAsXml(writer, exportRoot, dumpOptions.DumpRaw, dumpOptions.DumpExternal, dumpOptions.LeaveReference, dumpOptions.OmitRedundantCanvasArtifacts);
                    writer.WriteEndDocument();
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        public static bool TryExportImageAsJson(Wz_Image image, string jsonPath, string exportRoot, DumpingOptions dumpOptions, out Exception error)
        {
            error = null;
            try
            {
                if (image == null)
                {
                    throw new ArgumentNullException(nameof(image));
                }

                if (!image.TryExtract(out var extractError))
                {
                    error = extractError;
                    return false;
                }

                return TryExportNodeAsJson(image.Node, jsonPath, exportRoot, dumpOptions, out error);
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        public static bool TryExportNodeAsJson(Wz_Node node, string jsonPath, string exportRoot, DumpingOptions dumpOptions, out Exception error)
        {
            error = null;
            try
            {
                if (node == null)
                {
                    throw new ArgumentNullException(nameof(node));
                }

                dumpOptions ??= DumpingOptions.CreateJsonDefaults();
                exportRoot = NormalizeExportRoot(jsonPath, exportRoot);
                if (ShouldRemoveRedundantPngExports(node, dumpOptions))
                {
                    DeleteRedundantPngExports(exportRoot, node);
                }

                if (ShouldSkipPlaceholderImageDump(node, dumpOptions))
                {
                    DeleteOutputFile(jsonPath);
                    return TryExportNodeAssetsAsJson(node, exportRoot, dumpOptions, out error);
                }

                EnsureOutputDirectory(jsonPath);

                var jsonWriterOptions = new JsonWriterOptions()
                {
                    Indented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    SkipValidation = false,
                };

                using (var fs = new FileStream(jsonPath, FileMode.Create, FileAccess.Write))
                using (var writer = new Utf8JsonWriter(fs, jsonWriterOptions))
                {
                    node.DumpAsJson(writer, exportRoot, dumpOptions.DumpRaw, dumpOptions.DumpExternal, dumpOptions.LeaveReference, dumpOptions.OmitRedundantCanvasArtifacts);
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        public static IEnumerable<Wz_Image> EnumerateImages(Wz_Node node)
        {
            if (node == null)
            {
                yield break;
            }

            Wz_Image image = node.GetValue<Wz_Image>();
            if (image != null)
            {
                yield return image;
                yield break;
            }

            foreach (var child in node.Nodes)
            {
                foreach (var childImage in EnumerateImages(child))
                {
                    yield return childImage;
                }
            }
        }

        private static void EnsureOutputDirectory(string outputPath)
        {
            string directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        private static string NormalizeExportRoot(string outputPath, string exportRoot)
        {
            if (!string.IsNullOrWhiteSpace(exportRoot))
            {
                return exportRoot;
            }

            string directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                return directory;
            }

            return Directory.GetCurrentDirectory();
        }

        private static bool TryExportNodeAssetsAsXml(Wz_Node node, string exportRoot, DumpingOptions dumpOptions, out Exception error)
        {
            error = null;
            try
            {
                if (!dumpOptions.DumpExternal)
                {
                    return true;
                }

                var settings = new XmlWriterSettings()
                {
                    CloseOutput = false,
                    Indent = false,
                    Encoding = Encoding.UTF8,
                    CheckCharacters = true,
                    NewLineChars = Environment.NewLine,
                    NewLineOnAttributes = false,
                };

                using (var writer = XmlWriter.Create(Stream.Null, settings))
                {
                    writer.WriteStartDocument(true);
                    node.DumpAsXml(writer, exportRoot, false, true, false, true);
                    writer.WriteEndDocument();
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        private static bool TryExportNodeAssetsAsJson(Wz_Node node, string exportRoot, DumpingOptions dumpOptions, out Exception error)
        {
            error = null;
            try
            {
                if (!dumpOptions.DumpExternal)
                {
                    return true;
                }

                var jsonWriterOptions = new JsonWriterOptions()
                {
                    Indented = false,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    SkipValidation = false,
                };

                using (var writer = new Utf8JsonWriter(Stream.Null, jsonWriterOptions))
                {
                    node.DumpAsJson(writer, exportRoot, false, true, false, true);
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        private static bool ShouldSkipPlaceholderImageDump(Wz_Node node, DumpingOptions dumpOptions)
        {
            return dumpOptions?.OmitRedundantCanvasArtifacts == true
                && node?.GetValue<Wz_Image>() != null
                && node.IsCanvasImage();
        }

        private static bool ShouldRemoveRedundantPngExports(Wz_Node node, DumpingOptions dumpOptions)
        {
            return dumpOptions?.OmitRedundantCanvasArtifacts == true
                && node?.GetValue<Wz_Image>() != null
                && !node.IsCanvasImage();
        }

        private static void DeleteOutputFile(string outputPath)
        {
            if (!string.IsNullOrWhiteSpace(outputPath) && File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }

        private static void DeleteRedundantPngExports(string exportRoot, Wz_Node node)
        {
            if (string.IsNullOrWhiteSpace(exportRoot) || node == null || string.IsNullOrWhiteSpace(node.FullPathToFile))
            {
                return;
            }

            string imageDirectory = Path.Combine(exportRoot, node.FullPathToFile.Replace('\\', Path.DirectorySeparatorChar));
            if (!Directory.Exists(imageDirectory))
            {
                return;
            }

            foreach (string filePath in Directory.GetFiles(imageDirectory, "*.png", SearchOption.AllDirectories))
            {
                File.Delete(filePath);
            }

            DeleteEmptyDirectories(imageDirectory);
        }

        private static void DeleteEmptyDirectories(string directoryPath)
        {
            foreach (string childDirectory in Directory.GetDirectories(directoryPath))
            {
                DeleteEmptyDirectories(childDirectory);
            }

            if (Directory.GetFiles(directoryPath).Length == 0 && Directory.GetDirectories(directoryPath).Length == 0)
            {
                Directory.Delete(directoryPath);
            }
        }
    }
}