using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PortCCTV.Research;

namespace PortCCTV.Tests
{
    /// <summary>2026-11-16 scene rework: fog / transparency fix, screen layout rules, moving-vessel episodes.</summary>
    public class SceneQualityTests
    {
        SimWorld _w;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _w = new SimWorld();
            _w.Initialize();
        }

        [TearDown]
        public void TearDown() => _w.Dispose();

        [Test]
        public void BackgroundPrefabs_HaveNoTransparentMaterials()
        {
            // glTF transparent passes render without fog in the Windows player (docs/scene_fix_20261116/FOG_DEFECT.md)
            var bad = _w.Theme.prefabs.Where(p => p.prefab != null)
                .SelectMany(p => p.prefab.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(RenderDiagnostics.IsTransparent).Select(m => p.key + ":" + m.name))
                .Distinct().ToList();
            Assert.IsEmpty(bad, string.Join(", ", bad.Take(20)));
        }

        [Test]
        public void PlacementQuality_FlagsSameHullCloseLookalikeAndOverlap()
        {
            var s = SimulatorApp.DefaultScenarioFor(ContainerTerminal01.Id);
            s.vessels.Clear(); s.camera_preset_id = "ct_C_admin_roof";
            _w.Apply(s);
            var cam = _w.CctvCamera;
            var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.35f, 0f));
            var p = ray.GetPoint(-ray.origin.y / ray.direction.y);
            var vs = new System.Collections.Generic.List<VesselPlacement>
            {
                new VesselPlacement { object_id = "a", asset_id = "pilot_boat", x = p.x, z = p.z, heading_deg = 90f, immersion_m = 0.6f },
                new VesselPlacement { object_id = "b", asset_id = "pilot_boat", x = p.x + 25f, z = p.z, heading_deg = 92f, immersion_m = 0.6f },
            };
            var rep = PlacementQuality.Evaluate(_w, vs, 1920, 1080);
            Assert.IsTrue(rep.issues.Any(i => i.rule == "same_asset_close"), JsonUtility.ToJson(rep));
            Assert.IsTrue(rep.issues.Any(i => i.rule == "same_asset_lookalike"), JsonUtility.ToJson(rep));
            // a different hull on the same spot: the box_overlap rule must fire exactly when the predicted boxes overlap (IoU > 0.3, computed here)
            bool tested = false;
            foreach (var other in new[] { "sar_boat", "patrol_boat", "tug_harbor", "police_boat", "crrc" })
            {
                vs[1].asset_id = other; vs[1].x = p.x; vs[1].z = p.z; vs[1].heading_deg = 90f;
                rep = PlacementQuality.Evaluate(_w, vs, 1920, 1080);
                Assert.IsFalse(rep.issues.Any(i => i.rule.StartsWith("same_asset")), other);
                var ba = rep.vessels.First(v => v.object_id == "a"); var bb = rep.vessels.First(v => v.object_id == "b");
                float ix = Mathf.Max(0, Mathf.Min(ba.x_max, bb.x_max) - Mathf.Max(ba.x_min, bb.x_min)), iy = Mathf.Max(0, Mathf.Min(ba.y_max, bb.y_max) - Mathf.Max(ba.y_min, bb.y_min));
                float inter = ix * iy, iou = inter / ((ba.x_max - ba.x_min) * (ba.y_max - ba.y_min) + (bb.x_max - bb.x_min) * (bb.y_max - bb.y_min) - inter);
                Assert.AreEqual(iou > 0.3f, rep.issues.Any(i => i.rule == "box_overlap"), $"{other} IoU {iou:F3}: " + JsonUtility.ToJson(rep));
                if (iou > 0.3f) { tested = true; break; }
            }
            Assert.IsTrue(tested, "at least one hull overlaps enough to exercise the rule");
            Assert.IsFalse(PlacementQuality.Evaluate(_w, vs, 1920, 1080, (x, y) => true).issues.Any(i => i.rule == "box_overlap"), "exempt pairs");
        }

        [Test]
        public void DefaultScenes_PassTheScreenLayoutRules_OnEveryCamera_AndArePlausible()
        {
            foreach (var hid in new[] { ContainerTerminal01.Id, FishingPort01.Id, Marina01.Id })
                foreach (var cam in CameraPresetLibrary.ForHarbor(hid))
                {
                    var s = SimulatorApp.DefaultScenarioFor(hid); s.camera_preset_id = cam.id;
                    var reports = _w.Apply(s);
                    Assert.IsTrue(reports.All(r => r.ok), $"{cam.id}: " + string.Join("; ", reports.Where(r => !r.ok).Select(r => r.object_id + " " + string.Join(",", r.warnings))));
                    var rep = PlacementQuality.Evaluate(_w, s.vessels, 1920, 1080);
                    Assert.IsTrue(rep.ok, $"{cam.id}: " + string.Join("; ", rep.issues.Select(i => $"{i.rule} {i.a}/{i.b} {i.value:F2}")));
                }
        }

        [Test]
        public void EpisodePlanner_IsReproducible_MoversCrossTheFixedView()
        {
            var spec = new EpisodeSpec { experiment_id = "test", harbor_id = Marina01.Id, camera_preset_id = "mr_C_breakwater", seed = 7, frames = 6 };
            var a = EpisodePlanner.Plan(_w, spec);
            var b = EpisodePlanner.Plan(_w, spec);
            Assert.AreEqual("planned", a.status, string.Join(" | ", a.attempt_log));
            Assert.AreEqual(JsonUtility.ToJson(a), JsonUtility.ToJson(b), "same spec -> same episode");
            Assert.AreEqual(6, a.frames.Count);
            Assert.IsTrue(a.frames.All(f => f.movers_in_view_underway >= 1));
            var m = a.vessels.First(v => v.role == "moving").object_id;
            var first = a.frames.First().vessels.First(v => v.object_id == m); var last = a.frames.Last().vessels.First(v => v.object_id == m);
            Assert.Greater(new Vector2(last.x - first.x, last.z - first.z).magnitude, 20f, "the mover travels");
            // the camera is fixed: re-applying a frame scenario keeps the preset pose
            var s = JsonUtility.FromJson<ScenarioConfig>(JsonUtility.ToJson(a.scenario)); s.navigation.sim_time_s = a.frames.Last().sim_time_s;
            _w.Apply(s);
            var preset = CameraPresetLibrary.ForHarbor(Marina01.Id).First(c => c.id == "mr_C_breakwater");
            Assert.Less(Vector3.Distance(_w.CctvCamera.transform.position, preset.position), 1e-3f);
            Assert.IsTrue(_w.Nav.TryGetPose(m, out var xz, out _));
            Assert.Less(Vector2.Distance(xz, new Vector2(last.x, last.z)), 0.05f, "render-time navigation = planned pose");
        }
    }
}
