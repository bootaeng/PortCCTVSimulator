using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using PortCCTV.Research;

namespace PortCCTV.Tests
{
    /// <summary>2026-11-19 framing redesign (framing_v2_20261119): research presets against the measured real reference,
    /// legacy presets kept for older scenario files, episode profile per framing group, framing recorded in the capture.</summary>
    public class FramingTests
    {
        SimWorld _w;
        static readonly string[] Maps = { ContainerTerminal01.Id, FishingPort01.Id, Marina01.Id };

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _w = new SimWorld();
            _w.Initialize();
        }

        [TearDown]
        public void TearDown() => _w.Dispose();

        static float HorizonRow(CameraPreset p) => 0.5f - Mathf.Tan(p.euler_deg.x * Mathf.Deg2Rad) / (2f * Mathf.Tan(p.vertical_fov_deg * 0.5f * Mathf.Deg2Rad));

        [Test]
        public void ResearchPresets_AreLowShoreViews_WithTheHorizonNearMidFrame()
        {
            var research = Maps.SelectMany(CameraPresetLibrary.ResearchForHarbor).ToList();
            Assert.AreEqual(11, research.Count);
            Assert.GreaterOrEqual(research.Count(p => p.framing == CameraPresetLibrary.FramingSmd), 6, "the SMD on-shore type is the main group");
            Assert.GreaterOrEqual(research.Count(p => p.framing == CameraPresetLibrary.FramingSeaShips), 3);
            foreach (var hid in Maps)
            {
                var r = CameraPresetLibrary.ResearchForHarbor(hid);
                Assert.IsTrue(r.Any(p => p.framing == CameraPresetLibrary.FramingSmd) && r.Any(p => p.framing == CameraPresetLibrary.FramingSeaShips), hid + ": both groups on every map");
            }
            foreach (var p in research)
            {
                float hz = HorizonRow(p);
                // SMD VIS_Onshore train / validation groups: horizon row 0.27 .. 0.86 of the image height, median 0.52
                Assert.That(hz, Is.InRange(0.33f, 0.62f), p.id + " horizon row");
                Assert.AreEqual(0f, p.euler_deg.z, p.id + " no roll");
                if (p.framing == CameraPresetLibrary.FramingSmd)
                {
                    Assert.LessOrEqual(p.position.y, 8f, p.id + " low mount");
                    Assert.LessOrEqual(Mathf.Abs(p.euler_deg.x), 2.5f, p.id + " near-level view");
                }
                else Assert.LessOrEqual(p.position.y, 15f, p.id);
            }
            // not one viewpoint and magnification for all cameras
            Assert.Greater(research.Max(p => p.vertical_fov_deg) - research.Min(p => p.vertical_fov_deg), 8f);
            Assert.Greater(research.Max(HorizonRow) - research.Min(HorizonRow), 0.12f);
        }

