using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PortCCTV
{
    [Serializable]
    public class VesselAssetEntry
    {
        public string asset_id;
        public string display_name;
        public string category;           // e.g. rib, motorboat, passenger_ferry
        public GameObject prefab;         // root with MeshColliders added by the bootstrap
        public VesselGeometry geometry;
        public string source_file;
        public string source_sha256;
        public string license;
        public string source_url;
        public float default_immersion_m = 0.1f; // design placeholder, not a measured draft
        public string material_note;             // material substitutions made on the prefab copy
        public List<PaintSlot> paint_slots = new List<PaintSlot>();   // hull / superstructure / keep per (renderer, sub-mesh)
        public string paint_note;
    }

    public class VesselCatalog : ScriptableObject
    {
        public List<VesselAssetEntry> entries = new List<VesselAssetEntry>();

        public VesselAssetEntry Find(string assetId) => entries.FirstOrDefault(e => e.asset_id == assetId);

        public const string ResourcePath = "PortCCTV/VesselCatalog";
        public static VesselCatalog Load() => Resources.Load<VesselCatalog>(ResourcePath);
    }
}
