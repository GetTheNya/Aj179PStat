using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Aj179PStat
{
    public static class IconGenerator
    {
        public static Bitmap CreateBatteryBitmap(BatteryStatus status)
        {
            const int width = 32;
            const int height = 32;

            Bitmap bitmap = new Bitmap(width, height);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.Clear(Color.Transparent);

                Color textColor = Color.White;
                Color bgColor;
                Color borderColor;

                if (!status.IsConnected)
                {
                    bgColor = Color.FromArgb(80, 80, 80);
                    borderColor = Color.FromArgb(120, 120, 120);
                }
                else if (!status.IsMouseActive)
                {
                    // Asleep / Idle (data[4] == 1): Neutral Slate Gray badge showing last reported percentage
                    bgColor = Color.FromArgb(95, 105, 115);
                    borderColor = Color.FromArgb(145, 155, 165);
                }
                else if (status.BatteryPercent > 50)
                {
                    bgColor = Color.FromArgb(34, 139, 34); // Forest Green
                    borderColor = Color.FromArgb(50, 205, 50); // Lime Green
                }
                else if (status.BatteryPercent > 20)
                {
                    bgColor = Color.FromArgb(218, 120, 0); // Dark Orange
                    borderColor = Color.FromArgb(255, 165, 0); // Orange
                }
                else
                {
                    bgColor = Color.FromArgb(178, 34, 34); // Firebrick Red
                    borderColor = Color.FromArgb(220, 20, 60); // Crimson
                }

                // Draw rounded background badge
                using (GraphicsPath path = GetRoundedRectPath(new Rectangle(0, 0, width - 1, height - 1), 5))
                {
                    using SolidBrush bgBrush = new SolidBrush(bgColor);
                    g.FillPath(bgBrush, path);

                    using Pen borderPen = new Pen(borderColor, 1.5f);
                    g.DrawPath(borderPen, path);
                }

                string text = !status.IsConnected ? "?" : status.BatteryPercent.ToString();

                // Maximized font sizes for maximum readability in system tray
                float fontSize = text.Length switch
                {
                    1 => 22f,
                    2 => 18f,
                    _ => 13f
                };

                using Font font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
                using SolidBrush textBrush = new SolidBrush(textColor);
                using StringFormat sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };

                // Draw centered text filling the icon canvas
                RectangleF textRect = new RectangleF(0, 0, width, height + 1);
                g.DrawString(text, font, textBrush, textRect, sf);
            }

            return bitmap;
        }

        private static GraphicsPath GetRoundedRectPath(Rectangle rect, int cornerRadius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = cornerRadius * 2;
            Rectangle arc = new Rectangle(rect.X, rect.Y, diameter, diameter);

            // top left arc
            path.AddArc(arc, 180, 90);

            // top right arc
            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270, 90);

            // bottom right arc
            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0, 90);

            // bottom left arc
            arc.X = rect.Left;
            path.AddArc(arc, 90, 90);

            path.CloseFigure();
            return path;
        }
    }
}
