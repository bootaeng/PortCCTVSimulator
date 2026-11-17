using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PortCCTV
{
    /// <summary>
    /// Screen-space layout rules shared by every vessel placement path (research sampler, interactive / automatic additions,
    /// default map scenes). Added 2026-11-16 after the same hull kept appearing at similar image positions, sizes and headings.
    /// World-space clearance (footprints, structures, water regions) stays in the individual planners; these rules look at the
    /// FIXED CCTV image: predicted above-water boxes of each vessel from its own mesh vertices.
    ///   same_asset_close    same asset_id and box-centre distance &lt; MinSameAssetCenterSep x image width
    ///   same_asset_lookalike same asset_id, apparent heading within LookalikeHeadingDeg and box height ratio within e^LookalikeLogScale
    ///   box_overlap         IoU of two boxes &gt; MaxPairIoU (designed occluder pairs are exempt)
    ///   same_asset_count    more than MaxSameAsset instances of one asset_id in view
    /// </summary>
    public static class PlacementQuality
    {
        public const string Version = "screen_layout_v1";
        public const float MinSameAssetCenterSep = 0.25f;
        public const float LookalikeHeadingDeg = 30f;
        public const float LookalikeLogScale = 0.35f;
        public const float MaxPairIoU = 0.30f;
        public const int MaxSameAsset = 2;

        [Serializable]
        public class VesselView
        {
            public string object_id, asset_id;
            public bool in_view;
            public float x_min, y_min, x_max, y_max;   // predicted above-water box, pixels, top-left origin
            public float depth_m;                      // along the camera's horizontal view direction
            public float apparent_heading_deg;         // bow direction relative to the line of sight, folded to [0, 180]
            public float Cx => (x_min + x_max) * 0.5f;
            public float Cy => (y_min + y_max) * 0.5f;
            public float Hgt => y_max - y_min;
        }

        [Serializable] public class Issue { public string rule, a, b; public float value; }

        [Serializable]
        public class Report
        {
            public string version = Version;
            public int width, height;
            public List<VesselView> vessels = new List<VesselView>();
            public List<Issue> issues = new List<Issue>();
            public bool ok => issues.Count == 0;
            public bool Involves(string id) => issues.Any(i => i.a == id || i.b == id);
        }

        public static float IoU(VesselView a, VesselView b)
        {
            float ix = Mathf.Max(0f, Mathf.Min(a.x_max, b.x_max) - Mathf.Max(a.x_min, b.x_min));
            float iy = Mathf.Max(0f, Mathf.Min(a.y_max, b.y_max) - Mathf.Max(a.y_min, b.y_min));
            float i = ix * iy, u = (a.x_max - a.x_min) * (a.y_max - a.y_min) + (b.x_max - b.x_min) * (b.y_max - b.y_min) - i;
            return u > 0f ? i / u : 0f;
        }

        /// <summary>Predicted view of every vessel and the rule violations among the in-view ones. `exempt(a, b)` marks pairs whose
        /// overlap is intended (research occluders).</summary>
        public static Report Evaluate(SimWorld w, IList<VesselPlacement> vessels, int W, int H, Func<string, string, bool> exempt = null)
        {
            var rep = new Report { width = W, height = H };
            var cam = w.CctvCamera;
            float prevAspect = cam.aspect;
            cam.aspect = (float)W / H;
            try
            {
                var proj = LabelComputer.MakeProjection(cam, W, H);
                var fwd3 = cam.transform.forward; var fwd = new Vector2(fwd3.x, fwd3.z).normalized;
                var camXZ = new Vector2(cam.transform.position.x, cam.transform.position.z);
                foreach (var v in vessels)
                {
                    var e = w.Catalog.Find(v.asset_id);
                    var view = new VesselView { object_id = v.object_id, asset_id = e != null ? e.asset_id : v.asset_id };
                    if (e != null && e.geometry != null && Research.ScenePlanner.PredictBox(w, proj, e, v.x, v.z, v.heading_deg, v.immersion_m, W, H, out var box))
                    {
                        view.in_view = true; view.x_min = box.xMin; view.y_min = box.yMin; view.x_max = box.xMax; view.y_max = box.yMax;
                    }
                    var rel = new Vector2(v.x, v.z) - camXZ;
                    view.depth_m = Vector2.Dot(rel, fwd);
                    var bow = NavigationSim.Dir(v.heading_deg);
                    var los = rel.sqrMagnitude > 1e-6f ? rel.normalized : fwd;
                    view.apparent_heading_deg = Vector2.Angle(bow, los);
                    rep.vessels.Add(view);
                }
            }
            finally { cam.aspect = prevAspect; }
            var vis = rep.vessels.Where(v => v.in_view && v.Hgt > 0f).ToList();
            for (int i = 0; i < vis.Count; i++)
                for (int j = i + 1; j < vis.Count; j++)
                {
                    var a = vis[i]; var b = vis[j];
                    bool ex = exempt != null && (exempt(a.object_id, b.object_id) || exempt(b.object_id, a.object_id));
                    float iou = IoU(a, b);
                    if (!ex && iou > MaxPairIoU) rep.issues.Add(new Issue { rule = "box_overlap", a = a.object_id, b = b.object_id, value = iou });
                    if (a.asset_id != b.asset_id) continue;
                    float sep = Mathf.Sqrt((a.Cx - b.Cx) * (a.Cx - b.Cx) + (a.Cy - b.Cy) * (a.Cy - b.Cy)) / W;
                    if (sep < MinSameAssetCenterSep) rep.issues.Add(new Issue { rule = "same_asset_close", a = a.object_id, b = b.object_id, value = sep });
                    float dh = Mathf.Abs(a.apparent_heading_deg - b.apparent_heading_deg);
                    float ds = Mathf.Abs(Mathf.Log(Mathf.Max(a.Hgt, 1f) / Mathf.Max(b.Hgt, 1f)));
                    if (dh < LookalikeHeadingDeg && ds < LookalikeLogScale) rep.issues.Add(new Issue { rule = "same_asset_lookalike", a = a.object_id, b = b.object_id, value = dh });
                }
            foreach (var g in vis.GroupBy(v => v.asset_id).Where(g => g.Count() > MaxSameAsset))
                rep.issues.Add(new Issue { rule = "same_asset_count", a = g.Key, b = "", value = g.Count() });
            return rep;
        }
    }
}

