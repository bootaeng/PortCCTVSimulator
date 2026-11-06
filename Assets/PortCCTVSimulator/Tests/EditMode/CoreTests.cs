using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEditor.SceneManagement;

namespace PortCCTV.Tests
{
    public class GeneratedAssetRegressionTests
    {
        [Test]
        public void ProceduralHulls_HaveVisibleOutwardSidesAndStern()
        {
            foreach (string id in new[] { "barge", "container_ship", "tugboat", "korean_fishing_boat" })
            {
                var entry = VesselCatalog.Load().Find(id);
                Assert.IsNotNull(entry, id);
                var mf = entry.prefab.GetComponentsInChildren<MeshFilter>(true)
                    .First(x => x.name == "Procedural lofted hull");
                var mesh = mf.sharedMesh; var vertices = mesh.vertices; var tris = mesh.triangles;
                int sides = 0, stern = 0;
                for (int i = 0; i < tris.Length; i += 3)
                {
                    var a = vertices[tris[i]]; var b = vertices[tris[i + 1]]; var c = vertices[tris[i + 2]];
                    var centre = (a + b + c) / 3f; var normal = Vector3.Cross(b - a, c - a);
                    if (normal.sqrMagnitude < 1e-8f) continue;
                    if (Mathf.Abs(centre.x) < mesh.bounds.size.x * 0.1f &&
                        Mathf.Abs(centre.z) > mesh.bounds.size.z * 0.45f && centre.y < mesh.bounds.size.y * 0.85f)
                    {
                        Assert.Greater(normal.z * centre.z, 0f, id + " inward side face"); sides++;
                    }
                    if (Mathf.Abs(centre.x - mesh.bounds.min.x) < 0.001f)
                    { Assert.Less(normal.x, 0f, id + " inward stern"); stern++; }
                }
                Assert.Greater(sides, 8, id); Assert.Greater(stern, 2, id);
            }
        }

        [Test]
        public void PassengerVehiclePrefabs_RemainCentredAndFitStallsAfterPlacement()
        {
            foreach (var entry in HarborTheme.Load().prefabs.Where(e => e.key.StartsWith("car_a_") || e.key.StartsWith("car_b_") || e.key == "truck_kei"))
            {
                var go = Object.Instantiate(entry.prefab);
                try
                {
                    var stall = new Vector3(138f, 2.53f, -67f);
                    go.transform.SetPositionAndRotation(stall, Quaternion.Euler(0, -90f + HarborParts.VehicleYawOffset(entry.key), 0));
                    var renderers = go.GetComponentsInChildren<Renderer>();
                    var bounds = renderers[0].bounds;
                    foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
                    Assert.AreEqual(stall.x, bounds.center.x, 0.03f, entry.key + " lateral pivot");
                    Assert.AreEqual(stall.z, bounds.center.z, 0.03f, entry.key + " longitudinal pivot");
                    Assert.AreEqual(stall.y, bounds.min.y, 0.03f, entry.key + " ground contact");
                    Assert.Less(bounds.size.x, 2.7f, entry.key + " crosses side lines");
                    Assert.Less(bounds.size.z, 5.7f, entry.key + " crosses end lines");
                }
                finally { Object.DestroyImmediate(go); }
            }
        }
    }

    public class ScenarioRoundTripTests
    {
        [Test]
        public void SaveLoadSave_IsByteIdentical_AndFieldsMatch()
        {
            var a = SimulatorApp.DefaultScenario();
            a.camera_preset_id = "cam_B_breakwater_head";
            a.environment.preset_id = "light_haze";
            a.environment.water_preset_id = "light_chop";
            a.environment.water_time_s = 33.3f;
            a.capture.width = 1280; a.capture.height = 720; a.capture.mask_stride = 2;
            a.vessels[1].immersion_m = 0.123f;
            var dir = Path.Combine(Path.GetTempPath(), "portcctv_test_" + System.Guid.NewGuid().ToString("N"));
            var path = Path.Combine(dir, "s.json");
            ScenarioIO.Save(a, path, false);
            var b = ScenarioIO.Load(path);
            Assert.AreEqual(ScenarioIO.ToJson(a), ScenarioIO.ToJson(b));
            Assert.AreEqual("cam_B_breakwater_head", b.camera_preset_id);
            Assert.AreEqual(0.123f, b.vessels[1].immersion_m);
            Assert.AreEqual(3, b.vessels.Count);
            Assert.Throws<IOException>(() => ScenarioIO.Save(b, path, false), "must refuse to overwrite");
            Directory.Delete(dir, true);
        }

        [Test]
        public void UnknownSchemaVersion_IsRejected()
        {
            var json = ScenarioIO.ToJson(new ScenarioConfig()).Replace("\"schema_version\": 1", "\"schema_version\": 99");
            Assert.Throws<InvalidDataException>(() => ScenarioIO.FromJson(json));
        }
    }

    public class GeometryTests
    {
        [Test]
        public void HarborWaterRegions_ClassifyKnownPoints()
        {
            var h = HarborLibrary.Get(HarborGeneric02.Id);
            Assert.AreEqual("inner_basin", h.WaterRegionAt(new Vector2(0, -100)));
            Assert.IsNull(h.WaterRegionAt(new Vector2(-15, 30)), "finger pier is not water");
            Assert.IsNull(h.WaterRegionAt(new Vector2(-80, 80)), "north quay is not water");
            Assert.AreEqual("entrance_channel", h.WaterRegionAt(new Vector2(60, -248)));
            Assert.IsNull(h.WaterRegionAt(new Vector2(0, -248)), "south breakwater is not water");
        }

        [Test]
        public void OrientedRectOverlap()
        {
            var a = Geometry2D.OrientedRect(Vector2.zero, 0, new Vector2(-5, -1), new Vector2(5, 1));
            var b = Geometry2D.OrientedRect(new Vector2(0, 1.5f), 90, new Vector2(-5, -1), new Vector2(5, 1));
            var c = Geometry2D.OrientedRect(new Vector2(0, 3f), 0, new Vector2(-5, -1), new Vector2(5, 1));
            Assert.IsTrue(Geometry2D.ConvexOverlap(a, b));
            Assert.IsFalse(Geometry2D.ConvexOverlap(a, c));
        }

        [Test]
        public void CameraPresetsAreInsideHarborAndDistinct()
        {
            Assert.AreEqual(4, CameraPresetLibrary.ForHarbor(HarborGeneric02.Id).Length);
            Assert.AreEqual(CameraPresetLibrary.All.Length, CameraPresetLibrary.All.Select(p => p.id).Distinct().Count(), "camera ids unique across maps");
            foreach (var hid in HarborLibrary.Ids) Assert.Greater(CameraPresetLibrary.ForHarbor(hid).Length, 2, hid);
        }
    }

    /// <summary>Uses the real bootstrapped assets (Resources) in an empty edit-mode scene.</summary>
    public class WorldTests
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
        public void HullReferenceBottom_IsPlacedAtRequestedImmersion()
        {
            var s = SimulatorApp.DefaultScenario();
            _w.Apply(s);
            foreach (var v in _w.Vessels)
            {
                Assert.IsTrue(v.entry.geometry.hull_reference_found, v.entry.asset_id + " hull reference node");
                Assert.AreEqual(-v.placement.immersion_m, v.HullReferenceMinWorldY, 1e-4f, v.placement.object_id);
                Assert.Less(v.RenderMinWorldY, v.HullReferenceMinWorldY, "propulsion parts hang below the hull candidate");
            }
        }

