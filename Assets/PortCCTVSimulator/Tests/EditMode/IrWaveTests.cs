using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEditor.SceneManagement;

namespace PortCCTV.Tests
{
    /// <summary>2026-11-12: night IR illuminator and harbour water mask / wave strength.</summary>
    public class IrWaveTests
    {
        SimWorld _w;
        [SetUp] public void SetUp() { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); _w = new SimWorld(); _w.Initialize(); }
        [TearDown] public void TearDown() => _w.Dispose();

        /// <summary>Fishing-port coast road: guard rail in front, fishing boat at ~50 m, ferry ~640 m out (the validation view).</summary>
        static ScenarioConfig RoadView(bool ir, string env = "night_clear")
        {
            var s = SimulatorApp.DefaultScenarioFor(FishingPort01.Id);
            s.environment.preset_id = env; s.environment.water_preset_id = "calm";
            var pos = new Vector3(300f, 7f, -7.5f); var d = new Vector3(230f, 0f, -150f) - pos;
            s.camera_override = new CameraOverride
            {
                enabled = true, position = pos, vertical_fov_deg = 46f,
                euler_deg = new Vector3(-Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 0f),
            };
            s.vessels.Add(new VesselPlacement { object_id = "ir_mid", asset_id = "korean_fishing_boat", x = 282f, z = -58f, heading_deg = 300f, immersion_m = 0.45f });
            s.vessels.Add(new VesselPlacement { object_id = "ir_far", asset_id = "ferry", x = 150f, z = -620f, heading_deg = 250f, immersion_m = 0.4f });
            s.capture.width = 960; s.capture.height = 540;
            s.sensor = SensorConfig.Preset(ir ? "night_ir_illum" : "night_ir");
            s.sensor.gain = 4f; s.sensor.noise_read_dn = 0f; s.sensor.noise_shot = 0f; s.sensor.jpeg_quality = 0;
            return s;
        }

        static float Mean(Color32[] px, int W, int H, System.Func<int, int, bool> sel)
        {
            double a = 0; int n = 0;
            for (int y = 0; y < H; y += 2) for (int x = 0; x < W; x += 2) if (sel(x, y)) { var c = px[y * W + x]; a += (c.r + c.g + c.b) / 3.0; n++; }
            return n > 0 ? (float)(a / n) : -1f;
        }

        static Color32[] Load(string dir, string f, out int W, out int H)
        {
            var t = new Texture2D(2, 2); t.LoadImage(File.ReadAllBytes(Path.Combine(dir, f)));
            W = t.width; H = t.height; var px = t.GetPixels32(); Object.DestroyImmediate(t); return px;   // bottom-up rows
        }

