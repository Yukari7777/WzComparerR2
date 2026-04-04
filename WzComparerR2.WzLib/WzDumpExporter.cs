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
                    node.DumpAsXml(writer, exportRoot, dumpOptions.DumpRaw, dumpOptions.DumpExternal, dumpOptions.LeaveReference);
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
                    node.DumpAsJson(writer, exportRoot, dumpOptions.DumpRaw, dumpOptions.DumpExternal, dumpOptions.LeaveReference);
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
    }
}