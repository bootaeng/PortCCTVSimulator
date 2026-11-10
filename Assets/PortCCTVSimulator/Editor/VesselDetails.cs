using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PortCCTV.EditorTools
{
    /// <summary>
    /// Merged-mesh detail builder for the procedural vessels: railings, bitts, anchors, life rings, lifeboat, lashing bridges,
    /// deck cranes, fenders, floats, lights, antennas. Every primitive is baked into one mesh per material ("details_<mat>")
    /// so a vessel stays a handful of renderers/colliders. Coordinates: bow +X, keel y = 0 (same frame as ProceduralVessels).
    /// </summary>
    public static class VesselDetails
    {
        const string MeshDir = "Assets/PortCCTVSimulator/Meshes";

        class Combiner
        {
            static Mesh _cube, _cyl, _sph, _cap;
            static Mesh Prim(PrimitiveType t) { var g = GameObject.CreatePrimitive(t); var m = g.GetComponent<MeshFilter>().sharedMesh; Object.DestroyImmediate(g); return m; }
            public static Mesh Cube => _cube != null ? _cube : _cube = Prim(PrimitiveType.Cube);
            public static Mesh Cylinder => _cyl != null ? _cyl : _cyl = Prim(PrimitiveType.Cylinder);
            public static Mesh SphereMesh => _sph != null ? _sph : _sph = Prim(PrimitiveType.Sphere);
            public static Mesh Capsule => _cap != null ? _cap : _cap = Prim(PrimitiveType.Capsule);

            readonly Dictionary<Material, List<CombineInstance>> _g = new Dictionary<Material, List<CombineInstance>>();

            public void Add(Mesh mesh, Vector3 pos, Vector3 scale, Material m, Vector3 euler = default)
            {
                if (!_g.TryGetValue(m, out var l)) _g[m] = l = new List<CombineInstance>();
                l.Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.TRS(pos, Quaternion.Euler(euler), scale) });
            }
            public void Box(Vector3 p, Vector3 s, Material m, Vector3 e = default) => Add(Cube, p, s, m, e);
            /// <summary>Vertical cylinder of diameter d and height h centred at p (Unity's cylinder mesh is 2 units tall).</summary>
            public void Cyl(Vector3 p, float d, float h, Material m, Vector3 e = default) => Add(Cylinder, p, new Vector3(d, h / 2f, d), m, e);
            public void Sphere(Vector3 p, float d, Material m) => Add(SphereMesh, p, Vector3.one * d, m);
            /// <summary>Life ring / tyre: flat ring whose plane faces +-Z (hung on a side wall).</summary>
            public void RingZ(Vector3 p, float d, float thick, Material m) => Add(Cylinder, p, new Vector3(d, thick / 2f, d), m, new Vector3(90, 0, 0));
            public void Bitt(Vector3 p, float d, float h, Material m) { Cyl(p + Vector3.up * h / 2, d, h, m); Cyl(p + Vector3.up * (h - 0.05f), d * 1.5f, 0.1f, m); }
            public void BittPair(Vector3 p, float gap, float d, float h, Material m) { Bitt(p + Vector3.forward * gap / 2, d, h, m); Bitt(p - Vector3.forward * gap / 2, d, h, m); }

            /// <summary>Railing along a polyline (two rails + posts). yaw of a cube's +X axis along the segment: Unity's rotation about Y
            /// by t maps +X to (cos t, 0, -sin t), so t = atan2(-dz, dx).</summary>
            public void Rail(IList<Vector3> pts, float height, Material m, float postSpacing = 1.5f, bool closed = false)
            {
                int n = closed ? pts.Count : pts.Count - 1;
                for (int i = 0; i < n; i++)
                {
                    var a = pts[i]; var b = pts[(i + 1) % pts.Count]; var d = b - a; float len = d.magnitude;
                    if (len < 1e-3f) continue;
                    float yaw = Mathf.Atan2(-d.z, d.x) * Mathf.Rad2Deg;
                    foreach (var f in new[] { 0.5f, 1f })
                        Box(a + d * 0.5f + Vector3.up * height * f, new Vector3(len, 0.05f, 0.05f), m, new Vector3(0, yaw, 0));
                    int posts = Mathf.Max(1, Mathf.RoundToInt(len / postSpacing));
                    for (int k = 0; k <= posts; k++)
                    {
                        if (k == 0 && i > 0) continue;
                        var p = a + d * (k / (float)posts);
                        Box(p + Vector3.up * height / 2, new Vector3(0.06f, height, 0.06f), m);
                    }
                }
            }

            public void Flush(Transform parent, string vessel)
            {
                foreach (var kv in _g)
                {
                    string name = $"details_{vessel}_{kv.Key.name.Replace("M_vessel_", "")}";
                    var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    mesh.CombineMeshes(kv.Value.ToArray(), true, true);
                    mesh.RecalculateBounds();
                    var path = $"{MeshDir}/{name}.asset";
                    var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (old != null) { EditorUtility.CopySerialized(mesh, old); mesh = old; } else AssetDatabase.CreateAsset(mesh, path);
                    var go = new GameObject(name);
                    go.transform.SetParent(parent, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    go.AddComponent<MeshRenderer>().sharedMaterial = kv.Key;
                }
            }
        }

        /// <summary>Deck-edge polyline (one side) following the lofted hull's half-breadth and sheer, inset by `inset` metres.</summary>
        static List<Vector3> DeckEdge(float L, float B, float D, float bowF, float sternF, float sheerRise, float side, float u0, float u1, float inset, int steps = 14)
        {
            var pts = new List<Vector3>();
            for (int i = 0; i <= steps; i++)
            {
                float u = Mathf.Lerp(u0, u1, i / (float)steps);
                float half = ProceduralVessels.HalfBreadth(u, B, bowF, sternF);
                pts.Add(new Vector3(-L / 2 + u * L, ProceduralVessels.DeckY(u, D, sheerRise) + 0.02f, side * Mathf.Max(half - inset, 0.1f)));
            }
            return pts;
        }

        static List<Vector3> Rect(float x0, float x1, float z0, float z1, float y) =>
            new List<Vector3> { new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x1, y, z1), new Vector3(x0, y, z1) };

        static Material Grey => ProceduralVessels.Mat("detail_grey", new Color(0.62f, 0.63f, 0.64f), 0.5f, 0.6f);
        static Material Black => ProceduralVessels.Mat("detail_black", new Color(0.04f, 0.04f, 0.04f), 0.3f, 0f);
        static Material Orange => ProceduralVessels.Mat("detail_orange", new Color(0.95f, 0.38f, 0.05f), 0.5f, 0f);
        static Material White => ProceduralVessels.Mat("detail_white", new Color(0.9f, 0.9f, 0.88f), 0.45f, 0.1f);
        static Material Red => ProceduralVessels.Mat("nav_red", new Color(0.9f, 0.05f, 0.05f), 0.7f, 0f);
        static Material Green => ProceduralVessels.Mat("nav_green", new Color(0.05f, 0.8f, 0.2f), 0.7f, 0f);
        static Material Glass => ProceduralVessels.Mat("glass", new Color(0.06f, 0.08f, 0.10f), 0.9f, 0.1f);
        static Material Rust => ProceduralVessels.Mat("detail_rust", new Color(0.32f, 0.20f, 0.14f), 0.3f, 0.3f);

        // ------------------------------------------------------------------ tug 24 m
        public static void Tug(Transform t)
        {
            const float L = 24f, B = 9f, D = 3.6f, bowF = 1.6f, sternF = 2f, sheer = 0.25f;
            var c = new Combiner();
            // railing on the deckhouse roof (crew walkway) and on the open aft deck (bulwark band covers the forward deck)
            c.Rail(Rect(-3.7f, 5.7f, -2.9f, 2.9f, 6.2f), 1.0f, Grey, 1.4f, true);
            foreach (var s in new[] { 1f, -1f }) c.Rail(DeckEdge(L, B, D, bowF, sternF, sheer, s, 0.04f, 0.14f, 0.35f, 4), 0.95f, Grey, 1.2f);
            c.Rail(new[] { new Vector3(-11.0f, D + 0.02f, 2.6f), new Vector3(-11.0f, D + 0.02f, -2.6f) }, 0.95f, Grey, 1.2f);
            // mooring bitts, towing winch, anchor + hawse, ventilators, exhausts, windows, life rings, lights, radar
            c.BittPair(new Vector3(9.6f, D + 0.1f, 0), 2.6f, 0.35f, 0.9f, Black);
            c.BittPair(new Vector3(-10.2f, D + 0.1f, 0), 4.6f, 0.35f, 0.9f, Black);
            c.Add(Combiner.Cylinder, new Vector3(-6.5f, 4.9f, 0), new Vector3(1.3f, 1.0f, 1.3f), Grey, new Vector3(90, 0, 0));
            c.Box(new Vector3(-6.5f, 4.3f, 0), new Vector3(1.6f, 0.4f, 2.4f), Grey);
            c.Box(new Vector3(10.9f, D - 0.6f, 3.4f), new Vector3(0.5f, 0.9f, 0.25f), Black);
            c.Sphere(new Vector3(10.95f, D - 0.1f, 3.5f), 0.45f, Black);
            foreach (var s in new[] { 1f, -1f })
            {
                c.Cyl(new Vector3(-4.2f, 6.8f, s * 2.4f), 0.4f, 1.2f, White);
                c.Sphere(new Vector3(-4.2f, 7.5f, s * 2.4f), 0.6f, White);
                c.Cyl(new Vector3(-2.6f, 8.6f, s * 0.45f), 0.35f, 1.6f, Black);
                for (int i = 0; i < 4; i++) c.Box(new Vector3(-2.5f + i * 1.8f, 5.2f, s * 3.22f), new Vector3(0.9f, 0.7f, 0.06f), Glass);
                c.RingZ(new Vector3(-1.0f, 5.6f, s * 3.26f), 0.75f, 0.1f, Orange);
            }
            c.Sphere(new Vector3(2.4f, 8.95f, 3.0f), 0.3f, Red);
            c.Sphere(new Vector3(2.4f, 8.95f, -3.0f), 0.3f, Green);
            c.Sphere(new Vector3(2.4f, 11.85f, 0), 0.3f, White);
            c.Box(new Vector3(2.4f, 11.0f, 0), new Vector3(0.25f, 0.15f, 1.3f), Grey);
            c.Flush(t, "tug");
        }

        // ------------------------------------------------------------------ container feeder 172 m
        public static void ContainerShip(Transform t, Material deckGrey, Material white)
        {
            const float L = 172f, B = 27f, D = 14.5f, bowF = 2.4f, sternF = 1.5f, sheer = 0.12f;
            var c = new Combiner();
            foreach (var s in new[] { 1f, -1f })
            {
                c.Rail(DeckEdge(L, B, D, bowF, sternF, sheer, s, 0.10f, 0.92f, 0.5f, 24), 1.1f, Grey, 2.0f);
                // anchor in its hawse pipe, bow fairleads
                float halfBow = ProceduralVessels.HalfBreadth(0.955f, B, bowF, sternF);
                c.Box(new Vector3(78.5f, D - 3.2f, s * (halfBow + 0.35f)), new Vector3(1.3f, 2.0f, 0.6f), Black);
                c.Sphere(new Vector3(78.5f, D - 2.0f, s * (halfBow + 0.4f)), 1.1f, Black);
                c.BittPair(new Vector3(70f, D + 2.5f, s * 4.5f), 1.6f, 0.5f, 1.3f, Black);
                c.BittPair(new Vector3(-80f, D + 0.1f, s * 5.5f), 1.6f, 0.5f, 1.3f, Black);
                // nav lights on the bridge wings
                c.Sphere(new Vector3(-62f, D + 20.6f, s * 14.6f), 0.5f, s > 0 ? Red : Green);
            }

            c.Rail(Rect(-67f, -57f, -14.8f, 14.8f, D + 20.22f), 1.1f, Grey, 2.0f, true);
            c.Rail(Rect(-71.8f, -56.2f, -10.3f, 10.3f, D + 17.02f), 1.1f, Grey, 2.0f, true);
            // lashing bridges between bays (same bay layout as BuildContainerShip)
            for (int b = 0; b < 8; b++)
            {
                float bx = -45f + b * 13.2f + 6.6f;
                float u = (bx + L / 2) / L;
                float half = ProceduralVessels.HalfBreadth(u, B, bowF, sternF);
                int rows = Mathf.Clamp(Mathf.FloorToInt((half * 2 - 1.5f) / 2.5f), 1, 9);
                c.Box(new Vector3(bx, D + 1.0f + 2.8f, 0), new Vector3(0.5f, 5.6f, rows * 2.5f + 0.4f), deckGrey);
            }
            // Gearless feeder: cargo is handled by the terminal STS cranes.
            // free-fall lifeboat on the stern, radar/satcom domes, masthead light
            c.Add(Combiner.Capsule, new Vector3(-82.5f, D + 9.5f, 0), new Vector3(2.6f, 3.6f, 2.6f), Orange, new Vector3(0, 0, 55f));
            c.Box(new Vector3(-81f, D + 7.0f, 0), new Vector3(6f, 0.6f, 3.4f), Grey, new Vector3(0, 0, 35f));
            c.Sphere(new Vector3(-63f, D + 26.6f, 2.5f), 2.0f, White);
            c.Sphere(new Vector3(-63f, D + 27.8f, 0), 0.5f, White);
            c.Sphere(new Vector3(80f, D + 12.2f, 0), 0.5f, White);
            c.Flush(t, "container_ship");
        }

        // ------------------------------------------------------------------ Korean coastal fishing boat 19 m
        public static void KoreanFishingBoat(Transform t, Material blue)
        {
            const float L = 19f, B = 5.2f, D = 2.4f, bowF = 1.4f, sternF = 2f, sheer = 0.6f;
            var c = new Combiner();
            foreach (var s in new[] { 1f, -1f })
            {
                c.Rail(DeckEdge(L, B, D, bowF, sternF, sheer, s, 0.04f, 0.96f, 0.18f, 16), 0.8f, White, 1.2f);
                for (int i = 0; i < 4; i++)
                {
                    float x = -6f + i * 3.3f, u = (x + L / 2) / L;
                    float half = ProceduralVessels.HalfBreadth(u, B, bowF, sternF);
                    c.RingZ(new Vector3(x, ProceduralVessels.DeckY(u, D, sheer) - 0.55f, s * (half + 0.12f)), 0.7f, 0.22f, Black);
                }
                for (int i = 0; i < 6; i++)
                {
                    float x = -5f + i * 2.0f, u = (x + L / 2) / L;
                    float half = ProceduralVessels.HalfBreadth(u, B, bowF, sternF);
                    c.Sphere(new Vector3(x, ProceduralVessels.DeckY(u, D, sheer) + 0.35f, s * (half + 0.25f)), 0.45f, i % 2 == 0 ? Orange : White);
                }
                c.RingZ(new Vector3(-3.0f, 4.9f, s * 1.84f), 0.6f, 0.1f, Orange);
                c.Bitt(new Vector3(7.4f, ProceduralVessels.DeckY(0.89f, D, sheer), s * 1.1f), 0.25f, 0.6f, Black);
                c.Bitt(new Vector3(-8.4f, D + 0.02f, s * 1.7f), 0.25f, 0.6f, Black);
                c.Sphere(new Vector3(-0.9f, 5.5f, s * 1.6f), 0.22f, s > 0 ? Red : Green);
            }
            // forward deck crates, anchor roller + anchor, radar dome, antennas, stern flag
            c.Box(new Vector3(5.6f, ProceduralVessels.DeckY(0.8f, D, sheer) + 0.3f, 1.0f), new Vector3(1.0f, 0.6f, 0.8f), blue);
            c.Box(new Vector3(5.6f, ProceduralVessels.DeckY(0.8f, D, sheer) + 0.3f, -1.0f), new Vector3(1.0f, 0.6f, 0.8f), blue);
            c.Box(new Vector3(4.4f, ProceduralVessels.DeckY(0.73f, D, sheer) + 0.3f, 0f), new Vector3(1.0f, 0.6f, 0.8f), Orange);
            c.Box(new Vector3(9.2f, ProceduralVessels.DeckY(0.98f, D, sheer) + 0.2f, 0), new Vector3(0.7f, 0.3f, 0.35f), Grey);
            c.Box(new Vector3(9.55f, ProceduralVessels.DeckY(0.98f, D, sheer) - 0.4f, 0), new Vector3(0.25f, 0.8f, 0.5f), Black);
            c.Sphere(new Vector3(-4.2f, 5.75f, 0.9f), 0.7f, White);
            c.Cyl(new Vector3(-1.9f, 7.0f, -1.5f), 0.05f, 3.0f, Grey);
            c.Cyl(new Vector3(-4.6f, 6.6f, -1.2f), 0.05f, 2.2f, Grey);
            c.Cyl(new Vector3(-9.0f, D + 1.2f, 0), 0.06f, 2.4f, White);
            c.Box(new Vector3(-8.7f, D + 2.15f, 0), new Vector3(0.6f, 0.4f, 0.03f), Red);
            c.Flush(t, "kfish");
        }

        // ------------------------------------------------------------------ deck barge 50 m
        public static void Barge(Transform t)
        {
            const float B = 14f;
            var c = new Combiner();
            foreach (var s in new[] { 1f, -1f })
            {
                foreach (var x in new[] { -21f, -7f, 7f, 21f }) c.Bitt(new Vector3(x, 3.3f, s * 6.0f), 0.5f, 1.0f, Black);
                for (int i = 0; i < 5; i++) c.RingZ(new Vector3(-18f + i * 9f, 2.3f, s * (B / 2 + 0.1f)), 1.0f, 0.3f, Black);
                c.Box(new Vector3(-24.2f, 5.0f, s * 4.0f), new Vector3(1.2f, 3.6f, 1.0f), Rust);     // push knees
            }
            c.Box(new Vector3(-21.5f, 4.55f, 0), new Vector3(3.0f, 2.5f, 3.0f), Rust);                // deck shed
            c.Box(new Vector3(-21.5f, 5.9f, 0), new Vector3(3.3f, 0.2f, 3.3f), Black);
            c.Add(Combiner.Cylinder, new Vector3(22f, 4.0f, 0), new Vector3(1.2f, 1.2f, 1.2f), Grey, new Vector3(90, 0, 0)); // anchor winch
            c.Box(new Vector3(22f, 3.5f, 0), new Vector3(1.6f, 0.4f, 3.0f), Grey);
            c.Cyl(new Vector3(23.5f, 5.3f, 0), 0.12f, 4.0f, Grey);                                     // light pole
            c.Sphere(new Vector3(23.5f, 7.4f, 0), 0.3f, White);
            // ~20 t crawler excavator working the pile from the bow end (R01). 2026-11-09: the box excavator is replaced by the
            // imported model (Excavator (CAT), sarvesh777, CC BY 4.0; logos removed) as a separate deck-equipment child: tracks on
            // the deck plate (top y 3.30), boom over the pile towards -x. The hull reference node is the hull mesh alone, so the
            // equipment cannot change the immersion reference (checked in EveryCatalogHull_IsPlacedAtRequestedImmersion).
            var exPf = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(ReplacementAssets.ExcavatorPrefabPath);
            if (exPf != null)
            {
                var ex = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(exPf);
                ex.name = "deck_equipment_excavator";
                ex.transform.SetParent(t, false);
                ex.transform.localPosition = new Vector3(16.6f, 3.30f, 0.2f);
                ex.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
                foreach (var col in ex.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);
            }
            else
            {
                var yel = ProceduralVessels.Mat("detail_excavator_yellow", new Color(0.86f, 0.62f, 0.10f), 0.45f, 0.1f);
                foreach (var s in new[] { 1f, -1f }) c.Box(new Vector3(16.5f, 3.75f, s * 1.25f), new Vector3(4.4f, 0.9f, 0.6f), Black);
                c.Box(new Vector3(17.2f, 5.25f, 0f), new Vector3(3.6f, 1.1f, 2.5f), yel);
                Debug.LogWarning("[Bootstrap] equip_excavator prefab missing: barge keeps a box excavator");
            }
            c.Flush(t, "barge");
        }
    }
}