        [Test]
        public void ThemedMaps_DefaultScenesArePlausible_AndOddVesselsAreFlagged()
        {
            foreach (var hid in new[] { ContainerTerminal01.Id, FishingPort01.Id, Marina01.Id })
            {
                var def = SimulatorApp.DefaultScenarioFor(hid);
                var reps = _w.Apply(def);
                foreach (var r in reps) Assert.IsTrue(r.ok, $"{hid} {r.object_id} {r.asset_id}: " + string.Join(",", r.warnings));
            }
            var odd = SimulatorApp.DefaultScenarioFor(ContainerTerminal01.Id); odd.vessels.Clear();
            odd.vessels.Add(new VesselPlacement { object_id = "k", asset_id = "kayak", x = 200, z = -100 });
            odd.vessels.Add(new VesselPlacement { object_id = "t", asset_id = "tanker", x = 0, z = -150, immersion_m = 10 });
            var rr = _w.Apply(odd);
            Assert.IsTrue(rr[0].warnings.Contains("vessel_type_unusual_for_container_terminal"), string.Join(",", rr[0].warnings));
            Assert.IsTrue(rr[1].warnings.Any(w => w.StartsWith("vessel_unusual_in_region:terminal_basin")), string.Join(",", rr[1].warnings));
        }

        [Test]
        public void EveryCatalogHull_IsPlacedAtRequestedImmersion()
        {
            Assert.GreaterOrEqual(_w.Catalog.entries.Count, 12);
            foreach (var e in _w.Catalog.entries)
            {
                var s = new ScenarioConfig(); // mixed harbor: every type allowed
                // Open water south of the breakwater fits every hull including the 322 m tanker.
                s.vessels.Add(new VesselPlacement { object_id = "t", asset_id = e.asset_id, x = 0, z = -700, heading_deg = 0, immersion_m = e.default_immersion_m });
                var r = _w.Apply(s);
                var v = _w.Vessels[0];
                Assert.IsTrue(e.geometry.hull_reference_found, e.asset_id + " hull reference");
                Assert.AreEqual(-e.default_immersion_m, v.HullReferenceMinWorldY, 1e-3f, e.asset_id);
                Assert.LessOrEqual(v.RenderMinWorldY, v.HullReferenceMinWorldY + 1e-4f, e.asset_id);
                Assert.IsTrue(r[0].ok, e.asset_id + ": " + string.Join(",", r[0].warnings));
                Assert.Greater(e.geometry.vertices_root_space.Length, 100, e.asset_id);
            }
        }

        [Test]
        public void CameraPose_IsFixedAcrossReapplyAndScenarioReload()
        {
            var s = SimulatorApp.DefaultScenario();
            s.camera_preset_id = "cam_C_warehouse_roof";
            _w.Apply(s);
            var m1 = _w.CctvCamera.worldToCameraMatrix; var p1 = _w.CctvCamera.projectionMatrix;
            s.environment.preset_id = "dusk_low_light";
            s.vessels.RemoveAt(0);
            _w.Apply(ScenarioIO.FromJson(ScenarioIO.ToJson(s)));
            Assert.AreEqual(m1, _w.CctvCamera.worldToCameraMatrix);
            Assert.AreEqual(p1, _w.CctvCamera.projectionMatrix);
        }

        [Test]
        public void VesselOnQuay_IsFlagged_AndOverlapsAreReported()
        {
            var s = new ScenarioConfig();
            s.vessels.Add(new VesselPlacement { object_id = "on_quay", asset_id = "rib", x = -80, z = 80 });
            s.vessels.Add(new VesselPlacement { object_id = "a", asset_id = "ferry", x = 0, z = -100, heading_deg = 0 });
            s.vessels.Add(new VesselPlacement { object_id = "b", asset_id = "rib", x = 2, z = -100, heading_deg = 0 });
            var r = _w.Apply(s);
            var q = r.Single(x => x.object_id == "on_quay");
            Assert.IsNull(q.water_region);
            Assert.IsTrue(q.warnings.Any(w => w.StartsWith("intersects_structure")), string.Join(",", q.warnings));
            Assert.IsTrue(r.Single(x => x.object_id == "a").warnings.Any(w => w == "footprint_overlaps_vessel:b"));
            var ok = new ScenarioConfig();
            ok.vessels.Add(new VesselPlacement { object_id = "free", asset_id = "motorboat", x = 0, z = -150 });
            Assert.IsTrue(_w.Apply(ok).Single().ok);
        }

        [Test]
        public void RepeatedInteractiveSpawns_AreVisibleAndDoNotOverlap()
        {
            var cases = new[]
            {
                (ContainerTerminal01.Id, "rib"),
                (FishingPort01.Id, "fishing_boat"),
                (Marina01.Id, "motorboat"),
            };
            foreach (var item in cases)
            {
                foreach (var camera in CameraPresetLibrary.ForHarbor(item.Id))
                {
                    var s = SimulatorApp.DefaultScenarioFor(item.Id);
                    s.camera_preset_id = camera.id;
                    _w.Apply(s);
                    var entry = _w.Catalog.Find(item.Item2);
                    var random = new System.Random(1037);
                    var added = new System.Collections.Generic.List<VesselPlacement>();
                    int repeat = camera.id == "ct_E_basin_breakwater" ? 3 : 2;
                    for (int i = 0; i < repeat; i++)
                    {
                        Assert.IsTrue(VesselSpawnPlanner.TryFind(_w, entry, s.vessels, random, out var p), $"{item.Id}/{camera.id} spawn {i}");
                        p.object_id = "added_" + i;
                        s.vessels.Add(p);
                        added.Add(p);
                        var reports = _w.Apply(s);
                        Assert.IsTrue(reports.Last().ok, $"{item.Id}/{camera.id} spawn {i}: " + string.Join(",", reports.Last().warnings));
                        var vp = _w.CctvCamera.WorldToViewportPoint(new Vector3(p.x, 0, p.z));
                        Assert.Greater(vp.x, 0.04f); Assert.Less(vp.x, 0.96f);
                        Assert.Greater(vp.y, 0.08f); Assert.Less(vp.y, 0.94f);
                    }
                    if (camera.id == "ct_E_basin_breakwater")
                        Assert.Greater(added.Max(v => v.z) - added.Min(v => v.z), 45f, "successive boats should use different water depths");
                }
            }
        }

        [Test]
        public void ClickPlacement_UsesRequestedOpenWater()
        {
            var s = ContainerTerminal01.DefaultScenario();
            _w.Apply(s);
            var cam = _w.CctvCamera;
            var target = cam.WorldToViewportPoint(new Vector3(90f, 0f, -340f));
            Assert.Greater(target.z, 0f);
            var entry = _w.Catalog.Find("rib");
            Assert.IsTrue(VesselSpawnPlanner.TryFind(_w, entry, s.vessels, new System.Random(59), out var p,
                new Vector2(target.x, target.y)));
            p.object_id = "clicked";
            s.vessels.Add(p);
            Assert.IsTrue(_w.Apply(s).Last().ok);
            var placed = cam.WorldToViewportPoint(new Vector3(p.x, 0f, p.z));
            Assert.Less(Vector2.Distance(new Vector2(target.x, target.y), new Vector2(placed.x, placed.y)), 0.16f);
        }

        [Test]
        public void ProjectedBox_AgreesWithUnityWorldToScreen()
        {
            var s = SimulatorApp.DefaultScenario();
            _w.Apply(s);
            var cam = _w.CctvCamera;
            var proj = LabelComputer.MakeProjection(cam, cam.pixelWidth, cam.pixelHeight);
            var p = new Vector3(10, 1, -20);
            Assert.IsTrue(LabelComputer.Project(proj, p, out var px));
            var u = cam.WorldToScreenPoint(p);
            Assert.AreEqual(u.x, px.x, 0.01f);
            Assert.AreEqual(cam.pixelHeight - u.y, px.y, 0.01f);
        }

