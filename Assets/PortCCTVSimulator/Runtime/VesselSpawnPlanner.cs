using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PortCCTV
{
    /// <summary>Finds an unobstructed, visible water position for an interactively added vessel.</summary>
    public static class VesselSpawnPlanner
    {
        public static bool TryFind(SimWorld world, VesselAssetEntry entry, IList<VesselPlacement> existing,
            System.Random random, out VesselPlacement placement, Vector2? targetViewport = null)
        {
            placement = null;
            var cam = world.CctvCamera;
            var harbor = world.Harbor;
            var geom = entry.geometry;
            float length = geom.bounds_max.x - geom.bounds_min.x;
            float width = geom.bounds_max.z - geom.bounds_min.z;
            float near = Mathf.Clamp(length * 2.2f, 70f, 500f);
            float far = Mathf.Clamp(length * 7f + 300f, 400f, 1500f);
            float halfHorizontal = Mathf.Atan(Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad * 0.5f) * cam.aspect);
            float yaw = cam.transform.eulerAngles.y * Mathf.Deg2Rad;
            var forward = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
            var right = new Vector2(forward.y, -forward.x);
            var cameraXZ = new Vector2(cam.transform.position.x, cam.transform.position.z);
            var harborColliders = new HashSet<Collider>(world.HarborColliders);
            var occupied = new List<Vector2[]>();
            var occupiedScreen = new List<Vector2>();
            var occupiedImageBoxes = new List<Rect>();
            var sameTypeDepth = new List<float>();
            foreach (var v in existing)
            {
                var e = world.Catalog.Find(v.asset_id);
                if (e == null || e.geometry == null) continue;
                if (v.asset_id == entry.asset_id)
                    sameTypeDepth.Add(Vector2.Dot(new Vector2(v.x, v.z) - cameraXZ, forward));
                occupied.Add(Footprint(v, e.geometry, 0f));
                var vp = cam.WorldToViewportPoint(new Vector3(v.x, harbor.water_reference_y, v.z));
                if (vp.z > 0 && vp.x >= 0 && vp.x <= 1 && vp.y >= 0 && vp.y <= 1)
                    occupiedScreen.Add(new Vector2(vp.x, vp.y));
                if (ImageBox(cam, harbor, v, e.geometry, out var box)) occupiedImageBoxes.Add(box);
            }

            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < 640; i++)
            {
                // Screen placement casts through the clicked water. Auto placement samples the visible basin.
                Vector2 xz;
                float distance;
                if (targetViewport.HasValue && i < 240)
                {
                    var t = targetViewport.Value;
                    float jitter = i < 48 ? 0.015f : 0.075f;
                    var u = Mathf.Clamp01(t.x + ((float)random.NextDouble() * 2f - 1f) * jitter);
                    var v = Mathf.Clamp01(t.y + ((float)random.NextDouble() * 2f - 1f) * jitter);
                    var ray = cam.ViewportPointToRay(new Vector3(u, v, 0));
                    if (ray.direction.y >= -0.001f) continue;
                    float travel = (harbor.water_reference_y - ray.origin.y) / ray.direction.y;
                    if (travel <= 0f) continue;
                    var hit = ray.GetPoint(travel);
                    xz = new Vector2(hit.x, hit.z);
                    distance = Vector2.Distance(cameraXZ, xz);
                }
                else
                {
                    distance = Mathf.Lerp(near, far, (float)random.NextDouble());
                    float bearing = ((float)random.NextDouble() * 2f - 1f) * halfHorizontal * 0.78f;
                    xz = cameraXZ + forward * distance + right * (Mathf.Tan(bearing) * distance);
                }
                float cameraDepth = Vector2.Dot(xz - cameraXZ, forward);
                if (!targetViewport.HasValue && sameTypeDepth.Any(d => Mathf.Abs(d - cameraDepth) < Mathf.Max(35f, length * 0.3f)))
                    continue;
                float heading = Mathf.Repeat((random.Next(2) == 0 ? 90f : 270f) + (float)(random.NextDouble() * 56f - 28f), 360f);
                var candidate = new VesselPlacement
                {
                    asset_id = entry.asset_id, x = xz.x, z = xz.y, heading_deg = heading,
                    immersion_m = entry.default_immersion_m
                };
                string region = harbor.WaterRegionAt(xz);
                if (region == null) continue;
                if (harbor.RealismWarning(entry.asset_id, region)?.StartsWith("vessel_unusual_in_region:") == true) continue;
                var fp = Footprint(candidate, geom, Mathf.Clamp(length * 0.15f, 3f, 18f));
                bool inWater = true;
                for (int k = 0; k < 4; k++)
                    if (!harbor.InAnyWaterRegion(fp[k]) || !harbor.InAnyWaterRegion((fp[k] + fp[(k + 1) % 4]) * 0.5f))
                    { inWater = false; break; }
                if (!inWater || occupied.Any(o => Geometry2D.ConvexOverlap(fp, o))) continue;

                var center = new Vector3(xz.x, harbor.water_reference_y, xz.y);
                var localMid = (geom.bounds_min + geom.bounds_max) * 0.5f;
                var rot = Quaternion.Euler(0, heading, 0);
                center += rot * new Vector3(localMid.x, 0, localMid.z);
                var half = new Vector3((length + 4f) * 0.5f, 1f, (width + 4f) * 0.5f);
                if (Physics.OverlapBox(center, half, rot, ~0, QueryTriggerInteraction.Ignore)
                    .Any(c => harborColliders.Contains(c) && !c.gameObject.name.StartsWith("fender"))) continue;

                float minX = 1f, maxX = 0f, minY = 1f, maxY = 0f;
                bool visible = true;
                float rootY = harbor.water_reference_y - candidate.immersion_m - geom.hull_reference_min_y;
                foreach (float lx in new[] { geom.bounds_min.x, geom.bounds_max.x })
                foreach (float lz in new[] { geom.bounds_min.z, geom.bounds_max.z })
                foreach (float ly in new[] { geom.bounds_min.y, geom.bounds_max.y })
                {
                    var point = new Vector3(xz.x, rootY, xz.y) + rot * new Vector3(lx, ly, lz);
                    var vp = cam.WorldToViewportPoint(point);
                    if (vp.z <= 0f) { visible = false; break; }
                    minX = Mathf.Min(minX, vp.x); maxX = Mathf.Max(maxX, vp.x);
                    minY = Mathf.Min(minY, vp.y); maxY = Mathf.Max(maxY, vp.y);
                }
                if (!visible || minX < 0.04f || maxX > 0.96f || minY < 0.08f || maxY > 0.94f) continue;
                var imageBox = Rect.MinMaxRect(minX - 0.012f, minY - 0.012f, maxX + 0.012f, maxY + 0.012f);
                // Projected mast/rig bounds are often mostly transparent. Allow a little box overlap;
                // physical footprints and screen-centre separation still prevent boats piling up.
                if (occupiedImageBoxes.Any(b =>
                {
                    float xOverlap = Mathf.Max(0f, Mathf.Min(b.xMax, imageBox.xMax) - Mathf.Max(b.xMin, imageBox.xMin));
                    float yOverlap = Mathf.Max(0f, Mathf.Min(b.yMax, imageBox.yMax) - Mathf.Max(b.yMin, imageBox.yMin));
                    return xOverlap * yOverlap > imageBox.width * imageBox.height * 0.25f;
                })) continue;
                var screen = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
                float separation = occupiedScreen.Count == 0 ? 0.45f : occupiedScreen.Min(p => Vector2.Distance(p, screen));
                if (separation < 0.08f) continue;
                float screenWidth = maxX - minX;
                float depthSeparation = occupiedScreen.Count == 0 ? 0.2f : occupiedScreen.Min(p => Mathf.Abs(p.y - screen.y));
                float score = targetViewport.HasValue
                    ? -4f * Vector2.Distance(screen, targetViewport.Value) + 0.15f * Mathf.Min(separation, 0.5f)
                    : 4f * Mathf.Min(depthSeparation, 0.18f) + 0.25f * Mathf.Min(separation, 0.5f)
                        + 0.1f * Mathf.Clamp01(near / Mathf.Max(near, distance))
                        + 0.1f * Mathf.Min(screenWidth / 0.12f, 1f) + 0.02f * (float)random.NextDouble();
                if (score <= bestScore) continue;
                bestScore = score;
                placement = candidate;
            }
            if (targetViewport.HasValue && bestScore < -0.65f) placement = null;
            return placement != null;
        }

        static Vector2[] Footprint(VesselPlacement p, VesselGeometry g, float margin)
            => Geometry2D.OrientedRect(new Vector2(p.x, p.z), p.heading_deg,
                new Vector2(g.bounds_min.x - margin, g.bounds_min.z - margin),
                new Vector2(g.bounds_max.x + margin, g.bounds_max.z + margin));

        static bool ImageBox(Camera cam, HarborDefinition harbor, VesselPlacement p, VesselGeometry g, out Rect box)
        {
            box = default;
            float minX = 1f, maxX = 0f, minY = 1f, maxY = 0f;
            float rootY = harbor.water_reference_y - p.immersion_m - g.hull_reference_min_y;
            var root = new Vector3(p.x, rootY, p.z);
            var rot = Quaternion.Euler(0, p.heading_deg, 0);
            foreach (float lx in new[] { g.bounds_min.x, g.bounds_max.x })
            foreach (float lz in new[] { g.bounds_min.z, g.bounds_max.z })
            foreach (float ly in new[] { g.bounds_min.y, g.bounds_max.y })
            {
                var vp = cam.WorldToViewportPoint(root + rot * new Vector3(lx, ly, lz));
                if (vp.z <= 0) return false;
                minX = Mathf.Min(minX, vp.x); maxX = Mathf.Max(maxX, vp.x);
                minY = Mathf.Min(minY, vp.y); maxY = Mathf.Max(maxY, vp.y);
            }
            box = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return true;
        }
    }
}
