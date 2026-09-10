using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml;

namespace WzComparerR2.WzLib
{
    internal static class WzNodeDumper
    {
        public static bool IsCanvasImage(this Wz_Node node)
        {
            string imagePath = node?.GetNodeWzImage()?.Node?.FullPathToFile;
            return !string.IsNullOrEmpty(imagePath)
                && (imagePath.StartsWith("_Canvas\\", StringComparison.OrdinalIgnoreCase)
                    || imagePath.IndexOf("\\_Canvas\\", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        internal static void DumpAsXml(this Wz_Node node, XmlWriter writer, bool dumpRaw, bool leaveRef, WzDumpSerializationContext context)
        {
            using (context?.Activate())
            {
                DumpXmlNode(node, writer, dumpRaw, leaveRef);
            }
        }

        private static void DumpXmlNode(Wz_Node node, XmlWriter writer, bool dumpRaw, bool leaveRef)
        {
            WzDumpSerializationContext context = WzDumpSerializationContext.Current;
            object value = context?.GetResolvedNode(node)?.Value ?? node.Value;
            
            if (value == null || value is Wz_Image)
            {
                writer.WriteStartElement("dir");
                writer.WriteAttributeString("name", node.Text);
            }
            else if (value is Wz_Png png)
            {
                IReadOnlyList<string> referencedFiles = context?.GetExternalFiles(node);
                writer.WriteStartElement("png");
                writer.WriteAttributeString("name", node.Text);
                writer.WriteAttributeString("width", png.Width.ToString());
                writer.WriteAttributeString("height", png.Height.ToString());
                writer.WriteAttributeString("format", ((int)png.Format).ToString());
                writer.WriteAttributeString("scale", png.Scale.ToString());
                writer.WriteAttributeString("pages", png.Pages.ToString());
                if (dumpRaw)
                {
                    IReadOnlyList<byte[]> rawData = context?.GetRawData(node);
                    for (int i = 0; i < png.ActualPages; i++)
                    {
                        byte[] data = rawData != null && i < rawData.Count ? rawData[i] : null;
                        if (data == null)
                        {
                            using (var bmp = png.ExtractPng(i))
                            using (var ms = new MemoryStream())
                            {
                                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                                data = ms.ToArray();
                            }
                        }
                        string attrName = "value" + (i > 0 ? (i + 1).ToString() : null);
                        writer.WriteAttributeString(attrName, Convert.ToBase64String(data));
                    }
                }
                if (leaveRef)
                {
                    WriteXmlFileReferences(writer, referencedFiles);
                }
            }
            else if (value is Wz_Uol uol)
            {
                writer.WriteStartElement("uol");
                writer.WriteAttributeString("name", node.Text);
                writer.WriteAttributeString("value", uol.Uol);
            }
            else if (value is Wz_Vector vector)
            {
                writer.WriteStartElement("vector");
                writer.WriteAttributeString("name", node.Text);
                writer.WriteAttributeString("value", $"{vector.X}, {vector.Y}");
            }
            else if (value is Wz_Sound sound)
            {
                writer.WriteStartElement("sound");
                writer.WriteAttributeString("name", node.Text);
                if (dumpRaw)
                {
                    byte[] data = context?.GetRawData(node)?.FirstOrDefault();
                    if (data == null)
                    {
                        data = sound.ExtractSound();
                        if (data == null)
                        {
                            data = new byte[sound.DataLength];
                            sound.CopyTo(data, 0);
                        }
                    }
                    writer.WriteAttributeString("value", Convert.ToBase64String(data));
                }
                if (leaveRef)
                {
                    WriteXmlFileReferences(writer, context?.GetExternalFiles(node));
                }
            }
            else if (value is Wz_Convex contex)
            {
                writer.WriteStartElement("convex");
                writer.WriteAttributeString("name", node.Text);
                foreach (var point in contex.Points)
                {
                    writer.WriteStartElement("vector");
                    writer.WriteAttributeString("value", $"{point.X}, {point.Y}");
                    writer.WriteEndElement();
                }
            }
            else if (value is Wz_RawData rawdata)
            {
                writer.WriteStartElement("rawdata");
                writer.WriteAttributeString("name", node.Text);
                writer.WriteAttributeString("length", rawdata.Length.ToString());
                if (dumpRaw)
                {
                    byte[] data = context?.GetRawData(node)?.FirstOrDefault();
                    if (data == null)
                    {
                        data = new byte[rawdata.Length];
                        rawdata.CopyTo(data, 0);
                    }
                    writer.WriteAttributeString("value", Convert.ToBase64String(data));
                }
                if (leaveRef)
                {
                    WriteXmlFileReferences(writer, context?.GetExternalFiles(node));
                }
            }
            else if (value is Wz_Video video)
            {
                writer.WriteStartElement("video");
                writer.WriteAttributeString("name", node.Text);
                writer.WriteAttributeString("length", video.Length.ToString());
                if (dumpRaw)
                {
                    byte[] data = context?.GetRawData(node)?.FirstOrDefault();
                    if (data == null)
                    {
                        data = new byte[video.Length];
                        video.CopyTo(data, 0);
                    }
                    writer.WriteAttributeString("value", Convert.ToBase64String(data));
                }
                if (leaveRef)
                {
                    WriteXmlFileReferences(writer, context?.GetExternalFiles(node));
                }
            }
            else
            {
                var tag = value.GetType().Name.ToLower();
                writer.WriteStartElement(tag);
                writer.WriteAttributeString("name", node.Text);
                writer.WriteAttributeString("value", value.ToString());
            }

            //输出子节点
            foreach (var child in node.Nodes)
            {
                DumpXmlNode(child, writer, dumpRaw, leaveRef);
            }

            //结束标识
            writer.WriteEndElement();
        }

        private static void WriteXmlFileReferences(XmlWriter writer, IReadOnlyList<string> files)
        {
            if (files == null)
            {
                return;
            }
            for (int i = 0; i < files.Count; i++)
            {
                writer.WriteAttributeString(i == 0 ? "file" : "file" + (i + 1), files[i]);
            }
        }

        private const string JsonMetadataPrefix = "@";
        private const string JsonTypeKey = JsonMetadataPrefix + "type";
        private const string JsonValueKey = JsonMetadataPrefix + "value";
        private const string JsonFormatKey = JsonMetadataPrefix + "format";
        private const string JsonScaleKey = JsonMetadataPrefix + "scale";
        private const string JsonPagesKey = JsonMetadataPrefix + "pages";
        private const string JsonWidthKey = JsonMetadataPrefix + "width";
        private const string JsonHeightKey = JsonMetadataPrefix + "height";
        private const string JsonLengthKey = JsonMetadataPrefix + "length";
        private const string JsonMsKey = JsonMetadataPrefix + "ms";
        private const string JsonChannelsKey = JsonMetadataPrefix + "channels";
        private const string JsonFrequencyKey = JsonMetadataPrefix + "frequency";
        private const string JsonPointsKey = JsonMetadataPrefix + "points";
        private const string JsonDataKey = JsonMetadataPrefix + "data";

        internal static void DumpAsJson(this Wz_Node node, Utf8JsonWriter writer, bool dumpRaw, bool leaveRef, WzDumpSerializationContext context)
        {
            using (context?.Activate())
            {
                writer.WriteStartObject();
                WriteNodeProperty(node, writer, dumpRaw, leaveRef);
                writer.WriteEndObject();
            }
        }

        private static void WriteNodeProperty(Wz_Node node, Utf8JsonWriter writer, bool dumpRaw, bool leaveRef)
        {
            writer.WritePropertyName(node.Text);
            WriteNodeValue(node, writer, dumpRaw, leaveRef);
        }

        private static void WriteNodeValue(Wz_Node node, Utf8JsonWriter writer, bool dumpRaw, bool leaveRef)
        {
            WzDumpSerializationContext context = WzDumpSerializationContext.Current;
            object value = context?.GetResolvedNode(node)?.Value ?? node.Value;
            bool hasChildren = node.Nodes.Count > 0;

            if (value == null || value is Wz_Image)
            {
                WriteNodeAsObject(node, writer, dumpRaw, leaveRef, null);
                return;
            }

            if (value is Wz_Png png)
            {
                IReadOnlyList<string> exportedFiles = context?.GetExternalFiles(node);
                WriteNodeAsObject(node, writer, dumpRaw, leaveRef, () =>
                {
                    writer.WriteString(JsonTypeKey, "png");
                    if (context?.IncludePngDimensions == true)
                    {
                        writer.WriteNumber(JsonWidthKey, png.Width);
                        writer.WriteNumber(JsonHeightKey, png.Height);
                    }
                    if (!(png.Width == 1 && png.Height == 1))
                    {
                        writer.WriteNumber(JsonFormatKey, (int)png.Format);
                    }
                    if (png.Scale != 0)
                    {
                        writer.WriteNumber(JsonScaleKey, png.Scale);
                    }
                    if (png.ActualPages >= 2)
                    {
                        writer.WriteNumber(JsonPagesKey, png.ActualPages);
                    }

                    if (dumpRaw)
                    {
                        WritePngRawData(writer, png, context?.GetRawData(node));
                    }
                    
                    if (leaveRef && exportedFiles != null)
                    {
                        WriteFileReference(writer, exportedFiles);
                    }
                });
                return;
            }

            if (value is Wz_Uol uol)
            {
                WriteNodeAsObject(node, writer, dumpRaw, leaveRef, () =>
                {
                    writer.WriteString(JsonTypeKey, "uol");
                    writer.WriteString(JsonValueKey, uol.Uol);
                });
                return;
            }

            if (value is Wz_Vector vector)
            {
                WriteNodeAsObject(node, writer, dumpRaw, leaveRef, () =>
                {
                    writer.WriteString(JsonTypeKey, "vector");
                    writer.WriteString(JsonValueKey, $"{vector.X}, {vector.Y}");
                });
                return;
            }

            if (value is Wz_Sound sound)
            {
                IReadOnlyList<string> exportedFiles = context?.GetExternalFiles(node);
                WriteNodeAsObject(node, writer, dumpRaw, leaveRef, () =>
                {
                    writer.WriteString(JsonTypeKey, "sound");
                    writer.WriteString(JsonFormatKey, sound.SoundType switch
                    {
                        Wz_SoundType.Mp3 => "mp3",
                        Wz_SoundType.Pcm => "wav",
                        _ => "bin",
                    });
                    if (sound.DataLength > 0)
                    {
                        writer.WriteNumber(JsonLengthKey, sound.DataLength);
                    }
                    if (sound.Ms > 0)
                    {
                        writer.WriteNumber(JsonMsKey, sound.Ms);
                    }
                    if (sound.Channels > 0)
                    {
                        writer.WriteNumber(JsonChannelsKey, sound.Channels);
                    }
                    if (sound.Frequency > 0)
                    {
                        writer.WriteNumber(JsonFrequencyKey, sound.Frequency);
                    }

                    if (dumpRaw)
                    {
                        writer.WriteBase64String(JsonDataKey, context?.GetRawData(node)?.FirstOrDefault() ?? GetSoundBytes(sound));
                    }
                    
                    if (leaveRef && exportedFiles != null)
                    {
                        WriteFileReference(writer, exportedFiles);
                    }
                });
                return;
            }

            if (value is Wz_Convex convex)
            {
                WriteNodeAsObject(node, writer, dumpRaw, leaveRef, () =>
                {
                    writer.WriteString(JsonTypeKey, "convex");
                    writer.WritePropertyName(JsonPointsKey);
                    writer.WriteStartArray();
                    foreach (var point in convex.Points)
                    {
                        writer.WriteStartObject();
                        writer.WriteString(JsonValueKey, $"{point.X}, {point.Y}");
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                });
                return;
            }

            if (value is Wz_RawData rawData)
            {
                IReadOnlyList<string> exportedFiles = context?.GetExternalFiles(node);
                WriteNodeAsObject(node, writer, dumpRaw, leaveRef, () =>
                {
                    writer.WriteString(JsonTypeKey, "rawdata");
                    writer.WriteNumber(JsonLengthKey, rawData.Length);

                    if (dumpRaw)
                    {
                        writer.WriteBase64String(JsonDataKey, context?.GetRawData(node)?.FirstOrDefault() ?? GetRawDataBytes(rawData));
                    }
                    if (leaveRef && exportedFiles != null)
                    {
                        WriteFileReference(writer, exportedFiles);
                    }
                });
                return;
            }

            if (value is Wz_Video video)
            {
                IReadOnlyList<string> exportedFiles = context?.GetExternalFiles(node);
                WriteNodeAsObject(node, writer, dumpRaw, leaveRef, () =>
                {
                    writer.WriteString(JsonTypeKey, "video");
                    writer.WriteNumber(JsonLengthKey, video.Length);

                    if (dumpRaw)
                    {
                        writer.WriteBase64String(JsonDataKey, context?.GetRawData(node)?.FirstOrDefault() ?? GetVideoBytes(video));
                    }
                    if (leaveRef && exportedFiles != null)
                    {
                        WriteFileReference(writer, exportedFiles);
                    }
                });
                return;
            }

            if (value is string str)
            {
                WritePrimitive(node, writer, hasChildren, dumpRaw, leaveRef, () => writer.WriteStringValue(str), () => writer.WriteString(JsonValueKey, str));
                return;
            }

            if (value is bool boolean)
            {
                WritePrimitive(node, writer, hasChildren, dumpRaw, leaveRef, () => writer.WriteBooleanValue(boolean), () => writer.WriteBoolean(JsonValueKey, boolean));
                return;
            }

            if (value is sbyte sb)
            {
                WritePrimitive(node, writer, hasChildren, dumpRaw, leaveRef, () => writer.WriteNumberValue(sb), () => writer.WriteNumber(JsonValueKey, sb));
                return;
            }

            if (value is byte b)
            {
                WritePrimitive(node, writer, hasChildren, dumpRaw, leaveRef, () => writer.WriteNumberValue(b), () => writer.WriteNumber(JsonValueKey, b));
                return;
            }

            if (value is short s)
            {
                WritePrimitive(node, writer, hasChildren, dumpRaw, leaveRef, () => writer.WriteNumberValue(s), () => writer.WriteNumber(JsonValueKey, s));
                return;
            }

            if (value is ushort us)
            {
                WritePrimitive(node, writer, hasChildren, dumpRaw, leaveRef, () => writer.WriteNumberValue(us), () => writer.WriteNumber(JsonValueKey, us));
                return;
            }

            if (value is int i)
            {
                WritePrimitive(node, writer, hasChildren, dumpRaw, leaveRef, () => writer.WriteNumberValue(i), () => writer.WriteNumber(JsonValueKey, i));
                return;
            }

            if (value is uint ui)
            {
                WritePrimitive(node, writer, hasChildren, dumpRaw, leaveRef, () => writer.WriteNumberValue(ui), () => writer.WriteNumber(JsonValueKey, ui));
                return;
            }

            if (value is long l)
            {
                WritePrimitive(node, writer, hasChildren, dumpRaw, leaveRef, () => writer.WriteNumberValue(l), () => writer.WriteNumber(JsonValueKey, l));
                return;
            }

            if (value is ulong ul)
            {
                WritePrimitive(node, writer, hasChildren, dumpRaw, leaveRef, () => writer.WriteNumberValue(ul), () => writer.WriteNumber(JsonValueKey, ul));
                return;
            }

            if (value is float f)
            {
                WritePrimitive(node, writer, hasChildren, dumpRaw, leaveRef, () => writer.WriteNumberValue(f), () => writer.WriteNumber(JsonValueKey, f));
                return;
            }

            if (value is double d)
            {
                WritePrimitive(node, writer, hasChildren, dumpRaw, leaveRef, () => writer.WriteNumberValue(d), () => writer.WriteNumber(JsonValueKey, d));
                return;
            }

            if (value is decimal dec)
            {
                WritePrimitive(node, writer, hasChildren, dumpRaw, leaveRef, () => writer.WriteNumberValue(dec), () => writer.WriteNumber(JsonValueKey, dec));
                return;
            }

            WriteNodeAsObject(node, writer, dumpRaw, leaveRef, () =>
            {
                writer.WriteString(JsonTypeKey, value.GetType().Name.ToLowerInvariant());
                writer.WriteString(JsonValueKey, value.ToString());
            });
        }

        private static void WritePrimitive(Wz_Node node, Utf8JsonWriter writer, bool hasChildren, bool dumpRaw, bool leaveRef, Action writeValue, Action writeProperty)
        {
            if (!hasChildren)
            {
                writeValue();
                return;
            }

            WriteNodeAsObject(node, writer, dumpRaw, leaveRef, () => writeProperty());
        }

        private static void WriteNodeAsObject(Wz_Node node, Utf8JsonWriter writer, bool dumpRaw, bool leaveRef, Action metadataWriter)
        {
            writer.WriteStartObject();
            metadataWriter?.Invoke();
            if (node.Nodes.Count > 0)
            {
                foreach (var child in node.Nodes)
                {
                    WriteNodeProperty(child, writer, dumpRaw, leaveRef);
                }
            }
            writer.WriteEndObject();
        }

        private static void WritePngRawData(Utf8JsonWriter writer, Wz_Png png, IReadOnlyList<byte[]> rawData = null)
        {
            int pageCount = Math.Max(png.ActualPages, 1);
            if (pageCount <= 1)
            {
                if (rawData != null && rawData.Count > 0)
                {
                    writer.WriteBase64String(JsonDataKey, rawData[0]);
                    return;
                }
                using (var bmp = png.ExtractPng())
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    writer.WriteBase64String(JsonDataKey, ms.ToArray());
                }
                return;
            }

            writer.WritePropertyName(JsonDataKey);
            writer.WriteStartArray();
            for (int i = 0; i < pageCount; i++)
            {
                if (rawData != null && i < rawData.Count)
                {
                    writer.WriteBase64StringValue(rawData[i]);
                    continue;
                }
                using (var bmp = png.ExtractPng(i))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    writer.WriteBase64StringValue(ms.ToArray());
                }
            }
            writer.WriteEndArray();
        }

        private static void WriteFileReference(Utf8JsonWriter writer, IReadOnlyList<string> files)
        {
            if (files == null || files.Count == 0)
            {
                return;
            }

            if (files.Count == 1)
            {
                writer.WriteString("file", files[0]);
                return;
            }

            writer.WritePropertyName("files");
            writer.WriteStartArray();
            foreach (var file in files)
            {
                writer.WriteStringValue(file);
            }
            writer.WriteEndArray();
        }

        private static byte[] GetSoundBytes(Wz_Sound sound)
        {
            byte[] data = sound.ExtractSound();
            if (data == null)
            {
                data = new byte[sound.DataLength];
                sound.CopyTo(data, 0);
            }
            return data;
        }

        private static byte[] GetRawDataBytes(Wz_RawData rawData)
        {
            byte[] data = new byte[rawData.Length];
            rawData.CopyTo(data, 0);
            return data;
        }

        private static byte[] GetVideoBytes(Wz_Video video)
        {
            byte[] data = new byte[video.Length];
            video.CopyTo(data, 0);
            return data;
        }
    }
}

