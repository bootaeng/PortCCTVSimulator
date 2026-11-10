using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace PortCCTV.EditorTools
{
    /// <summary>Writes Assets/PortCCTVSimulator/Resources/PortCCTV/BuildManifest.json (see <see cref="BuildManifest"/>).
    /// Called by BuildScript before every player build; also: -executeMethod PortCCTV.EditorTools.BuildManifestGenerator.RunBatch</summary>
    public static class BuildManifestGenerator
    {
        public const string OutPath = "Assets/PortCCTVSimulator/Resources/PortCCTV/BuildManifest.json";
        static readonly string[] Roots = { "Assets/PortCCTVSimulator", "ProjectSettings", "Packages/manifest.json", "Packages/packages-lock.json" };

        public static void RunBatch()
        {
            try { var m = Generate(); Debug.Log($"[BuildManifest] {m.content_files} files, {m.content_bytes} bytes, sha256 {m.content_sha256}"); EditorApplication.Exit(0); }
            catch (Exception ex) { Debug.LogError("[BuildManifest] FAILED: " + ex); EditorApplication.Exit(1); }
        }

        static string Hex(byte[] b) { var sb = new StringBuilder(b.Length * 2); foreach (var x in b) sb.Append(x.ToString("x2")); return sb.ToString(); }

        static readonly Dictionary<string, (long len, DateTime mtime, string sha)> s_fileCache = new Dictionary<string, (long, DateTime, string)>();

        static string FileSha(string path, SHA256 sha)
        {
            var fi = new FileInfo(path);
            if (s_fileCache.TryGetValue(path, out var c) && c.len == fi.Length && c.mtime == fi.LastWriteTimeUtc) return c.sha;
            string h; using (var fs = File.OpenRead(path)) h = Hex(sha.ComputeHash(fs));
            s_fileCache[path] = (fi.Length, fi.LastWriteTimeUtc, h);
            return h;
        }

        static IEnumerable<string> Files(string root)
        {
            if (File.Exists(root)) { yield return root; yield break; }
            if (!Directory.Exists(root)) yield break;
            foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) yield return f.Replace('\\', '/');
        }

        static bool Excluded(string p) =>
            p == OutPath || p == OutPath + ".meta" || p.Contains("/Tests/") || p.EndsWith(".DS_Store");

        static (string sha, int n) Digest(IEnumerable<string> paths, SHA256 sha)
        {
            var lines = new StringBuilder(); int n = 0;
            foreach (var p in paths.Distinct().OrderBy(x => x, StringComparer.Ordinal))
            {
                if (!File.Exists(p)) continue;
                lines.Append(p).Append('\t').Append(FileSha(p, sha)).Append('\n');
                if (File.Exists(p + ".meta")) lines.Append(p).Append(".meta\t").Append(FileSha(p + ".meta", sha)).Append('\n');
                n++;
            }
            return (Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(lines.ToString()))), n);
        }

        static string Git(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("git", args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                using var p = Process.Start(psi);
                var o = p.StandardOutput.ReadToEnd(); p.WaitForExit(20000);
                return p.ExitCode == 0 ? o.Trim() : null;
            }
            catch { return null; }
        }

        public static BuildManifest Generate()
        {
            var m = new BuildManifest { generated_utc = DateTime.UtcNow.ToString("o"), unity_version = Application.unityVersion };
            m.hashed_roots.AddRange(Roots);
            using var sha = SHA256.Create();
            var files = Roots.SelectMany(Files).Where(p => !Excluded(p)).OrderBy(p => p, StringComparer.Ordinal).ToList();
            var lines = new StringBuilder();
            foreach (var p in files) { lines.Append(p).Append('\t').Append(FileSha(p, sha)).Append('\n'); m.content_bytes += new FileInfo(p).Length; }
            m.content_files = files.Count;
            m.content_sha256 = Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(lines.ToString())));

            m.git_commit = Git("rev-parse HEAD") ?? "";
            var st = Git("status --porcelain -- " + string.Join(" ", Roots.Select(r => "\"" + r + "\"")));
            m.git_uncommitted_files = st == null ? -1 : st.Split('\n').Count(l => l.Trim().Length > 0 && !l.Contains("BuildManifest.json"));

            var cat = VesselCatalog.Load();
            if (cat != null)
                foreach (var e in cat.entries)
                {
                    if (e.prefab == null) continue;
                    var path = AssetDatabase.GetAssetPath(e.prefab);
                    var (d, n) = Digest(AssetDatabase.GetDependencies(path, true), sha);
                    m.vessel_prefabs.Add(new NamedDigest { id = e.asset_id, asset_path = path, dependency_sha256 = d, dependency_files = n });
                }
            foreach (var (id, obj) in new (string, UnityEngine.Object)[] { ("vessel_catalog", cat), ("harbor_theme", HarborTheme.Load()), ("sky_library", Resources.Load(SkyLibrary.ResourcePath)) })
            {
                if (obj == null) continue;
                var path = AssetDatabase.GetAssetPath(obj);
                var (d, n) = Digest(AssetDatabase.GetDependencies(path, true), sha);
                m.libraries.Add(new NamedDigest { id = id, asset_path = path, dependency_sha256 = d, dependency_files = n });
            }

            Directory.CreateDirectory(Path.GetDirectoryName(OutPath));
            File.WriteAllText(OutPath, JsonUtility.ToJson(m, true), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(OutPath, ImportAssetOptions.ForceUpdate);
            BuildManifestInfo.ResetCache();
            return m;
        }
    }
}
