using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FakeLock
{
    public class CardPanel : Panel
    {
        public CardPanel()
        {
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = new Rectangle(1, 1, this.Width - 3, this.Height - 3);
            int radius = 16;
            using (GraphicsPath path = GetRoundedPath(rect, radius))
            {
                using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(24, 30, 42)))
                {
                    g.FillPath(bgBrush, path);
                }

                using (Pen borderPen = new Pen(Color.FromArgb(0, 180, 220), 2f))
                {
                    g.DrawPath(borderPen, path);
                }
            }
        }

        private GraphicsPath GetRoundedPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