        [Test]
        public void OverviewPresets_KeepTheir20261116Poses_RetiredOnesAreNoCameras_MapAnchorsStay()
        {
            // 2026-11-20: five elevated views of the 2026-11-16 set stay as current presets, six are retired
            var overview = Maps.SelectMany(CameraPresetLibrary.ForHarbor).Where(p => p.framing == CameraPresetLibrary.FramingOverview).Select(p => p.id).OrderBy(i => i).ToList();
            CollectionAssert.AreEqual(new[] { "ct_A_quay_east_mast", "ct_B_breakwater_head", "ct_C_admin_roof", "fp_A_market_roof", "mr_C_breakwater" }, overview);
            Assert.AreEqual(16, Maps.SelectMany(CameraPresetLibrary.ForHarbor).Count(), "11 research + 5 overview presets");
            void Same(string id, Vector3 pos, Vector3 euler, float fov)
            {
                var p = CameraPresetLibrary.Get(id);
                Assert.Less(Vector3.Distance(p.position, pos), 1e-4f, id);
                Assert.Less(Vector3.Distance(p.euler_deg, euler), 1e-4f, id);
                Assert.AreEqual(fov, p.vertical_fov_deg, 1e-4f, id);
                Assert.IsFalse(p.display_name.Contains("2026-11-16") || UiText.Camera(p).Contains("이전 구도"), id + " is a current preset");
            }
            Same("ct_A_quay_east_mast", new Vector3(-60f, 22.5f, 66f), new Vector3(5f, 155f, 0f), 24f);
            Same("ct_B_breakwater_head", new Vector3(232f, 13.5f, -463f), new Vector3(5f, 125f, 0f), 32f);
            Same("ct_C_admin_roof", new Vector3(340f, 31f, 65f), new Vector3(7f, 182f, 0f), 28f);
            Same("fp_A_market_roof", new Vector3(-30f, 15f, 65f), new Vector3(5f, 170f, 0f), 28f);
            Same("mr_C_breakwater", new Vector3(96f, 12.5f, -209f), new Vector3(5f, 130f, 0f), 32f);
            var retired = new[] { "ct_D_hill_tower", "ct_E_basin_breakwater", "fp_B_hillside", "fp_C_breakwater", "mr_A_promenade", "mr_B_tower" };
            foreach (var id in retired)
            {
                Assert.IsFalse(CameraPresetLibrary.Exists(id), id + " is no camera any more");
                // ... but the map is still built around its pose (hill, mast, distant towers, tree-free corridor)
                Assert.IsTrue(Maps.Any(m => CameraPresetLibrary.LayoutViewsForHarbor(m).Any(v => v.id == id)), id + " stays a map anchor");
            }
            var s = SimulatorApp.DefaultScenarioFor(Marina01.Id); s.camera_preset_id = "mr_B_tower";
            Assert.Throws<System.ArgumentException>(() => SimWorld.Validate(s), "a scenario file naming a retired preset is refused, not rendered from another pose");
            Assert.AreEqual(22, Maps.Sum(m => CameraPresetLibrary.LayoutViewsForHarbor(m).Length), "the map layout still sees all 22 poses of 2026-11-19");
        }

        [Test]
        public void ResearchCameras_SeeWater_NotTheirOwnMount()
        {
            // the centre column below the horizon must reach the water of a navigable region within 400 m for every research camera
            foreach (var hid in Maps)
                foreach (var cam in CameraPresetLibrary.ResearchForHarbor(hid))
                {
                    var s = SimulatorApp.DefaultScenarioFor(hid); s.vessels.Clear(); s.camera_preset_id = cam.id;
                    _w.Apply(s);
                    var c = _w.CctvCamera; c.aspect = 16f / 9f;
                    int water = 0, blocked = 0;
                    for (float u = 0.1f; u <= 0.9f; u += 0.1f)
                        for (float v = 0.62f; v <= 0.95f; v += 0.05f)
                        {
                            var ray = c.ViewportPointToRay(new Vector3(u, 1f - v, 0f));
                            if (ray.direction.y >= 0f) continue;
                            float t = (_w.Harbor.water_reference_y - ray.origin.y) / ray.direction.y;
                            var p = ray.GetPoint(t);
                            bool hitStructure = Physics.Raycast(ray, out var hit, t - 0.5f, ~0, QueryTriggerInteraction.Ignore) && _w.HarborColliders.Contains(hit.collider);
                            if (hitStructure) blocked++;
                            else if (_w.Harbor.InAnyWaterRegion(new Vector2(p.x, p.z))) water++;
                        }
                    Assert.GreaterOrEqual(water, 40, $"{cam.id}: water rays {water}, blocked {blocked}");
                    if (cam.framing == CameraPresetLibrary.FramingSmd) Assert.LessOrEqual(blocked, 6, $"{cam.id}: open-water view, blocked {blocked}");
                }
        }

