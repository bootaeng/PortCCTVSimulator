using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PortCCTV
{
    /// <summary>HDRI skies (Poly Haven CC0) with the sun direction measured from the cubemap by the Editor bootstrap.</summary>
    public class SkyLibrary : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public string key;
            public Cubemap cubemap;
            /// <summary>Sun azimuth in cubemap space (deg, clockwise from +Z, Unity yaw convention).</summary>
            public float sun_azimuth_cube_deg;
            public float sun_elevation_deg;
            /// <summary>Cosine-weighted upper-hemisphere integral of the cubemap (HDRP 'upperHemisphereLuxValue').</summary>
            public float upper_hemisphere_lux;
            public string source_file;
            public string source_sha256;
        }

        public List<Entry> entries = new List<Entry>();

        public Entry Find(string key) => entries.FirstOrDefault(e => e.key == key);

        public const string ResourcePath = "PortCCTV/SkyLibrary";
        public static SkyLibrary Load() => Resources.Load<SkyLibrary>(ResourcePath);
    }
}
