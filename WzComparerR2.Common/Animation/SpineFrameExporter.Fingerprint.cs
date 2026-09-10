using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xna.Framework.Graphics;
using WzComparerR2.Common;
using WzComparerR2.WzLib;

namespace WzComparerR2.Animation
{
    public static partial class SpineFrameExporter
    {
        private static string Fingerprint(Wz_Node group, int fps, GlobalFindNodeFunction findNode)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            void Text(string value)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(value ?? "");
                hash.AppendData(BitConverter.GetBytes(bytes.Length));
                hash.AppendData(bytes);
            }
            Text("spine-raster-v2");
            Text(fps.ToString(CultureInfo.InvariantCulture));
            foreach (var type in new[] { typeof(SpineFrameExporter), typeof(Wz_Png), typeof(Spine.Skeleton), typeof(Texture2D), typeof(System.Drawing.Bitmap) })
                Text(type.Module.ModuleVersionId.ToString());
            Text(Environment.Version.ToString());
            var visiting = new HashSet<Wz_Node>();
            void Visit(Wz_Node node)
            {
                if (!visiting.Add(node)) throw new InvalidOperationException("Cyclic Spine source: " + node.FullPathToFile);
                try
                {
                    Text(node.Text);
                    Text(node.Value?.GetType().FullName);
                    if (node.Value is Wz_Uol)
                    {
                        var resolved = node.ResolveUol();
                        if (resolved == null) throw new InvalidOperationException("Unresolved Spine UOL: " + node.FullPathToFile);
                        Visit(resolved);
                    }
                    else if (node.Value is Wz_Png)
                    {
                        var source = node.GetLinkedSourceNode(findNode) ?? node;
                        if (!(source.Value is Wz_Png png)) throw new InvalidOperationException("Invalid Spine texture: " + node.FullPathToFile);
                        Text(png.Width.ToString(CultureInfo.InvariantCulture));
                        Text(png.Height.ToString(CultureInfo.InvariantCulture));
                        Text(png.Format.ToString());
                        Text(png.Scale.ToString(CultureInfo.InvariantCulture));
                        byte[] pixels = png.GetRawData();
                        hash.AppendData(BitConverter.GetBytes(pixels.Length));
                        hash.AppendData(pixels);
                    }
                    else if (node.Value is IMapleStoryBlob blob)
                    {
                        var bytes = new byte[blob.Length];
                        blob.CopyTo(bytes, 0);
                        hash.AppendData(BitConverter.GetBytes(bytes.Length));
                        hash.AppendData(bytes);
                    }
                    else if (node.Value is Wz_Vector vector)
                    {
                        Text(vector.X.ToString(CultureInfo.InvariantCulture));
                        Text(vector.Y.ToString(CultureInfo.InvariantCulture));
                    }
                    else Text(Convert.ToString(node.Value, CultureInfo.InvariantCulture));
                    foreach (var child in node.Nodes) Visit(child);
                    Text("end-node");
                }
                finally { visiting.Remove(node); }
            }
            Visit(group);
            return Convert.ToHexString(hash.GetHashAndReset());
        }
    }
}
