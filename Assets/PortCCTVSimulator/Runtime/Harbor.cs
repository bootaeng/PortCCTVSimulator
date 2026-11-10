using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace PortCCTV
{
    /// <summary>One element of the harbor. Every element gets a collider that matches its rendered mesh.</summary>
    [Serializable]
    public class HarborElement
    {
        public string id;
        public string kind;      // land, quay, pier, breakwater, warehouse, container, crane, bollard, light_pole, buoy, mast, fender, rock, ...
        public string shape;     // box | cylinder | sphere | prefab
        public Vector3 center;   // box/cylinder: centre; prefab: pivot position
        public Vector3 size;     // box: full size; cylinder: (diameter, height, diameter); prefab: local scale
        public float yaw_deg;
        public Vector3 euler_deg; // extra rotation applied as Euler(euler.x, yaw_deg + euler.y, euler.z)
        public string material;  // HarborTheme material key (box/cylinder)
        public string prefab;    // HarborTheme prefab key (shape == prefab)
        public bool is_vessel_like_non_vessel; // e.g. buoys: potential hard negatives, never labelled as vessel

        public Quaternion Rotation => Quaternion.Euler(euler_deg.x, yaw_deg + euler_deg.y, euler_deg.z);
    }

    [Serializable]
    public class WaterRegion
    {
        public string id;
        public List<Vector2> polygon_xz = new List<Vector2>(); // counter-clockwise or clockwise, simple polygon

        public bool Contains(Vector2 p) => Geometry2D.PointInPolygon(p, polygon_xz);
    }

    /// <summary>Which vessel types belong in a map, and optionally in which water regions (empty = anywhere).</summary>
    [Serializable]
    public class VesselRule
    {
        public string asset_id;
        public List<string> regions = new List<string>();
        public string note;
    }

    [Serializable]
    public class HarborDefinition
    {
        public string harbor_id;
        public string display_name;
        /// <summary>container_terminal | fishing_port | marina | mixed</summary>
        public string harbor_type = "mixed";
        public string description;
        /// <summary>Empty = every catalogue vessel is plausible (legacy/mixed maps).</summary>
        public List<VesselRule> vessel_rules = new List<VesselRule>();
        public float water_reference_y = 0f;
        public List<HarborElement> elements = new List<HarborElement>();
        public List<WaterRegion> water_regions = new List<WaterRegion>();
        /// <summary>Parking lots / truck parks / service rows / yards with their aisles and driveways, and every marked bay
        /// (lines, wheel stops and vehicles are generated from these records; Parking.cs, checked by ParkingCheck).</summary>
        public List<ParkingLotInfo> parking_lots = new List<ParkingLotInfo>();
        public List<ParkingBay> parking_bays = new List<ParkingBay>();

        public bool InAnyWaterRegion(Vector2 xz) => water_regions.Any(r => r.Contains(xz));

        public string WaterRegionAt(Vector2 xz)
        {
            var r = water_regions.FirstOrDefault(w => w.Contains(xz));
            return r?.id;
        }

        [NonSerialized] string _hash;
        /// <summary>SHA-256 of the definition JSON (cached: definitions are not modified after Build()).</summary>
        public string ComputeHash() => _hash ??= ScenarioIO.Sha256Hex(JsonUtility.ToJson(this));

        public bool AllowsVessel(string assetId) => vessel_rules.Count == 0 || vessel_rules.Any(r => r.asset_id == assetId);

        /// <summary>Null when plausible; otherwise a warning code for the placement report.</summary>
        public string RealismWarning(string assetId, string region)
        {
            if (vessel_rules.Count == 0) return null;
            var rule = vessel_rules.FirstOrDefault(r => r.asset_id == assetId);
            if (rule == null) return "vessel_type_unusual_for_" + harbor_type;
            if (rule.regions.Count > 0 && region != null && !rule.regions.Contains(region)) return "vessel_unusual_in_region:" + region;
            return null;
        }
    }

    public static class Geometry2D
    {
        public static bool PointInPolygon(Vector2 p, IList<Vector2> poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                var a = poly[i]; var b = poly[j];
                if ((a.y > p.y) != (b.y > p.y))
                {
                    float x = (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x;
                    if (p.x < x) inside = !inside;
                }
            }
            return inside;
        }

        /// <summary>Corners of an oriented rectangle in XZ. Local x = length axis.</summary>
        public static Vector2[] OrientedRect(Vector2 center, float yawDeg, Vector2 minLocal, Vector2 maxLocal)
        {
            var q = Quaternion.Euler(0, yawDeg, 0);
            Vector2 T(float lx, float lz)
            {
                var v = q * new Vector3(lx, 0, lz);
                return new Vector2(center.x + v.x, center.y + v.z);
            }
            return new[] { T(minLocal.x, minLocal.y), T(maxLocal.x, minLocal.y), T(maxLocal.x, maxLocal.y), T(minLocal.x, maxLocal.y) };
        }

        /// <summary>Separating-axis test for two convex quads (strict overlap; touching is not overlap).</summary>
        public static bool ConvexOverlap(Vector2[] a, Vector2[] b)
        {
            foreach (var poly in new[] { a, b })
            {
                for (int i = 0; i < poly.Length; i++)
                {
                    var e = poly[(i + 1) % poly.Length] - poly[i];
                    var n = new Vector2(-e.y, e.x);
                    Project(a, n, out var amin, out var amax);
                    Project(b, n, out var bmin, out var bmax);
                    if (amax <= bmin || bmax <= amin) return false;
                }
            }
            return true;
        }

        static void Project(Vector2[] p, Vector2 axis, out float min, out float max)
        {
            min = float.MaxValue; max = float.MinValue;
            foreach (var v in p) { var d = Vector2.Dot(v, axis); min = Mathf.Min(min, d); max = Mathf.Max(max, d); }
        }
    }

    /// <summary>Anonymised generic harbor. Procedural so no third-party background assets are needed.</summary>
    public static class HarborLibrary
    {
        public const string LegacyHarborId = "generic_harbor_01";
        public const string DefaultHarborId = ContainerTerminal01.Id;

        public static HarborDefinition Get(string id)
        {
            if (id == LegacyHarborId) return BuildGeneric01();
            if (id == HarborGeneric02.Id) return HarborGeneric02.Build();
            if (id == ContainerTerminal01.Id) return ContainerTerminal01.Build();
            if (id == FishingPort01.Id) return FishingPort01.Build();
            if (id == Marina01.Id) return Marina01.Build();
            throw new ArgumentException("Unknown harbor: " + id);
        }

        public static string[] Ids => new[] { ContainerTerminal01.Id, FishingPort01.Id, Marina01.Id, HarborGeneric02.Id, LegacyHarborId };

        public static string DisplayName(string id) => id switch
        {
            ContainerTerminal01.Id => "Container terminal",
            FishingPort01.Id => "Fishing port",
            Marina01.Id => "Marina",
            HarborGeneric02.Id => "Mixed harbor (v0.2)",
            _ => "Legacy simple harbor",
        };

        static HarborElement Box(string id, string kind, float x0, float x1, float y0, float y1, float z0, float z1, string mat, float yaw = 0)
            => new HarborElement
            {
                id = id, kind = kind, shape = "box",
                center = new Vector3((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2),
                size = new Vector3(x1 - x0, y1 - y0, z1 - z0), material = mat, yaw_deg = yaw
            };

        static HarborElement Cyl(string id, string kind, float x, float z, float y0, float y1, float diameter, string mat, bool vesselLike = false)
            => new HarborElement
            {
                id = id, kind = kind, shape = "cylinder", center = new Vector3(x, (y0 + y1) / 2, z),
                size = new Vector3(diameter, y1 - y0, diameter), material = mat, is_vessel_like_non_vessel = vesselLike
            };

        static HarborDefinition BuildGeneric01()
        {
            const float deck = 2.5f, crest = 3.5f, bottom = -12f;
            var h = new HarborDefinition
            {
                harbor_id = LegacyHarborId,
                description = "Anonymised rectangular basin: north quay with finger pier, west quay, east and south breakwaters with a 55 m entrance. Water reference plane y=0.",
                water_reference_y = 0f,
            };
            var e = h.elements;
            // Land and quays (tops at deck level, extending below the water so the infinite ocean is hidden underneath).
            e.Add(Box("land_north", "land", -500, 500, bottom, deck, 100, 500, "asphalt"));
            e.Add(Box("land_west", "land", -500, -170, bottom, deck, -240, 100, "asphalt"));
            e.Add(Box("quay_north", "quay", -170, 150, bottom, deck, 60, 100, "concrete"));
            e.Add(Box("quay_west", "quay", -170, -110, bottom, deck, -240, 60, "concrete"));
            e.Add(Box("pier_finger", "pier", -25, -5, bottom, deck, 0, 60, "concrete"));
            e.Add(Box("breakwater_east", "breakwater", 110, 125, bottom, crest, -255, 60, "breakwater"));
            e.Add(Box("breakwater_south_w", "breakwater", -170, 40, bottom, crest, -255, -240, "breakwater"));
            e.Add(Box("breakwater_south_e", "breakwater", 95, 110, bottom, crest, -255, -240, "breakwater"));
            e.Add(Box("quay_north_east_end", "quay", 125, 150, bottom, deck, 60, 100, "concrete"));

            // Dark fender strips along the north quay face and finger pier.
            e.Add(Box("fender_north_w", "fender", -110, -25, 0.2f, 1.6f, 59.4f, 60f, "fender"));
            e.Add(Box("fender_north_e", "fender", -5, 110, 0.2f, 1.6f, 59.4f, 60f, "fender"));
            e.Add(Box("fender_pier_w", "fender", -25.6f, -25f, 0.2f, 1.6f, 0, 60, "fender"));
            e.Add(Box("fender_pier_e", "fender", -5f, -4.4f, 0.2f, 1.6f, 0, 60, "fender"));

            // Warehouses.
            e.Add(Box("warehouse_west", "warehouse", -100, -50, deck, 14, 112, 140, "warehouse_wall"));
            e.Add(Box("warehouse_west_roof", "warehouse", -101, -49, 14, 14.6f, 111, 141, "warehouse_roof"));
            e.Add(Box("warehouse_east", "warehouse", 20, 70, deck, 12, 115, 145, "warehouse_wall"));
            e.Add(Box("warehouse_east_roof", "warehouse", 19, 71, 12, 12.6f, 114, 146, "warehouse_roof"));

            // Container stacks (12.2 x 2.6 x 2.44 m boxes), deterministic colours.
            string[] cmats = { "container_red", "container_blue", "container_green", "container_grey" };
            int ci = 0;
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 4; col++)
                {
                    int tiers = 1 + (row + col) % 3;
                    for (int t = 0; t < tiers; t++)
                    {
                        float x0 = 80 + col * 13f, z0 = 70 + row * 3.0f, y0 = deck + t * 2.6f;
                        e.Add(Box($"container_{row}_{col}_{t}", "container", x0, x0 + 12.2f, y0, y0 + 2.6f, z0, z0 + 2.44f, cmats[ci++ % cmats.Length]));
                    }
                }

            // Two ship-to-shore gantry crane proxies with a boom over the water (occluders for regression tests).
            foreach (var (cx, name) in new[] { (-92f, "crane_w"), (-55f, "crane_e") })
            {
                foreach (var dx in new[] { -8f, 8f })
                    foreach (var z in new[] { 64f, 84f })
                        e.Add(Box($"{name}_leg_{dx}_{z}", "crane", cx + dx - 0.75f, cx + dx + 0.75f, deck, 32, z - 0.75f, z + 0.75f, "crane"));
                e.Add(Box($"{name}_portal_s", "crane", cx - 9, cx + 9, 30, 32, 63.25f, 64.75f, "crane"));
                e.Add(Box($"{name}_portal_n", "crane", cx - 9, cx + 9, 30, 32, 83.25f, 84.75f, "crane"));
                e.Add(Box($"{name}_boom", "crane", cx - 1.5f, cx + 1.5f, 32, 34.5f, 20, 98, "crane"));
                e.Add(Box($"{name}_house", "crane", cx - 3, cx + 3, 34.5f, 38, 76, 86, "crane"));
            }

            // Bollards and light poles along the north quay edge.
            for (int i = 0; i < 14; i++)
            {
                float x = -105 + i * 15f;
                if (x > -27 && x < -3) continue;
                e.Add(Cyl($"bollard_n_{i}", "bollard", x, 61.2f, deck, deck + 0.8f, 0.6f, "steel_dark"));
            }
            for (int i = 0; i < 6; i++)
                e.Add(Box($"light_pole_n_{i}", "light_pole", -100 + i * 40f - 0.15f, -100 + i * 40f + 0.15f, deck, deck + 15, 97.85f, 98.15f, "steel_dark"));

            // Channel buoys (hard-negative candidates; never labelled as vessels).
            e.Add(Cyl("buoy_red_in", "buoy", 88, -222, -0.8f, 1.8f, 1.6f, "buoy_red", true));
            e.Add(Cyl("buoy_green_in", "buoy", 47, -222, -0.8f, 1.8f, 1.6f, "buoy_green", true));
            e.Add(Cyl("buoy_red_out", "buoy", 100, -280, -0.8f, 1.8f, 1.6f, "buoy_red", true));
            e.Add(Cyl("buoy_green_out", "buoy", 35, -280, -0.8f, 1.8f, 1.6f, "buoy_green", true));

            // CCTV masts under each preset (camera sits on top).
            foreach (var p in CameraPresetLibrary.ForHarbor(LegacyHarborId))
            {
                float baseY = p.id.Contains("breakwater") ? crest : p.id.Contains("warehouse_roof") ? 12.6f : deck;
                e.Add(Box("mast_" + p.id, "mast", p.position.x - 0.15f, p.position.x + 0.15f, baseY, p.position.y - 0.6f,
                    p.position.z - 0.15f, p.position.z + 0.15f, "steel_dark"));
            }

            h.water_regions.Add(new WaterRegion
            {
                id = "inner_basin",
                polygon_xz = new List<Vector2>
                {
                    new Vector2(-110, -240), new Vector2(110, -240), new Vector2(110, 60), new Vector2(-5, 60),
                    new Vector2(-5, 0), new Vector2(-25, 0), new Vector2(-25, 60), new Vector2(-110, 60)
                }
            });
            h.water_regions.Add(new WaterRegion
            {
                id = "entrance_channel",
                polygon_xz = new List<Vector2> { new Vector2(40, -255), new Vector2(95, -255), new Vector2(95, -240), new Vector2(40, -240) }
            });
            h.water_regions.Add(new WaterRegion
            {
                id = "outer_south",
                polygon_xz = new List<Vector2> { new Vector2(-500, -1500), new Vector2(500, -1500), new Vector2(500, -255), new Vector2(-500, -255) }
            });
            h.water_regions.Add(new WaterRegion
            {
                id = "outer_east",
                polygon_xz = new List<Vector2>
                {
                    new Vector2(125, -255), new Vector2(500, -255), new Vector2(500, 100), new Vector2(150, 100),
                    new Vector2(150, 60), new Vector2(125, 60)
                }
            });
            return h;
        }
    }

    /// <summary>Instantiates a HarborDefinition into the scene with matching colliders.</summary>
    public static class HarborBuilder
    {
        public static GameObject Build(HarborDefinition def, HarborTheme theme, out List<Collider> colliders,
            out Dictionary<string, GameObject> objects)
        {
            colliders = new List<Collider>();
            objects = new Dictionary<string, GameObject>();
            var root = new GameObject("Harbor_" + def.harbor_id);
            var groups = new Dictionary<string, Transform>();
            var merged = new Dictionary<(string kind, string mat, int cx, int cz), List<HarborElement>>();
            foreach (var el in def.elements)
            {
                var kind = el.kind ?? "misc";
                if (el.shape == "billboard_tree") continue;      // merged into impostor meshes by Vegetation.Scatter
                if (el.shape == "marker") continue;              // build-time markers (Vegetation footpath starts), nothing to draw
                if (el.shape == "box" && MergedKinds.Contains(kind))
                {
                    var key = (kind, el.material, Mathf.FloorToInt(el.center.x / MergeChunk), Mathf.FloorToInt(el.center.z / MergeChunk));
                    if (!merged.TryGetValue(key, out var lst)) merged[key] = lst = new List<HarborElement>();
                    lst.Add(el);
                    continue;
                }
                if (!groups.TryGetValue(kind, out var parent))
                {
                    parent = new GameObject(kind).transform;
                    parent.SetParent(root.transform, false);
                    groups[kind] = parent;
                }
                GameObject go;
                if (el.shape == "prefab")
                {
                    var pf = theme != null ? theme.GetPrefab(el.prefab) : null;
                    if (pf == null) { Debug.LogWarning("[PortCCTV] missing harbor prefab " + el.prefab); continue; }
                    go = UnityEngine.Object.Instantiate(pf, parent);
                    go.transform.localScale = el.size == Vector3.zero ? Vector3.one : el.size;
                    if (el.kind == "car") CarPaint.Apply(go, el.id);                 // Korean car colour mix (white / grey / black)
                    colliders.AddRange(go.GetComponentsInChildren<Collider>(true));
                }
                else
                {
                    if (el.shape == "cylinder")
                    {
                        go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                        // Replace the default capsule (which does not match the rendered cylinder) with the exact mesh.
                        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
                        var mc = go.AddComponent<MeshCollider>();
                        mc.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
                        mc.convex = true;
                        // Unity cylinder primitive: diameter 1, height 2.
                        go.transform.localScale = new Vector3(el.size.x, el.size.y / 2f, el.size.z);
                    }
                    else if (el.shape == "sphere")
                    {
                        go = GameObject.CreatePrimitive(PrimitiveType.Sphere); // uniform scale keeps the SphereCollider exact
                        go.transform.localScale = Vector3.one * el.size.x;
                    }
                    else
                    {
                        go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        go.transform.localScale = el.size;
                    }
                    go.transform.SetParent(parent, false);
                    var r = go.GetComponent<MeshRenderer>();
                    var m = theme != null ? theme.Get(el.material) : null;
                    if (m != null) r.sharedMaterial = m;
                    else if (theme != null) Debug.LogWarning("[PortCCTV] missing harbor material " + el.material);
                    colliders.Add(go.GetComponent<Collider>());
                }
                go.name = el.id;
                go.transform.localPosition = el.center;
                go.transform.localRotation = el.Rotation;
                objects[el.id] = go;
            }
            BuildMerged(merged, theme, root.transform, groups, colliders);
            return root;
        }

        /// <summary>Box kinds that occur by the thousand (container stacks, stall lines, steel stock, paint markings): built as one
        /// combined mesh per (kind, material, 150 m tile) with a MeshCollider of the same mesh, instead of one GameObject +
        /// BoxCollider per box. Same geometry and materials; the individual boxes are not addressable in HarborObjects.</summary>
        public static readonly HashSet<string> MergedKinds = new HashSet<string> { "container", "parking_lines", "laydown", "marking", "reefer_rack", "wheel_stop" };
        const float MergeChunk = 150f;
        static Mesh s_Cube;

        static void BuildMerged(Dictionary<(string kind, string mat, int cx, int cz), List<HarborElement>> merged, HarborTheme theme, Transform root,
            Dictionary<string, Transform> groups, List<Collider> colliders)
        {
            if (merged.Count == 0) return;
            if (s_Cube == null)
            {
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                s_Cube = tmp.GetComponent<MeshFilter>().sharedMesh;
                UnityEngine.Object.DestroyImmediate(tmp);
            }
            var cv = s_Cube.vertices; var cn = s_Cube.normals; var cuv = s_Cube.uv; var ct = s_Cube.triangles;
            foreach (var kv in merged)
            {
                if (!groups.TryGetValue(kv.Key.kind, out var parent))
                {
                    parent = new GameObject(kv.Key.kind).transform;
                    parent.SetParent(root, false);
                    groups[kv.Key.kind] = parent;
                }
                var els = kv.Value;
                var v = new Vector3[els.Count * cv.Length]; var n = new Vector3[v.Length]; var uv = new Vector2[v.Length]; var t = new int[els.Count * ct.Length];
                for (int i = 0; i < els.Count; i++)
                {
                    var el = els[i]; var rot = el.Rotation; var m = Matrix4x4.TRS(el.center, rot, el.size);
                    int vo = i * cv.Length, to = i * ct.Length;
                    for (int k = 0; k < cv.Length; k++) { v[vo + k] = m.MultiplyPoint3x4(cv[k]); n[vo + k] = rot * cn[k]; uv[vo + k] = cuv[k]; }
                    for (int k = 0; k < ct.Length; k++) t[to + k] = vo + ct[k];
                }
                var mesh = new Mesh { name = $"merged_{kv.Key.kind}_{kv.Key.mat}_{kv.Key.cx}_{kv.Key.cz}", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.vertices = v; mesh.normals = n; mesh.uv = uv; mesh.triangles = t;
                mesh.RecalculateBounds(); mesh.RecalculateTangents();
                var go = new GameObject(mesh.name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                var mat = theme != null ? theme.Get(kv.Key.mat) : null;
                if (mat != null) r.sharedMaterial = mat;
                else if (theme != null) Debug.LogWarning("[PortCCTV] missing harbor material " + kv.Key.mat);
                var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = mesh;
                colliders.Add(mc);
            }
        }
    }
}
