using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace HsbgCardLookup.Mods
{
    /// <summary>One mod DLL found in Hearthstone's folder.</summary>
    public sealed class InstalledMod
    {
        public string Name;       // the DLL's product name, else its file name
        public string FileName;
        public string FullPath;
        public string Author;     // the DLL's company when it is a real one, else read off a reverse-DNS file name; may be ""
        public string Version;    // product version without its "+commit" suffix; may be ""
        public string Loader;     // "BepInEx" | "BepInEx patcher" | "MelonLoader"
    }

    /// <summary>
    /// Finds Hearthstone and lists the mods in its loader folders: BepInEx\plugins and BepInEx\patchers
    /// (recursive), and MelonLoader's Mods and Plugins when MelonLoader is there. Read from the files'
    /// version info only — nothing is loaded, so no file stays locked.
    /// </summary>
    public static class InstalledMods
    {
        /// <summary>Hearthstone's install folder: the running client, else its Apps &amp; features entry; null when not found.</summary>
        public static string HearthstoneDir()
        {
            try
            {
                foreach (var p in Process.GetProcessesByName("Hearthstone"))
                    using (p)
                    {
                        try { var dir = Path.GetDirectoryName(p.MainModule.FileName); if (Directory.Exists(dir)) return dir; } catch { }
                    }
            }
            catch { }
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                try
                {
                    using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Hearthstone"))
                    {
                        var dir = key?.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
                    }
                }
                catch { }
            }
            return null;
        }

        public static bool HasBepInEx(string hsDir) => hsDir != null && Directory.Exists(Path.Combine(hsDir, "BepInEx"));

        public static List<InstalledMod> Scan(string hsDir)
        {
            var mods = new List<InstalledMod>();
            if (hsDir == null) return mods;
            Add(mods, Path.Combine(hsDir, "BepInEx", "plugins"), SearchOption.AllDirectories, "BepInEx");
            Add(mods, Path.Combine(hsDir, "BepInEx", "patchers"), SearchOption.AllDirectories, "BepInEx patcher");
            if (Directory.Exists(Path.Combine(hsDir, "MelonLoader")))
            {
                Add(mods, Path.Combine(hsDir, "Mods"), SearchOption.TopDirectoryOnly, "MelonLoader");
                Add(mods, Path.Combine(hsDir, "Plugins"), SearchOption.TopDirectoryOnly, "MelonLoader");
            }
            return mods.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void Add(List<InstalledMod> mods, string dir, SearchOption depth, string loader)
        {
            if (!Directory.Exists(dir)) return;
            string[] files;
            try { files = Directory.GetFiles(dir, "*.dll", depth); } catch { return; }
            foreach (var path in files)
            {
                try { mods.Add(Read(path, loader)); } catch { }
            }
        }

        private static InstalledMod Read(string path, string loader)
        {
            var vi = FileVersionInfo.GetVersionInfo(path);
            string file = Path.GetFileName(path);
            string bare = Path.GetFileNameWithoutExtension(path);

            // An SDK-built plugin with no company set reports its assembly name there (and as its
            // description) — that is not an author.
            string company = (vi.CompanyName ?? "").Trim();
            if (company.Length == 0 || string.Equals(company, bare, StringComparison.OrdinalIgnoreCase)
                || string.Equals(company, (vi.FileDescription ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                company = AuthorFromFileName(bare);

            string name = (vi.ProductName ?? "").Trim();
            if (name.Length == 0 || string.Equals(name, bare, StringComparison.OrdinalIgnoreCase)) name = bare;

            string version = (vi.ProductVersion ?? vi.FileVersion ?? "").Trim();
            int plus = version.IndexOf('+');
            if (plus >= 0) version = version.Substring(0, plus);

            return new InstalledMod { Name = name, FileName = file, FullPath = path, Author = company, Version = version, Loader = loader };
        }

        // "com.tantrumua.replaywatcher" → "tantrumua": the reverse-DNS id BepInEx plugins are usually named by.
        private static string AuthorFromFileName(string bare)
        {
            var parts = bare.Split('.');
            if (parts.Length >= 3 && new[] { "com", "org", "net", "io", "dev", "me", "github" }.Contains(parts[0].ToLowerInvariant()))
                return parts[1];
            return "";
        }

        /// <summary>The DLL and its .pdb beside it, to the Recycle Bin. Throws when Windows refuses (the
        /// game has the file open, say).</summary>
        public static void Recycle(InstalledMod mod)
        {
            foreach (var path in new[] { mod.FullPath, Path.ChangeExtension(mod.FullPath, ".pdb") })
                if (File.Exists(path))
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                        Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
        }

        /// <summary>a &lt; b for dotted versions ("0.30.0" &lt; "0.31"); false when either won't parse.</summary>
        public static bool IsOlder(string a, string b)
        {
            Version va, vb;
            return Version.TryParse(Pad(a), out va) && Version.TryParse(Pad(b), out vb) && va < vb;
        }

        private static string Pad(string v)
        {
            v = (v ?? "").Trim().TrimStart('v', 'V');
            return v.IndexOf('.') < 0 ? v + ".0" : v;
        }
    }
}
