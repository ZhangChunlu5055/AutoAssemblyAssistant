using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace AutoAssemblyAddin.UI;

internal static class TaskPaneImages
{
    public static string[] Create()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoAssemblyAddin", "icons");
        Directory.CreateDirectory(directory);
        return new[] { 20, 32, 40, 64, 96, 128 }.Select(size =>
        {
            var path = Path.Combine(directory, $"assistant-{size}.png");
            if (!File.Exists(path))
            {
                using var bitmap = new Bitmap(size, size);
                using var graphics = Graphics.FromImage(bitmap);
                using var brush = new SolidBrush(Color.FromArgb(35, 94, 180));
                using var pen = new Pen(Color.White, Math.Max(1, size / 14F));
                graphics.Clear(Color.FromArgb(192, 192, 192));
                graphics.FillRectangle(brush, size * .1F, size * .15F, size * .8F, size * .65F);
                graphics.FillPolygon(brush, new[] { new PointF(size * .2F, size * .7F), new PointF(size * .2F, size * .95F), new PointF(size * .45F, size * .7F) });
                graphics.DrawLine(pen, size * .27F, size * .37F, size * .73F, size * .37F);
                graphics.DrawLine(pen, size * .27F, size * .57F, size * .57F, size * .57F);
                bitmap.Save(path, ImageFormat.Png);
            }
            return path;
        }).ToArray();
    }
}