        [Test]
        public void IrIlluminator_LightsTheNearScene_NotTheShadowsOrFarSea_AndKeepsTheMasks()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "portcctv_ir_" + System.Guid.NewGuid().ToString("N"));
            Debug.Log("[IrTest] output " + tmp);
            _w.Apply(RoadView(false));
            var off = CaptureService.Capture(_w, Path.Combine(tmp, "off"), "off");
            Assert.IsTrue(off.success, off.error);
            Assert.IsNull(off.metadata.sensor.ir == null || !off.metadata.sensor.ir.on ? null : off.metadata.sensor.ir, "IR off must not write an IR record");
            // a 2 m block on the verge 12 m in front of the camera: a known occluder for the shadow check
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "ir_test_block"; block.transform.position = new Vector3(292f, 3.5f, -19f); block.transform.localScale = Vector3.one * 2f;
            _w.Apply(RoadView(true));
            var on = CaptureService.Capture(_w, Path.Combine(tmp, "on"), "on");
            // the same with the illuminator 3 m lower on the pole: its shadows become visible from the camera (built into the
            // housing, light and camera see the same surfaces and shadows hide behind their occluders)
            var low = RoadView(true); low.sensor.ir_mount_drop_m = 3f; _w.Apply(low);
            var lowRes = CaptureService.Capture(_w, Path.Combine(tmp, "low"), "low");
            Object.DestroyImmediate(block);
            Assert.IsTrue(lowRes.success, lowRes.error);
            var lk = lowRes.metadata.sensor.ir.checks;
            Debug.Log("[IrTest] low mount checks " + JsonUtility.ToJson(lk));
            Assert.Greater(lk.shadowed, 5, "visible umbra samples behind the block");
            Assert.Less(lk.shadow_leak_ratio, 0.1f, $"IR leaks into shadows: shadowed {lk.shadowed_mean} vs lit {lk.near_lit_mean}");
            Assert.AreEqual(0f, lowRes.metadata.sensor.ir.dome_reflection, "external illuminator: no light inside the dome");
            Assert.IsTrue(on.success, on.error);
            var ir = on.metadata.sensor.ir;
            Assert.IsNotNull(ir); Assert.IsTrue(ir.on); Assert.AreEqual("wide", ir.profile);
            var ck = ir.checks;
            Debug.Log("[IrTest] built-in checks " + JsonUtility.ToJson(ck));
            Assert.Greater(ck.near_lit, 30, "near lit samples"); Assert.Greater(ck.far_sea, 30, "far sea samples");
            Assert.Greater(ck.near_lit_mean, 0.05f, "near surfaces must be lit by the IR beam");
            if (ck.shadowed > 0) Assert.Less(ck.shadow_leak_ratio, 0.1f, $"IR leaks into shadows: shadowed {ck.shadowed_mean} vs lit {ck.near_lit_mean}");
            Assert.Less(ck.far_sea_mean, 0.002f, "far sea beyond the beam must get no IR");
            if (ck.outside_beam_mean >= 0f) Assert.Less(ck.outside_beam_mean, ck.near_lit_mean * 0.3f, "outside the cone");
            // masks keep their geometric meaning (identical with IR on / off)
            CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(tmp, "off", "mask_instances.png")), File.ReadAllBytes(Path.Combine(tmp, "on", "mask_instances.png")));
            // in the final image: the lower (near) part brightens, the sky / far horizon band does not
            var a = Load(Path.Combine(tmp, "off"), "rgb.png", out int W, out int H); var b = Load(Path.Combine(tmp, "on"), "rgb.png", out _, out _);
            System.Func<int, int, bool> near = (x, y) => y < H * 0.25f, top = (x, y) => y > H * 0.75f;
            Assert.Greater(Mean(b, W, H, near), Mean(a, W, H, near) + 8f, "near foreground brighter with IR");
            Assert.Less(Mathf.Abs(Mean(b, W, H, top) - Mean(a, W, H, top)), 2f, "sky band unchanged");
            Directory.Delete(tmp, true);
        }

        [Test]
        public void IrDome_ReflectsTheOwnIlluminatorInLensDrops_AndCoverIsRecorded()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "portcctv_irdome_" + System.Guid.NewGuid().ToString("N"));
            Debug.Log("[IrTest] output " + tmp);
            var s = RoadView(true, "night_overcast");
            s.environment.weather.precipitation = "rain"; s.environment.weather.intensity = 0.7f; s.environment.weather.overcast = true;
            s.sensor.ir_dome = 1; _w.Apply(s);
            var dome = CaptureService.Capture(_w, Path.Combine(tmp, "dome"), "dome");
            s.sensor.ir_dome = 0; _w.Apply(s);
            var flat = CaptureService.Capture(_w, Path.Combine(tmp, "flat"), "flat");
            Assert.IsTrue(dome.success && flat.success);
            Assert.Greater(dome.metadata.sensor.ir.dome_reflection, 0f); Assert.AreEqual(0f, flat.metadata.sensor.ir.dome_reflection);
            Assert.Greater(dome.metadata.sensor.lens_drop_cover_fraction_image, 0.005f);
            Assert.AreEqual(dome.metadata.sensor.lens_drop_cover_fraction_image, flat.metadata.sensor.lens_drop_cover_fraction_image, 1e-6f, "same drops, same seed / time");
            var a = Load(Path.Combine(tmp, "flat"), "rgb.png", out int W, out int H); var b = Load(Path.Combine(tmp, "dome"), "rgb.png", out _, out _);
            var rain = LensRain.Make(s.sensor.seed, dome.metadata.sensor.lens_drop_density, s.navigation.sim_time_s, W, H);
            float inDrop = Mean(b, W, H, (x, y) => rain.Covered(new Vector2(x + 0.5f, H - 1 - y + 0.5f))) - Mean(a, W, H, (x, y) => rain.Covered(new Vector2(x + 0.5f, H - 1 - y + 0.5f)));
            float outDrop = Mean(b, W, H, (x, y) => !rain.Covered(new Vector2(x + 0.5f, H - 1 - y + 0.5f))) - Mean(a, W, H, (x, y) => !rain.Covered(new Vector2(x + 0.5f, H - 1 - y + 0.5f)));
            Assert.Greater(inDrop, 3f, "dome IR haze inside the drops");
            Assert.Less(Mathf.Abs(outDrop), 0.25f * inDrop, "little change between drops (only the faint film edges)");
            Directory.Delete(tmp, true);
        }

        [Test]
        public void LongRangeIlluminator_OnTheHillsideMast_LightsTheEntrance()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "portcctv_irlong_" + System.Guid.NewGuid().ToString("N"));
            Debug.Log("[IrTest] output " + tmp);
            var s = SimulatorApp.DefaultScenarioFor(FishingPort01.Id);
            s.camera_preset_id = "fp_B_hillside"; s.environment.preset_id = "night_clear"; s.environment.water_preset_id = "calm";
            s.capture.width = 960; s.capture.height = 540;
            s.sensor = SensorConfig.Preset("night_ir_illum"); s.sensor.noise_read_dn = 0f; s.sensor.noise_shot = 0f; s.sensor.jpeg_quality = 0;
            _w.Apply(s);
            // a known target on the beam axis, 3 m above the water: a 6 m block ~190 m out
            var cam = _w.CctvCamera; var f = cam.transform.forward;
            float t = (3f - cam.transform.position.y) / f.y;
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "ir_long_target"; block.transform.position = cam.transform.position + f * t; block.transform.localScale = Vector3.one * 6f;
            block.transform.rotation = Quaternion.LookRotation(new Vector3(f.x, 0f, f.z));
            var res = CaptureService.Capture(_w, tmp, "long");
            Object.DestroyImmediate(block);
            Assert.IsTrue(res.success, res.error);
            var ir = res.metadata.sensor.ir;
            Assert.AreEqual("long", ir.profile); Assert.IsFalse(ir.dome); Assert.AreEqual(400f, ir.range_m);
            Assert.Greater(ir.mount_unshadowed, 0, "the camera mast next to the illuminator");
            var px = Load(tmp, "ir_component.png", out int W, out int H);
            int cx = W / 2, cy = H / 2;                                                // the block sits on the optical axis
            float target = 0f; int n = 0;
            for (int y = cy - 3; y <= cy + 3; y++) for (int x = cx - 3; x <= cx + 3; x++) { target += px[y * W + x].r; n++; }
            target /= n;
            float around = Mean(px, W, H, (x, y) => Mathf.Abs(y - cy) < 40 && Mathf.Abs(x - cx) > 60 && Mathf.Abs(x - cx) < 140);
            Debug.Log($"[IrTest] long beam target {target} at {t:F0} m, water beside it {around}, checks " + JsonUtility.ToJson(ir.checks));
            Assert.Greater(target, 40f, "a target on the beam axis ~190 m out must be lit");
            Assert.Less(around, 4f, "water around it stays dark under IR");
            Assert.Less(ir.checks.far_sea_mean, 0.002f);
            Directory.Delete(tmp, true);
        }

        [Test]
        public void HarbourMask_CalmsTheInnerHarbour_AndRenderedWaterMatchesTheVesselHeightQuery()
        {
            var t = HarborWaterMask.Build(HarborLibrary.Get(FishingPort01.Id), out var f);
            float inner = HarborWaterMask.Sample(t, new Vector2(-60f, 20f)).x, entrance = HarborWaterMask.Sample(t, new Vector2(5f, -100f)).x, sea = HarborWaterMask.Sample(t, new Vector2(0f, -900f)).x;
            Assert.Less(inner, 0.25f); Assert.Greater(sea, 0.99f); Assert.That(entrance, Is.InRange(inner, sea));
            // the seaward side of the east breakwater is open sea (not sheltered by the harbour)
            Assert.Greater(HarborWaterMask.Sample(t, new Vector2(280f, -60f)).x, 0.99f, "outside the east breakwater");
            Object.DestroyImmediate(t);

            foreach (var mask in new[] { "on", "off" })
            {
                // west headland camera: inner harbour and the entrance (open sea from ~300 m)
                var s = SimulatorApp.DefaultScenarioFor(FishingPort01.Id);
                s.camera_preset_id = "fp_B_hillside"; s.vessels.Clear();
                s.environment.water_preset_id = "rough"; s.environment.water_mask = mask;
                _w.Apply(s);
                var rep = WaterProbe.Run(_w);
                var ins = rep.zones.First(z => z.zone == "inside_harbour"); var sea2 = rep.zones.First(z => z.zone == "open_sea");
                var mid = rep.zones.First(z => z.zone == "open_sea_300_700m");
                Debug.Log($"[WaterProbe] hillside mask {mask}: " + JsonUtility.ToJson(rep));
                Assert.Greater(ins.samples, 100, mask); Assert.Greater(mid.samples, 40, mask);
                Assert.Less(ins.p95_abs_diff_m, 0.25f, $"mask {mask}: rendered surface vs vessel height query inside the harbour");
                Assert.Less(mid.p95_abs_diff_m, 0.6f, $"mask {mask}: open sea 300-700 m");
                if (mask == "on") Assert.Less(ins.query_height_std_m, 0.5f * sea2.query_height_std_m, "inner harbour calmer than the open sea");
                else Assert.Greater(ins.query_height_std_m, 0.7f * sea2.query_height_std_m, "without the mask the harbour is as rough as the sea");
                // coast-road camera 7 m above the outer sea: near open water
                var r = RoadView(false, "day_clear"); r.vessels.Clear(); r.sensor.enabled = false;
                r.environment.water_preset_id = "rough"; r.environment.water_mask = mask;
                _w.Apply(r);
                var rep2 = WaterProbe.Run(_w);
                var near = rep2.zones.First(z => z.zone == "open_sea_0_300m");
                Debug.Log($"[WaterProbe] road mask {mask}: " + JsonUtility.ToJson(rep2));
                Assert.Greater(near.samples, 100, mask);
                Assert.Less(near.p95_abs_diff_m, 0.35f, $"mask {mask}: rendered surface vs vessel height query, open sea within 300 m");
            }
        }
    }
}
