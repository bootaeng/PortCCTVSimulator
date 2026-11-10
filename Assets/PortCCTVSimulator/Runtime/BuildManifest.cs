using System;
using System.Collections.Generic;
using UnityEngine;

namespace PortCCTV
{
    /// <summary>
    /// Content fingerprint of the project state a player was built from (written by the Editor's BuildManifestGenerator just
    /// before every build, shipped as Resources/PortCCTV/BuildManifest.json). Captures copy it into metadata.json so a result can
    /// be traced to the exact prefabs, materials, textures, meshes, settings and packages, not only to the original source files.
    /// </summary>
    [Serializable]
    public class BuildManifest
    {
        public string manifest_schema = "portcctv.buildmanifest.v1";
        public string generated_utc;
        public string unity_version;
        /// <summary>SHA-256 over the sorted "path \t sha256" lines of every hashed file (content_files).</summary>
        public string content_sha256;
        public int content_files;
        public long content_bytes;
        /// <summary>Folders and files that were hashed (asset content + .meta import settings, project settings, package lock).</summary>
        public List<string> hashed_roots = new List<string>();
        public string git_commit = "";
        /// <summary>Uncommitted changes in the hashed tree at build time (content_sha256 still identifies the exact state).</summary>
        public int git_uncommitted_files = -1;
        /// <summary>Per vessel asset: SHA-256 over the prefab and every asset it depends on (materials, textures, meshes, shaders).</summary>
        public List<NamedDigest> vessel_prefabs = new List<NamedDigest>();
        /// <summary>Per resource library (harbor theme, sky library, vessel catalog): same dependency digest.</summary>
        public List<NamedDigest> libraries = new List<NamedDigest>();
    }

    [Serializable]
    public class NamedDigest
    {
        public string id;
        public string asset_path;
        public string dependency_sha256;
        public int dependency_files;
    }

    /// <summary>What a capture records about the build it came from.</summary>
    [Serializable]
    public class BuildRecord
    {
        public bool manifest_present;
        /// <summary>"player" = manifest generated for this build; "editor" = Editor run: the manifest is from the last build and may
        /// not match the current project (use git_commit + the scenario hash, or rebuild).</summary>
        public string origin;
        public string content_sha256;
        public string manifest_generated_utc;
        public string git_commit;
        public int git_uncommitted_files = -1;
    }

    public static class BuildManifestInfo
    {
        public const string ResourcePath = "PortCCTV/BuildManifest";
        static BuildManifest s_cached; static bool s_loaded;

        public static BuildManifest Load()
        {
            if (s_loaded) return s_cached;
            s_loaded = true;
            var ta = Resources.Load<TextAsset>(ResourcePath);
            if (ta != null) { try { s_cached = JsonUtility.FromJson<BuildManifest>(ta.text); } catch { s_cached = null; } }
            return s_cached;
        }

        public static void ResetCache() { s_loaded = false; s_cached = null; }

        public static BuildRecord Record()
        {
            var m = Load();
            return new BuildRecord
            {
                manifest_present = m != null, origin = Application.isEditor ? "editor" : "player",
                content_sha256 = m?.content_sha256 ?? "", manifest_generated_utc = m?.generated_utc ?? "",
                git_commit = m?.git_commit ?? "", git_uncommitted_files = m?.git_uncommitted_files ?? -1,
            };
        }

        public static string VesselDigest(string assetId)
        {
            var m = Load();
            if (m == null) return "";
            foreach (var d in m.vessel_prefabs) if (d.id == assetId) return d.dependency_sha256;
            return "";
        }
    }
}
