using UnityEngine;

namespace PortCCTV
{
    /// <summary>Continuous, gently sloped marina beach. The same mesh is used for drawing and collision.</summary>
    internal static class BeachSurface
    {
        const int Along = 160;
        const int Across = 20;

        internal static float ShoreZ(float x, float startX, float length)
        {
            float t = Mathf.Clamp01((x - startX) / length);
            // Broad bay curvature with small, smooth shoreline variation. Both ends meet the headlands.
            return -16f - 29f * Mathf.Sin(Mathf.PI * t)
                + 1.8f * Mathf.Sin(5f * Mathf.PI * t) * Mathf.Sin(Mathf.PI * t);
        }

        internal static Mesh MakeMesh(HarborElement el)
        {
            int stride = Across + 1;
            var vertices = new Vector3[(Along + 1) * stride];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[Along * Across * 6];
            float startX = el.center.x - el.size.x * 0.5f;
            float backZ = el.size.z;

            for (int i = 0; i <= Along; i++)
            {
                float localX = el.size.x * i / Along - el.size.x * 0.5f;
                float worldX = el.center.x + localX;
                float shore = ShoreZ(worldX, startX, el.size.x);
                for (int j = 0; j <= Across; j++)
                {
                    float z = Mathf.Lerp(shore, backZ, j / (float)Across);
                    float d = z - shore;
                    float y = Mathf.SmoothStep(-0.85f, 0.7f, d / 24f);
                    // Rise behind the furniture to meet the promenade deck without a vertical sand slab.
                    if (z > 44f) y = Mathf.SmoothStep(0.7f, HarborGeneric02.Deck, (z - 44f) / (backZ - 44f));
                    int v = i * stride + j;
                    vertices[v] = new Vector3(localX, y, z - el.center.z);
                    uv[v] = new Vector2(worldX / 4f, z / 4f);
                    if (i == Along || j == Across) continue;
                    int q = (i * Across + j) * 6;
                    int a = v, b = v + stride, c = v + 1, d2 = b + 1;
                    triangles[q] = a; triangles[q + 1] = c; triangles[q + 2] = b;
                    triangles[q + 3] = c; triangles[q + 4] = d2; triangles[q + 5] = b;
                }
            }

            var mesh = new Mesh { name = "marina_beach_continuous" };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        internal static GameObject Create(HarborElement el, Material material, Transform parent, out MeshCollider collider)
        {
            var go = new GameObject(el.id);
            go.transform.SetParent(parent, false);
            var mesh = MakeMesh(el);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            return go;
        }
    }
}
