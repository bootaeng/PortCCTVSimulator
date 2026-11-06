using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PortCCTV.EditorTools
{
    /// <summary>
    /// Procedural Korean coastal-village houses (HillFeatures.HouseKinds): hipped / gable / flat-roof houses and a steel shed,
    /// one merged mesh per material (walls, roof, glass, frames, slab, tank) with MeshColliders. Local frame: long axis x,
    /// floor y = 0, centred in x/z. Materials are HDRP/Lit triplanar (ProceduralAssets.MakeLit), so no UVs are needed.
    /// Placed on hill terraces (HarborGeneric02.HillVillage) and along coastal roads (HarborParts.CoastalVillage).
    /// </summary>
    public static partial class VillageAssets
    {
        const string MeshDir = "Assets/PortCCTVSimulator/Meshes";
        const string PrefabDir = "Assets/PortCCTVSimulator/HarborPrefabs";

        class MB
        {
            public readonly Dictionary<string, (List<Vector3> v, List<int> t)> parts = new Dictionary<string, (List<Vector3>, List<int>)>();

            (List<Vector3> v, List<int> t) P(string mat)
            {
                if (!parts.TryGetValue(mat, out var p)) parts[mat] = p = (new List<Vector3>(), new List<int>());
                return p;
            }

            public void Tri(string mat, Vector3 a, Vector3 b, Vector3 c)
            {
                var p = P(mat); int i = p.v.Count; p.v.Add(a); p.v.Add(b); p.v.Add(c); p.t.Add(i); p.t.Add(i + 1); p.t.Add(i + 2);
            }
            public void Quad(string mat, Vector3 a, Vector3 b, Vector3 c, Vector3 d) { Tri(mat, a, b, c); Tri(mat, a, c, d); }

            /// <summary>Triangle / quad whose winding is fixed so the face normal points along `outward` (Unity: n = cross(b-a, c-a)).</summary>
            public void TriN(string mat, Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) >= 0) Tri(mat, a, b, c); else Tri(mat, a, c, b);
            }
            public void QuadN(string mat, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward) { TriN(mat, a, b, c, outward); TriN(mat, a, c, d, outward); }

            /// <summary>Axis-aligned box (optionally rotated about y by yawDeg around its centre).</summary>
            public void Box(string mat, Vector3 c, Vector3 s, float yawDeg = 0f)
            {
                var q = Quaternion.Euler(0, yawDeg, 0);
                Vector3 V(float x, float y, float z) => c + q * new Vector3(x * s.x / 2, y * s.y / 2, z * s.z / 2);
                var p000 = V(-1, -1, -1); var p100 = V(1, -1, -1); var p110 = V(1, 1, -1); var p010 = V(-1, 1, -1);
                var p001 = V(-1, -1, 1); var p101 = V(1, -1, 1); var p111 = V(1, 1, 1); var p011 = V(-1, 1, 1);
                Quad(mat, p000, p010, p110, p100);   // -z
                Quad(mat, p101, p111, p011, p001);   // +z
                Quad(mat, p001, p011, p010, p000);   // -x
                Quad(mat, p100, p110, p111, p101);   // +x
                Quad(mat, p010, p011, p111, p110);   // +y
                Quad(mat, p000, p100, p101, p001);   // -y
            }

            /// <summary>Hipped roof over a w x d footprint (eaves at y0, ridge at y0+h along the longer axis).</summary>
            public void Hip(string mat, float w, float d, float y0, float h)
            {
                float hw = w / 2, hd = d / 2;
                var a = new Vector3(-hw, y0, -hd); var b = new Vector3(hw, y0, -hd); var c = new Vector3(hw, y0, hd); var e = new Vector3(-hw, y0, hd);
                if (w >= d)
                {
                    float r = Mathf.Max(hw - hd, 0.3f);
                    var r0 = new Vector3(-r, y0 + h, 0); var r1 = new Vector3(r, y0 + h, 0);
                    QuadN(mat, a, b, r1, r0, new Vector3(0, 1, -1)); QuadN(mat, c, e, r0, r1, new Vector3(0, 1, 1));
                    TriN(mat, b, c, r1, new Vector3(1, 1, 0)); TriN(mat, e, a, r0, new Vector3(-1, 1, 0));
                }
                else
                {
                    float r = Mathf.Max(hd - hw, 0.3f);
                    var r0 = new Vector3(0, y0 + h, -r); var r1 = new Vector3(0, y0 + h, r);
                    QuadN(mat, b, c, r1, r0, new Vector3(1, 1, 0)); QuadN(mat, e, a, r0, r1, new Vector3(-1, 1, 0));
                    TriN(mat, a, b, r0, new Vector3(0, 1, -1)); TriN(mat, c, e, r1, new Vector3(0, 1, 1));
                }
                QuadN(mat, a, e, c, b, Vector3.down); // underside (visible from below the eaves)
            }

            /// <summary>Gable roof (ridge along x) with the triangular gable ends in the wall material.</summary>
            public void Gable(string mat, string wallMat, float w, float d, float y0, float h, float wallInset)
            {
                float hw = w / 2, hd = d / 2;
                var a = new Vector3(-hw, y0, -hd); var b = new Vector3(hw, y0, -hd); var c = new Vector3(hw, y0, hd); var e = new Vector3(-hw, y0, hd);
                var r0 = new Vector3(-hw, y0 + h, 0); var r1 = new Vector3(hw, y0 + h, 0);
                QuadN(mat, a, b, r1, r0, new Vector3(0, 1, -1)); QuadN(mat, c, e, r0, r1, new Vector3(0, 1, 1));
                QuadN(mat, a, e, c, b, Vector3.down);
                // gable walls (inset from the eaves)
                float gx = hw - wallInset, gz = hd - wallInset, gh = h * (gz / hd);
                TriN(wallMat, new Vector3(gx, y0, -gz), new Vector3(gx, y0 + gh, 0), new Vector3(gx, y0, gz), Vector3.right);
                TriN(wallMat, new Vector3(-gx, y0, gz), new Vector3(-gx, y0 + gh, 0), new Vector3(-gx, y0, -gz), Vector3.left);
            }

            public GameObject Flush(string name)
            {
                var root = new GameObject(name);
                foreach (var kv in parts)
                {
                    var mesh = new Mesh { name = $"{name}_{kv.Key}", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    mesh.SetVertices(kv.Value.v); mesh.SetTriangles(kv.Value.t, 0);
                    mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.RecalculateTangents();
                    var path = $"{MeshDir}/{mesh.name}.asset";
                    var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (old != null) { EditorUtility.CopySerialized(mesh, old); mesh = old; } else AssetDatabase.CreateAsset(mesh, path);
                    var go = new GameObject(kv.Key);
                    go.transform.SetParent(root.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    go.AddComponent<MeshRenderer>().sharedMaterial = _theme.Get(kv.Key);
                    var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = mesh;
                }
                return root;
            }
        }

        static HarborTheme _theme;

        public static void BuildMaterials(HarborTheme theme, Dictionary<string, ProceduralAssets.TexSet> t)
        {
            void Add(string key, Material m)
            {
                theme.materials.RemoveAll(e => e.key == key);
                theme.materials.Add(new HarborTheme.Entry { key = key, material = m });
            }
            Add("house_wall_white", ProceduralAssets.MakeLit("house_wall_white", new Color(0.88f, 0.88f, 0.85f), 0.2f, 0, t["concrete"].albedo, t["concrete"].normal, 3f, 0.6f));
            Add("house_wall_cream", ProceduralAssets.MakeLit("house_wall_cream", new Color(0.86f, 0.80f, 0.66f), 0.2f, 0, t["concrete"].albedo, t["concrete"].normal, 3f, 0.6f));
            Add("house_wall_grey", ProceduralAssets.MakeLit("house_wall_grey", new Color(0.62f, 0.62f, 0.60f), 0.2f, 0, t["concrete"].albedo, t["concrete"].normal, 3f, 0.6f));
            Add("house_wall_brick", ProceduralAssets.MakeLit("house_wall_brick", new Color(0.50f, 0.30f, 0.22f), 0.2f, 0, t["concrete"].albedo, t["concrete"].normal, 3f, 0.6f));
            Add("house_roof_orange", ProceduralAssets.MakeLit("house_roof_orange", new Color(0.62f, 0.28f, 0.12f), 0.3f, 0, t["roof"].albedo, t["roof"].normal, 1.2f, 1.2f));
            Add("house_roof_blue", ProceduralAssets.MakeLit("house_roof_blue", new Color(0.10f, 0.26f, 0.52f), 0.4f, 0.3f, t["corrugated"].albedo, t["corrugated"].normal, 1.5f, 1.2f));
            Add("house_roof_slate", ProceduralAssets.MakeLit("house_roof_slate", new Color(0.30f, 0.32f, 0.34f), 0.35f, 0.2f, t["corrugated"].albedo, t["corrugated"].normal, 1.5f, 1.2f));
            Add("house_roof_green", ProceduralAssets.MakeLit("house_roof_green", new Color(0.20f, 0.46f, 0.30f), 0.35f, 0, t["concrete"].albedo, null, 4f));
            Add("house_roof_steel", ProceduralAssets.MakeLit("house_roof_steel", new Color(0.60f, 0.62f, 0.63f), 0.5f, 0.7f, t["corrugated"].albedo, t["corrugated"].normal, 1.5f, 1.2f));
            Add("house_glass", ProceduralAssets.MakeLit("house_glass", new Color(0.09f, 0.12f, 0.15f), 0.9f, 0.1f));
            Add("house_frame", ProceduralAssets.MakeLit("house_frame", new Color(0.85f, 0.85f, 0.83f), 0.5f, 0.4f));
            Add("house_slab", ProceduralAssets.MakeLit("house_slab", new Color(0.58f, 0.57f, 0.54f), 0.2f, 0, t["concrete"].albedo, t["concrete"].normal, 3f));
            Add("house_tank", ProceduralAssets.MakeLit("house_tank", new Color(0.62f, 0.64f, 0.66f), 0.55f, 0.8f));
            Add("retaining_wall", ProceduralAssets.MakeLit("retaining_wall", new Color(0.52f, 0.50f, 0.46f), 0.15f, 0, t["concrete"].albedo, t["concrete"].normal, 2.5f, 1.4f));
            Add("road_asphalt", ProceduralAssets.MakeLit("road_asphalt", new Color(0.16f, 0.16f, 0.165f), 0.3f, 0, t["asphalt"].albedo, t["asphalt"].normal, 5f));
            Add("lane_concrete", ProceduralAssets.MakeLit("lane_concrete", new Color(0.60f, 0.59f, 0.56f), 0.2f, 0, t["concrete"].albedo, t["concrete"].normal, 4f));
            Add("field", ProceduralAssets.MakeLit("field", new Color(1f, 1f, 1f), 0.1f, 0, t["field"].albedo, t["field"].normal, 6f, 1.2f));
            Add("guard_rail", ProceduralAssets.MakeLit("guard_rail", new Color(0.70f, 0.72f, 0.73f), 0.55f, 0.8f));
            BuildVehicleMaterials(Add);
            EditorUtility.SetDirty(theme);
        }

        // ------------------------------------------------------------------ house builders
        static void Windows(MB b, string frame, float w, float d, float y, float h, float ww, int nFront, int nSide, bool door, float z0 = 0f)
        {
            // front (-z) and back (+z)
            foreach (var sz in new[] { -1f, 1f })
            {
                for (int k = 0; k < nFront; k++)
                {
                    float x = -w / 2 + (k + 0.5f) * w / nFront;
                    if (door && sz < 0 && k == nFront / 2)
                    {
                        b.Box(frame, new Vector3(x, 1.05f, sz * (d / 2 + 0.03f)), new Vector3(1.0f, 2.1f, 0.06f));
                        b.Box("house_wall_brick", new Vector3(x, 1.0f, sz * (d / 2 + 0.05f)), new Vector3(0.9f, 2.0f, 0.04f));
                        continue;
                    }
                    b.Box(frame, new Vector3(x, y + h / 2, sz * (d / 2 + 0.03f)), new Vector3(ww + 0.14f, h + 0.14f, 0.06f));
                    b.Box("house_glass", new Vector3(x, y + h / 2, sz * (d / 2 + 0.06f)), new Vector3(ww, h, 0.04f));
                }
            }
            foreach (var sx in new[] { -1f, 1f })
                for (int k = 0; k < nSide; k++)
                {
                    float z = -d / 2 + (k + 0.5f) * d / nSide;
                    b.Box(frame, new Vector3(sx * (w / 2 + 0.03f), y + h / 2, z), new Vector3(0.06f, h + 0.14f, ww * 0.8f + 0.14f));
                    b.Box("house_glass", new Vector3(sx * (w / 2 + 0.06f), y + h / 2, z), new Vector3(0.04f, h, ww * 0.8f));
                }
        }

        static void Slab(MB b, float w, float d) => b.Box("house_slab", new Vector3(0, -0.35f, 0), new Vector3(w + 0.6f, 0.8f, d + 0.6f));

        static GameObject HipHouse(string name, float w, float d, string wall, string roof, float wallH, float roofH, bool annex)
        {
            var b = new MB();
            Slab(b, w, d);
            b.Box(wall, new Vector3(0, wallH / 2, 0), new Vector3(w, wallH, d));
            b.Hip(roof, w + 1.2f, d + 1.2f, wallH, roofH);
            b.Box("house_frame", new Vector3(0, wallH - 0.1f, 0), new Vector3(w + 1.2f, 0.2f, d + 1.2f));   // fascia / gutter
            Windows(b, "house_frame", w, d, 0.9f, 1.3f, 1.5f, 3, 1, true);
            b.Box("house_slab", new Vector3(w * 0.3f, wallH + roofH * 0.55f, d * 0.2f), new Vector3(0.5f, 1.2f, 0.5f));   // chimney / vent
            if (annex) { b.Box(wall, new Vector3(w / 2 + 1.4f, 1.2f, -d / 4), new Vector3(3.0f, 2.4f, 3.2f)); b.Box("house_roof_slate", new Vector3(w / 2 + 1.4f, 2.5f, -d / 4), new Vector3(3.6f, 0.15f, 3.8f), 0f); }
            return b.Flush(name);
        }

        static GameObject FlatHouse(string name, float w, float d, string wall, int storeys)
        {
            var b = new MB();
            float sh = 3.0f, wallH = sh * storeys;
            Slab(b, w, d);
            b.Box(wall, new Vector3(0, wallH / 2, 0), new Vector3(w, wallH, d));
            b.Box("house_roof_green", new Vector3(0, wallH + 0.04f, 0), new Vector3(w - 0.3f, 0.08f, d - 0.3f));
            // parapet, stair bulkhead, water tank, balcony band
            foreach (var sz in new[] { -1f, 1f }) b.Box(wall, new Vector3(0, wallH + 0.45f, sz * (d / 2 - 0.1f)), new Vector3(w, 0.9f, 0.2f));
            foreach (var sx in new[] { -1f, 1f }) b.Box(wall, new Vector3(sx * (w / 2 - 0.1f), wallH + 0.45f, 0), new Vector3(0.2f, 0.9f, d));
            b.Box(wall, new Vector3(-w / 4, wallH + 1.15f, d / 4), new Vector3(2.6f, 2.3f, 3.0f));
            b.Box("house_roof_slate", new Vector3(-w / 4, wallH + 2.35f, d / 4), new Vector3(3.0f, 0.12f, 3.4f));
            b.Box("house_tank", new Vector3(w / 4, wallH + 1.0f, -d / 4), new Vector3(1.6f, 1.6f, 1.6f));
            for (int s = 0; s < storeys; s++)
            {
                Windows(b, "house_frame", w, d, s * sh + 1.0f, 1.4f, 1.6f, 3, 2, s == 0);
                if (s > 0) b.Box("house_frame", new Vector3(0, s * sh + 0.5f, -d / 2 - 0.5f), new Vector3(w * 0.6f, 1.0f, 1.0f));   // balcony
            }
            return b.Flush(name);
        }

        static GameObject GableHouse(string name, float w, float d, string wall, string roof)
        {
            var b = new MB();
            const float wallH = 2.9f, roofH = 1.9f;
            Slab(b, w, d);
            b.Box(wall, new Vector3(0, wallH / 2, 0), new Vector3(w, wallH, d));
            b.Gable(roof, wall, w + 0.8f, d + 1.4f, wallH, roofH, 0.4f);
            Windows(b, "house_frame", w, d, 0.9f, 1.2f, 1.4f, 4, 1, true);
            // lean-to porch on the front
            b.Box("house_roof_slate", new Vector3(0, 2.6f, -d / 2 - 1.1f), new Vector3(w * 0.5f, 0.1f, 2.2f));
            foreach (var sx in new[] { -1f, 1f }) b.Box("house_frame", new Vector3(sx * w * 0.22f, 1.3f, -d / 2 - 2.0f), new Vector3(0.12f, 2.6f, 0.12f));
            return b.Flush(name);
        }

        static GameObject Shed(string name, float w, float d)
        {
            var b = new MB();
            Slab(b, w, d);
            b.Box("house_roof_steel", new Vector3(0, 1.5f, 0), new Vector3(w, 3.0f, d));
            b.Box("house_roof_steel", new Vector3(0, 3.15f, 0), new Vector3(w + 0.6f, 0.1f, d + 0.6f), 0f);
            b.Box("house_roof_slate", new Vector3(0, 3.3f, 0.3f), new Vector3(w + 0.6f, 0.1f, d + 0.2f), 0f);
            b.Box("house_frame", new Vector3(w * 0.2f, 1.1f, -d / 2 - 0.03f), new Vector3(2.4f, 2.2f, 0.06f));   // roller door
            return b.Flush(name);
        }

        public static void BuildPrefabs(HarborTheme theme)
        {
            _theme = theme;
            Directory.CreateDirectory(PrefabDir);
            void Add(string key, GameObject go, string src)
            {
                var p = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{key}.prefab");
                Object.DestroyImmediate(go);
                theme.prefabs.RemoveAll(e => e.key == key);
                theme.prefabs.Add(new HarborTheme.PrefabEntry { key = key, prefab = p, source = src });
            }
            var k = HillFeatures.HouseKinds;
            Add(k[0].key, HipHouse(k[0].key, k[0].w, k[0].d, "house_wall_cream", "house_roof_orange", 3.0f, 2.0f, false), "procedural 1-storey hipped tile-roof house (VillageAssets)");
            Add(k[1].key, HipHouse(k[1].key, k[1].w, k[1].d, "house_wall_white", "house_roof_blue", 2.9f, 1.8f, true), "procedural 1-storey hipped blue-sheet-roof house with annex (VillageAssets)");
            Add(k[2].key, FlatHouse(k[2].key, k[2].w, k[2].d, "house_wall_grey", 2), "procedural 2-storey flat-roof house, green waterproofed roof, tank (VillageAssets)");
            Add(k[3].key, GableHouse(k[3].key, k[3].w, k[3].d, "house_wall_white", "house_roof_slate"), "procedural gable slate-roof house with porch (VillageAssets)");
            Add(k[4].key, FlatHouse(k[4].key, k[4].w, k[4].d, "house_wall_cream", 1), "procedural 1-storey flat-roof house (VillageAssets)");
            Add(k[5].key, Shed(k[5].key, k[5].w, k[5].d), "procedural corrugated steel shed (VillageAssets)");
            BuildVehiclePrefabs(Add);
            EditorUtility.SetDirty(theme);
        }
    }
}
