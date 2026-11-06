using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace PortCCTV
{
    /// <summary>Pixel box, top-left origin, pixel-edge convention: x in [0,W], y in [0,H]; xyxy.</summary>
    [Serializable]
    public class PixelBox
    {
        public bool valid;
        public float x_min, y_min, x_max, y_max;
        public float Width => x_max - x_min;
        public float Height => y_max - y_min;

        public static PixelBox Empty => new PixelBox { valid = false };

        public PixelBox ClipTo(int w, int h)
        {
            if (!valid) return Empty;
            var b = new PixelBox
            {
                x_min = Mathf.Clamp(x_min, 0, w), x_max = Mathf.Clamp(x_max, 0, w),
                y_min = Mathf.Clamp(y_min, 0, h), y_max = Mathf.Clamp(y_max, 0, h)
            };
            b.valid = b.x_max > b.x_min && b.y_max > b.y_min;
            return b;
        }
    }

    [Serializable]
    public class ProjectedBoxLabel
    {
        public string definition;
        public PixelBox unclipped = PixelBox.Empty;
        public PixelBox clipped = PixelBox.Empty;
        public bool truncated_by_image_border;
        public int vertices_used;
        public int vertices_behind_camera;
        public float vertex_fraction;   // fraction of all vertices that fall in this subset
    }

    [Serializable]
    public class VisibleMaskLabel
    {
        public string definition;
        public bool computed;
        public int pixel_count;
        public PixelBox bbox = PixelBox.Empty;    // bbox of mask pixels (pixel-edge convention)
        public bool touches_image_border;
        public string status;
    }

    [Serializable]
    public class ObjectLabel
    {
        public string object_id;
        public string asset_id;
        public string category;
        public int instance_index;
        public Vector3 root_position_world;
        public Quaternion root_rotation_world;
        public float heading_deg;
        public float immersion_m;
        /// <summary>Navigation at this frame: motion mode (none | auto | route), state and speed over ground (kn).</summary>
        /// <summary>exp(-distance / fog mean free path) from the camera to the hull centre (1 = no fog). A vessel can be in the
        /// visible mask yet almost invisible in heavy fog; filter on this if needed.</summary>
        public float fog_transmittance = 1f;
        /// <summary>compare_cpu_mask only: CPU ray-mask pixel count and IoU with the GPU mask for this object.</summary>
        public int cpu_mask_pixel_count = -1;
        public float cpu_gpu_mask_iou = -1f;
        /// <summary>Mean luma (0..255, Rec.709 of the final rgb.png) inside the visible mask; -1 if no pixels. Night / fog / glare filter.</summary>
        public float visible_mean_luma = -1f;
        /// <summary>sensor.lens drops: share of the visible-mask pixels behind a water drop on the lens (0 when none).</summary>
        public float lens_drop_cover_fraction = 0f;
        /// <summary>Navigation lights of this vessel whose bulbs face the camera (masthead, starboard, port, stern, allround, anchor_*);
        /// empty by day or when lights are off.</summary>
        public List<string> nav_lights_facing_camera = new List<string>();
        /// <summary>Paint applied to this vessel (appearance.paint / vessel paint): mode, scheme, colours, repainted slots.</summary>
        public PaintRecord paint;
        public string motion_mode = "none";
        public string nav_state = "fixed";
        public float speed_kn;
        public float hull_reference_min_y_world;
        public float render_min_y_world;
        public string hull_reference_node;
        public bool hull_reference_found;
        public string water_region;
        public List<string> placement_warnings = new List<string>();
        public ProjectedBoxLabel full_mesh_box;
        public ProjectedBoxLabel above_water_box;
        public VisibleMaskLabel visible_mask;
        public bool in_camera_frustum;
    }

    [Serializable]
    public class NonVesselObject
    {
        public string id;
        public string kind;
        public PixelBox full_box_clipped = PixelBox.Empty;
        public int visible_pixels_in_mask_as_occluder = -1;
    }

    [Serializable]
    public class LabelSet
    {
        public string label_status = "provisional_diagnostic";
        public bool training_ready = false;
        public string human_review = "not_performed";
        public int image_width, image_height;
        public string box_convention = "pixel-edge xyxy, origin top-left, x right, y down, units = output pixels";
        public List<ObjectLabel> objects = new List<ObjectLabel>();
        public List<NonVesselObject> non_vessel_objects = new List<NonVesselObject>();
        public List<string> definitions = new List<string>
        {
            "full_mesh_box: projection of ALL vessel render vertices (includes submerged hull and parts hidden by other objects).",
            "above_water_box: projection of vertices with world y > mean water plane (y=water_reference_y); ignores waves and occlusion.",
            "visible_mask (status gpu_instance_id_pass_depth_tested, default): the capture camera renders the same frame again and every vessel renderer is drawn with its instance index, depth-tested against that frame's depth buffer, so a pixel belongs to a vessel exactly where the vessel is the front-most opaque surface in the rendered image (rendered waves, water line, quays, buildings, other hulls). Not hidden by transparent effects (rain, snow, fog, decals); alpha-cut details count as solid; one sample per pixel (no anti-aliasing coverage). See fog_transmittance.",
            "visible_mask (status cpu_ray_*_mean_water_plane, capture.mask_source = cpu or mask_instances_cpu.png): one CPU ray per pixel centre against colliders with the MEAN water plane as occluder; waves, fog and transparency ignored.",
        };
    }

    public static class LabelComputer
    {
        public struct Projection
        {
            public Matrix4x4 viewProj;
            public int width, height;
            public float near;
            /// <summary>Optional lens distortion: projected points are mapped into the distorted output image.</summary>
            public LensModel lens;
        }

        public static Projection MakeProjection(Camera cam, int width, int height)
        {
            return new Projection
            {
                viewProj = cam.projectionMatrix * cam.worldToCameraMatrix,
                width = width, height = height, near = cam.nearClipPlane
            };
        }

        /// <summary>Projects a world point; returns false if behind the near plane. Output pixel coords, top-left origin.</summary>
        public static bool Project(in Projection p, Vector3 w, out Vector2 px)
        {
            var c = p.viewProj * new Vector4(w.x, w.y, w.z, 1f);
            if (c.w < p.near * 0.999f) { px = default; return false; }
            float nx = c.x / c.w, ny = c.y / c.w;
            px = new Vector2((nx * 0.5f + 0.5f) * p.width, (0.5f - ny * 0.5f) * p.height);
            if (p.lens != null && p.lens.OutputOf(px, out var d)) px = d;
            return true;
        }

        public static void ComputeBoxes(PlacedVessel v, in Projection p, float waterY, out ProjectedBoxLabel full, out ProjectedBoxLabel above)
        {
            full = new ProjectedBoxLabel { definition = "full_mesh_box" };
            above = new ProjectedBoxLabel { definition = "above_water_box" };
            var verts = v.entry.geometry.vertices_root_space;
            var m = v.root.transform.localToWorldMatrix;
            float fx0 = float.MaxValue, fy0 = float.MaxValue, fx1 = float.MinValue, fy1 = float.MinValue;
            float ax0 = float.MaxValue, ay0 = float.MaxValue, ax1 = float.MinValue, ay1 = float.MinValue;
            int aboveCount = 0;
            for (int i = 0; i < verts.Length; i++)
            {
                var w = m.MultiplyPoint3x4(verts[i]);
                bool isAbove = w.y > waterY;
                if (isAbove) aboveCount++;
                if (!Project(p, w, out var px))
                {
                    full.vertices_behind_camera++;
                    if (isAbove) above.vertices_behind_camera++;
                    continue;
                }
                full.vertices_used++;
                if (px.x < fx0) fx0 = px.x; if (px.x > fx1) fx1 = px.x;
                if (px.y < fy0) fy0 = px.y; if (px.y > fy1) fy1 = px.y;
                if (isAbove)
                {
                    above.vertices_used++;
                    if (px.x < ax0) ax0 = px.x; if (px.x > ax1) ax1 = px.x;
                    if (px.y < ay0) ay0 = px.y; if (px.y > ay1) ay1 = px.y;
                }
            }
            full.vertex_fraction = 1f;
            above.vertex_fraction = verts.Length > 0 ? (float)aboveCount / verts.Length : 0f;
            Finish(full, fx0, fy0, fx1, fy1, p);
            Finish(above, ax0, ay0, ax1, ay1, p);
        }

        static void Finish(ProjectedBoxLabel l, float x0, float y0, float x1, float y1, in Projection p)
        {
            if (l.vertices_used == 0) { l.unclipped = PixelBox.Empty; l.clipped = PixelBox.Empty; return; }
            l.unclipped = new PixelBox { valid = true, x_min = x0, y_min = y0, x_max = x1, y_max = y1 };
            l.clipped = l.unclipped.ClipTo(p.width, p.height);
            l.truncated_by_image_border = l.vertices_behind_camera > 0 || x0 < 0 || y0 < 0 || x1 > p.width || y1 > p.height;
        }

        /// <summary>
        /// CPU visible-instance mask: index[y*W+x] (top-left origin) = vessel instance_index or 0. One ray per pixel centre
        /// (stride s: one ray per s x s block, written to the whole block) against all colliders; the mean water plane occludes
        /// anything below it. Rays are cast only inside <paramref name="roi"/> (top-left pixel rectangles that contain
        /// every vessel's projected bounds; null = whole image): a pixel outside all of them cannot show a vessel, so the result
        /// is the same as casting everywhere, without the cost of millions of rays into a dense scene.
        /// </summary>
        public static byte[] ComputeVisibleMask(Camera cam, int width, int height, int stride, float waterY,
            Dictionary<Collider, int> colliderToInstance, List<RectInt> roi = null)
        {
            stride = Mathf.Max(1, stride);
            var mask = new byte[width * height];
            int cols = (width + stride - 1) / stride;
            int sampleRows = (height + stride - 1) / stride;
            var samples = new List<int>();
            for (int r = 0; r < sampleRows; r++)
            {
                int y = r * stride;
                for (int c = 0; c < cols; c++)
                {
                    int x = c * stride;
                    bool inside = roi == null;
                    if (!inside) foreach (var q in roi) if (x >= q.xMin && x < q.xMax && y >= q.yMin && y < q.yMax) { inside = true; break; }
                    if (inside) samples.Add(r * cols + c);
                }
            }
            var qp = new QueryParameters(~0, false, QueryTriggerInteraction.Ignore, false);
            const int batch = 32768;
            for (int s0 = 0; s0 < samples.Count; s0 += batch)
            {
                int n = Mathf.Min(batch, samples.Count - s0);
                var cmds = new NativeArray<RaycastCommand>(n, Allocator.TempJob);
                var hits = new NativeArray<RaycastHit>(n, Allocator.TempJob);
                try
                {
                    for (int k = 0; k < n; k++)
                    {
                        int idx = samples[s0 + k], x = (idx % cols) * stride, yTop = (idx / cols) * stride;
                        // Viewport: origin bottom-left. Pixel centre of top-left-indexed pixel (x, yTop).
                        var vp = new Vector3((x + 0.5f) / width, 1f - (yTop + 0.5f) / height, 0f);
                        var ray = cam.ViewportPointToRay(vp);
                        float dist = cam.farClipPlane;
                        if (ray.direction.y < -1e-6f)
                        {
                            float t = (waterY - ray.origin.y) / ray.direction.y;
                            if (t > 0) dist = Mathf.Min(dist, t);
                        }
                        cmds[k] = new RaycastCommand(ray.origin, ray.direction, qp, dist);
                    }
                    RaycastCommand.ScheduleBatch(cmds, hits, 64, 1).Complete();
                    for (int k = 0; k < n; k++)
                    {
                        var col = hits[k].collider;
                        if (col == null || !colliderToInstance.TryGetValue(col, out var inst)) continue;
                        int idx = samples[s0 + k], x0 = (idx % cols) * stride, yTop = (idx / cols) * stride;
                        for (int dy = 0; dy < stride && yTop + dy < height; dy++)
                            for (int dx = 0; dx < stride && x0 + dx < width; dx++)
                                mask[(yTop + dy) * width + x0 + dx] = (byte)inst;
                    }
                }
                finally { cmds.Dispose(); hits.Dispose(); }
            }
            return mask;
        }

        /// <summary>Top-left pixel rectangles (padded by 2 px) covering each vessel's projected render bounds; a vessel with a
        /// bounds corner behind the camera gets the whole image.</summary>
        public static List<RectInt> VesselScreenRects(Camera cam, int width, int height, IEnumerable<PlacedVessel> vessels)
        {
            var list = new List<RectInt>();
            foreach (var v in vessels)
            {
                var g = v.entry.geometry; float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue; bool behind = false;
                for (int k = 0; k < 8; k++)
                {
                    var c = new Vector3((k & 1) == 0 ? g.bounds_min.x : g.bounds_max.x, (k & 2) == 0 ? g.bounds_min.y : g.bounds_max.y, (k & 4) == 0 ? g.bounds_min.z : g.bounds_max.z);
                    var vp = cam.WorldToViewportPoint(v.root.transform.TransformPoint(c));
                    if (vp.z <= cam.nearClipPlane) { behind = true; break; }
                    float px = vp.x * width, py = (1f - vp.y) * height;
                    x0 = Mathf.Min(x0, px); x1 = Mathf.Max(x1, px); y0 = Mathf.Min(y0, py); y1 = Mathf.Max(y1, py);
                }
                if (behind) { list.Add(new RectInt(0, 0, width, height)); continue; }
                int ix0 = Mathf.Clamp(Mathf.FloorToInt(x0) - 2, 0, width), iy0 = Mathf.Clamp(Mathf.FloorToInt(y0) - 2, 0, height);
                int ix1 = Mathf.Clamp(Mathf.CeilToInt(x1) + 2, 0, width), iy1 = Mathf.Clamp(Mathf.CeilToInt(y1) + 2, 0, height);
                if (ix1 > ix0 && iy1 > iy0) list.Add(new RectInt(ix0, iy0, ix1 - ix0, iy1 - iy0));
            }
            return list;
        }

        public static VisibleMaskLabel SummariseMask(byte[] mask, int width, int height, int instance, int stride, string status = null)
        {
            var l = new VisibleMaskLabel
            {
                definition = "visible_mask",
                computed = true,
                status = status ?? (stride == 1 ? "cpu_ray_pixel_centre_mean_water_plane" : $"cpu_ray_stride_{stride}_mean_water_plane"),
            };
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1, count = 0;
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    if (mask[row + x] != instance) continue;
                    count++;
                    if (x < x0) x0 = x; if (x > x1) x1 = x;
                    if (y < y0) y0 = y; if (y > y1) y1 = y;
                }
            }
            l.pixel_count = count;
            if (count > 0)
            {
                l.bbox = new PixelBox { valid = true, x_min = x0, y_min = y0, x_max = x1 + 1, y_max = y1 + 1 };
                l.touches_image_border = x0 == 0 || y0 == 0 || x1 == width - 1 || y1 == height - 1;
            }
            return l;
        }
    }
}
