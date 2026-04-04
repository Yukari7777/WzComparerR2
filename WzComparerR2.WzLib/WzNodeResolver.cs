using System;
using System.Collections.Generic;

namespace WzComparerR2.WzLib
{
    public static class WzNodeResolver
    {
        public static bool TryResolveExactPath(Wz_Node root, string fullPath, out ResolvedNode resolvedNode, out Exception error)
        {
            resolvedNode = null;
            error = null;

            var extractedImages = new List<Wz_Image>();
            try
            {
                if (root == null)
                {
                    throw new ArgumentNullException(nameof(root));
                }

                if (string.IsNullOrWhiteSpace(fullPath))
                {
                    throw new ArgumentException("Path cannot be empty.", nameof(fullPath));
                }

                string[] pathSegments = fullPath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (pathSegments.Length == 0)
                {
                    throw new ArgumentException("Path cannot be empty.", nameof(fullPath));
                }

                Wz_Node currentNode = root;
                Wz_Image rootImage = null;

                for (int i = 0; i < pathSegments.Length; i++)
                {
                    string segment = pathSegments[i];
                    Wz_Node childNode = FindChildNode(currentNode, segment);
                    if (childNode == null)
                    {
                        throw new KeyNotFoundException($"Path segment not found: {segment}");
                    }

                    Wz_Image image = childNode.GetValue<Wz_Image>();
                    if (image != null)
                    {
                        if (!image.TryExtract(out var extractError))
                        {
                            error = extractError ?? new InvalidOperationException($"Failed to extract image '{image.Name}'.");
                            Cleanup(extractedImages);
                            return false;
                        }

                        if (!extractedImages.Contains(image))
                        {
                            extractedImages.Add(image);
                        }

                        rootImage ??= image;
                        currentNode = image.Node;
                    }
                    else
                    {
                        currentNode = childNode;
                    }
                }

                resolvedNode = new ResolvedNode(currentNode, rootImage, extractedImages);
                return true;
            }
            catch (Exception ex)
            {
                Cleanup(extractedImages);
                error = ex;
                return false;
            }
        }

        private static Wz_Node FindChildNode(Wz_Node parentNode, string name)
        {
            foreach (var childNode in parentNode.Nodes)
            {
                if (string.Equals(childNode.Text, name, StringComparison.OrdinalIgnoreCase))
                {
                    return childNode;
                }
            }

            return null;
        }

        private static void Cleanup(List<Wz_Image> extractedImages)
        {
            for (int i = extractedImages.Count - 1; i >= 0; i--)
            {
                extractedImages[i].Unextract();
            }
        }

        public sealed class ResolvedNode : IDisposable
        {
            private readonly IReadOnlyList<Wz_Image> extractedImages;
            private bool disposed;

            internal ResolvedNode(Wz_Node node, Wz_Image rootImage, IReadOnlyList<Wz_Image> extractedImages)
            {
                this.Node = node;
                this.RootImage = rootImage;
                this.extractedImages = extractedImages;
            }

            public Wz_Node Node { get; }

            public Wz_Image RootImage { get; }

            public bool IsInsideImage => this.RootImage != null;

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                for (int i = extractedImages.Count - 1; i >= 0; i--)
                {
                    extractedImages[i].Unextract();
                }

                disposed = true;
            }
        }
    }
}