using System.Drawing;
using System.Drawing.Drawing2D;

namespace Shiftline.Plugin
{
    /// <summary>Draws the left menu icon in code so the project needs no image resource.</summary>
    internal static class IconFactory
    {
        /// <summary>A horizontal traffic light (rounded bar with three dots), white on transparent, 24x24.</summary>
        public static Bitmap Create()
        {
            Bitmap bitmap = new Bitmap(24, 24);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (SolidBrush brush = new SolidBrush(Color.White))
            using (Pen pen = new Pen(Color.White, 2f))
            using (GraphicsPath body = RoundedRectangle(2f, 6f, 20f, 12f, 6f))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                graphics.DrawPath(pen, body);
                foreach (float x in new[] { 7f, 12f, 17f })
                {
                    graphics.FillEllipse(brush, x - 2f, 10f, 4f, 4f);
                }
            }

            return bitmap;
        }

        private static GraphicsPath RoundedRectangle(float x, float y, float width, float height, float radius)
        {
            float d = radius * 2f;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + width - d, y, d, d, 270, 90);
            path.AddArc(x + width - d, y + height - d, d, d, 0, 90);
            path.AddArc(x, y + height - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
