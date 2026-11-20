using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PortCCTV.Tests
{
    /// <summary>2026-11-20: vessel poses must not depend on whether a scenario is the first one applied after a harbour build.
    /// Found in the framing verification render: the first fishing-port job of a batch (and a frame re-rendered alone by a resumed
    /// batch) had one vessel in another pose than the same scene in the other weather cells / in the episode plan, because
    /// colliders created and then moved by the vegetation pass were still at the map origin in the physics scene.</summary>
    public class NavigationSyncTests
    {
        static readonly string[] Maps = { ContainerTerminal01.Id, FishingPort01.Id, Marina01.Id };

        static SimWorld Fresh()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var w = new SimWorld();
            w.Initialize();
            return w;
        }

        [Test]
        public void HarbourBuild_LeavesEveryColliderAtItsTransformInThePhysicsScene()
        {
            var build = typeof(SimWorld).GetMethod("EnsureHarbor", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(build, "SimWorld.EnsureHarbor");
            foreach (var hid in Maps)
            {
                var w = Fresh();
                try
                {
                    build.Invoke(w, new object[] { hid });
                    var before = w.HarborColliders.Where(c => c != null).Select(c => (c, b: c.bounds)).ToList();
                    Assert.Greater(before.Count, 100, hid);
                    Physics.SyncTransforms();
                    var stale = before.Where(t => (t.c.bounds.center - t.b.center).magnitude > 0.01f || (t.c.bounds.size - t.b.size).magnitude > 0.01f)
                        .Select(t => t.c.gameObject.name + " " + t.b.center + " -> " + t.c.bounds.center).ToList();
                    Assert.IsEmpty(stale, hid + ": colliders that a physics query would have found at another place right after the build");
                }
                finally { w.Dispose(); }
            }
        }

        // Verification episode ep_fp_D_market_entrance_s0033, frame 6 (t = 13 s): an inflatable heading for a waypoint 12.6 m from the
        // map origin, where the shore line pieces were piled before the fix. First applied scenario: held at (-33.1, -12.5) for an
        // obstacle; every later application: arrived near (-5.0, -0.6).
        static ScenarioConfig BoatTowardsTheMapOrigin()
        {
            var s = FishingPort01.DefaultScenario();
            s.scenario_id = "nav_sync_test";
            s.camera_preset_id = "fp_D_market_entrance";
            s.vessels.Clear();
            s.vessels.Add(new VesselPlacement
            {
                object_id = "m00", asset_id = "crrc", x = -60.1425f, z = -12.9132f, heading_deg = -17.5292f, immersion_m = 0.15f,
                motion = new VesselMotion { mode = "route", speed_kn = 9.1449f, route = { new Vector2(8.9135f, 8.8987f) }, loop = false, keep_in_view = false, seed = 33 },
            });
            s.navigation = new NavigationConfig { sim_time_s = 13f, dt_s = 0.25f, collision_avoidance = true, wind_from_deg = 300f, wind_speed_kn = 8f, current_set_deg = 60f, current_drift_kn = 0.4f };
            return s;
        }

        [Test]
        public void FirstScenarioAfterAHarbourBuild_GivesTheSameVesselPosesAsARepeat()
        {
            var cfg = BoatTowardsTheMapOrigin();
            var w = Fresh();
            try
            {
                w.Apply(cfg);
                var first = w.Nav.Record().vessels.Single();
                w.Apply(cfg);
                var again = w.Nav.Record().vessels.Single();
                Assert.AreEqual(again.state, first.state);
                Assert.Less(Vector2.Distance(first.position_xz, again.position_xz), 1e-3f, $"first {first.position_xz} ({first.state}), repeat {again.position_xz} ({again.state})");
                Assert.Less(Mathf.Abs(Mathf.DeltaAngle(first.heading_deg, again.heading_deg)), 1e-2f);
                // the case stays meaningful only while the boat really crosses the water around the origin
                Assert.Less(again.position_xz.magnitude, 20f, "the boat reaches the origin area: " + again.position_xz);
                Assert.AreNotEqual("holding_obstacle", again.state);
            }
            finally { w.Dispose(); }
        }

        // Vessel hulls are in the same physics scene as the harbour during the interactive playback (hundreds of mesh colliders per
        // prefab). The structure test must still find the quay when the box holds more other colliders than its buffer.
        // Guards the complete-query path only: with the former 32-slot buffer this arrangement passed as well (the harbour collider
        // happened to be among the first results), so no wrong answer of the old code was reproduced.
        [Test]
        public void Blocked_FindsTheHarbourStructure_AmongMoreOtherCollidersThanTheBufferHolds()
        {
            var cfg = FishingPort01.DefaultScenario(); cfg.vessels.Clear();
            var w = Fresh();
            var crowd = new GameObject("crowd_not_harbour");
            try
            {
                w.Apply(cfg);
                var hit = w.HarborColliders.Where(c => c != null).Select(c => new Vector2(c.bounds.center.x, c.bounds.center.z)).FirstOrDefault(p => w.Nav.Blocked(p, 3f));
                Assert.IsTrue(w.Nav.Blocked(hit, 3f), "a harbour collider on the waterline band");
                float y = w.Harbor.water_reference_y + 1f;
                for (int i = 0; i < 1500; i++)
                {
                    var g = new GameObject("c" + i); g.transform.SetParent(crowd.transform, false);
                    g.transform.position = new Vector3(hit.x + (i % 5 - 2) * 0.2f, y, hit.y + (i / 5 % 5 - 2) * 0.2f);
                    g.AddComponent<SphereCollider>().radius = 0.3f;
                }
                Physics.SyncTransforms();
                Assert.Greater(Physics.OverlapBox(new Vector3(hit.x, y, hit.y), new Vector3(3f, 1f, 3f)).Length, 1500, "the crowd is in the query box");
                Assert.IsTrue(w.Nav.Blocked(hit, 3f), "still blocked by the structure");
                Assert.IsFalse(w.Nav.Blocked(new Vector2(0f, -900f), 3f), "open sea stays clear");
            }
            finally { Object.DestroyImmediate(crowd); w.Dispose(); }
        }
    }
}
