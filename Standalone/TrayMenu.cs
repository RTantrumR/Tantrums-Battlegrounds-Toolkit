using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace Toolkit.Standalone
{
    /// <summary>
    /// The tray icons' right-click menus in the toolkit's own colours (the settings window's panel,
    /// border and gold), instead of Windows' light default. WinForms only, no WPF, so the watcher's
    /// menu can use it too and its process stays small. <c>TrayMenu.Create()</c> gives the menu,
    /// <c>TrayMenu.Add(menu, text, click)</c> an entry. On Windows 11 the menu gets the system's
    /// rounded corners and shadow with a border in the toolkit's colour; elsewhere a square border.
    /// </summary>
    internal static class TrayMenu
    {
        private static readonly Color Panel = Color.FromArgb(0x16, 0x1C, 0x28);
        private static readonly Color Rim = Color.FromArgb(0x39, 0x47, 0x5E);
        private static readonly Color Hover = Color.FromArgb(0x24, 0x2E, 0x42);
        private static readonly Color Text = Color.FromArgb(0xF2, 0xF5, 0xFA);
        private static readonly Color Accent = Color.FromArgb(0xE8, 0xB5, 0x4B);

        private static bool _systemRounds;   // set once the system took the rounded corners

        public static Forms.ContextMenuStrip Create()
        {
            var menu = new Forms.ContextMenuStrip
            {
                Renderer = new Renderer(), ShowImageMargin = false, ShowCheckMargin = false,
                BackColor = Panel, ForeColor = Text, Font = new Font("Segoe UI", 10f),
                Padding = new Forms.Padding(4, 5, 4, 5), DropShadowEnabled = true
            };
            menu.HandleCreated += (s, e) => Round(menu.Handle);
            return menu;
        }

        public static Forms.ToolStripMenuItem Add(Forms.ContextMenuStrip menu, string text, EventHandler click)
        {
            var item = new Forms.ToolStripMenuItem(text, null, click)
            {
                ForeColor = Text, Padding = new Forms.Padding(8, 5, 28, 5), Margin = new Forms.Padding(0, 1, 0, 1)
            };
            menu.Items.Add(item);
            return item;
        }

        /// <summary>Test aid: draw the menu into a PNG without showing it, item <paramref name="hovered"/>
        /// as under the cursor. Returns what it did.</summary>
        public static string Render(Forms.ContextMenuStrip menu, string path, int hovered)
        {
            try
            {
                var size = menu.GetPreferredSize(Size.Empty);
                menu.Size = size;
                menu.PerformLayout();
                if (hovered >= 0 && hovered < menu.Items.Count) menu.Items[hovered].Select();
                using (var bmp = new Bitmap(size.Width, size.Height))
                {
                    menu.DrawToBitmap(bmp, new Rectangle(Point.Empty, size));
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
                return "saved " + size.Width + "x" + size.Height + " to " + path;
            }
            catch (Exception ex) { return "failed: " + ex.Message; }
        }

        // Windows 11: rounded corners (DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUNDSMALL = 3) and
        // the border's colour (DWMWA_BORDER_COLOR = 34, COLORREF 0x00BBGGRR). Older Windows refuses both.
        private static void Round(IntPtr hwnd)
        {
            try
            {
                int corner = 3;
                if (DwmSetWindowAttribute(hwnd, 33, ref corner, 4) == 0)
                {
                    _systemRounds = true;
                    int border = Rim.R | (Rim.G << 8) | (Rim.B << 16);
                    DwmSetWindowAttribute(hwnd, 34, ref border, 4);
                }
            }
            catch { }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private sealed class Renderer : Forms.ToolStripRenderer
        {
            protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e) => e.Graphics.Clear(Panel);

            protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
            {
                if (_systemRounds) return;   // the system draws it, rounded
                using (var pen = new Pen(Rim))
                    e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            }

            protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
            {
                if (!e.Item.Selected || !e.Item.Enabled) return;
                // Within the menu's inner width: an item can be laid out wider than the menu it sits in.
                int w = Math.Min(e.Item.Width, e.ToolStrip.DisplayRectangle.Right - e.Item.Bounds.X);
                var r = new Rectangle(2, 0, w - 4, e.Item.Height);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = RoundRect(r, 5))
                using (var fill = new SolidBrush(Hover))
                    e.Graphics.FillPath(fill, path);
            }

            protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Selected && e.Item.Enabled ? Accent : Text;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
            {
                int y = e.Item.Height / 2;
                using (var pen = new Pen(Rim))
                    e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
            }

            private static GraphicsPath RoundRect(Rectangle r, int radius)
            {
                int d = radius * 2;
                var p = new GraphicsPath();
                p.AddArc(r.X, r.Y, d, d, 180, 90);
                p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                p.CloseFigure();
                return p;
            }
        }
    }
}