        [Test]
        public void EpisodeProfile_Auto_PlacesSizedVessels_NearAndFar()
        {
            foreach (var cam in new[] { "ct_F_south_bw_sea", "fp_D_market_entrance" })
            {
                var preset = CameraPresetLibrary.Get(cam);
                string hid = cam.StartsWith("ct_") ? ContainerTerminal01.Id : FishingPort01.Id;
                var spec = new EpisodeSpec { experiment_id = "test_v2", harbor_id = hid, camera_preset_id = cam, seed = 3, frames = 4, profile = "auto" };
                var a = EpisodePlanner.Plan(_w, spec);
                Assert.AreEqual("planned", a.status, cam + ": " + string.Join(" | ", a.attempt_log.Skip(System.Math.Max(0, a.attempt_log.Count - 6))));
                var b = EpisodePlanner.Plan(_w, new EpisodeSpec { experiment_id = "test_v2", harbor_id = hid, camera_preset_id = cam, seed = 3, frames = 4, profile = "auto" });
                Assert.AreEqual(JsonUtility.ToJson(a), JsonUtility.ToJson(b), cam + ": same spec -> same episode");
                StringAssert.Contains(preset.framing, a.spec.profile_applied);
                Assert.Greater(a.spec.target_height_px_median, 0f);
                Assert.AreEqual(a.vessels.Count, a.vessels.Select(v => v.asset_id).Distinct().Count(), cam + ": no repeated hull in an episode");
                if (preset.framing == CameraPresetLibrary.FramingSmd) Assert.GreaterOrEqual(a.vessels.Count, 3, cam + ": several vessels per frame");
                var camXZ = new Vector2(preset.position.x, preset.position.z);
                foreach (var v in a.vessels.Where(v => v.role == "static"))
                {
                    float d = Vector2.Distance(v.entry, camXZ);
                    if (v.route_kind == "static_far") Assert.That(d, Is.InRange(a.spec.near_max_m, a.spec.far_max_m), cam + " far static");
                    else Assert.LessOrEqual(d, a.spec.near_max_m + 0.5f, cam + " near static");
                }
                float hzPx = HorizonRow(preset) * spec.height;
                foreach (var f in a.frames)
                {
                    Assert.GreaterOrEqual(f.movers_in_view_underway, 1);
                    foreach (var v in f.vessels.Where(v => v.in_view && v.role == "static"))
                    {
                        float h = v.y_max - v.y_min;
                        Assert.That(h, Is.InRange(a.spec.height_frac_min * spec.height - 1f, a.spec.height_frac_max * spec.height + 1f), cam + " " + v.object_id + " box height");
                        // low camera: the waterline of a vessel lies a little below the horizon line, never above it
                        if (preset.framing == CameraPresetLibrary.FramingSmd) Assert.That(v.y_max - hzPx, Is.InRange(-6f, 0.35f * spec.height), cam + " " + v.object_id + " waterline vs horizon");
                    }
                }
            }
        }

        [Test]
        public void EpisodePlanner_WithoutProfile_OnALegacyCamera_IsUnchanged()
        {
            // the 2026-11-16 path: no profile, uniform row choice, old static placement (regression pin of the first frame)
            var spec = new EpisodeSpec { experiment_id = "jmse_wx_ep", harbor_id = Marina01.Id, camera_preset_id = "mr_C_breakwater", seed = 41, frames = 4 };
            var a = EpisodePlanner.Plan(_w, spec);
            Assert.AreEqual("planned", a.status);
            Assert.AreEqual("", a.spec.profile_applied);
            Assert.AreEqual(0f, a.spec.target_height_px_median);
        }

        [Test]
        public void CaptureCameraRecord_CarriesFramingAndPresetSetVersion()
        {
            var s = SimulatorApp.DefaultScenarioFor(Marina01.Id); s.camera_preset_id = "mr_E_beach_bw_bay";
            _w.Apply(s);
            var r = CaptureService.MakeCameraRecord(_w, _w.CctvCamera, 1920, 1080);
            Assert.AreEqual("mr_E_beach_bw_bay", r.preset_id);
            Assert.AreEqual(CameraPresetLibrary.FramingSmd, r.framing);
            Assert.AreEqual(CameraPresetLibrary.PresetSetVersion, r.preset_set_version);
            Assert.Less(Vector3.Distance(r.position_world, new Vector3(268f, 7.5f, -154f)), 1e-3f);
            s.camera_preset_id = "mr_C_breakwater"; _w.Apply(s);
            Assert.AreEqual(CameraPresetLibrary.FramingOverview, CaptureService.MakeCameraRecord(_w, _w.CctvCamera, 1920, 1080).framing);
        }
    }
}
