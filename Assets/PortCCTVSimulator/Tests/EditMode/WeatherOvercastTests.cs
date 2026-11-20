using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PortCCTV.Tests
{
    /// <summary>2026-11-20: an overcast day lowers visibility. Before, "overcast" changed the sky and the light only: the fog mean
    /// free path stayed that of clear weather and vessels had a HIGHER luma contrast than in clear weather (white sea).
    /// The research weather cells that must render as in the 2026-11-16 experiment pin overcast_haze to 0.</summary>
    public class WeatherOvercastTests
    {
        SimWorld _w;
        [SetUp] public void SetUp() { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); _w = new SimWorld(); _w.Initialize(); }
        [TearDown] public void TearDown() => _w.Dispose();

        (float mfp, float ev) Apply(ScenarioConfig s, WeatherConfig wx)
        {
            s.environment.weather = wx; _w.Apply(s);
            return (_w.Environment.fog_mean_free_path_m, _w.Environment.exposure_ev100);
        }

        [Test]
        public void Overcast_LowersVisibilityAndExposure_PinnedZeroKeepsTheEarlierRendering_RainIsUnchanged()
        {
            var s = SimulatorApp.DefaultScenarioFor(FishingPort01.Id);
            var clear = Apply(s, new WeatherConfig());
            Assert.Greater(clear.mfp, 1000f, "clear day: aerial haze only");

            var oc = Apply(s, new WeatherConfig { overcast = true });
            Assert.AreEqual(0.5f * clear.mfp, oc.mfp, 1e-2f * clear.mfp, "overcast: fog mean free path halved");
            Assert.AreEqual(clear.ev + 0.1f, oc.ev, 1e-4f, "overcast with haze: 0.1 EV darker than a clear day");
            Assert.AreEqual(1f, _w.Environment.weather.overcast_haze, 1e-6f);
            Assert.AreEqual(0.5f, _w.Environment.weather.overcast_visibility_factor, 1e-6f);
            StringAssert.Contains(SimWorld.OvercastSky, _w.Environment.sky);

            // research cells C3 / C4 (fog under an overcast sky): the rendering values of the 2026-11-16 experiment
            var legacy = Apply(s, new WeatherConfig { overcast = true, overcast_haze = 0f });
            Assert.AreEqual(clear.mfp, legacy.mfp, 1e-3f * clear.mfp, "haze 0: sky and light only");
            Assert.AreEqual(clear.ev - 0.7f, legacy.ev, 1e-4f);
            var fog = Apply(s, new WeatherConfig { overcast = true, overcast_haze = 0f, visibility_scale = 0.05f });
            Assert.AreEqual(0.05f * clear.mfp, fog.mfp, 1e-3f * clear.mfp, "dense fog cell: 160 m at the day_clear preset, as before");

            // rain and snow keep their own visibility factor and exposure rule (automatic haze 0)
            var wx = new WeatherConfig { precipitation = "rain", intensity = 0.9f, seed = 3 };
            var rain = Apply(s, wx);
            Assert.AreEqual(0f, _w.Environment.weather.overcast_haze, 1e-6f);
            Assert.AreEqual(clear.mfp * wx.PrecipitationVisibilityFactor, rain.mfp, 1e-3f * clear.mfp);
            Assert.AreEqual(clear.ev - (0.7f + 0.4f * 0.9f), rain.ev, 1e-4f);

            // a half haze sits in between; a clear sky ignores the setting
            var half = Apply(s, new WeatherConfig { overcast = true, overcast_haze = 0.5f });
            Assert.AreEqual(0.75f * clear.mfp, half.mfp, 1e-2f * clear.mfp);
            Assert.AreEqual(clear.ev - 0.3f, half.ev, 1e-4f);
            var ignored = Apply(s, new WeatherConfig { overcast = false, overcast_haze = 1f });
            Assert.AreEqual(clear.mfp, ignored.mfp, 1e-3f * clear.mfp);
            Assert.AreEqual(clear.ev, ignored.ev, 1e-4f);
        }

        [Test]
        public void OvercastHaze_RoundTripsThroughTheScenarioFile_AndOlderFilesMeanAutomatic()
        {
            var s = SimulatorApp.DefaultScenarioFor(Marina01.Id);
            s.environment.weather = new WeatherConfig { overcast = true, overcast_haze = 0f };
            var back = ScenarioIO.FromJson(ScenarioIO.ToJson(s));
            Assert.AreEqual(0f, back.environment.weather.overcast_haze, 1e-6f);
            // a scenario file written before the field existed: automatic
            var json = ScenarioIO.ToJson(s).Replace("\"overcast_haze\": 0.0,", "").Replace("\"overcast_haze\": 0.0", "");
            StringAssert.DoesNotContain("overcast_haze", json);
            var old = ScenarioIO.FromJson(json);
            Assert.Less(old.environment.weather.overcast_haze, 0f);
            Assert.AreEqual(1f, old.environment.weather.OvercastHaze, 1e-6f);
        }
    }
}
