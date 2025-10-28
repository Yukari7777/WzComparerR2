using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace WzComparerR2.WzLib
{
    public class Wz_Node : ICloneable, IComparable, IComparable<Wz_Node>
    {
        public Wz_Node()
        {
            this.nodes = new WzNodeCollection(this);
        }

        public Wz_Node(string nodeText)
            : this()
        {
            this.text = nodeText;
        }

        //fields
        private object value;
        private string text;
        private WzNodeCollection nodes;
        private Wz_Node parentNode;

        //properties
        public object Value
        {
            get { return value; }
            set { this.value = value; }
        }

        public string Text
        {
            get { return this.text; }
            set { this.text = value; }
        }

        public string FullPath
        {
            get
            {
                Stack<string> path = new Stack<string>();
                Wz_Node node = this;
                do
                {
                    path.Push(node.text);
                    node = node.parentNode;
                } while (node != null);
                return string.Join("\\", path.ToArray());
            }
        }

        public string FullPathToFile
        {
            get
            {
                Stack<string> path = new Stack<string>();
                Wz_Node node = this;
                do
                {
                    if (node.value is Wz_File wzf && !wzf.IsSubDir)
                    {
                        if (node.text.EndsWith(".wz", StringComparison.OrdinalIgnoreCase))
                        {
                            path.Push(node.text.Substring(0, node.text.Length - 3));
                        }
                        else
                        {
                            path.Push(node.text);
                        }
                        break;
                    }

                    path.Push(node.text);

                    var img = node.GetValue<Wz_Image>();
                    if (img != null)
                    {
                        node = img.OwnerNode;
                    }

                    if (node != null)
                    {
                        node = node.parentNode;
                    }
                } while (node != null);
                return string.Join("\\", path.ToArray());
            }
        }

        public WzNodeCollection Nodes
        {
            get { return this.nodes; }
        }

        public Wz_Node ParentNode
        {
            get { return parentNode; }
            private set { parentNode = value; }
        }

        //methods
        public override string ToString()
        {
            return this.Text + " " + (this.value != null ? this.value.ToString() : "-") + " " + this.nodes.Count;
        }

        public Wz_Node FindNodeByPath(string fullPath)
        {
            return FindNodeByPath(fullPath, false);
        }

        public Wz_Node FindNodeByPath(string fullPath, bool extractImage)
        {
            string[] patten = fullPath.Split('\\');
            return FindNodeByPath(extractImage, patten);
        }

        public Wz_Node FindNodeByPath(bool extractImage, params string[] fullPath)
        {
            return FindNodeByPath(extractImage, false, fullPath);
        }

        public Wz_Node FindNodeByPath(bool extractImage, bool ignoreCase, params string[] fullPath)
        {
            Wz_Node node = this;

            Wz_Image img;

            //首次解压
            if (extractImage && (img = this.GetValue<Wz_Image>()) != null)
            {
                if (img.TryExtract())
                {
                    node = img.Node;
                }
            }

            foreach (string txt in fullPath)
            {
                if (ignoreCase)
                {
                    bool find = false;

                    foreach (Wz_Node subNode in node.nodes)
                    {
                        if (string.Equals(subNode.text, txt, StringComparison.OrdinalIgnoreCase))
                        {
                            find = true;
                            node = subNode;
                        }
                    }
                    if (!find)
                        node = null;
                }
                else
                {
                    node = node.nodes[txt];
                }

                if (node == null)
                    return null;

                if (extractImage)
                {
                    img = node.GetValue<Wz_Image>();
                    if (img != null && img.TryExtract()) //判断是否是img
                    {
                        node = img.Node;
                    }
                }
            }
            return node;
        }

        public T GetValue<T>(T defaultValue)
        {
            var typeT = typeof(T);
            if (typeof(Wz_Image) == typeT)
            {
                if (this is Wz_Image.Wz_ImageNode)
                {
                    return (T)(object)(((Wz_Image.Wz_ImageNode)this).Image);
                }
                else
                {
                    return (this.value is T) ? (T)this.value : default(T);
                }
            }
            if (this.value == null)
                return defaultValue;
            if (this.value is T)
                return (T)this.value;


            if (this.value is string s)
            {
                if (ObjectConverter.TryParse<T>(s, out T result, out bool hasTryParse))
                {
                    return result;
                }
                if (hasTryParse)
                {
                    return defaultValue;
                }
            }

            if (this.value is IConvertible iconvertible)
            {
                if (typeT.IsGenericType && typeT.GetGenericTypeDefinition() == typeof(Nullable<>))
                {
                    typeT = typeT.GetGenericArguments()[0];
                }

                try
                {
                    T result = (T)iconvertible.ToType(typeT, null);
                    return result;
                }
                catch
                {
                }
            }
            return defaultValue;
        }

        public T GetValue<T>()
        {
            return GetValue<T>(default(T));
        }

        //innerClass
        public class WzNodeCollection : IEnumerable<Wz_Node>
        {
            public WzNodeCollection(Wz_Node owner)

            {
                this.owner = owner;
                this.innerCollection = null;
            }

            private readonly Wz_Node owner;
            private InnerCollection innerCollection;

            public Wz_Node this[int index]
            {
                get { return this.innerCollection?[index]; }
            }

            public Wz_Node this[string key]
            {
                get { return this.innerCollection?[key]; }
            }

            public int Count
            {
                get { return this.innerCollection?.Count ?? 0; }
            }

            public Wz_Node Add(string nodeText)
            {
                this.EnsureInnerCollection();
                return this.innerCollection.Add(nodeText);
            }

            public void Add(Wz_Node item)
            {
                this.EnsureInnerCollection();
                this.innerCollection.Add(item);
            }

            public void Sort()
            {
                this.innerCollection?.Sort();
            }

            public void Sort<T>(Func<Wz_Node, T> getKeyFunc) where T : IComparable<T>
            {
                if (getKeyFunc == null)
                {
                    this.Sort();
                }
                else if (this.innerCollection != null)
                {
                    this.innerCollection.Sort(getKeyFunc);
                }
            }

            public void Trim()
            {
                this.innerCollection?.Trim();
            }

            public void Clear()
            {
                this.innerCollection?.Clear();
            }

            public IEnumerator<Wz_Node> GetEnumerator()
            {
                return this.innerCollection?.GetEnumerator() ?? System.Linq.Enumerable.Empty<Wz_Node>().GetEnumerator();
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return this.GetEnumerator();
            }

            private void EnsureInnerCollection()
            {
                if (this.innerCollection == null)
                {
                    this.innerCollection = new InnerCollection(this.owner);
                }
            }

            private class InnerCollection : KeyedCollection<string, Wz_Node>
            {
                public InnerCollection(Wz_Node owner)
                    : base(null, 12)
                {
                    this.parentNode = owner;
                }

                public Wz_Node Add(string nodeText)
                {
                    Wz_Node newNode = new Wz_Node(nodeText);
                    this.Add(newNode);
                    return newNode;
                }

                public new void Add(Wz_Node item)
                {
                    base.Add(item);
                    if (item.parentNode != null)
                    {
                        int index = item.parentNode.nodes.innerCollection.Items.IndexOf(item);
                        if (index > -1)
                        {
                            item.parentNode.nodes.innerCollection.RemoveItem(index);
                        }
                    }
                    item.parentNode = this.parentNode;
                }

                protected override void RemoveItem(int index)
                {
                    var item = this[index];
                    if (item != null)
                    {
                        item.parentNode = null;
                    }
                    base.RemoveItem(index);
                }

                private readonly Wz_Node parentNode;

                public void Sort()
                {
                    (base.Items as List<Wz_Node>)?.Sort();
                }

                public void Sort<T>(Func<Wz_Node, T> getKeyFunc) where T : IComparable<T>
                {
                    ListSorter.Sort(base.Items as List<Wz_Node>, getKeyFunc);
                }

                public void Trim()
                {
                    (base.Items as List<Wz_Node>)?.TrimExcess();
                }

                public new Wz_Node this[string key]
                {
                    get
                    {
                        if (key == null)
                        {
                            return null;
                        }
                        if (this.Dictionary != null)
                        {
                            Wz_Node node;
                            this.Dictionary.TryGetValue(key, out node);
                            return node;
                        }
                        else
                        {
                            List<Wz_Node> list = this.Items as List<Wz_Node>;
                            foreach (var node in list)
                            {
                                if (this.Comparer.Equals(this.GetKeyForItem(node), key))
                                {
                                    return node;
                                }
                            }
                            return null;
                        }
                    }
                }

                protected override string GetKeyForItem(Wz_Node item)
                {
                    return item.text;
                }
            }

            internal static class ListSorter
            {
                public static void Sort<T, TKey>(List<T> list, Func<T, TKey> getKeyFunc)
                {
                    T[] innerArray = list.GetType()
                        .GetField("_items", BindingFlags.Instance | BindingFlags.NonPublic)
                        .GetValue(list) as T[];

                    TKey[] keys = new TKey[list.Count];

                    for (int i = 0; i < keys.Length; i++)
                    {
                        keys[i] = getKeyFunc(innerArray[i]);
                    }

                    Array.Sort(keys, innerArray, 0, keys.Length);
                }
            }
        }

        public object Clone()
        {
            Wz_Node newNode = new Wz_Node(this.text);
            newNode.value = this.value;
            foreach (Wz_Node node in this.nodes)
            {
                Wz_Node newChild = node.Clone() as Wz_Node;
                newNode.nodes.Add(newChild);
            }
            return newNode;
        }

        int IComparable.CompareTo(object obj)
        {
            return ((IComparable<Wz_Node>)this).CompareTo(obj as Wz_Node);
        }

        int IComparable<Wz_Node>.CompareTo(Wz_Node other)
        {
            if (other != null)
            {
                //return string.Compare(this.Text, other.Text, StringComparison.Ordinal);
                return StrCmpLogicalW(this.Text, other.Text);
            }
            else
            {
                return 1;
            }
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        static extern int StrCmpLogicalW(string psz1, string psz2);
    }

    public static class Wz_NodeExtension
    {
        public static T GetValueEx<T>(this Wz_Node node, T defaultValue)
        {
            if (node == null)
                return defaultValue;
            return node.GetValue<T>(defaultValue);
        }

        public static T? GetValueEx<T>(this Wz_Node node) where T : struct
        {
            if (node == null)
                return null;
            return node.GetValue<T>();
        }

        public static Wz_Node ResolveUol(this Wz_Node node)
        {
            if (node == null)
                return null;
            while (node?.Value is Wz_Uol uol)
            {
                node = uol.HandleUol(node);
            }
            return node;
        }

        /// <summary>
        /// 搜索node所属的wz_file，若搜索不到则返回null。
        /// </summary>
        /// <param Name="node">要搜索的wznode。</param>
        /// <returns></returns>
        public static Wz_File GetNodeWzFile(this Wz_Node node)
        {
            Wz_File wzfile = null;
            while (node != null)
            {
                if ((wzfile = node.Value as Wz_File) != null)
                {
                    while (wzfile.OwnerWzFile != null)
                    {
                        wzfile = wzfile.OwnerWzFile;
                        node = wzfile.Node;
                    }
                    if (!wzfile.IsSubDir)
                    {
                        return wzfile;
                    }
                }
                else if (node.Value is Wz_Image wzImg || (wzImg = (node as Wz_Image.Wz_ImageNode)?.Image) != null)
                {
                    switch (wzImg.WzFile)
                    {
                        case Wz_File wzfile2:
                            node = wzfile2.Node;
                            continue;

                        default:
                            if (node.ParentNode == null)
                            {
                                node = wzImg.OwnerNode;
                                continue;
                            }
                            break;
                    }
                }
                node = node.ParentNode;
            }
            return wzfile;
        }

        public static int GetMergedVersion(this Wz_File wzFile)
        {
            if (wzFile.Header.WzVersion != 0)
            {
                return wzFile.Header.WzVersion;
            }
            foreach (var subFile in wzFile.MergedWzFiles)
            {
                if (subFile.Header.WzVersion != 0)
                {
                    return subFile.Header.WzVersion;
                }
            }
            return 0;
        }

        public static Wz_Image GetNodeWzImage(this Wz_Node node)
        {
            Wz_Image wzImg = null;
            while (node != null)
            {
                if ((wzImg = node.Value as Wz_Image) != null
                    || (wzImg = (node as Wz_Image.Wz_ImageNode)?.Image) != null)
                {
                    break;
                }
                node = node.ParentNode;
            }
            return wzImg;
        }

        public static void DumpAsXml(this Wz_Node node, XmlWriter writer, string dir)
        {
            DumpAsXml(node, writer, dir, false, true, false);
        }

        public static void DumpAsXml(this Wz_Node node, XmlWriter writer, string dir, bool dumpRaw, bool dumpExt, bool leaveRef)
        {
            object value = node.Value;

            if (value == null || value is Wz_Image)
            {
                writer.WriteStartElement("dir");
                writer.WriteAttributeString("name", node.Text);
            }
            else if (value is Wz_Png png)
            {
                writer.WriteStartElement("png");
                writer.WriteAttributeString("name", node.Text);
                writer.WriteAttributeString("width", png.Width.ToString());
                writer.WriteAttributeString("height", png.Height.ToString());
                writer.WriteAttributeString("format", ((int)png.Format).ToString());
                writer.WriteAttributeString("scale", png.Scale.ToString());
                writer.WriteAttributeString("pages", png.Pages.ToString());
                for(int i = 0; i < png.ActualPages; i++)
                {
                    using (var bmp = png.ExtractPng())
                    {
                        using (var bmp = png.ExtractPng())
                        {
                            using (var ms = new MemoryStream())
                            {
                                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                                byte[] data = ms.ToArray();
                                string attrName = "value" + (i > 0 ? (i + 1).ToString() : null);
                                writer.WriteAttributeString(attrName, Convert.ToBase64String(data));
                            }
                        }
                    }
                }
                else if (dumpExt)
                {
                    for (int i = 0; i < png.ActualPages; i++)
                    {

                        using (var bmp = png.ExtractPng())
                        {
                            string path = dir + "\\" + node.ParentNode.FullPathToFile + "\\" + (i > 0 ? i.ToString() + "\\" : null);
                            if (!Directory.Exists(path)) 
                            {
                                Directory.CreateDirectory(path);
                            }

                            string fname = path + node.Text + ".png";
                            bmp.Save(fname);
                        }
                    }
                    if (leaveRef) writer.WriteAttributeString("file", node.FullPathToFile.Replace('\\', '.'));
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
                byte[] data = sound.ExtractSound();
                if (data == null)
                {
                    data = new byte[sound.DataLength];
                    sound.CopyTo(data, 0);
                }
                writer.WriteAttributeString("value", Convert.ToBase64String(data));
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
                byte[] data = new byte[rawdata.Length];
                rawdata.CopyTo(data, 0);
                writer.WriteAttributeString("value", Convert.ToBase64String(data));
            }
            else if (value is Wz_Video video)
            {
                writer.WriteStartElement("video");
                writer.WriteAttributeString("name", node.Text);
                writer.WriteAttributeString("length", video.Length.ToString());
                byte[] data = new byte[video.Length];
                video.CopyTo(data, 0);
                writer.WriteAttributeString("value", Convert.ToBase64String(data));
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
                DumpAsXml(child, writer, dir, dumpRaw, dumpExt, leaveRef);
            }

            //结束标识
            writer.WriteEndElement();
        }
        public static void DumpAsJson(this Wz_Node node, Utf8JsonWriter writer, string dir)
        {
            DumpAsJson(node, writer, dir, false, false, false);
        }

        public static void DumpAsJson(this Wz_Node node, Utf8JsonWriter writer, string dir, bool dumpRaw, bool dumpExt, bool leaveRef)
        {
            writer.WriteStartObject();
            WriteNodeProperty(node, writer, dir, dumpRaw, dumpExt, leaveRef);
            writer.WriteEndObject();
        }

        private static void WriteNodeProperty(Wz_Node node, Utf8JsonWriter writer, string dir, bool dumpRaw, bool dumpExt, bool leaveRef)
        {
            writer.WritePropertyName(node.Text);
            WriteNodeValue(node, writer, dir, dumpRaw, dumpExt, leaveRef);
        }

        private static void WriteNodeValue(Wz_Node node, Utf8JsonWriter writer, string dir, bool dumpRaw, bool dumpExt, bool leaveRef)
        {
            object value = node.Value;
            bool hasChildren = node.Nodes.Count > 0;

            if (value == null || value is Wz_Image)
            {
                WriteNodeAsObject(node, writer, dir, dumpRaw, dumpExt, leaveRef, null);
                return;
            }

            if (value is Wz_Png png)
            {
                List<string> exportedFiles = null;
                WriteNodeAsObject(node, writer, dir, dumpRaw, dumpExt, leaveRef, () =>
                {
                    writer.WriteString("type", "png");
                    if (!(png.Width == 1 && png.Height == 1))
                    {
                        writer.WriteNumber("format", (int)png.Format);
                    }
                    if (png.Scale != 0)
                    {
                        writer.WriteNumber("scale", png.Scale);
                    }
                    if (png.ActualPages >= 2)
                    {
                        writer.WriteNumber("pages", png.ActualPages);
                    }

                    if (dumpRaw)
                    {
                        WritePngRawData(writer, png);
                    }
                    else if (dumpExt || leaveRef)
                    {
                        exportedFiles = ExportPng(node, png, dir);
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
                WriteNodeAsObject(node, writer, dir, dumpRaw, dumpExt, leaveRef, () =>
                {
                    writer.WriteString("type", "uol");
                    writer.WriteString("value", uol.Uol);
                });
                return;
            }

            if (value is Wz_Vector vector)
            {
                WriteNodeAsObject(node, writer, dir, dumpRaw, dumpExt, leaveRef, () =>
                {
                    writer.WriteString("type", "vector");
                    writer.WriteString("value", $"{vector.X}, {vector.Y}");
                });
                return;
            }

            if (value is Wz_Sound sound)
            {
                List<string> exportedFiles = null;
                WriteNodeAsObject(node, writer, dir, dumpRaw, dumpExt, leaveRef, () =>
                {
                    writer.WriteString("type", "sound");
                    if (sound.DataLength > 0)
                    {
                        writer.WriteNumber("length", sound.DataLength);
                    }
                    if (sound.Ms > 0)
                    {
                        writer.WriteNumber("ms", sound.Ms);
                    }
                    if (sound.Channels > 0)
                    {
                        writer.WriteNumber("channels", sound.Channels);
                    }
                    if (sound.Frequency > 0)
                    {
                        writer.WriteNumber("frequency", sound.Frequency);
                    }

                    if (dumpRaw)
                    {
                        writer.WriteBase64String("data", GetSoundBytes(sound));
                    }
                    else if (dumpExt || leaveRef)
                    {
                        exportedFiles = ExportSound(node, sound, dir);
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
                WriteNodeAsObject(node, writer, dir, dumpRaw, dumpExt, leaveRef, () =>
                {
                    writer.WriteString("type", "convex");
                    writer.WritePropertyName("points");
                    writer.WriteStartArray();
                    foreach (var point in convex.Points)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("value", $"{point.X}, {point.Y}");
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                });
                return;
            }

            if (value is Wz_RawData rawData)
            {
                List<string> exportedFiles = null;
                WriteNodeAsObject(node, writer, dir, dumpRaw, dumpExt, leaveRef, () =>
                {
                    writer.WriteString("type", "rawdata");
                    writer.WriteNumber("length", rawData.Length);

                    if (dumpRaw)
                    {
                        writer.WriteBase64String("data", GetRawDataBytes(rawData));
                    }
                    else if (dumpExt || leaveRef)
                    {
                        exportedFiles = ExportRawData(node, rawData, dir);
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
                List<string> exportedFiles = null;
                WriteNodeAsObject(node, writer, dir, dumpRaw, dumpExt, leaveRef, () =>
                {
                    writer.WriteString("type", "video");
                    writer.WriteNumber("length", video.Length);

                    if (dumpRaw)
                    {
                        writer.WriteBase64String("data", GetVideoBytes(video));
                    }
                    else if (dumpExt || leaveRef)
                    {
                        exportedFiles = ExportVideo(node, video, dir);
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
                WritePrimitive(node, writer, dir, hasChildren, dumpRaw, dumpExt, leaveRef, () => writer.WriteStringValue(str), () => writer.WriteString("value", str));
                return;
            }

            if (value is bool boolean)
            {
                WritePrimitive(node, writer, dir, hasChildren, dumpRaw, dumpExt, leaveRef, () => writer.WriteBooleanValue(boolean), () => writer.WriteBoolean("value", boolean));
                return;
            }

            if (value is sbyte sb)
            {
                WritePrimitive(node, writer, dir, hasChildren, dumpRaw, dumpExt, leaveRef, () => writer.WriteNumberValue(sb), () => writer.WriteNumber("value", sb));
                return;
            }

            if (value is byte b)
            {
                WritePrimitive(node, writer, dir, hasChildren, dumpRaw, dumpExt, leaveRef, () => writer.WriteNumberValue(b), () => writer.WriteNumber("value", b));
                return;
            }

            if (value is short s)
            {
                WritePrimitive(node, writer, dir, hasChildren, dumpRaw, dumpExt, leaveRef, () => writer.WriteNumberValue(s), () => writer.WriteNumber("value", s));
                return;
            }

            if (value is ushort us)
            {
                WritePrimitive(node, writer, dir, hasChildren, dumpRaw, dumpExt, leaveRef, () => writer.WriteNumberValue(us), () => writer.WriteNumber("value", us));
                return;
            }

            if (value is int i)
            {
                WritePrimitive(node, writer, dir, hasChildren, dumpRaw, dumpExt, leaveRef, () => writer.WriteNumberValue(i), () => writer.WriteNumber("value", i));
                return;
            }

            if (value is uint ui)
            {
                WritePrimitive(node, writer, dir, hasChildren, dumpRaw, dumpExt, leaveRef, () => writer.WriteNumberValue(ui), () => writer.WriteNumber("value", ui));
                return;
            }

            if (value is long l)
            {
                WritePrimitive(node, writer, dir, hasChildren, dumpRaw, dumpExt, leaveRef, () => writer.WriteNumberValue(l), () => writer.WriteNumber("value", l));
                return;
            }

            if (value is ulong ul)
            {
                WritePrimitive(node, writer, dir, hasChildren, dumpRaw, dumpExt, leaveRef, () => writer.WriteNumberValue(ul), () => writer.WriteNumber("value", ul));
                return;
            }

            if (value is float f)
            {
                WritePrimitive(node, writer, dir, hasChildren, dumpRaw, dumpExt, leaveRef, () => writer.WriteNumberValue(f), () => writer.WriteNumber("value", f));
                return;
            }

            if (value is double d)
            {
                WritePrimitive(node, writer, dir, hasChildren, dumpRaw, dumpExt, leaveRef, () => writer.WriteNumberValue(d), () => writer.WriteNumber("value", d));
                return;
            }

            if (value is decimal dec)
            {
                WritePrimitive(node, writer, dir, hasChildren, dumpRaw, dumpExt, leaveRef, () => writer.WriteNumberValue(dec), () => writer.WriteNumber("value", dec));
                return;
            }

            WriteNodeAsObject(node, writer, dir, dumpRaw, dumpExt, leaveRef, () =>
            {
                writer.WriteString("type", value.GetType().Name.ToLowerInvariant());
                writer.WriteString("value", value.ToString());
            });
        }

        private static void WritePrimitive(Wz_Node node, Utf8JsonWriter writer, string dir, bool hasChildren, bool dumpRaw, bool dumpExt, bool leaveRef, Action writeValue, Action writeProperty)
        {
            if (!hasChildren)
            {
                writeValue();
                return;
            }

            WriteNodeAsObject(node, writer, dir, dumpRaw, dumpExt, leaveRef, () => writeProperty());
        }

        private static void WriteNodeAsObject(Wz_Node node, Utf8JsonWriter writer, string dir, bool dumpRaw, bool dumpExt, bool leaveRef, Action metadataWriter)
        {
            writer.WriteStartObject();
            metadataWriter?.Invoke();
            if (node.Nodes.Count > 0)
            {
                foreach (var child in node.Nodes)
                {
                    WriteNodeProperty(child, writer, dir, dumpRaw, dumpExt, leaveRef);
                }
            }
            writer.WriteEndObject();
        }

        private static void WritePngRawData(Utf8JsonWriter writer, Wz_Png png)
        {
            int pageCount = Math.Max(png.ActualPages, 1);
            if (pageCount <= 1)
            {
                using (var bmp = png.ExtractPng())
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    writer.WriteBase64String("data", ms.ToArray());
                }
                return;
            }

            writer.WritePropertyName("data");
            writer.WriteStartArray();
            for (int i = 0; i < pageCount; i++)
            {
                using (var bmp = png.ExtractPng(i))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    writer.WriteBase64StringValue(ms.ToArray());
                }
            }
            writer.WriteEndArray();
        }

        private static List<string> ExportPng(Wz_Node node, Wz_Png png, string dir)
        {
            var files = new List<string>();
            int pageCount = Math.Max(png.ActualPages, 1);
            string baseDir = EnsureExportDirectory(dir, node);

            for (int i = 0; i < pageCount; i++)
            {
                string targetDir = baseDir;
                if (pageCount > 1 && i > 0)
                {
                    targetDir = Path.Combine(targetDir, i.ToString());
                    Directory.CreateDirectory(targetDir);
                }

                string filePath = Path.Combine(targetDir, node.Text + ".png");
                using (var bmp = png.ExtractPng(i))
                {
                    bmp.Save(filePath);
                }
                files.Add(ToRelativeExportPath(dir, filePath));
            }

            return files;
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

        private static List<string> ExportSound(Wz_Node node, Wz_Sound sound, string dir)
        {
            string extension = sound.SoundType switch
            {
                Wz_SoundType.Mp3 => ".mp3",
                Wz_SoundType.Pcm => ".wav",
                Wz_SoundType.Binary => ".bin",
                _ => ".bin",
            };

            byte[] data = GetSoundBytes(sound);
            string exportDir = EnsureExportDirectory(dir, node);
            string filePath = Path.Combine(exportDir, node.Text + extension);
            File.WriteAllBytes(filePath, data);

            return new List<string> { ToRelativeExportPath(dir, filePath) };
        }

        private static byte[] GetRawDataBytes(Wz_RawData rawData)
        {
            byte[] data = new byte[rawData.Length];
            rawData.CopyTo(data, 0);
            return data;
        }

        private static List<string> ExportRawData(Wz_Node node, Wz_RawData rawData, string dir)
        {
            byte[] data = GetRawDataBytes(rawData);
            string exportDir = EnsureExportDirectory(dir, node);
            string filePath = Path.Combine(exportDir, node.Text + ".bin");
            File.WriteAllBytes(filePath, data);
            return new List<string> { ToRelativeExportPath(dir, filePath) };
        }

        private static byte[] GetVideoBytes(Wz_Video video)
        {
            byte[] data = new byte[video.Length];
            video.CopyTo(data, 0);
            return data;
        }

        private static List<string> ExportVideo(Wz_Node node, Wz_Video video, string dir)
        {
            byte[] data = GetVideoBytes(video);
            string exportDir = EnsureExportDirectory(dir, node);
            string filePath = Path.Combine(exportDir, node.Text + ".mcv");
            File.WriteAllBytes(filePath, data);
            return new List<string> { ToRelativeExportPath(dir, filePath) };
        }

        private static string EnsureExportDirectory(string dir, Wz_Node node)
        {
            string exportDir = string.IsNullOrEmpty(dir) ? Directory.GetCurrentDirectory() : dir;
            string parentPath = node.ParentNode?.FullPathToFile;
            if (!string.IsNullOrEmpty(parentPath))
            {
                exportDir = Path.Combine(exportDir, parentPath.Replace('\\', Path.DirectorySeparatorChar));
            }

            Directory.CreateDirectory(exportDir);
            return exportDir;
        }

        private static string ToRelativeExportPath(string baseDir, string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return filePath;
            }

            if (string.IsNullOrEmpty(baseDir))
            {
                return filePath.Replace('\\', '/');
            }

            if (!baseDir.EndsWith(Path.DirectorySeparatorChar.ToString()) && !baseDir.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
            {
                baseDir += Path.DirectorySeparatorChar;
            }

            var baseUri = new Uri(baseDir);
            var fileUri = new Uri(filePath);
            string relative = Uri.UnescapeDataString(baseUri.MakeRelativeUri(fileUri).ToString());
            return relative.Replace('\\', '/');
        }

        public static void SortByImgID(this Wz_Node.WzNodeCollection nodes)
        {
            if (regexImgID == null)
            {
                regexImgID = new Regex(@"^(\d+)\.img$", RegexOptions.Compiled);
            }

            nodes.Sort(GetKey);
        }

        private static Regex regexImgID;

        private static SortKey GetKey(Wz_Node node)
        {
            var key = new SortKey();
            var m = regexImgID.Match(node.Text);
            if (m.Success)
            {
                key.HasID = Int32.TryParse(m.Result("$1"), out key.ImgID);
            }
            key.Text = node.Text;
            return key;
        }

        private struct SortKey : IComparable<SortKey>
        {
            public bool HasID;
            public int ImgID;
            public string Text;

            public int CompareTo(SortKey other)
            {
                if (this.HasID && other.HasID) return this.ImgID.CompareTo(other.ImgID);
                return StringComparer.Ordinal.Compare(this.Text, other.Text);
            }
        }
    }

    public static class ObjectConverter
    {
        private static readonly Dictionary<Type, Delegate> cache = new Dictionary<Type, Delegate>();
        private delegate bool TryParseFunc<T>(string s, out T value);

        public static bool TryParse<T>(string s, out T value, out bool hasTryParse)
        {
            var typeT = typeof(T);

            TryParseFunc<T> tryParseFunc = null;
            if (!cache.TryGetValue(typeT, out var dele))
            {
                bool isNullable = false;
                Type innerType;
                if (typeT.IsGenericType && typeT.GetGenericTypeDefinition() == typeof(Nullable<>))
                {
                    isNullable = true;
                    innerType = typeT.GetGenericArguments()[0];
                }
                else
                {
                    innerType = typeT;
                }

                var methodInfo = innerType.GetMethod("TryParse",
                    BindingFlags.Static | BindingFlags.Public,
                    null,
                    new[] { typeof(string), innerType.MakeByRefType() },
                    null);

                if (methodInfo != null && methodInfo.ReturnType == typeof(bool))
                {
                    if (isNullable)
                    {
                        dele = Delegate.CreateDelegate(typeof(TryParseFunc<>).MakeGenericType(innerType), methodInfo);
                        var proxyType = typeof(NullableTryParse<>).MakeGenericType(innerType);
                        var proxyInstance = Activator.CreateInstance(proxyType, dele);
                        var proxyParseFunc = proxyType.GetMethod("TryParse", BindingFlags.Public | BindingFlags.Instance);
                        cache[typeT] = tryParseFunc = (TryParseFunc<T>)Delegate.CreateDelegate(typeof(TryParseFunc<T>), proxyInstance, proxyParseFunc);
                    }
                    else
                    {
                        cache[typeT] = tryParseFunc = (TryParseFunc<T>)Delegate.CreateDelegate(typeof(TryParseFunc<T>), methodInfo);
                    }
                }
                else
                {
                    cache[typeT] = null;
                }
            }
            else
            {
                tryParseFunc = dele as TryParseFunc<T>;
            }

            if (tryParseFunc != null)
            {
                hasTryParse = true;
                return tryParseFunc(s, out value);
            }
            else
            {
                hasTryParse = false;
                value = default(T);
                return false;
            }
        }

        private class NullableTryParse<T> where T : struct
        {
            public NullableTryParse(TryParseFunc<T> func)
            {
                this.func = func;
            }

            private readonly TryParseFunc<T> func;

            public bool TryParse(string s, out T? value)
            {
                if (this.func(s, out var v))
                {
                    value = v;
                    return true;
                }
                else
                {
                    value = default(T?);
                    return false;
                }
            }
        }
    }
}