        [Test]
        public void UnknownIds_Throw()
        {
            var s = new ScenarioConfig { camera_preset_id = "nope" };
            Assert.Throws<System.ArgumentException>(() => _w.Apply(s));
            var t = new ScenarioConfig();
            t.vessels.Add(new VesselPlacement { object_id = "x", asset_id = "submarine" });
            Assert.Throws<System.ArgumentException>(() => _w.Apply(t));
        }
    }

    /// <summary>Vessel navigation: reproducibility, water/structure safety along the route, head-on collision avoidance.</summary>
    public class NavigationTests
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
        public void VisibleMask_WithVesselRois_EqualsFullImageRaycast()
        {
            foreach (var hid in new[] { ContainerTerminal01.Id, Marina01.Id })
            {
                var s = SimulatorApp.DefaultScenarioFor(hid);
                if (hid == Marina01.Id) s.camera_preset_id = "mr_A_promenade";     // the default marina view is a wide bay overview
                _w.Apply(s);
                if (hid == Marina01.Id) { s.camera_preset_id = "mr_A_promenade"; _w.Apply(s); } // close berths exercise mask edges, independent of overview default
                const int W = 480, H = 270;
                var cam = _w.CctvCamera; cam.aspect = (float)W / H;
                Physics.SyncTransforms();
                var colToInst = new System.Collections.Generic.Dictionary<Collider, int>();
                foreach (var v in _w.Vessels) foreach (var c in v.colliders) colToInst[c] = v.instance_index;
                float wy = _w.Harbor.water_reference_y;
                var full = LabelComputer.ComputeVisibleMask(cam, W, H, 1, wy, colToInst);
                var roi = LabelComputer.ComputeVisibleMask(cam, W, H, 1, wy, colToInst, LabelComputer.VesselScreenRects(cam, W, H, _w.Vessels));
                Assert.Greater(full.Count(b => b != 0), 50, hid + ": vessels visible in the test view");
                Assert.AreEqual(full, roi, hid + ": ROI-limited mask must equal the full-image mask");
            }
        }

        ScenarioConfig MarinaAuto()
        {
            var s = SimulatorApp.DefaultScenarioFor(Marina01.Id);
            _w.Apply(s);
            Assert.Greater(_w.SetAutoNavigationForUnmoored(s), 3, "offshore marina vessels switch to auto");
            return s;
        }

        [Test]
        public void PosesAtTime_AreReproducible_AndEqualToIncrementalPlayback()
        {
            var s = MarinaAuto();
            s.navigation.sim_time_s = 90f;
            _w.Apply(s);
            var a = _w.Vessels.ToDictionary(v => v.placement.object_id, v => (v.placement.x, v.placement.z, v.placement.heading_deg));
            _w.Apply(s);
            foreach (var v in _w.Vessels) Assert.AreEqual(a[v.placement.object_id], (v.placement.x, v.placement.z, v.placement.heading_deg), "re-apply " + v.placement.object_id);
            s.navigation.sim_time_s = 0f;
            _w.Apply(s);
            foreach (var t in new[] { 10f, 33.25f, 61f, 90f }) _w.AdvanceNavigation(t);
            foreach (var v in _w.Vessels)
            {
                var e = a[v.placement.object_id];
                Assert.AreEqual(e.x, v.placement.x, 1e-4f, v.placement.object_id);
                Assert.AreEqual(e.z, v.placement.z, 1e-4f, v.placement.object_id);
                Assert.AreEqual(e.heading_deg, v.placement.heading_deg, 1e-3f, v.placement.object_id);
                Assert.AreEqual(e.x, v.root.transform.position.x, 1e-3f, "object follows the state");
            }
            int moved = _w.Vessels.Count(v => _w.Nav.IsMoving(v.placement.object_id) && Vector2.Distance(new Vector2(v.placement.x, v.placement.z),
                new Vector2(s.vessels.First(q => q.object_id == v.placement.object_id).x, s.vessels.First(q => q.object_id == v.placement.object_id).z)) > 20f);
            Assert.Greater(moved, 3, "auto vessels actually move in 90 s");
        }

        [Test]
        public void MovingVessels_StayInWater_AndClearOfStructures()
        {
            foreach (var hid in new[] { ContainerTerminal01.Id, FishingPort01.Id, Marina01.Id })
            {
                var s = SimulatorApp.DefaultScenarioFor(hid);
                _w.Apply(s);
                _w.SetAutoNavigationForUnmoored(s);
                for (float t = 0f; t <= 300f; t += 30f)
                {
                    s.navigation.sim_time_s = t;
                    var reps = _w.Apply(s);
                    foreach (var r in reps.Where(r => _w.Nav.IsMoving(r.object_id)))
                    {
                        Assert.IsNotNull(r.water_region, $"{hid} t={t} {r.object_id} left the water regions");
                        Assert.IsFalse(r.warnings.Any(x => x.StartsWith("intersects_structure")), $"{hid} t={t} {r.object_id}: " + string.Join(",", r.warnings));
                    }
                }
            }
        }

        [Test]
        public void FreeRoam_IsReproducible_StaysInWater_AndKeepsDrawingWaypoints()
        {
            foreach (var hid in new[] { ContainerTerminal01.Id, Marina01.Id })
            {
                var s = SimulatorApp.DefaultScenarioFor(hid);
                _w.Apply(s);
                Assert.Greater(_w.SetAutoNavigationForUnmoored(s, "roam"), 2, hid + ": unmoored vessels switch to roam");
                // reproducible: the same scenario at the same time gives the same poses
                s.navigation.sim_time_s = 600f;
                _w.Apply(s);
                var a = _w.Vessels.ToDictionary(v => v.placement.object_id, v => (v.placement.x, v.placement.z, v.placement.heading_deg));
                _w.Apply(s);
                foreach (var v in _w.Vessels) Assert.AreEqual(a[v.placement.object_id], (v.placement.x, v.placement.z, v.placement.heading_deg), hid + " re-apply " + v.placement.object_id);
                // safe over 20 minutes and still wandering (new waypoints drawn, not "arrived")
                int roaming = 0;
                for (float t = 0f; t <= 1200f; t += 60f)
                {
                    s.navigation.sim_time_s = t;
                    var reps = _w.Apply(s);
                    foreach (var r in reps.Where(r => _w.Nav.IsMoving(r.object_id)))
                    {
                        Assert.IsNotNull(r.water_region, $"{hid} t={t} {r.object_id} left the water regions");
                        Assert.IsFalse(r.warnings.Any(x => x.StartsWith("intersects_structure")), $"{hid} t={t} {r.object_id}: " + string.Join(",", r.warnings));
                    }
                    if (t >= 1200f) roaming = _w.Nav.Record().vessels.Count(n => n.mode == "roam" && n.state != "arrived" && n.route != null && n.route.Count >= 2);
                }
                Assert.Greater(roaming, 2, hid + ": roaming vessels keep drawing waypoints after 20 min: " +
                    string.Join("; ", _w.Nav.Record().vessels.Where(n => n.mode == "roam").Select(n => $"{n.object_id}:{n.state}:route{n.route.Count}:distance{n.distance_travelled_m:F0}")));
            }
        }

        [Test]
        public void Rudder_SettlesOnCourse_WithoutOscillation_AndCurrentIsCompensated()
        {
            // motorboat 90 deg off its course to a far waypoint, 0.8 kn cross current + 12 kn beam wind: it must settle on course
            // (bounded overshoot, few yaw-rate sign changes) and still make good the waypoint line despite the drift
            var s = SimulatorApp.DefaultScenarioFor(ContainerTerminal01.Id);
            s.vessels.Clear();
            s.navigation.collision_avoidance = false;
            s.navigation.wind_from_deg = 90f; s.navigation.wind_speed_kn = 12f;
            s.navigation.current_set_deg = 270f; s.navigation.current_drift_kn = 0.8f;
            s.vessels.Add(new VesselPlacement { object_id = "a", asset_id = "motorboat", x = -600, z = -1500, heading_deg = 90,
                motion = new VesselMotion { mode = "route", loop = false, route = new System.Collections.Generic.List<Vector2> { new Vector2(1200, -1500) } } });
            s.navigation.sim_time_s = 0f;
            _w.Apply(s);
            float maxOver = 0f; int signChanges = 0; float prevYaw = 0f; float worstErrAfterSettle = 0f; float maxOffTrack = 0f;
            for (float t = 0.25f; t <= 150f; t += 0.25f)
            {
                _w.AdvanceNavigation(t);
                var r = _w.Nav.Record().vessels[0];
                float err = Mathf.DeltaAngle(r.heading_deg, NavigationSim.HeadingOf(new Vector2(1200, -1500) - r.position_xz));
                if (t < 40f) maxOver = Mathf.Max(maxOver, err);   // starts with err = -90 (turning to port); positive err = overshoot past the course
                if (t >= 40f) worstErrAfterSettle = Mathf.Max(worstErrAfterSettle, Mathf.Abs(err));
                if (t >= 40f) maxOffTrack = Mathf.Max(maxOffTrack, Mathf.Abs(r.position_xz.y + 1500f));
                if (Mathf.Abs(r.yaw_rate_deg_s) > 0.05f && Mathf.Abs(prevYaw) > 0.05f && Mathf.Sign(r.yaw_rate_deg_s) != Mathf.Sign(prevYaw)) signChanges++;
                if (Mathf.Abs(r.yaw_rate_deg_s) > 0.05f) prevYaw = r.yaw_rate_deg_s;
            }
            string info = $"(overshoot {maxOver:F1} deg, yaw sign changes {signChanges}, worst err after 40 s {worstErrAfterSettle:F1} deg, max off-track {maxOffTrack:F1} m)";
            Assert.Less(maxOver, 30f, "heading overshoot bounded " + info);
            Assert.LessOrEqual(signChanges, 4, "yaw rate does not keep oscillating " + info);
            Assert.Less(worstErrAfterSettle, 20f, "steady course error (crab angle against the drift stays small) after 40 s " + info);
            Assert.Less(maxOffTrack, 60f, "the drift is compensated: the boat stays near the waypoint line " + info);
            var rec = _w.Nav.Record();
            Assert.AreEqual(12f, rec.wind_speed_kn, 1e-3f); Assert.AreEqual(90f, rec.wind_from_deg, 1e-2f);
            Assert.AreEqual(0.8f, rec.current_drift_kn, 1e-3f); Assert.AreEqual(270f, rec.current_set_deg, 1e-2f);
        }

        [Test]
        public void AnchoredVessels_SwingAboutTheAnchor_IntoWindAndCurrent_AndStayClear()
        {
            var s = SimulatorApp.DefaultScenarioFor(ContainerTerminal01.Id);
            var anchored = s.vessels.Where(v => v.motion != null && v.motion.Anchored).Select(v => v.object_id).ToList();
            Assert.GreaterOrEqual(anchored.Count, 2, "default terminal scene has anchored vessels in the anchorage");
            _w.Apply(s);
            var rec0 = _w.Nav.Record();
            var env = _w.Nav.Current + _w.Nav.Wind * 2f * Mathf.Max(0.02f, NavigationSim.LeewayFactor("tanker"));
            float intoEnv = NavigationSim.HeadingOf(-env);
            for (float t = 0f; t <= 1200f; t += 60f)
            {
                s.navigation.sim_time_s = t;
                var reps = _w.Apply(s);
                var rec = _w.Nav.Record();
                foreach (var id in anchored)
                {
                    var r = reps.First(q => q.object_id == id);
                    Assert.IsNotNull(r.water_region, $"t={t} {id} left the water");
                    Assert.IsFalse(r.warnings.Any(x => x.StartsWith("intersects_structure")), $"t={t} {id}: " + string.Join(",", r.warnings));
                    var n = rec.vessels.First(q => q.object_id == id);
                    var n0 = rec0.vessels.First(q => q.object_id == id);
                    float L = _w.Vessels.First(q => q.placement.object_id == id).entry.geometry.bounds_max.x - _w.Vessels.First(q => q.placement.object_id == id).entry.geometry.bounds_min.x;
                    Assert.AreEqual(n0.anchor_xz, n.anchor_xz, $"{id} anchor fixed");
                    Assert.AreEqual(n.anchor_scope_m + 0.5f * L, Vector2.Distance(n.anchor_xz, n.position_xz), 0.05f, $"t={t} {id} on the swing circle");
                    Assert.AreEqual("at_anchor", n.state); Assert.AreEqual(0f, n.speed_kn, 1e-4f);
                }
            }
            var tanker = _w.Nav.Record().vessels.First(q => q.object_id == "v06");
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(tanker.heading_deg, intoEnv)), 35f, $"tanker bow into the wind/current resultant after 20 min (heading {tanker.heading_deg:F0}, resultant {intoEnv:F0})");
            Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(tanker.heading_deg, 80f)), 10f, "it actually swung away from its start heading");
        }

        [Test]
        public void AutoNavigation_DefaultFleetFootprintsDoNotCollide()
        {
            foreach (var harbor in new[] { ContainerTerminal01.Id, FishingPort01.Id, Marina01.Id })
            {
                var scenario = SimulatorApp.DefaultScenarioFor(harbor);
                _w.Apply(scenario);
                _w.SetAutoNavigationForUnmoored(scenario);
                _w.Apply(scenario);
                for (float t = 0f; t <= 180f; t += 1f)
                {
                    _w.AdvanceNavigation(t);
                    var boats = _w.Vessels;
                    for (int i = 0; i < boats.Count; i++) for (int j = i + 1; j < boats.Count; j++)
                        Assert.IsFalse(Geometry2D.ConvexOverlap(boats[i].footprint_xz, boats[j].footprint_xz),
                            $"{harbor} t={t:F1}: {boats[i].placement.object_id} / {boats[j].placement.object_id}");
                }
            }
        }

        [Test]
        public void DefaultAvoidance_PreventsMovingHullFromCrossingStationaryAndMovingVessels()
        {
            var s = SimulatorApp.DefaultScenarioFor(ContainerTerminal01.Id);
            s.vessels.Clear();
            Assert.IsTrue(s.navigation.collision_avoidance, "collision avoidance must be the default");
            s.navigation.wind_speed_kn = 0f; s.navigation.current_drift_kn = 0f;
            s.vessels.Add(new VesselPlacement { object_id = "a", asset_id = "motorboat", x = -110f, z = -1000f, heading_deg = 0f,
                motion = new VesselMotion { mode = "route", loop = false, route = new System.Collections.Generic.List<Vector2> { new Vector2(170f, -1000f) } } });
            s.vessels.Add(new VesselPlacement { object_id = "b", asset_id = "tugboat", x = 0f, z = -1000f, heading_deg = 0f });
            s.vessels.Add(new VesselPlacement { object_id = "c", asset_id = "motorboat", x = 30f, z = -850f, heading_deg = 90f,
                motion = new VesselMotion { mode = "route", loop = false, route = new System.Collections.Generic.List<Vector2> { new Vector2(30f, -1150f) } } });
            _w.Apply(s);
            float moved = 0f;
            for (float t = 0.25f; t <= 110f; t += 0.25f)
            {
                _w.AdvanceNavigation(t);
                var boats = _w.Vessels;
                moved = Mathf.Max(moved, Vector2.Distance(new Vector2(boats[0].placement.x, boats[0].placement.z), new Vector2(-110f, -1000f)));
                for (int i = 0; i < boats.Count; i++) for (int j = i + 1; j < boats.Count; j++)
                    Assert.IsFalse(Geometry2D.ConvexOverlap(boats[i].footprint_xz, boats[j].footprint_xz),
                        $"hulls overlap at t={t:F2}: {boats[i].placement.object_id} / {boats[j].placement.object_id}");
            }
            Assert.Greater(moved, 20f, "the collision guard must not prevent all navigation");
        }

        [Test]
        public void HeadOnVessels_GiveWayToStarboard()
        {
            float MinGap(bool avoid)
            {
                var s = SimulatorApp.DefaultScenarioFor(ContainerTerminal01.Id);
                s.vessels.Clear();
                s.navigation.collision_avoidance = avoid;
                s.vessels.Add(new VesselPlacement { object_id = "a", asset_id = "motorboat", x = -400, z = -1500, heading_deg = 0,
                    motion = new VesselMotion { mode = "route", loop = false, route = new System.Collections.Generic.List<Vector2> { new Vector2(400, -1500) } } });
                s.vessels.Add(new VesselPlacement { object_id = "b", asset_id = "motorboat", x = 400, z = -1500, heading_deg = 180,
                    motion = new VesselMotion { mode = "route", loop = false, route = new System.Collections.Generic.List<Vector2> { new Vector2(-400, -1500) } } });
                s.navigation.sim_time_s = 0f;
                _w.Apply(s);
                float min = float.MaxValue;
                for (float t = 0.25f; t <= 200f; t += 0.25f)
                {
                    _w.AdvanceNavigation(t);
                    var p0 = _w.Vessels[0].placement; var p1 = _w.Vessels[1].placement;
                    min = Mathf.Min(min, Vector2.Distance(new Vector2(p0.x, p0.z), new Vector2(p1.x, p1.z)));
                }
                return min;
            }
            float with = MinGap(true), without = MinGap(false);
            var L = _w.Catalog.Find("motorboat").geometry; float len = L.bounds_max.x - L.bounds_min.x;
            Assert.Less(without, 2f, "without avoidance the straight routes meet");
            Assert.Greater(with, len + 5f, $"with avoidance they pass clear (min centre distance {with:F1} m, hull {len:F1} m)");
        }
    }

    public class DevFlyCamTests
    {
        static DevFlyCam Cam(float yaw, float pitch) { var c = new DevFlyCam { minY = 1f }; c.SetPose(new Vector3(0, 50, 0), yaw, pitch); return c; }
        static void Hold(DevFlyCam c, FlyInput i, float seconds) { for (int k = 0; k < Mathf.RoundToInt(seconds * 60f); k++) c.Step(i, 1f / 60f); }

        [Test]
        public void W_MovesHorizontallyAlongHeading_EvenWhenLookingDown_AndStopsSmoothly()
        {
            var c = Cam(90f, 40f);                                    // heading +X, looking 40 deg down
            Hold(c, new FlyInput { move = new Vector2(0, 1) }, 1f);
            Assert.AreEqual(50f, c.position.y, 1e-3f, "horizontal move keeps the altitude");
            Assert.AreEqual(0f, c.position.z, 1e-3f);
            Assert.Greater(c.position.x, 0.8f * 40f, "about one second of travel at 40 m/s (minus the smoothing lag)");
            Assert.Less(c.position.x, 40f);
            float x1 = c.position.x;
            Hold(c, new FlyInput(), 1f);
            Assert.Greater(c.position.x, x1, "coasts a little after release");
            Assert.Less(c.position.x - x1, 8f, "but stops within a fraction of a second");
            Assert.AreEqual(Vector3.zero, c.velocity, "and comes to rest");
        }

        [Test]
        public void AlongView_W_Descends_ButNeverBelowMinY()
        {
            var c = Cam(0f, 45f); c.moveAlongView = true;
            Hold(c, new FlyInput { move = new Vector2(0, 1), fast = true }, 3f);
            Assert.AreEqual(1f, c.position.y, 1e-4f, "clamped at minY");
            Assert.Greater(c.position.z, 50f, "still moved forward");
            var d = Cam(0f, 0f);
            Hold(d, new FlyInput { vertical = -1f }, 10f);
            Assert.AreEqual(1f, d.position.y, 1e-4f, "Q never goes below the water margin");
        }

        [Test]
        public void Look_ClampsPitch_WrapsYaw_AndStrafeIsPerpendicular()
        {
            var c = Cam(350f, 0f);
            c.Step(new FlyInput { lookDeg = new Vector2(20f, 200f) }, 1f / 60f);
            Assert.AreEqual(10f, c.yaw, 1e-3f); Assert.AreEqual(89f, c.pitch, 1e-3f);
            c.Step(new FlyInput { lookDeg = new Vector2(0f, -400f) }, 1f / 60f);
            Assert.AreEqual(-89f, c.pitch, 1e-3f);
            var s = Cam(0f, 0f);                                      // heading +Z: D strafes to +X
            Hold(s, new FlyInput { move = new Vector2(1, 0) }, 1f);
            Assert.Greater(s.position.x, 30f); Assert.AreEqual(0f, s.position.z, 1e-3f);
        }

        [Test]
        public void Wheel_ScalesSpeed_WithinLimits_ShiftAndCtrlModify()
        {
            var c = Cam(0f, 0f);
            c.Step(new FlyInput { wheel = 2f }, 1f / 60f);
            Assert.AreEqual(40f * 1.25f * 1.25f, c.baseSpeed, 1e-3f);
            c.Step(new FlyInput { wheel = 100f }, 1f / 60f);
            Assert.AreEqual(DevFlyCam.MaxSpeed, c.baseSpeed, 1e-3f);
            c.Step(new FlyInput { wheel = -100f }, 1f / 60f);
            Assert.AreEqual(DevFlyCam.MinSpeed, c.baseSpeed, 1e-3f);
            c.baseSpeed = 40f;
            Assert.AreEqual(160f, c.CurrentSpeed(true, false), 1e-3f);
            Assert.AreEqual(10f, c.CurrentSpeed(false, true), 1e-3f);
        }
    }

    public class WeatherAndHullTests
    {
        SimWorld _w;
        [SetUp] public void SetUp() { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); _w = new SimWorld(); _w.Initialize(); }
        [TearDown] public void TearDown() => _w.Dispose();

        [Test]
        public void Rain_IsReproducible_LowersVisibility_AndRoundTrips()
        {
            var s = SimulatorApp.DefaultScenarioFor(ContainerTerminal01.Id);
            _w.Apply(s);
            float clearMfp = _w.Environment.fog_mean_free_path_m, clearEv = _w.Environment.exposure_ev100;
            s.environment.weather = new WeatherConfig { precipitation = "rain", intensity = 0.8f, seed = 3 };
            _w.Apply(s);
            Assert.Less(_w.Environment.fog_mean_free_path_m, 0.5f * clearMfp, "rain lowers the fog mean free path");
            Assert.Less(_w.Environment.exposure_ev100, clearEv, "overcast lowers the exposure");
            StringAssert.Contains(SimWorld.OvercastSky, _w.Environment.sky, "overcast HDRI");
            Assert.AreEqual("rain", _w.Environment.weather.precipitation);
            var a = _w.Weather.ParticlePositions();
            Assert.Greater(a.Length, 500, "rain particles alive after prewarm");
            float wy = _w.Harbor.water_reference_y, camY = _w.CctvCamera.transform.position.y;
            Assert.IsTrue(a.All(p => p.y > wy - 1f && p.y < camY + 80f), "near-band drops between the water plane and the top of the view");
            _w.Apply(s);
            var b = _w.Weather.ParticlePositions();
            Assert.AreEqual(a.Length, b.Length); for (int k = 0; k < a.Length; k++) Assert.AreEqual(a[k], b[k], "same seed + camera -> same drops");
            // far bands exist, rings lie on water only, the ground is wet
            Assert.Greater(_w.Weather.ParticlePositions(2).Length, 200, "far rain band");
            Assert.Greater(_w.Weather.RingCount, 50, "rain rings on the water");
            Assert.IsTrue(_w.Weather.RingPositions().All(p => _w.Harbor.InAnyWaterRegion(new Vector2(p.x, p.z)) && Mathf.Abs(p.y - wy) < 0.5f), "rings only on water");
            Assert.AreEqual(0.9f, _w.Weather.WetDecalFade, 1e-4f, "auto wetness 0.5 + 0.5 x 0.8");
            Assert.AreEqual(0f, _w.Weather.SnowDecalFade, "no snow cover in rain");
            var back = ScenarioIO.FromJson(ScenarioIO.ToJson(s));
            Assert.AreEqual("rain", back.environment.weather.precipitation); Assert.AreEqual(0.8f, back.environment.weather.intensity, 1e-6f);
            var old = ScenarioIO.FromJson(ScenarioIO.ToJson(SimulatorApp.DefaultScenarioFor(ContainerTerminal01.Id)).Replace("\"weather\"", "\"weather_old\""));
            Assert.AreEqual("none", old.environment.weather.precipitation, "files without weather load as no precipitation");
            s.environment.weather = new WeatherConfig { precipitation = "snow", intensity = 0.5f };
            _w.Apply(s);
            Assert.Greater(_w.Weather.ParticlePositions().Length, 500, "snow particles");
            Assert.AreEqual(0.625f, _w.Weather.SnowDecalFade, 1e-4f, "auto snow cover 0.25 + 0.75 x 0.5");
            Assert.AreEqual(0, _w.Weather.RingCount, "no rain rings in snow");
            s.environment.weather = new WeatherConfig();
            _w.Apply(s);
            Assert.AreEqual(clearMfp, _w.Environment.fog_mean_free_path_m, 1e-3f, "back to the preset");
            Assert.AreEqual(0f, _w.Weather.WetDecalFade); Assert.AreEqual(0f, _w.Weather.SnowDecalFade);
            s.environment.weather = new WeatherConfig { wetness = 0.6f };                  // wet roads after the rain, no precipitation
            _w.Apply(s);
            Assert.AreEqual(0.6f, _w.Weather.WetDecalFade, 1e-4f, "manual wetness without rain");
        }

        [Test]
        public void EveryVesselType_CanBeAddedOnEveryMap()
        {
            foreach (var hid in new[] { ContainerTerminal01.Id, FishingPort01.Id, Marina01.Id })
            {
                var s = SimulatorApp.DefaultScenarioFor(hid);
                s.vessels.Clear();
                _w.Apply(s);
                var rnd = new System.Random(5);
                foreach (var e in _w.Catalog.entries)
                {
                    // the UI tries the active camera first, then the map's other cameras
                    bool ok = false; VesselPlacement place = null;
                    foreach (var p in CameraPresetLibrary.ForHarbor(hid))
                    {
                        var tmp = s.Clone(); tmp.camera_preset_id = p.id; _w.ApplyCamera(tmp);
                        if (ok = VesselSpawnPlanner.TryFind(_w, e, s.vessels, rnd, out place)) break;
                    }
                    _w.ApplyCamera(s);
                    Assert.IsTrue(ok, $"{hid}: no visible clear position for {e.asset_id} from any camera");
                    place.object_id = "t_" + e.asset_id; s.vessels.Add(place);
                    var reps = _w.Apply(s);
                    var rep = reps.First(r => r.object_id == place.object_id);
                    Assert.IsFalse(rep.warnings.Any(w => w.StartsWith("intersects_structure") || w.StartsWith("footprint_leaves_water")), $"{hid} {e.asset_id}: " + string.Join(",", rep.warnings));
                    s.vessels.Clear();
                }
            }
        }
    }

    public class GpuMaskTests
    {
        SimWorld _w;
        [SetUp] public void SetUp() { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); _w = new SimWorld(); _w.Initialize(); }
        [TearDown] public void TearDown() => _w.Dispose();

        byte[] Gpu(int W, int H)
        {
            var cam = _w.CctvCamera; cam.aspect = (float)W / H;
            var rt = new RenderTexture(W, H, 24); var prev = cam.targetTexture; cam.targetTexture = rt;
            cam.Render(); _w.FreezeWaterAt(12f); cam.Render();
            cam.targetTexture = prev; rt.Release(); Object.DestroyImmediate(rt);
            return _w.GpuMask.Render(cam, _w.Vessels, W, H);
        }

        [Test]
        public void GpuMask_AgreesWithCpuMask_IsDeterministic_AndRespectsTheWaterLine()
        {
            var s = SimulatorApp.DefaultScenarioFor(ContainerTerminal01.Id);
            s.environment.water_preset_id = "calm";
            _w.Apply(s);
            Assert.IsTrue(_w.GpuMask.Available, "instance-id shader available");
            const int W = 640, H = 360;
            var gpu = Gpu(W, H);
            var gpu2 = _w.GpuMask.Render(_w.CctvCamera, _w.Vessels, W, H);
            Assert.AreEqual(gpu, gpu2, "same frame -> identical mask");
            var colToInst = new System.Collections.Generic.Dictionary<Collider, int>();
            foreach (var v in _w.Vessels) foreach (var c in v.colliders) colToInst[c] = v.instance_index;
            Physics.SyncTransforms();
            var cpu = LabelComputer.ComputeVisibleMask(_w.CctvCamera, W, H, 1, _w.Harbor.water_reference_y, colToInst);
            var proj = LabelComputer.MakeProjection(_w.CctvCamera, W, H);
            int checkedVessels = 0;
            foreach (var v in _w.Vessels)
            {
                int inter = 0, uni = 0, cpuN = 0, gpuN = 0, gy1 = -1;
                for (int k = 0; k < gpu.Length; k++)
                {
                    bool a = gpu[k] == v.instance_index, b = cpu[k] == v.instance_index;
                    if (a) { gpuN++; gy1 = Mathf.Max(gy1, k / W); }
                    if (b) cpuN++;
                    if (a && b) inter++;
                    if (a || b) uni++;
                }
                if (cpuN < 150) continue;
                checkedVessels++;
                Assert.Greater((float)inter / uni, 0.75f, $"{v.placement.object_id} ({v.placement.asset_id}): GPU {gpuN} px vs CPU {cpuN} px");
                LabelComputer.ComputeBoxes(v, proj, _w.Harbor.water_reference_y, out var full, out var above);
                if (above.clipped.valid) Assert.LessOrEqual(gy1, above.clipped.y_max + 3f, $"{v.placement.object_id}: GPU mask reaches below the water line");
            }
            Assert.GreaterOrEqual(checkedVessels, 3, "several sizeable vessels in the default terminal view");
        }

        [Test]
        public void GpuMask_HidesASubmergedHull_AndVesselsBehindTheCamera()
        {
            var s = SimulatorApp.DefaultScenarioFor(ContainerTerminal01.Id);
            s.environment.water_preset_id = "calm";
            var t = s.vessels.First(v => v.asset_id == "barge");
            t.immersion_m = 30f;                                         // far below the surface
            _w.Apply(s);
            var mask = Gpu(480, 270);
            var sunk = _w.Vessels.First(v => v.placement.object_id == t.object_id);
            Assert.AreEqual(0, mask.Count(b => b == sunk.instance_index), "a hull 30 m under water is hidden by the rendered water");
            Assert.Greater(mask.Count(b => b != 0), 100, "other vessels still visible");
        }
    }

    public class SensorTests
    {
        SimWorld _w;
        [SetUp] public void SetUp() { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); _w = new SimWorld(); _w.Initialize(); }
        [TearDown] public void TearDown() => _w.Dispose();

        [Test]
        public void LensModel_InvertsExactly_AndFillsTheFrame()
        {
            foreach (var (k1, k2) in new[] { (0.15f, 0.03f), (-0.12f, 0f), (0.3f, 0.05f) })
            {
                var lm = LensModel.Make(new SensorConfig { enabled = true, distortion_k1 = k1, distortion_k2 = k2 }, 1920, 1080);
                var rng = new System.Random(3);
                for (int i = 0; i < 300; i++)
                {
                    var p = new Vector2((float)rng.NextDouble() * 1920f, (float)rng.NextDouble() * 1080f);
                    Assert.IsTrue(lm.OutputOf(lm.SourceOf(p), out var back), $"k1={k1}");
                    Assert.Less((back - p).magnitude, 0.01f, $"k1={k1} p={p}");
                }
                for (int i = 0; i <= 50; i++)
                    foreach (var b in new[] { new Vector2(0, i / 50f * 1080), new Vector2(1920, i / 50f * 1080), new Vector2(i / 50f * 1920, 0), new Vector2(i / 50f * 1920, 1080) })
                    {
                        var s = lm.SourceOf(b);
                        Assert.IsTrue(s.x >= -0.01f && s.x <= 1920.01f && s.y >= -0.01f && s.y <= 1080.01f, $"k1={k1}: border {b} samples outside the render ({s})");
                    }
            }
        }

        RenderTexture Clean(int W, int H)
        {
            var cam = _w.CctvCamera; cam.aspect = (float)W / H;
            var rt = new RenderTexture(new RenderTextureDescriptor(W, H, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB, 24));
            rt.Create();
            var prev = cam.targetTexture; cam.targetTexture = rt;
            cam.Render(); _w.FreezeWaterAt(12f); cam.Render();
            cam.targetTexture = prev;
            return rt;
        }

        static Color32[] Read(RenderTexture rt)
        {
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var t = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, false);
            t.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); t.Apply();
            RenderTexture.active = prev;
            var px = t.GetPixels32(); Object.DestroyImmediate(t); return px;
        }

        [Test]
        public void Degradation_IsIdentityWhenNeutral_Deterministic_AndDropsLandWhereTheLabelSays()
        {
            var s = SimulatorApp.DefaultScenarioFor(ContainerTerminal01.Id);
            s.environment.water_preset_id = "calm";
            _w.Apply(s);
            Assert.IsTrue(_w.Sensor.Available, "degradation shader available");
            const int W = 640, H = 360;
            var rt = Clean(W, H);
            var clean = Read(rt);
            var neutral = new SensorConfig { enabled = true, noise_chroma = 0f, lens_drops = 0f };
            var id = _w.Sensor.Process(rt, neutral, 1f, null);
            int maxd = 0;
            for (int i = 0; i < clean.Length; i++)
                maxd = Mathf.Max(maxd, Mathf.Max(Mathf.Abs(clean[i].r - id[i].r), Mathf.Max(Mathf.Abs(clean[i].g - id[i].g), Mathf.Abs(clean[i].b - id[i].b))));
            Assert.LessOrEqual(maxd, 1, "neutral settings reproduce the render (no flip, no shift)");

            var noisy = SensorConfig.Preset("low_quality"); noisy.lens_drops = 0.5f;
            var drops = SensorProcessor.MakeDrops(noisy.seed, 0.5f, W, H);
            var a = _w.Sensor.Process(rt, noisy, 2f, drops);
            var b = _w.Sensor.Process(rt, noisy, 2f, drops);
            Assert.IsTrue(a.SequenceEqual(b), "same settings -> identical image");
            noisy.seed++;
            Assert.IsFalse(a.SequenceEqual(_w.Sensor.Process(rt, noisy, 2f, drops)), "another seed -> other noise");

            // one big drop in the top-left quarter (top-left pixel coordinates): the change must be there, not mirrored
            var one = new System.Collections.Generic.List<LensDrop> { new LensDrop { x = 120, y = 90, rx = 45, ry = 45, opacity = 0.95f, refraction = 0.8f, blur = 0.4f } };
            var dropped = _w.Sensor.Process(rt, new SensorConfig { enabled = true, lens_drops = 0f, noise_chroma = 0f }, 1f, one);
            double Diff(int x0, int y0)
            {
                double d = 0;
                for (int y = y0; y < y0 + 60; y++)
                    for (int x = x0; x < x0 + 60; x++)
                    {
                        int k = (H - 1 - y) * W + x;
                        d += Mathf.Abs(dropped[k].r - clean[k].r) + Mathf.Abs(dropped[k].g - clean[k].g) + Mathf.Abs(dropped[k].b - clean[k].b);
                    }
                return d;
            }
            double atLabel = Diff(90, 60), mirrored = Diff(90, H - 120);
            Assert.Greater(atLabel, 4 * mirrored + 1, $"drop at its label position (diff {atLabel:F0}) vs vertically mirrored ({mirrored:F0}) -> SensorProcessor.UvYDown is wrong");
            rt.Release(); Object.DestroyImmediate(rt);
        }

        [Test]
        public void DistortedCapture_KeepsMaskAndBoxesTogether_AndWritesTheCleanImage()
        {
            var s = SimulatorApp.DefaultScenarioFor(ContainerTerminal01.Id);
            s.environment.water_preset_id = "calm";
            s.capture.width = 960; s.capture.height = 540;
            s.sensor = SensorConfig.Preset("low_quality");
            s.sensor.distortion_k1 = 0.3f; s.sensor.motion_blur_px = 0f;
            _w.Apply(s);
            var dir = Path.Combine(Path.GetTempPath(), "portcctv_sensor_" + System.Guid.NewGuid().ToString("N"));
            var res = CaptureService.Capture(_w, dir, "test");
            Assert.IsTrue(res.success, res.error);
            Assert.IsTrue(File.Exists(Path.Combine(dir, "rgb_clean.png")));
            Assert.IsTrue(res.metadata.sensor.enabled);
            Assert.Less(res.metadata.sensor.distortion_scale, 1f);
            int checkedN = 0;
            foreach (var o in res.labels.objects)
            {
                var bb = o.visible_mask.bbox; var full = o.full_mesh_box.clipped;
                if (!bb.valid || o.visible_mask.pixel_count < 80) continue;
                checkedN++;
                Assert.GreaterOrEqual(bb.x_min, full.x_min - 3f, o.object_id); Assert.LessOrEqual(bb.x_max, full.x_max + 3f, o.object_id);
                Assert.GreaterOrEqual(bb.y_min, full.y_min - 3f, o.object_id); Assert.LessOrEqual(bb.y_max, full.y_max + 3f, o.object_id);
            }
            Assert.GreaterOrEqual(checkedN, 3);
            Directory.Delete(dir, true);
        }
    }

    public class NightLightTests
    {
        SimWorld _w;
        [SetUp] public void SetUp() { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); _w = new SimWorld(); _w.Initialize(); }
        [TearDown] public void TearDown() => _w.Dispose();

        [Test]
        public void NightTurnsLightsOn_ArcsFollowTheCameraBearing_AndBulbsStayOutOfTheMask()
        {
            var s = SimulatorApp.DefaultScenarioFor(ContainerTerminal01.Id);
            s.environment.water_preset_id = "calm";
            _w.Apply(s);
            Assert.IsFalse(_w.Lights.On, "day: lights off");
            s.environment.preset_id = "night_clear";
            _w.Apply(s);
            Assert.IsTrue(_w.Lights.On);
            Assert.Greater(_w.Lights.Resolved.harbor_lamps, 20);
            Assert.Greater(_w.Lights.Resolved.vessel_nav_lights, 10);

            var tug = _w.Vessels.First(v => v.placement.object_id == "v02");
            var probe = new GameObject("probe").AddComponent<Camera>();
            System.Collections.Generic.List<string> Seen(Vector3 dirLocal)
            {
                var t = tug.root.transform;
                probe.transform.position = t.position + t.TransformDirection(dirLocal.normalized) * 300f + Vector3.up * 15f;
                _w.Lights.UpdateForCamera(probe, 1080);
                return _w.Lights.VisibleNavLights(tug);
            }
            var fromStarboard = Seen(new Vector3(0, 0, -1));
            CollectionAssert.Contains(fromStarboard, "starboard"); CollectionAssert.DoesNotContain(fromStarboard, "port");
            CollectionAssert.Contains(fromStarboard, "masthead"); CollectionAssert.DoesNotContain(fromStarboard, "stern");
            var fromPort = Seen(new Vector3(0, 0, 1));
            CollectionAssert.Contains(fromPort, "port"); CollectionAssert.DoesNotContain(fromPort, "starboard");
            var fromAstern = Seen(new Vector3(-1, 0, 0));
            CollectionAssert.AreEquivalent(new[] { "stern" }, fromAstern);
            Object.DestroyImmediate(probe.gameObject);

            var anchored = _w.Vessels.First(v => v.placement.object_id == "v12");
            _w.Lights.UpdateForCamera(_w.CctvCamera, 1080);
            Assert.IsTrue(_w.Lights.VisibleNavLights(anchored).All(k => k.StartsWith("anchor")), "anchored ship shows anchor lights only");

            // bulbs are excluded from the GPU mask: same vessel pixel counts with lights on and off (bulbs may hide a few hull pixels)
            const int W = 640, H = 360;
            byte[] Mask()
            {
                var cam = _w.CctvCamera; cam.aspect = (float)W / H;
                var rt = new RenderTexture(W, H, 24); cam.targetTexture = rt;
                cam.Render(); _w.FreezeWaterAt(12f); cam.Render();
                cam.targetTexture = null; rt.Release(); Object.DestroyImmediate(rt);
                _w.Lights.UpdateForCamera(cam, H);
                return _w.GpuMask.Render(cam, _w.Vessels, W, H);
            }
            var on = Mask();
            s.environment.lights = "off"; _w.Apply(s);
            var off = Mask();
            foreach (var v in _w.Vessels)
            {
                int a = on.Count(b => b == v.instance_index), c = off.Count(b => b == v.instance_index);
                Assert.LessOrEqual(Mathf.Abs(a - c), 20 + c / 50, $"{v.placement.object_id}: {a} px with lights vs {c} without");
            }
        }
    }

    public class PaintTests
    {
        SimWorld _w;
        [SetUp] public void SetUp() { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); _w = new SimWorld(); _w.Initialize(); }
        [TearDown] public void TearDown() => _w.Dispose();

        [Test]
        public void Catalog_HullSlotsReachTheWaterline_AndExcludedMaterialsStayOriginal()
        {
            var cat = VesselCatalog.Load();
            int withHull = 0;
            foreach (var e in cat.entries)
            {
                Assert.IsNotNull(e.paint_slots, e.asset_id);
                Assert.Greater(e.paint_slots.Count, 0, e.asset_id + ": no slots analysed (bootstrap not re-run?)");
                var hull = e.paint_slots.Where(s => s.role == "hull").ToList();
                if (hull.Count > 0) withHull++;
                foreach (var s in e.paint_slots.Where(s => s.role != "keep"))
                {
                    var n = s.material_name.ToLowerInvariant(); if (n.StartsWith(e.asset_id + "_")) n = n.Substring(e.asset_id.Length + 1);
                    foreach (var w in new[] { "glass", "glaz", "steel", "alumin", "teak", "rubber", "navlight" })
                        Assert.IsFalse(n.Contains(w), $"{e.asset_id}: {s.material_name} is repainted ({s.role})");
                    Assert.IsNotNull(VesselPaint.Find(e.prefab.transform, s.renderer_path)?.GetComponent<Renderer>(), $"{e.asset_id}: slot path {s.renderer_path} not found");
                }
            }
            Assert.GreaterOrEqual(withHull, 20, "vessel types with a repaintable hull");
        }

        ScenarioConfig Scene(string paint, int seed)
        {
            var s = SimulatorApp.DefaultScenarioFor(ContainerTerminal01.Id);
            s.environment.water_preset_id = "calm";
            s.appearance = new AppearanceConfig { paint = paint, paint_seed = seed };
            return s;
        }

        System.Collections.Generic.Dictionary<string, string> Hulls() => _w.Vessels.ToDictionary(v => v.placement.object_id, v => v.paint.hull_srgb);

        [Test]
        public void RandomPaint_IsDeterministic_PerVessel_AndOriginalKeepsThePrefabMaterials()
        {
            _w.Apply(Scene("original", 1));
            foreach (var v in _w.Vessels)
            {
                Assert.AreEqual("original", v.paint.mode);
                var a = v.root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).ToList();
                var b = v.entry.prefab.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).ToList();
                CollectionAssert.AreEqual(b, a, v.placement.object_id + ": original paint must keep the prefab materials");
            }

            _w.Apply(Scene("random", 5));
            var first = Hulls();
            Assert.Greater(first.Values.Count(h => h != ""), first.Count / 2, "most vessels repainted");
            Assert.Greater(first.Values.Where(h => h != "").Distinct().Count(), 2, "not all one colour");
            _w.Apply(Scene("random", 5));
            CollectionAssert.AreEquivalent(first, Hulls(), "same seed -> same colours");

            var more = Scene("random", 5);
            more.vessels.RemoveAt(0);
            _w.Apply(more);
            foreach (var kv in Hulls()) Assert.AreEqual(first[kv.Key], kv.Value, kv.Key + " changed colour when another vessel was removed");

            _w.Apply(Scene("random", 6));
            Assert.IsTrue(Hulls().Any(kv => first[kv.Key] != kv.Value), "another seed -> other colours");

            var fixedS = Scene("original", 1);
            fixedS.vessels[0].paint = "hull=#AA2211;super=#EEEEEE";
            _w.Apply(fixedS);
            Assert.AreEqual("#AA2211", _w.Vessels[0].paint.hull_srgb);
            Assert.AreEqual("original", _w.Vessels[1].paint.mode);
            Assert.Throws<System.ArgumentException>(() => { var bad = Scene("purple", 1); SimWorld.Validate(bad); });
        }

        [Test]
        public void Repainting_DoesNotChangeTheGpuMask()
        {
            const int W = 640, H = 360;
            byte[] Mask(string paint)
            {
                _w.Apply(Scene(paint, 3));
                var cam = _w.CctvCamera; cam.aspect = (float)W / H;
                var rt = new RenderTexture(W, H, 24); cam.targetTexture = rt;
                cam.Render(); _w.FreezeWaterAt(12f); cam.Render();
                cam.targetTexture = null; rt.Release(); Object.DestroyImmediate(rt);
                return _w.GpuMask.Render(cam, _w.Vessels, W, H);
            }
            var a = Mask("original"); var b = Mask("random");
            int diff = 0; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) diff++;
            Assert.LessOrEqual(diff, 0, "instance mask must not depend on paint");
        }
    }
}
