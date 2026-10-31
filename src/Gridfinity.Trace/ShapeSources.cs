using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gridfinity.Core;

namespace Gridfinity.Trace
{
    /// <summary>Reads outlines from any supported file: a photo or scan (traced here), or a DXF, SVG or ZIP.</summary>
    public static class ShapeSources
    {
        public static readonly string FileFilter =
            "Photos and outlines|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff;*.webp;*.dxf;*.svg;*.zip|" +
            "Photos and scans (*.jpg;*.png;*.tif;...)|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff;*.webp|" +
            "Outlines (*.dxf;*.svg;*.zip)|*.dxf;*.svg;*.zip|All files (*.*)|*.*";

        public static bool IsSupported(string path)
        {
            string e = (Path.GetExtension(path) ?? "").ToLowerInvariant();
            return PhotoTracer.IsImage(path) || e == ".dxf" || e == ".svg" || e == ".zip";
        }

        /// <summary>The shapes in one file. For a photo, <paramref name="overlayPath"/> receives the tracing preview.</summary>
        public static List<TracedShape> Read(string path, OutlineImportOptions o, List<string> notes, out string overlayPath)
        {
            overlayPath = null;
            if (PhotoTracer.IsImage(path))
            {
                var r = PhotoTracer.Trace(path, o, notes);
                overlayPath = r.OverlayPath;
                return r.Shapes;
            }
            return OutlineImporter.ReadFile(path, notes);
        }

        public static List<TracedShape> Read(string path, OutlineImportOptions o, List<string> notes)
        {
            string ignored;
            return Read(path, o, notes, out ignored);
        }
    }
}
