using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace PortCCTV
{
    [Serializable]
    public class CameraRecord
    {
        public string preset_id;
        public string mount_note;
        public Vector3 position_world;
        public Quaternion rotation_world;
        public Vector3 euler_deg;
        public float vertical_fov_deg;
        public float horizontal_fov_deg;
        public float near_m, far_m;
        public int width, height;
        public float aspect;
        public float[] world_to_camera_4x4_row_major;
        public float[] projection_4x4_row_major;
        public float fx_px, fy_px, cx_px, cy_px; // pinhole intrinsics, top-left origin, pixel-edge convention
        public string antialiasing = "None";
        public bool dynamic_resolution = false;
    }

    [Serializable]
    public class AssetRecord
    {
        public string asset_id;
        public string source_file;
        public string source_sha256;
        public string license;
        public string source_url;
        public string hull_reference_node;
        public bool hull_reference_found;
        public float hull_reference_min_y_local;
        public float render_min_y_local;
        public int label_vertex_count;
        public string material_note;
    }

    [Serializable]
    public class CaptureMetadata
    {
        public string metadata_schema = "portcctv.capture.v1";
        public string app_name = "PortCCTVSimulator";
        public string app_version;
        public string unity_version;
        public string hdrp_version = "16.0.6";
        public string platform;
        public string graphics_device;
        public string graphics_api;
        public string capture_utc;
        public string run_mode;           // interactive | batch | cli
        public string scenario_id;
        public string scenario_sha256;
        public string harbor_id;
        public string harbor_definition_sha256;
        public CameraRecord camera;
        /// <summary>Simulated time and per-vessel motion state (route, speed, course, start pose) for this frame.</summary>
        public NavigationRecord navigation;
        public ResolvedEnvironment environment;
        /// <summary>CCTV degradation actually applied to rgb.png (enabled=false: rgb.png is the clean render).</summary>
        public SensorRecord sensor;
        public List<AssetRecord> assets = new List<AssetRecord>();
        public List<string> files = new List<string>();
        public List<string> file_sha256 = new List<string>();
        public int warmup_frames;
        public double render_ms, mask_ms, total_ms;
        public string label_status = "provisional_diagnostic";
        public bool training_ready = false;
        public string determinism_note =
            "Inputs (scenario, presets, asset hashes, water time) are recorded; bitwise-identical HDRP output across runs/GPUs/drivers is NOT guaranteed.";
        public List<string> limitations = new List<string>
        {
            "Engine environment parameters (wind km/h, fog mean free path, EV) are not calibrated sea state, visibility or camera exposure.",
            "Vessel vertical placement uses the MEAN water plane; vessels do not float on the rendered waves.",
            "visible_mask (default GPU instance-id pass) follows the rendered water line and opaque occluders but is not hidden by rain, snow, fog, decals, glass or lens drops; one sample per pixel.",
            "Night lights: lamp / navigation-light intensities are engine values; bulbs have a minimum on-screen size of ~2 px (rendering floor, not photometry).",
            "Hull reference nodes are name-based candidates (semantic_region_confirmed=false); immersion_m is not a measured draft.",
        };
        public bool complete;
    }

    public class CaptureResult
    {
        public bool success;
        public string output_dir;
        public string error;
        public LabelSet labels;
        public CaptureMetadata metadata;
    }

    public static class CaptureService
    {
        public const string AppVersion = "0.1.0-mvp";

        /// <summary>
        /// Renders the fixed CCTV camera and writes rgb.png, mask_instances.png, overlay.png, labels.json, metadata.json,
        /// scenario.json, yolo_visible_bbox_provisional.txt into <paramref name="finalDir"/>.
        /// Writes into "<finalDir>.partial_<ts>" first and renames only on success; never overwrites an existing finalDir.
        /// </summary>
        public static CaptureResult Capture(SimWorld world, string finalDir, string runMode)
        {
            var res = new CaptureResult { output_dir = finalDir };
            if (Directory.Exists(finalDir) || File.Exists(finalDir))
            {
                res.error = "Output already exists (refusing to overwrite): " + finalDir;
                return res;
            }
            var cfg = world.Scenario;
            string partial = finalDir + ".partial_" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff");
            Directory.CreateDirectory(partial);
            var total = Stopwatch.StartNew();
            RenderTexture rt = null;
            var cam = world.CctvCamera;
            var prevTarget = cam.targetTexture;
            bool prevEnabled = cam.enabled;
            float prevWaterMult = world.Water.timeMultiplier;
            try
            {
                int W = cfg.capture.width, H = cfg.capture.height;
                world.ApplyCamera(cfg);
                world.Weather?.ResetDeterministic(cam);
                world.Lights?.UpdateForCamera(cam, H);
                var desc = new RenderTextureDescriptor(W, H, GraphicsFormat.R8G8B8A8_SRGB, 24) { msaaSamples = 1 };
                rt = new RenderTexture(desc) { name = "PortCCTV_Capture" };
                rt.Create();
                cam.enabled = true;
                cam.targetTexture = rt;
                cam.aspect = (float)W / H;

                var rsw = Stopwatch.StartNew();
                cam.Render(); // allocate water simulation resources
                world.FreezeWaterAt(cfg.environment.water_time_s);
                for (int i = 0; i < Mathf.Max(1, cfg.capture.warmup_frames); i++) { cam.Render(); AsyncGPUReadback.WaitAllRequests(); }
                world.Environment.water_time_actual_s = world.ReadWaterSimulationTime();

                var prevActive = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply(false);
                RenderTexture.active = prevActive;
                rsw.Stop();
                var rgbPixels = tex.GetPixels32(); // bottom-left origin
                for (int i = 0; i < rgbPixels.Length; i++) rgbPixels[i].a = 255;

                // ---- CCTV degradation (sensor) ----
                var sc = cfg.sensor ?? new SensorConfig();
                bool degrade = sc.enabled && world.Sensor != null && world.Sensor.Available;
                var sensorRec = new SensorRecord { enabled = degrade, config = sc.Clone() };
                LensModel lens = null;
                List<LensDrop> drops = null;
                if (degrade)
                {
                    if (sc.save_clean) { tex.SetPixels32(rgbPixels); WriteFile(partial, "rgb_clean.png", tex.EncodeToPNG()); }
                    float gain = sc.gain >= 0f ? sc.gain : SensorProcessor.AutoGain(world.Environment.exposure_ev100);
                    float density = SensorProcessor.DropDensity(sc, world.Environment.weather, cfg.environment.weather);
                    drops = SensorProcessor.MakeDrops(sc.seed, density, W, H);
                    lens = LensModel.Make(sc, W, H);
                    rgbPixels = world.Sensor.Process(rt, sc, gain, drops);
                    sensorRec.gain_applied = gain; sensorRec.lens_drop_density = density; sensorRec.lens_drops = drops;
                    sensorRec.distortion_scale = lens != null ? lens.scale : 1f; sensorRec.jpeg_quality = sc.jpeg_quality;
                }
                tex.SetPixels32(rgbPixels);
                WriteFile(partial, "rgb.png", tex.EncodeToPNG());

                // ---- labels ----
                var proj = LabelComputer.MakeProjection(cam, W, H);
                proj.lens = lens;
                float waterY = world.Harbor.water_reference_y;
                var labels = new LabelSet { image_width = W, image_height = H };
                var colToInst = new Dictionary<Collider, int>();
                foreach (var v in world.Vessels)
                    foreach (var c in v.colliders) colToInst[c] = v.instance_index;

                var msw = Stopwatch.StartNew();
                byte[] mask = null, cpuMask = null;
                string maskStatus = null;
                bool gpu = cfg.capture.mask_source != "cpu" && world.GpuMask != null && world.GpuMask.Available;
                if (cfg.capture.write_mask && gpu)
                {
                    mask = world.GpuMask.Render(cam, world.Vessels, W, H);   // same frozen water / camera as the RGB frame
                    maskStatus = GpuInstanceMask.Status;
                }
                if (cfg.capture.write_mask && (!gpu || cfg.capture.compare_cpu_mask))
                {
                    Physics.SyncTransforms();
                    cpuMask = LabelComputer.ComputeVisibleMask(cam, W, H, cfg.capture.mask_stride, waterY, colToInst, LabelComputer.VesselScreenRects(cam, W, H, world.Vessels));
                    if (!gpu) mask = cpuMask;
                }
                if (lens != null)
                {
                    // labels follow the distorted image (same mapping as the GPU pass, nearest neighbour)
                    if (mask != null) mask = lens.WarpMask(mask, W, H);
                    if (cpuMask != null) cpuMask = gpu ? lens.WarpMask(cpuMask, W, H) : mask;
                }
                msw.Stop();

                var planes = GeometryUtility.CalculateFrustumPlanes(cam);
                foreach (var v in world.Vessels)
                {
                    LabelComputer.ComputeBoxes(v, proj, waterY, out var full, out var above);
                    var g = v.entry.geometry;
                    var worldBounds = new Bounds(v.root.transform.TransformPoint((g.bounds_min + g.bounds_max) / 2), Vector3.zero);
                    foreach (var corner in Corners(g.bounds_min, g.bounds_max)) worldBounds.Encapsulate(v.root.transform.TransformPoint(corner));
                    var ol = new ObjectLabel
                    {
                        object_id = v.placement.object_id, asset_id = v.placement.asset_id, category = v.entry.category,
                        instance_index = v.instance_index,
                        root_position_world = v.root.transform.position, root_rotation_world = v.root.transform.rotation,
                        heading_deg = v.placement.heading_deg, immersion_m = v.placement.immersion_m,
                        hull_reference_min_y_world = v.HullReferenceMinWorldY, render_min_y_world = v.RenderMinWorldY,
                        hull_reference_node = g.hull_reference_node, hull_reference_found = g.hull_reference_found,
                        water_region = v.report.water_region, placement_warnings = new List<string>(v.report.warnings),
                        full_mesh_box = full, above_water_box = above,
                        visible_mask = mask != null
                            ? LabelComputer.SummariseMask(mask, W, H, v.instance_index, gpu ? 1 : cfg.capture.mask_stride, maskStatus)
                            : new VisibleMaskLabel { definition = "visible_mask", computed = false, status = "disabled" },
                        fog_transmittance = world.Environment.fog_enabled && world.Environment.fog_mean_free_path_m > 0f
                            ? Mathf.Exp(-Vector3.Distance(cam.transform.position, worldBounds.center) / world.Environment.fog_mean_free_path_m) : 1f,
                        in_camera_frustum = GeometryUtility.TestPlanesAABB(planes, worldBounds),
                    };
                    if (gpu && cpuMask != null && mask != null)
                    {
                        int inter = 0, uni = 0, cpuN = 0;
                        for (int i = 0; i < mask.Length; i++)
                        {
                            bool a = mask[i] == v.instance_index, b = cpuMask[i] == v.instance_index;
                            if (b) cpuN++;
                            if (a && b) inter++;
                            if (a || b) uni++;
                        }
                        ol.cpu_mask_pixel_count = cpuN; ol.cpu_gpu_mask_iou = uni > 0 ? (float)inter / uni : 1f;
                    }
                    if (mask != null)
                    {
                        double lsum = 0; int ln = 0, under = 0;
                        for (int i = 0; i < mask.Length; i++)
                        {
                            if (mask[i] != v.instance_index) continue;
                            int x = i % W, yTop = i / W;
                            var c = rgbPixels[(H - 1 - yTop) * W + x];
                            lsum += 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b; ln++;
                            if (drops != null && drops.Count > 0)
                            {
                                var src = lens != null ? lens.SourceOf(new Vector2(x + 0.5f, yTop + 0.5f)) : new Vector2(x + 0.5f, yTop + 0.5f);
                                if (SensorProcessor.UnderDrop(drops, src)) under++;
                            }
                        }
                        ol.visible_mean_luma = ln > 0 ? (float)(lsum / ln) : -1f;
                        ol.lens_drop_cover_fraction = ln > 0 ? (float)under / ln : 0f;
                    }
                    if (world.Lights != null && world.Lights.On) ol.nav_lights_facing_camera = world.Lights.VisibleNavLights(v);
                    ol.paint = v.paint;
                    var nr = world.Nav?.Record().vessels.FirstOrDefault(r => r.object_id == v.placement.object_id);
                    if (nr != null) { ol.motion_mode = nr.mode; ol.nav_state = nr.state; ol.speed_kn = nr.speed_kn; }
                    labels.objects.Add(ol);
                }
                foreach (var el in world.Harbor.elements.Where(e => e.is_vessel_like_non_vessel))
                {
                    var nv = new NonVesselObject { id = el.id, kind = el.kind };
                    var b = new Bounds(el.center, el.size);
                    if (world.HarborObjects.TryGetValue(el.id, out var hgo))
                    {
                        var rs = hgo.GetComponentsInChildren<Renderer>();
                        if (rs.Length > 0) { b = rs[0].bounds; foreach (var rr in rs) b.Encapsulate(rr.bounds); }
                    }
                    float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue; bool any = false;
                    foreach (var c in Corners(b.min, b.max))
                        if (LabelComputer.Project(proj, c, out var px))
                        {
                            any = true; x0 = Mathf.Min(x0, px.x); y0 = Mathf.Min(y0, px.y); x1 = Mathf.Max(x1, px.x); y1 = Mathf.Max(y1, px.y);
                        }
                    if (any) nv.full_box_clipped = new PixelBox { valid = true, x_min = x0, y_min = y0, x_max = x1, y_max = y1 }.ClipTo(W, H);
                    labels.non_vessel_objects.Add(nv);
                }

                if (mask != null)
                {
                    var mtex = new Texture2D(W, H, TextureFormat.RGBA32, false, true);
                    var mp = new Color32[W * H];
                    for (int y = 0; y < H; y++)
                        for (int x = 0; x < W; x++)
                        {
                            byte id = mask[y * W + x];
                            mp[(H - 1 - y) * W + x] = new Color32(id, 0, 0, 255);
                        }
                    mtex.SetPixels32(mp);
                    WriteFile(partial, "mask_instances.png", mtex.EncodeToPNG());
                    if (gpu && cpuMask != null)
                    {
                        for (int y = 0; y < H; y++)
                            for (int x = 0; x < W; x++) mp[(H - 1 - y) * W + x] = new Color32(cpuMask[y * W + x], 0, 0, 255);
                        mtex.SetPixels32(mp);
                        WriteFile(partial, "mask_instances_cpu.png", mtex.EncodeToPNG());
                    }
                    Util.SafeDestroy(mtex);
                }

                var overlay = OverlayRenderer.Draw(rgbPixels, W, H, mask, labels);
                tex.SetPixels32(overlay);
                WriteFile(partial, "overlay.png", tex.EncodeToPNG());
                Util.SafeDestroy(tex);

                // YOLO-style provisional file from the visible-mask bbox (class 0 = vessel). Diagnostic only.
                var yolo = new StringBuilder();
                foreach (var o in labels.objects)
                {
                    var bb = o.visible_mask.bbox;
                    if (!o.visible_mask.computed || !bb.valid) continue;
                    yolo.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "0 {0:F7} {1:F7} {2:F7} {3:F7}",
                        (bb.x_min + bb.x_max) / 2 / W, (bb.y_min + bb.y_max) / 2 / H, bb.Width / W, bb.Height / H));
                }
                WriteFile(partial, "yolo_visible_bbox_provisional.txt", Encoding.UTF8.GetBytes(yolo.ToString()));

                string scenarioJson = ScenarioIO.ToJson(cfg);
                WriteFile(partial, "scenario.json", new UTF8Encoding(false).GetBytes(scenarioJson));
                WriteFile(partial, "labels.json", new UTF8Encoding(false).GetBytes(JsonUtility.ToJson(labels, true)));

                total.Stop();
                var meta = new CaptureMetadata
                {
                    app_version = AppVersion,
                    unity_version = Application.unityVersion,
                    platform = Application.platform.ToString(),
                    graphics_device = SystemInfo.graphicsDeviceName,
                    graphics_api = SystemInfo.graphicsDeviceType.ToString(),
                    capture_utc = DateTime.UtcNow.ToString("o"),
                    run_mode = runMode,
                    scenario_id = cfg.scenario_id,
                    scenario_sha256 = ScenarioIO.Sha256Hex(scenarioJson),
                    harbor_id = world.Harbor.harbor_id,
                    harbor_definition_sha256 = world.Harbor.ComputeHash(),
                    navigation = world.Nav?.Record(),
                    camera = MakeCameraRecord(world, cam, W, H),
                    environment = world.Environment,
                    sensor = sensorRec,
                    warmup_frames = cfg.capture.warmup_frames,
                    render_ms = rsw.Elapsed.TotalMilliseconds,
                    mask_ms = msw.Elapsed.TotalMilliseconds,
                    total_ms = total.Elapsed.TotalMilliseconds,
                };
                foreach (var e in world.Vessels.Select(v => v.entry).Distinct())
                    meta.assets.Add(new AssetRecord
                    {
                        asset_id = e.asset_id, source_file = e.source_file, source_sha256 = e.source_sha256, license = e.license,
                        source_url = e.source_url, hull_reference_node = e.geometry.hull_reference_node,
                        hull_reference_found = e.geometry.hull_reference_found,
                        hull_reference_min_y_local = e.geometry.hull_reference_min_y, render_min_y_local = e.geometry.render_min_y,
                        label_vertex_count = e.geometry.vertices_root_space.Length, material_note = e.material_note,
                    });
                foreach (var f in Directory.GetFiles(partial).OrderBy(f => f, StringComparer.Ordinal))
                {
                    meta.files.Add(Path.GetFileName(f));
                    meta.file_sha256.Add(ScenarioIO.Sha256Hex(File.ReadAllBytes(f)));
                }
                meta.complete = true;
                WriteFile(partial, "metadata.json", new UTF8Encoding(false).GetBytes(JsonUtility.ToJson(meta, true)));

                if (Directory.Exists(finalDir)) throw new IOException("Output appeared during capture: " + finalDir);
                Directory.Move(partial, finalDir);
                res.success = true;
                res.labels = labels;
                res.metadata = meta;
            }
            catch (Exception ex)
            {
                res.error = ex.ToString();
                try { File.WriteAllText(Path.Combine(partial, "ERROR.txt"), ex.ToString()); } catch { /* keep original error */ }
                res.output_dir = partial;
                Debug.LogError("[PortCCTV] Capture failed: " + ex);
            }
            finally
            {
                cam.targetTexture = prevTarget;
                cam.enabled = prevEnabled;
                if (prevWaterMult > 0f) world.UnfreezeWater();
                if (rt != null) { rt.Release(); Util.SafeDestroy(rt); }
            }
            return res;
        }

        static void WriteFile(string dir, string name, byte[] data) => File.WriteAllBytes(Path.Combine(dir, name), data);

        static IEnumerable<Vector3> Corners(Vector3 a, Vector3 b)
        {
            for (int i = 0; i < 8; i++)
                yield return new Vector3((i & 1) == 0 ? a.x : b.x, (i & 2) == 0 ? a.y : b.y, (i & 4) == 0 ? a.z : b.z);
        }

        public static float[] RowMajor(Matrix4x4 m)
        {
            var r = new float[16];
            for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) r[i * 4 + j] = m[i, j];
            return r;
        }

        public static CameraRecord MakeCameraRecord(SimWorld world, Camera cam, int W, int H)
        {
            var p = world.ActiveCamera;
            float fy = H / (2f * Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad / 2f));
            float fx = fy; // square pixels, aspect = W/H
            return new CameraRecord
            {
                preset_id = p.id, mount_note = p.mount_note,
                position_world = cam.transform.position, rotation_world = cam.transform.rotation, euler_deg = p.euler_deg,
                vertical_fov_deg = cam.fieldOfView,
                horizontal_fov_deg = Camera.VerticalToHorizontalFieldOfView(cam.fieldOfView, (float)W / H),
                near_m = cam.nearClipPlane, far_m = cam.farClipPlane, width = W, height = H, aspect = cam.aspect,
                world_to_camera_4x4_row_major = RowMajor(cam.worldToCameraMatrix),
                projection_4x4_row_major = RowMajor(cam.projectionMatrix),
                fx_px = fx, fy_px = fy, cx_px = W / 2f, cy_px = H / 2f,
            };
        }
    }

    /// <summary>Software overlay (no GPU): mask tint + boxes. Used for capture QA images.</summary>
    public static class OverlayRenderer
    {
        static readonly Color32[] s_Palette =
        {
            new Color32(230, 25, 75, 255), new Color32(60, 180, 75, 255), new Color32(0, 130, 200, 255),
            new Color32(245, 130, 48, 255), new Color32(145, 30, 180, 255), new Color32(70, 240, 240, 255),
            new Color32(240, 50, 230, 255), new Color32(210, 245, 60, 255),
        };

        public static Color32 InstanceColor(int i) => s_Palette[(i - 1 + s_Palette.Length) % s_Palette.Length];

        /// <param name="rgb">bottom-left origin pixels</param>
        /// <param name="mask">top-left origin instance indices, or null</param>
        public static Color32[] Draw(Color32[] rgb, int W, int H, byte[] mask, LabelSet labels)
        {
            var o = (Color32[])rgb.Clone();
            if (mask != null)
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int id = mask[y * W + x];
                        if (id == 0) continue;
                        int k = (H - 1 - y) * W + x;
                        o[k] = Color32.Lerp(o[k], InstanceColor(id), 0.45f);
                    }
            int t = Mathf.Max(1, W / 960);
            foreach (var ob in labels.objects)
            {
                Rect(o, W, H, ob.full_mesh_box.clipped, new Color32(255, 220, 0, 255), t);
                Rect(o, W, H, ob.above_water_box.clipped, new Color32(0, 255, 255, 255), t);
                Rect(o, W, H, ob.visible_mask.bbox, InstanceColor(ob.instance_index), t + 1);
            }
            foreach (var nv in labels.non_vessel_objects)
                Rect(o, W, H, nv.full_box_clipped, new Color32(255, 255, 255, 255), t);
            return o;
        }

        static void Rect(Color32[] o, int W, int H, PixelBox b, Color32 c, int thick)
        {
            if (b == null || !b.valid) return;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(b.x_min), 0, W - 1), x1 = Mathf.Clamp(Mathf.CeilToInt(b.x_max) - 1, 0, W - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(b.y_min), 0, H - 1), y1 = Mathf.Clamp(Mathf.CeilToInt(b.y_max) - 1, 0, H - 1);
            for (int k = 0; k < thick; k++)
            {
                for (int x = x0; x <= x1; x++) { Put(o, W, H, x, y0 + k, c); Put(o, W, H, x, y1 - k, c); }
                for (int y = y0; y <= y1; y++) { Put(o, W, H, x0 + k, y, c); Put(o, W, H, x1 - k, y, c); }
            }
        }

        static void Put(Color32[] o, int W, int H, int x, int yTop, Color32 c)
        {
            if (x < 0 || x >= W || yTop < 0 || yTop >= H) return;
            o[(H - 1 - yTop) * W + x] = c;
        }
    }
}
