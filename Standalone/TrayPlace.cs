using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Toolkit.Standalone
{
    /// <summary>
    /// Where the toolkit's tray icon is on screen (device pixels), for the settings window's closing
    /// flight. Asked of the shell by the icon's window and id, which WinForms keeps private. An icon
    /// tucked away in the overflow has no place of its own on the taskbar: then the start of the
    /// notification area (where the overflow arrow sits) stands in, and failing that too the corner
    /// of the work area.
    /// </summary>
    internal static class TrayPlace
    {
        public static Rect? Of(Forms.NotifyIcon tray)
        {
            try
            {
                Rect? area = NotifyArea();
                Rect? icon = IconRect(tray);
                if (icon != null && (area == null || area.Value.Contains(Centre(icon.Value)))) return icon;
                if (area != null)
                {
                    var a = area.Value;
                    // A horizontal taskbar: the arrow is at the area's near end. A vertical one: at its top.
                    return a.Width >= a.Height
                        ? new Rect(a.Left, a.Top + (a.Height - 24) / 2, 24, 24)
                        : new Rect(a.Left + (a.Width - 24) / 2, a.Top, 24, 24);
                }
                var work = Forms.Screen.PrimaryScreen.WorkingArea;
                return new Rect(work.Right - 40, work.Bottom - 8, 24, 24);
            }
            catch { return null; }
        }

        private static Point Centre(Rect r) => new Point(r.Left + r.Width / 2, r.Top + r.Height / 2);

        private static Rect? IconRect(Forms.NotifyIcon tray)
        {
            try
            {
                const BindingFlags hidden = BindingFlags.NonPublic | BindingFlags.Instance;
                var window = typeof(Forms.NotifyIcon).GetField("window", hidden)?.GetValue(tray) as Forms.NativeWindow;
                object id = typeof(Forms.NotifyIcon).GetField("id", hidden)?.GetValue(tray);
                if (window == null || id == null || window.Handle == IntPtr.Zero) return null;
                var which = new NOTIFYICONIDENTIFIER { cbSize = Marshal.SizeOf(typeof(NOTIFYICONIDENTIFIER)), hWnd = window.Handle, uID = Convert.ToUInt32(id) };
                RECT r;
                if (Shell_NotifyIconGetRect(ref which, out r) != 0) return null;
                if (r.Right <= r.Left || r.Bottom <= r.Top) return null;
                return new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            }
            catch { return null; }
        }

        private static Rect? NotifyArea()
        {
            try
            {
                IntPtr bar = FindWindow("Shell_TrayWnd", null);
                if (bar == IntPtr.Zero) return null;
                IntPtr area = FindWindowEx(bar, IntPtr.Zero, "TrayNotifyWnd", null);
                RECT r;
                if (area == IntPtr.Zero || !GetWindowRect(area, out r) || r.Right <= r.Left || r.Bottom <= r.Top) return null;
                return new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            }
            catch { return null; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct NOTIFYICONIDENTIFIER
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uID;
            public Guid guidItem;
        }

        [DllImport("shell32.dll")]
        private static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string windowName);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string windowName);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    }
}
