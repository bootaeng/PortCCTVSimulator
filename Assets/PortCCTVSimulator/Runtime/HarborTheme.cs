using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PortCCTV
{
    /// <summary>Harbor materials, created by the Editor bootstrap (HDRP/Lit) and loaded from Resources.</summary>
    public class HarborTheme : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public string key;
            public Material material;
        }

        public List<Entry> materials = new List<Entry>();

        [Serializable]
        public class PrefabEntry
        {
            public string key;
            public GameObject prefab;
            public string source; // provenance note
        }

        public List<PrefabEntry> prefabs = new List<PrefabEntry>();

        public Material Get(string key) => materials.FirstOrDefault(e => e.key == key)?.material;
        public GameObject GetPrefab(string key) => prefabs.FirstOrDefault(e => e.key == key)?.prefab;

        public const string ResourcePath = "PortCCTV/HarborTheme";
        public static HarborTheme Load() => Resources.Load<HarborTheme>(ResourcePath);
    }
}