namespace PortCCTV
{
    /// <summary>
    /// Default map scenes are hand-written lists (berth rows, anchorage, traffic). Diversify swaps a vessel to another allowed
    /// hull of similar length (same map pool) when its hull is already used twice in the scene or by a vessel within
    /// MinSameAssetWorldM, so neighbouring berths do not show the same model. Positions and headings are kept; the immersion
    /// becomes the new hull's catalog default. Vessels with no admissible substitute are dropped (listed in the scenario notes).
    /// The screen rules (PlacementQuality) are then checked for every camera of the map by an EditMode test.
    /// </summary>
    public static class DefaultSceneLayout
    {
        public const float MinSameAssetWorldM = 600f;

        public static readonly System.Collections.Generic.Dictionary<string, string[]> Pools = new System.Collections.Generic.Dictionary<string, string[]>
        {
            { ContainerTerminal01.Id, new[] { "feeder_ship", "container_ship_gogi", "container_ship_rm02", "container_ship_loaded", "general_cargo", "tug_harbor", "tugboat", "pilot_boat", "patrol_boat", "police_boat", "sar_boat", "barge", "rib", "motorboat", "bulk_carrier", "lng_carrier" } },
            { FishingPort01.Id, new[] { "korean_fishing_boat", "fishing_boat_iii", "trawler", "fishing_vessel", "inshore_fishing_boat", "fishing_boat", "small_motorboat", "cabin_cruiser", "motorboat", "rib", "crrc", "speedboat", "police_boat", "patrol_boat", "sar_boat", "ferry", "barge" } },
            { Marina01.Id, new[] { "sailing_yacht", "cabin_cruiser", "motorboat", "speedboat", "small_motorboat", "rib", "crrc", "sailing_dinghy", "kayak", "canoe", "fishing_boat_iii", "police_boat", "sar_boat", "patrol_boat", "motor_yacht" } },
        };

        public static ScenarioConfig Diversify(ScenarioConfig s, VesselCatalog cat)
        {
            if (cat == null || !Pools.TryGetValue(s.harbor_id ?? "", out var pool)) return s;
            float Len(string id) { var e = cat.Find(id); return e?.geometry != null ? e.geometry.bounds_max.x - e.geometry.bounds_min.x : -1f; }
            var kept = new System.Collections.Generic.List<VesselPlacement>();
            var dropped = new System.Collections.Generic.List<string>();
            var count = new System.Collections.Generic.Dictionary<string, int>();
            bool Ok(string a, VesselPlacement v) =>
                (!count.TryGetValue(a, out var c) || c < PlacementQuality.MaxSameAsset)
                && !kept.Any(k => k.asset_id == a && new Vector2(k.x - v.x, k.z - v.z).magnitude < MinSameAssetWorldM);
            for (int i = 0; i < s.vessels.Count; i++)
            {
                var v = s.vessels[i];
                if (!Ok(v.asset_id, v))
                {
                    float L = Len(v.asset_id);
                    string sub = null;
                    for (int k = 0; k < pool.Length && sub == null; k++)
                    {
                        var r = pool[(i + k) % pool.Length];
                        float Lr = Len(r);
                        if (r == v.asset_id || Lr < 0f || L < 0f || Lr > 1.05f * L || Lr < 0.6f * L) continue;   // never longer: berths are spaced for the original hull
                        if (Ok(r, v)) sub = r;
                    }
                    if (sub == null) { dropped.Add($"{v.object_id}:{v.asset_id}"); continue; }
                    v.asset_id = sub;
                    v.immersion_m = cat.Find(sub).default_immersion_m;
                }
                count[v.asset_id] = (count.TryGetValue(v.asset_id, out var n) ? n : 0) + 1;
                kept.Add(v);
            }
            s.vessels = kept;
            s.notes = (s.notes ?? "") + $" [default layout {PlacementQuality.Version}: hulls diversified" + (dropped.Count > 0 ? "; dropped " + string.Join(",", dropped) : "") + "]";
            return s;
        }
    }
}
