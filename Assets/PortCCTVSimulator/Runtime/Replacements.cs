using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static PortCCTV.HarborGeneric02;

namespace PortCCTV
{
    internal static partial class HarborParts
    {
        /// <summary>
        /// Near-camera containers (2026-11-09, asset review): the yard holds tens of thousands of container boxes, merged into a
        /// few meshes. Boxes within <paramref name="range"/> m of a preset CCTV camera and (any direction) become the detailed 20 ft model (container_detail_&lt;colour&gt;: doors, locking bars, corner castings,
        /// corrugation; LOD1 = a plain box), scaled to the box (40 ft boxes are stretched along their length). The nearest
        /// <paramref name="cap"/> are converted; the rest stay merged boxes, so the frame cost stays bounded. Returns the count.
        /// </summary>
        internal static int DetailNearContainers(HarborDefinition h, float range = 150f, int cap = 400)
        {
            var cams = CameraPresetLibrary.ForHarbor(h.harbor_id);
            if (cams == null || cams.Length == 0) return 0;
            var cand = new List<(int i, float d)>();
            for (int i = 0; i < h.elements.Count; i++)
            {
                var e = h.elements[i];
                if (e.kind != "container" || e.shape != "box" || e.material == null || !ContainerColours.Contains(e.material)) continue;
                float best = float.MaxValue;
                foreach (var c in cams)
                {
                    var d = new Vector2(e.center.x - c.position.x, e.center.z - c.position.z);
                    float dist = d.magnitude; if (dist > range) continue;
                    float hfov = 2f * Mathf.Atan(Mathf.Tan(c.vertical_fov_deg * 0.5f * Mathf.Deg2Rad) * 16f / 9f) * Mathf.Rad2Deg;
                    float bearing = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
                    // (no view-cone filter: the preset cameras mostly look over the water, and the stacks beside / behind them are what
                    // a re-aimed or dev camera sees first)
                    best = Mathf.Min(best, dist);
                }
                if (best < float.MaxValue) cand.Add((i, best));
            }
            int n = 0;
            foreach (var (i, _) in cand.OrderBy(c => c.d).Take(cap))
            {
                var e = h.elements[i];
                bool alongZ = e.size.z > e.size.x;
                float len = alongZ ? e.size.z : e.size.x, wid = alongZ ? e.size.x : e.size.z;
                if (len < 5.5f || e.size.y < 2.3f) continue;                                    // not a standard box (racks, parts)
                e.shape = "prefab";
                e.prefab = e.material.Replace("container_", "container_detail_");
                e.center = new Vector3(e.center.x, e.center.y - e.size.y / 2f, e.center.z);
                e.yaw_deg += alongZ ? 90f : 0f;
                e.size = new Vector3(len / 6.058f, e.size.y / 2.591f, wid / 2.438f);
                n++;
            }
            Debug.Log($"[Harbor] {h.harbor_id}: {n} near-camera containers use the detailed model ({cand.Count} in range)");
            return n;
        }
    }
}
