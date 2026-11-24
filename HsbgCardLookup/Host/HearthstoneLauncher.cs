using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace HsbgCardLookup.Host
{
    /// <summary>
    /// Starts Hearthstone through Battle.net, the way its Play button does: <c>Battle.net.exe
    /// --exec="launch WTCG"</c> (HDT's "Start Hearthstone" works the same way). When Battle.net is not
    /// running it is opened first through its <c>battlenet://</c> link, which needs no install path.
    ///
    /// Battle.net drops or holds the command while it is still starting and logging in (seen
    /// 2026-11-24: the first command of a cold start did nothing; queued ones ran only after the
    /// login), so it is sent again every <see cref="ResendEvery"/> until the Hearthstone process
    /// exists, then the game's window is waited for. Nothing is written or downloaded: it only
    /// starts Blizzard's own launcher.
    /// </summary>
    internal static class HearthstoneLauncher
    {
        public static Action<string> Log;

        private static readonly TimeSpan ResendEvery = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(3);
        private static readonly TimeSpan WindowWait = TimeSpan.FromMinutes(2);
        private static int _running;   // one launch at a time

        /// <summary>Any thread. True once <paramref name="windowUp"/> says the game's window is there.</summary>
        public static async Task<bool> StartAsync(Func<bool> windowUp, CancellationToken cancel)
        {
            if (Interlocked.Exchange(ref _running, 1) == 1) return false;
            try { return await Task.Run(() => Run(windowUp, cancel)).ConfigureAwait(false); }
            finally { Interlocked.Exchange(ref _running, 0); }
        }

        private static bool Run(Func<bool> windowUp, CancellationToken cancel)
        {
            if (SafeWindowUp(windowUp)) return true;
            var started = DateTime.UtcNow;

            if (!GameProcessUp())
            {
                if (BattleNetPath() == null)
                {
                    Say("Battle.net is not running: opening it through battlenet://");
                    try { Process.Start("battlenet://"); }
                    catch (Exception ex) { Say("could not open battlenet://: " + ex.Message); }
                }

                DateTime lastSent = DateTime.MinValue;
                int sent = 0;
                while (!GameProcessUp())
                {
                    if (cancel.IsCancellationRequested) return false;
                    if (DateTime.UtcNow - started > GiveUpAfter) { Say("gave up: Hearthstone did not start within " + GiveUpAfter.TotalMinutes + " min"); return false; }
                    if (DateTime.UtcNow - lastSent >= ResendEvery)
                    {
                        string exe = BattleNetPath() ?? InstalledBattleNet();
                        if (exe != null)
                        {
                            try
                            {
                                Process.Start(new ProcessStartInfo(exe, "--exec=\"launch WTCG\"") { UseShellExecute = false });
                                sent++;
                                Say("sent \"launch WTCG\" to " + exe + " (#" + sent + ")");
                            }
                            catch (Exception ex) { Say("could not start " + exe + ": " + ex.Message); }
                            lastSent = DateTime.UtcNow;
                        }
                    }
                    if (cancel.WaitHandle.WaitOne(1000)) return false;
                }
                Say("Hearthstone process is up after " + (int)(DateTime.UtcNow - started).TotalSeconds + " s");
            }

            var windowFrom = DateTime.UtcNow;
            while (!SafeWindowUp(windowUp))
            {
                if (DateTime.UtcNow - windowFrom > WindowWait || !GameProcessUp()) { Say("Hearthstone's window did not come up"); return false; }
                if (cancel.WaitHandle.WaitOne(500)) return false;
            }
            Say("Hearthstone's window is up after " + (int)(DateTime.UtcNow - started).TotalSeconds + " s");
            return true;
        }

        private static bool SafeWindowUp(Func<bool> windowUp)
        {
            try { return windowUp(); } catch { return false; }
        }

        private static bool GameProcessUp()
        {
            var ps = Process.GetProcessesByName("Hearthstone");
            foreach (var p in ps) p.Dispose();
            return ps.Length > 0;
        }

        // The running launcher's own exe (its helper processes share the name; any of them gives the path).
        private static string BattleNetPath()
        {
            var ps = Process.GetProcessesByName("Battle.net");
            try
            {
                foreach (var p in ps)
                {
                    try { string f = p.MainModule?.FileName; if (!string.IsNullOrEmpty(f)) return f; }
                    catch { /* elevated or exiting: try the next one */ }
                }
                return null;
            }
            finally { foreach (var p in ps) p.Dispose(); }
        }

        // Where Battle.net is installed, for when its running process can't be read: its uninstall entry.
        private static string InstalledBattleNet()
        {
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                try
                {
                    using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Battle.net"))
                    {
                        string dir = key?.GetValue("InstallLocation") as string;
                        if (string.IsNullOrEmpty(dir)) continue;
                        string exe = System.IO.Path.Combine(dir.Trim('"'), "Battle.net.exe");
                        if (System.IO.File.Exists(exe)) return exe;
                    }
                }
                catch { }
            }
            return null;
        }

        private static void Say(string m) { try { Log?.Invoke("HS launch: " + m); } catch { } }
    }
}
