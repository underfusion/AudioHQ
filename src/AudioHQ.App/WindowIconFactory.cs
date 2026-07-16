using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using AudioHQ.Core;

namespace AudioHQ.App;

public static class WindowIconFactory
{
    public static BitmapSource? Build(bool dot)
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var name = Array.Find(asm.GetManifestResourceNames(),
                n => n.EndsWith("app.ico", StringComparison.OrdinalIgnoreCase));
            System.Drawing.Icon? baseIcon = null;
            if (name is not null)
            {
                using var stream = asm.GetManifestResourceStream(name);
                if (stream is not null) baseIcon = new System.Drawing.Icon(stream, 32, 32);
            }

            using var bmp = new System.Drawing.Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.Transparent);
                if (baseIcon is not null)
                {
                    using var baseBmp = baseIcon.ToBitmap();
                    g.DrawImage(baseBmp, 0, 0, 32, 32);
                }
                if (dot)
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    // The app's own green, not GDI's LimeGreen - this dot means the same
                    // "on" as the ON pill and must not be a different green from it.
                    using var dotBrush = new System.Drawing.SolidBrush(ThemeResources.DrawingColor("Color.Green"));
                    g.FillEllipse(dotBrush, 20, 20, 11, 11);
                }
            }
            baseIcon?.Dispose();

            var hBmp = bmp.GetHbitmap();
            try
            {
                return Imaging.CreateBitmapSourceFromHBitmap(
                    hBmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            }
            finally
            {
                DeleteObject(hBmp);
            }
        }
        catch (Exception ex)
        {
            Log.Write($"WindowIconFactory.Build(dot={dot}) failed: {ex.Message}");
            return null;
        }
    }

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
