using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PortCCTV.EditorTools
{
    /// <summary>
    /// Procedural road vehicles for parking lots and terminal traffic (all background scenery, never labelled):
    /// sedan / SUV / hatchback / one-box van in six body colours, a 1-ton flatbed truck (Korean "Porter" class, generic shape),
    /// container tractor units in four cab colours and a 40 ft skeletal trailer chassis, plus a 10-stall parking-line strip.
    /// Local frame: nose along +x, ground y = 0, centred in x/z. Real-world dimensions (m). Shared meshes per body type; colour
    /// variants only swap the "car_paint" renderer material.
    /// </summary>
    public static partial class VillageAssets
    {
        public static readonly string[] CarPaints = { "white", "black", "silver", "grey", "navy", "red" };
        public static readonly string[] CarTypes = { "car_sedan", "car_suv", "car_hatch", "car_van" };
        public static readonly string[] TractorCabs = { "red", "white", "blue", "green" };

        static void ExtrudeProfile(MB b, string capMat, string sideMat, Vector2[] prof, float z0, float z1)
        {
            // profile CCW in (x, y); fan from the centroid (profiles are star-shaped around it)
            var c = Vector2.zero; foreach (var p in prof) c += p; c /= prof.Length;
            for (int i = 0; i < prof.Length; i++)
            {
                var a = prof[i]; var d = prof[(i + 1) % prof.Length];
                b.TriN(capMat, new Vector3(c.x, c.y, z0), new Vector3(a.x, a.y, z0), new Vector3(d.x, d.y, z0), Vector3.back);
                b.TriN(capMat, new Vector3(c.x, c.y, z1), new Vector3(a.x, a.y, z1), new Vector3(d.x, d.y, z1), Vector3.forward);
                var e = d - a; var n = new Vector3(e.y, -e.x, 0f);
                b.QuadN(sideMat, new Vector3(a.x, a.y, z0), new Vector3(d.x, d.y, z0), new Vector3(d.x, d.y, z1), new Vector3(a.x, a.y, z1), n);
            }
        }

        /// <summary>Wheel: n-sided prism along z (tyre) with a lighter hub disc on the outer face.</summary>
        static void Wheel(MB b, float x, float z, float r, float w, bool outerPlusZ, int n = 10)
        {
            var prof = new Vector2[n];
            for (int i = 0; i < n; i++) { float a = Mathf.PI * 2 * i / n; prof[i] = new Vector2(x + r * Mathf.Cos(a), r + r * Mathf.Sin(a)); }
            ExtrudeProfile(b, "rubber", "rubber", prof, z - w / 2, z + w / 2);
            float s = outerPlusZ ? 1f : -1f;
            b.Box("car_hub", new Vector3(x, r, z + s * (w / 2 + 0.005f)), new Vector3(r * 1.1f, r * 1.1f, 0.01f));
        }

        static void Lights(MB b, float L, float y, float hw, float h = 0.14f)
        {
            foreach (var sz in new[] { -1f, 1f })
            {
                b.Box("car_light_front", new Vector3(L / 2 + 0.01f, y, sz * (hw - 0.25f)), new Vector3(0.04f, h, 0.36f));
                b.Box("car_light_rear", new Vector3(-L / 2 - 0.01f, y, sz * (hw - 0.22f)), new Vector3(0.04f, h, 0.30f));
            }
        }

        static void Bumpers(MB b, float L, float hw, float y0, float y1)
        {
            b.Box("car_trim", new Vector3(L / 2 - 0.02f, (y0 + y1) / 2, 0), new Vector3(0.12f, y1 - y0, hw * 2 - 0.06f));
            b.Box("car_trim", new Vector3(-L / 2 + 0.02f, (y0 + y1) / 2, 0), new Vector3(0.12f, y1 - y0, hw * 2 - 0.06f));
        }

        /// <summary>Passenger car: lower body + glass cabin + painted roof plate, pillars, mirrors, bumpers, lamps, wheels.</summary>
        static GameObject Car(string name, float L, float W, float wheelR, float wheelBase, Vector2[] body, Vector2[] cabin, float roofX0, float roofX1, float roofY)
        {
            var b = new MB();
            float hw = W / 2, chw = hw - 0.11f;
            ExtrudeProfile(b, "car_paint", "car_paint", body, -hw, hw);
            ExtrudeProfile(b, "car_glass", "car_glass", cabin, -chw, chw);
            b.Box("car_paint", new Vector3((roofX0 + roofX1) / 2, roofY + 0.025f, 0), new Vector3(roofX1 - roofX0 + 0.06f, 0.05f, chw * 2 + 0.04f));
            // B / C pillars on both sides (paint stripes over the side glass)
            float cy0 = cabin[0].y, cy1 = roofY;
            foreach (var px in new[] { (roofX0 + roofX1) / 2 + 0.15f, roofX0 + 0.05f })
                foreach (var sz in new[] { -1f, 1f })
                    b.Box("car_paint", new Vector3(px, (cy0 + cy1) / 2, sz * (chw + 0.005f)), new Vector3(0.12f, cy1 - cy0, 0.02f));
            foreach (var sz in new[] { -1f, 1f }) b.Box("car_paint", new Vector3(roofX1 + 0.55f, cy0 + 0.12f, sz * (hw + 0.08f)), new Vector3(0.12f, 0.1f, 0.18f));   // mirrors
            Bumpers(b, L, hw, wheelR * 0.75f, wheelR * 1.35f);
            Lights(b, L, wheelR * 1.9f, hw);
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sz in new[] { -1f, 1f })
                    Wheel(b, sx * wheelBase / 2, sz * (hw - 0.09f), wheelR, 0.22f, sz > 0);   // outer face 2 cm proud of the body side
            return b.Flush(name);
        }

        static GameObject Sedan() => Car("car_sedan", 4.75f, 1.84f, 0.32f, 2.84f,
            new[] { new Vector2(-2.37f, 0.30f), new Vector2(2.37f, 0.30f), new Vector2(2.40f, 0.62f), new Vector2(2.12f, 0.80f), new Vector2(1.05f, 0.94f), new Vector2(-1.55f, 0.97f), new Vector2(-2.30f, 0.92f), new Vector2(-2.40f, 0.62f) },
            new[] { new Vector2(-1.55f, 0.96f), new Vector2(1.05f, 0.93f), new Vector2(0.22f, 1.42f), new Vector2(-0.95f, 1.42f) },
            -0.95f, 0.22f, 1.42f);

        static GameObject Suv() => Car("car_suv", 4.70f, 1.90f, 0.37f, 2.80f,
            new[] { new Vector2(-2.35f, 0.38f), new Vector2(2.35f, 0.38f), new Vector2(2.38f, 0.78f), new Vector2(2.05f, 1.02f), new Vector2(1.10f, 1.08f), new Vector2(-2.20f, 1.10f), new Vector2(-2.38f, 0.80f) },
            new[] { new Vector2(-2.20f, 1.09f), new Vector2(1.10f, 1.07f), new Vector2(0.35f, 1.72f), new Vector2(-2.05f, 1.72f) },
            -2.05f, 0.35f, 1.72f);

        static GameObject Hatch() => Car("car_hatch", 4.05f, 1.78f, 0.31f, 2.60f,
            new[] { new Vector2(-2.02f, 0.30f), new Vector2(2.02f, 0.30f), new Vector2(2.05f, 0.62f), new Vector2(1.80f, 0.80f), new Vector2(0.85f, 0.93f), new Vector2(-1.95f, 0.98f), new Vector2(-2.05f, 0.62f) },
            new[] { new Vector2(-1.95f, 0.97f), new Vector2(0.85f, 0.92f), new Vector2(0.05f, 1.48f), new Vector2(-1.80f, 1.46f) },
            -1.80f, 0.05f, 1.47f);

        static GameObject Van() => Car("car_van", 5.15f, 1.98f, 0.35f, 3.20f,
            new[] { new Vector2(-2.57f, 0.36f), new Vector2(2.57f, 0.36f), new Vector2(2.60f, 0.78f), new Vector2(2.30f, 1.02f), new Vector2(-2.50f, 1.06f), new Vector2(-2.60f, 0.80f) },
            new[] { new Vector2(-2.50f, 1.05f), new Vector2(2.30f, 1.01f), new Vector2(1.35f, 1.96f), new Vector2(-2.45f, 1.96f) },
            -2.45f, 1.35f, 1.96f);

        /// <summary>1-ton flatbed truck: tall cab-over at the front, drop-side bed behind.</summary>
        static GameObject LightTruck()
        {
            var b = new MB();
            const float L = 5.1f, hw = 0.87f;
            b.Box("car_trim", new Vector3(0, 0.55f, 0), new Vector3(L - 0.3f, 0.22f, 1.0f));                       // chassis rails
            b.Box("car_paint", new Vector3(1.75f, 1.18f, 0), new Vector3(1.55f, 1.40f, hw * 2));                    // cab lower
            ExtrudeProfile(b, "car_glass", "car_glass", new[] { new Vector2(1.00f, 1.88f), new Vector2(2.50f, 1.88f), new Vector2(2.35f, 2.40f), new Vector2(1.00f, 2.40f) }, -hw + 0.06f, hw - 0.06f);
            b.Box("car_paint", new Vector3(1.75f, 2.42f, 0), new Vector3(1.55f, 0.06f, hw * 2));                   // cab roof
            b.Box("car_paint", new Vector3(1.05f, 2.14f, 0), new Vector3(0.12f, 0.56f, hw * 2));                    // cab back wall top
            // bed: floor + drop sides (light grey steel)
            b.Box("house_roof_steel", new Vector3(-0.75f, 0.92f, 0), new Vector3(3.3f, 0.08f, hw * 2 + 0.06f));
            foreach (var sz in new[] { -1f, 1f }) b.Box("house_roof_steel", new Vector3(-0.75f, 1.16f, sz * (hw + 0.02f)), new Vector3(3.3f, 0.40f, 0.04f));
            b.Box("house_roof_steel", new Vector3(-2.40f, 1.16f, 0), new Vector3(0.04f, 0.40f, hw * 2 + 0.06f));
            b.Box("house_roof_steel", new Vector3(0.90f, 1.16f, 0), new Vector3(0.04f, 0.40f, hw * 2 + 0.06f));
            Bumpers(b, L, hw, 0.28f, 0.52f);
            Lights(b, L, 0.75f, hw);
            foreach (var sz in new[] { -1f, 1f }) { Wheel(b, 1.75f, sz * (hw - 0.14f), 0.30f, 0.20f, sz > 0); Wheel(b, -1.10f, sz * (hw - 0.14f), 0.30f, 0.26f, sz > 0); }
            return b.Flush("truck_light");
        }

        /// <summary>Container tractor unit (cab-over, 6x4), 6.9 m, fifth wheel at x = -1.6.</summary>
        static GameObject Tractor()
        {
            var b = new MB();
            const float hw = 1.25f;
            b.Box("car_trim", new Vector3(-0.4f, 0.95f, 0), new Vector3(6.6f, 0.35f, 0.9f));                        // frame
            b.Box("car_paint", new Vector3(2.35f, 1.95f, 0), new Vector3(2.1f, 1.9f, hw * 2));                      // cab lower
            ExtrudeProfile(b, "car_glass", "car_paint", new[] { new Vector2(1.30f, 2.90f), new Vector2(3.40f, 2.90f), new Vector2(3.30f, 3.75f), new Vector2(1.30f, 3.75f) }, -hw, hw);
            b.Box("car_glass", new Vector3(3.42f, 3.32f, 0), new Vector3(0.04f, 0.70f, hw * 2 - 0.3f));            // windscreen
            b.Box("car_paint", new Vector3(2.35f, 3.80f, 0), new Vector3(2.15f, 0.10f, hw * 2 + 0.02f));
            b.Box("car_trim", new Vector3(1.10f, 2.6f, -0.9f), new Vector3(0.18f, 2.4f, 0.18f));                   // exhaust stack
            b.Box("steel_dark", new Vector3(-1.6f, 1.25f, 0), new Vector3(1.2f, 0.12f, 1.4f));                      // fifth wheel
            b.Box("car_trim", new Vector3(3.40f, 0.85f, 0), new Vector3(0.20f, 0.40f, hw * 2));                    // bumper
            Lights(b, 6.9f, 1.1f, hw, 0.18f);
            foreach (var sz in new[] { -1f, 1f })
            {
                Wheel(b, 2.40f, sz * (hw - 0.18f), 0.52f, 0.30f, sz > 0);
                foreach (var x in new[] { -1.05f, -2.40f }) Wheel(b, x, sz * (hw - 0.30f), 0.52f, 0.55f, sz > 0);
                b.Box("car_trim", new Vector3(-1.7f, 1.15f, sz * (hw - 0.2f)), new Vector3(2.6f, 0.05f, 0.5f));   // mudguards
            }
            return b.Flush("truck_tractor");
        }

        /// <summary>40 ft skeletal chassis: twin beams, twistlock cross members, landing legs, tri-axle at the rear (12.4 m).</summary>
        static GameObject Chassis()
        {
            var b = new MB();
            const float L = 12.4f, hw = 1.25f;
            foreach (var sz in new[] { -0.5f, 0.5f }) b.Box("car_trim", new Vector3(0, 1.25f, sz), new Vector3(L, 0.35f, 0.18f));
            foreach (var x in new[] { -6.0f, -3.0f, 0f, 3.0f, 6.0f }) b.Box("car_trim", new Vector3(x, 1.38f, 0), new Vector3(0.25f, 0.12f, hw * 2));
            foreach (var sz in new[] { -1f, 1f }) b.Box("steel_dark", new Vector3(3.6f, 0.62f, sz * 0.9f), new Vector3(0.18f, 1.25f, 0.18f));   // landing legs
            foreach (var sz in new[] { -1f, 1f }) foreach (var x in new[] { -3.2f, -4.5f, -5.8f }) Wheel(b, x, sz * (hw - 0.30f), 0.50f, 0.55f, sz > 0);
            b.Box("car_light_rear", new Vector3(-L / 2 - 0.02f, 0.95f, 0), new Vector3(0.04f, 0.14f, hw * 2 - 0.2f));
            return b.Flush("trailer_chassis");
        }

        /// <summary>Ten 2.5 m parking stalls: 11 white stall lines 5 m long (+ the end line at the aisle side is the aisle edge).</summary>
        static GameObject ParkingStripes()
        {
            var b = new MB();
            for (int i = 0; i <= 10; i++) b.Box("paint_white", new Vector3(-12.5f + i * 2.5f, 0.005f, 0), new Vector3(0.12f, 0.01f, 5.0f));
            return b.Flush("parking_stripes_10");
        }

        /// <summary>Vinyl greenhouse: half-elliptic tunnel 7 m wide, 3.2 m high, 40 m long (z), film over the whole shell, a door
        /// frame on each end and a low concrete kerb.</summary>
        static GameObject Greenhouse()
        {
            var b = new MB();
            const int n = 14; var prof = new Vector2[n + 1];
            for (int i = 0; i <= n; i++) { float a = Mathf.PI * i / n; prof[i] = new Vector2(3.5f * Mathf.Cos(a), 0.15f + 3.05f * Mathf.Sin(a)); }
            ExtrudeProfile(b, "greenhouse_film", "greenhouse_film", prof, -20f, 20f);
            foreach (var sx in new[] { -1f, 1f }) b.Box("lane_concrete", new Vector3(sx * 3.45f, 0.08f, 0), new Vector3(0.2f, 0.16f, 40f));
            foreach (var sz in new[] { -1f, 1f })
            {
                b.Box("house_frame", new Vector3(0, 1.1f, sz * 20.02f), new Vector3(1.8f, 2.2f, 0.05f));
                for (float z = -18f; z <= 18f; z += 6f) b.Box("house_frame", new Vector3(0, 3.22f, z), new Vector3(0.06f, 0.06f, 0.06f));
            }
            return b.Flush("greenhouse_vinyl");
        }

        /// <summary>Beach parasol (2.4 m, 8-panel canopy r 1.1 m in "car_paint" for colour variants) with two loungers.</summary>
        static GameObject Parasol()
        {
            var b = new MB();
            b.Box("guard_rail", new Vector3(0, 1.2f, 0), new Vector3(0.05f, 2.4f, 0.05f));
            const int n = 8; var top = new Vector3(0, 2.45f, 0);
            for (int i = 0; i < n; i++)
            {
                float a0 = Mathf.PI * 2 * i / n, a1 = Mathf.PI * 2 * (i + 1) / n;
                var p0 = new Vector3(1.1f * Mathf.Cos(a0), 2.0f, 1.1f * Mathf.Sin(a0)); var p1 = new Vector3(1.1f * Mathf.Cos(a1), 2.0f, 1.1f * Mathf.Sin(a1));
                var mid = (p0 + p1) / 2f;
                b.TriN("car_paint", top, p0, p1, new Vector3(mid.x, 1.5f, mid.z));
                b.TriN("car_paint", top, p1, p0, new Vector3(-mid.x, -1f, -mid.z));
            }
            foreach (var sx in new[] { -1f, 1f })
            {
                b.Box("house_frame", new Vector3(sx * 0.75f, 0.32f, 0.3f), new Vector3(0.6f, 0.08f, 1.9f));
                b.Box("house_frame", new Vector3(sx * 0.75f, 0.5f, 1.15f), new Vector3(0.6f, 0.4f, 0.08f), 0f);
            }
            return b.Flush("beach_parasol");
        }

        /// <summary>Lifeguard tower: four legs, platform, small cabin with a red roof, ladder (4.2 m).</summary>
        static GameObject LifeguardTower()
        {
            var b = new MB();
            foreach (var sx in new[] { -1f, 1f }) foreach (var sz in new[] { -1f, 1f }) b.Box("house_frame", new Vector3(sx * 1.1f, 1.2f, sz * 1.1f), new Vector3(0.14f, 2.4f, 0.14f));
            b.Box("house_frame", new Vector3(0, 2.45f, 0), new Vector3(2.6f, 0.12f, 2.6f));
            b.Box("house_wall_white", new Vector3(0, 3.3f, 0.2f), new Vector3(2.0f, 1.6f, 1.8f));
            b.Box("house_glass", new Vector3(0, 3.5f, -0.72f), new Vector3(1.6f, 0.6f, 0.04f));
            b.Box("car_paint_red", new Vector3(0, 4.15f, 0.1f), new Vector3(2.5f, 0.12f, 2.4f));
            for (float y = 0.3f; y < 2.4f; y += 0.35f) b.Box("house_frame", new Vector3(0, y, -1.6f - y * 0.25f), new Vector3(0.7f, 0.05f, 0.18f));
            return b.Flush("lifeguard_tower");
        }

        /// <summary>Park bench 1.8 m: wooden slats on two steel frames; faces -z.</summary>
        static GameObject Bench()
        {
            var b = new MB();
            for (int i = 0; i < 3; i++) b.Box("bench_wood", new Vector3(0, 0.45f, -0.18f + i * 0.14f), new Vector3(1.8f, 0.04f, 0.11f));
            for (int i = 0; i < 2; i++) b.Box("bench_wood", new Vector3(0, 0.62f + i * 0.14f, 0.2f), new Vector3(1.8f, 0.11f, 0.04f));
            foreach (var sx in new[] { -0.75f, 0.75f }) { b.Box("car_trim", new Vector3(sx, 0.22f, 0), new Vector3(0.06f, 0.44f, 0.5f)); b.Box("car_trim", new Vector3(sx, 0.6f, 0.22f), new Vector3(0.06f, 0.4f, 0.06f)); }
            return b.Flush("bench");
        }

        /// <summary>Pallet with a 2 x 2 x 5 stack of plastic fish boxes (0.58 x 0.28 x 0.48 m each, "car_paint" colour), 1.6 m high.</summary>
        static GameObject FishBoxStack()
        {
            var b = new MB();
            b.Box("bench_wood", new Vector3(0, 0.07f, 0), new Vector3(1.2f, 0.14f, 1.0f));
            for (int k = 0; k < 5; k++)
                foreach (var sx in new[] { -0.3f, 0.3f }) foreach (var sz in new[] { -0.25f, 0.25f })
                    b.Box("car_paint", new Vector3(sx, 0.14f + 0.14f + k * 0.29f, sz), new Vector3(0.58f, 0.27f, 0.48f));
            return b.Flush("fishbox_stack");
        }

        /// <summary>Pile of fishing net (low irregular mound, r ~1.7 m, 0.9 m high) with orange floats.</summary>
        static GameObject NetPile()
        {
            var b = new MB();
            const int n = 10; var top = new Vector3(0.2f, 0.9f, -0.1f);
            var rim = new Vector3[n]; var mid = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.PI * 2 * i / n, r = 1.7f * (0.85f + 0.15f * Mathf.Sin(3f * a));
                rim[i] = new Vector3(r * Mathf.Cos(a), 0f, r * Mathf.Sin(a) * 0.8f);
                mid[i] = new Vector3(0.6f * rim[i].x, 0.65f, 0.6f * rim[i].z);
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n; var o = new Vector3(rim[i].x + rim[j].x, 0.5f, rim[i].z + rim[j].z);
                b.QuadN("net_green", rim[i], rim[j], mid[j], mid[i], o);
                b.TriN("net_green", mid[i], mid[j], top, new Vector3(o.x * 0.3f, 1f, o.z * 0.3f));
                if (i % 2 == 0) b.Box("float_orange", (rim[i] + mid[i]) / 2f + Vector3.up * 0.1f, new Vector3(0.22f, 0.16f, 0.22f), i * 23f);
            }
            return b.Flush("net_pile");
        }

        /// <summary>Rail-mounted gantry crane (ARMG) for perpendicular container blocks: legs on rails at x = +-17 m, 12 m wide
        /// along the rails (z), girders at 24-26 m, trolley with cabin and spreader, sill beams and bogies.</summary>
        static GameObject ArmgCrane()
        {
            var b = new MB();
            const float hx = 17f, hz = 6f, top = 24f;
            foreach (var sx in new[] { -hx, hx })
            {
                foreach (var sz in new[] { -hz, hz })
                {
                    b.Box("crane_blue", new Vector3(sx, top / 2f, sz), new Vector3(1.2f, top, 1.2f));
                    b.Box("steel_dark", new Vector3(sx, 0.7f, sz), new Vector3(1.6f, 1.4f, 3.2f));            // bogie
                }
                b.Box("crane_blue", new Vector3(sx, 2.2f, 0), new Vector3(1.4f, 1.4f, 2f * hz + 1.2f));         // sill beam
                b.Box("crane_blue", new Vector3(sx, top - 1.2f, 0), new Vector3(1.4f, 1.6f, 2f * hz + 1.2f));
            }
            foreach (var sz in new[] { -hz, hz }) b.Box("crane_blue", new Vector3(0, top + 1f, sz), new Vector3(2f * hx + 6f, 2.2f, 1.6f));   // main girders (cantilevers)
            for (float x = -hx; x <= hx; x += 8.5f) b.Box("crane_blue", new Vector3(x, top + 1.9f, 0), new Vector3(0.6f, 0.4f, 2f * hz));
            float tx = 3f;
            b.Box("crane_house", new Vector3(tx, top + 2.8f, 0), new Vector3(6f, 1.6f, 2f * hz - 1f));                  // trolley
            b.Box("crane_house", new Vector3(tx + 2.5f, top - 1.6f, -hz + 1.6f), new Vector3(2.2f, 2.2f, 2.2f));       // operator cabin
            b.Box("glass_dark", new Vector3(tx + 2.5f, top - 1.6f, -hz + 0.48f), new Vector3(1.8f, 1.2f, 0.04f));
            foreach (var sz in new[] { -2f, 2f }) b.Box("steel_dark", new Vector3(tx, top - 6f, sz), new Vector3(0.06f, 12f, 0.06f));  // ropes
            b.Box("crane_yellow", new Vector3(tx, top - 12.3f, 0), new Vector3(2.6f, 0.6f, 12.4f));                // spreader
            return b.Flush("armg_crane");
        }

        /// <summary>Empty-container handler (top-lift forklift for empties): body 7.5 m, cab, twin mast 16 m, spreader, big wheels.</summary>
        static GameObject EmptyHandler()
        {
            var b = new MB();
            b.Box("crane_yellow", new Vector3(-0.8f, 1.6f, 0), new Vector3(6.0f, 1.6f, 3.4f));
            b.Box("crane_yellow", new Vector3(-3.4f, 2.6f, 0), new Vector3(1.4f, 1.4f, 3.2f));                 // counterweight
            b.Box("crane_yellow", new Vector3(0.8f, 3.6f, 1.0f), new Vector3(1.8f, 2.2f, 1.4f));                // cab
            b.Box("car_glass", new Vector3(1.72f, 3.8f, 1.0f), new Vector3(0.04f, 1.2f, 1.2f));
            foreach (var sz in new[] { -0.9f, 0.9f }) b.Box("steel_dark", new Vector3(2.6f, 8.5f, sz), new Vector3(0.5f, 15f, 0.35f));   // mast
            b.Box("steel_dark", new Vector3(3.1f, 8.0f, 0), new Vector3(0.6f, 0.6f, 6.2f));                       // spreader
            foreach (var sz in new[] { -1f, 1f }) { Wheel(b, 1.6f, sz * 1.45f, 0.75f, 0.6f, sz > 0); Wheel(b, -2.8f, sz * 1.45f, 0.7f, 0.5f, sz > 0); }
            return b.Flush("empty_handler");
        }

        /// <summary>Market stall tent 3 x 3 m: four poles, pitched canopy ("car_paint" colour), counter table with fish boxes.</summary>
        static GameObject StallTent()
        {
            var b = new MB();
            foreach (var sx in new[] { -1.45f, 1.45f }) foreach (var sz in new[] { -1.45f, 1.45f }) b.Box("guard_rail", new Vector3(sx, 1.1f, sz), new Vector3(0.05f, 2.2f, 0.05f));
            var top = new Vector3(0, 3.0f, 0);
            var c = new[] { new Vector3(-1.55f, 2.2f, -1.55f), new Vector3(1.55f, 2.2f, -1.55f), new Vector3(1.55f, 2.2f, 1.55f), new Vector3(-1.55f, 2.2f, 1.55f) };
            for (int i = 0; i < 4; i++)
            {
                var a = c[i]; var d = c[(i + 1) % 4]; var o = (a + d) / 2f; o.y = 1.5f;
                b.TriN("car_paint", top, a, d, o); b.TriN("car_paint", top, d, a, -o);
            }
            b.Box("house_frame", new Vector3(0, 0.45f, -1.0f), new Vector3(2.6f, 0.9f, 0.8f));
            foreach (var x in new[] { -0.8f, 0f, 0.8f }) b.Box("car_paint_blue", new Vector3(x, 1.0f, -1.0f), new Vector3(0.55f, 0.2f, 0.45f));
            return b.Flush("stall_tent");
        }

        /// <summary>Boat in dry storage: white hull 8.5 m on steel cradle stands, tarp cover in "car_paint" colour.</summary>
        static GameObject CoveredBoat()
        {
            var b = new MB();
            var prof = new[] { new Vector2(-4.25f, 1.0f), new Vector2(2.6f, 0.75f), new Vector2(3.8f, 1.3f), new Vector2(4.35f, 2.35f), new Vector2(-4.25f, 2.25f) };
            ExtrudeProfile(b, "house_wall_white", "house_wall_white", prof, -1.35f, 1.35f);
            b.Box("car_paint", new Vector3(-0.3f, 2.75f, 0), new Vector3(7.4f, 1.0f, 2.85f));                   // tarp
            b.Box("car_paint", new Vector3(3.5f, 2.45f, 0), new Vector3(1.4f, 0.4f, 2.4f));
            foreach (var x in new[] { -2.8f, 0f, 2.2f }) foreach (var sz in new[] { -1.0f, 1.0f }) b.Box("steel_dark", new Vector3(x, 0.5f, sz), new Vector3(0.15f, 1.0f, 0.15f));
            foreach (var x in new[] { -2.8f, 0f, 2.2f }) b.Box("steel_dark", new Vector3(x, 0.08f, 0), new Vector3(0.3f, 0.16f, 2.6f));
            return b.Flush("covered_boat");
        }

        /// <summary>Marine fuel pump with hose reel (1.7 m).</summary>
        static GameObject FuelPump()
        {
            var b = new MB();
            b.Box("house_wall_white", new Vector3(0, 0.75f, 0), new Vector3(0.8f, 1.5f, 0.5f));
            b.Box("car_paint_red", new Vector3(0, 1.6f, 0), new Vector3(0.85f, 0.2f, 0.55f));
            b.Box("car_glass", new Vector3(0, 1.1f, -0.26f), new Vector3(0.4f, 0.25f, 0.02f));
            b.Box("car_trim", new Vector3(0.45f, 0.9f, 0), new Vector3(0.1f, 0.5f, 0.5f));
            return b.Flush("fuel_pump");
        }

        static void BuildVehicleMaterials(System.Action<string, Material> add)
        {
            var paint = new Dictionary<string, (Color c, float s, float m)>
            {
                { "white", (new Color(0.86f, 0.86f, 0.85f), 0.80f, 0.15f) }, { "black", (new Color(0.025f, 0.025f, 0.03f), 0.85f, 0.25f) },
                { "silver", (new Color(0.62f, 0.63f, 0.64f), 0.75f, 0.75f) }, { "grey", (new Color(0.28f, 0.29f, 0.30f), 0.75f, 0.55f) },
                { "navy", (new Color(0.05f, 0.09f, 0.20f), 0.80f, 0.40f) }, { "red", (new Color(0.48f, 0.04f, 0.04f), 0.80f, 0.30f) },
                { "blue", (new Color(0.28f, 0.46f, 0.70f), 0.70f, 0.20f) }, { "green", (new Color(0.12f, 0.36f, 0.20f), 0.70f, 0.20f) },
            };
            foreach (var kv in paint) add("car_paint_" + kv.Key, ProceduralAssets.MakeLit("car_paint_" + kv.Key, kv.Value.c, kv.Value.s, kv.Value.m));
            add("car_paint", ProceduralAssets.MakeLit("car_paint", paint["white"].c, 0.8f, 0.15f));
            add("car_glass", ProceduralAssets.MakeLit("car_glass", new Color(0.05f, 0.065f, 0.08f), 0.95f, 0.2f));
            add("car_trim", ProceduralAssets.MakeLit("car_trim", new Color(0.06f, 0.06f, 0.065f), 0.35f, 0f));
            add("car_hub", ProceduralAssets.MakeLit("car_hub", new Color(0.55f, 0.56f, 0.57f), 0.6f, 0.8f));
            add("car_light_front", ProceduralAssets.MakeLit("car_light_front", new Color(0.82f, 0.82f, 0.78f), 0.9f, 0.3f));
            add("net_green", ProceduralAssets.MakeLit("net_green", new Color(0.10f, 0.22f, 0.14f), 0.2f, 0f));
            add("float_orange", ProceduralAssets.MakeLit("float_orange", new Color(0.85f, 0.35f, 0.05f), 0.5f, 0f));
            add("bench_wood", ProceduralAssets.MakeLit("bench_wood", new Color(0.45f, 0.30f, 0.18f), 0.25f, 0f));
            add("greenhouse_film", ProceduralAssets.MakeLit("greenhouse_film", new Color(0.80f, 0.82f, 0.81f), 0.55f, 0f));
            add("car_light_rear", ProceduralAssets.MakeLit("car_light_rear", new Color(0.45f, 0.03f, 0.03f), 0.9f, 0.1f));
        }

        /// <summary>Saves `go` once per paint: the "car_paint" child gets car_paint_&lt;p&gt; (meshes shared between variants).</summary>
        static void PaintVariants(GameObject go, string baseKey, IEnumerable<string> paints, System.Action<string, GameObject, string> add, string src)
        {
            foreach (var p in paints)
            {
                var v = Object.Instantiate(go); v.name = $"{baseKey}_{p}";
                foreach (var r in v.GetComponentsInChildren<MeshRenderer>(true))
                    if (r.gameObject.name == "car_paint") r.sharedMaterial = _theme.Get("car_paint_" + p) ?? throw new System.InvalidOperationException("missing car_paint_" + p);
                add($"{baseKey}_{p}", v, src + $", {p}");
            }
            Object.DestroyImmediate(go);
        }

        /// <summary>Vehicles get one BoxCollider (rendered bounds) instead of a MeshCollider per material part: hundreds of parked
        /// cars with ~8 mesh colliders each made the CPU visible-mask raycasts 3-5x slower (marina 0.6 s -> 3.3 s per capture).
        /// Flat paint (stall lines) and small beach / quay furniture (parasols, benches, lifeguard towers, fish-box pallets, net
        /// piles: hundreds per map, on land, practically never in front of a vessel) get no collider at all.</summary>
        static GameObject SimpleCollider(GameObject go, bool none = false)
        {
            foreach (var mc in go.GetComponentsInChildren<MeshCollider>(true)) Object.DestroyImmediate(mc);
            if (none) return go;
            var rs = go.GetComponentsInChildren<Renderer>(true);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            var bc = go.AddComponent<BoxCollider>(); bc.center = go.transform.InverseTransformPoint(b.center); bc.size = b.size;
            return go;
        }

        static void BuildVehiclePrefabs(System.Action<string, GameObject, string> add)
        {
            const string src = "procedural road vehicle, background scenery, not labelled (VillageAssets.Vehicles)";
            PaintVariants(SimpleCollider(Sedan()), "car_sedan", CarPaints, add, src + ": sedan 4.75 x 1.84 x 1.45 m");
            PaintVariants(SimpleCollider(Suv()), "car_suv", CarPaints, add, src + ": SUV 4.70 x 1.90 x 1.77 m");
            PaintVariants(SimpleCollider(Hatch()), "car_hatch", CarPaints, add, src + ": hatchback 4.05 x 1.78 x 1.52 m");
            PaintVariants(SimpleCollider(Van()), "car_van", CarPaints, add, src + ": one-box van 5.15 x 1.98 x 2.0 m");
            PaintVariants(SimpleCollider(LightTruck()), "truck_light", new[] { "blue", "white" }, add, src + ": 1-ton flatbed truck 5.1 x 1.74 x 2.45 m");
            PaintVariants(SimpleCollider(Tractor()), "truck_tractor", TractorCabs, add, src + ": container tractor unit 6.9 x 2.5 x 3.85 m");
            add("trailer_chassis", SimpleCollider(Chassis()), src + ": 40 ft skeletal container chassis 12.4 m, deck top 1.44 m");
            PaintVariants(SimpleCollider(Parasol(), true), "beach_parasol", new[] { "red", "blue", "white", "green", "navy" }, add, "procedural beach parasol with two loungers (VillageAssets.Vehicles)");
            add("lifeguard_tower", SimpleCollider(LifeguardTower(), true), "procedural lifeguard tower 4.2 m (VillageAssets.Vehicles)");
            PaintVariants(SimpleCollider(FishBoxStack(), true), "fishbox_stack", new[] { "blue", "red", "green", "white" }, add, "procedural pallet of plastic fish boxes, 1.6 m (VillageAssets.Vehicles)");
            add("net_pile", SimpleCollider(NetPile(), true), "procedural fishing-net pile with floats (VillageAssets.Vehicles)");
            add("empty_handler", SimpleCollider(EmptyHandler()), "procedural empty-container handler (top lift), 16 m mast (VillageAssets.Vehicles)");
            PaintVariants(SimpleCollider(StallTent(), true), "stall_tent", new[] { "red", "blue", "white", "green" }, add, "procedural market stall tent 3 x 3 m (VillageAssets.Vehicles)");
            PaintVariants(SimpleCollider(CoveredBoat()), "covered_boat", new[] { "blue", "white", "navy", "green" }, add, "procedural covered boat on a dry-storage cradle, 8.5 m (VillageAssets.Vehicles)");
            add("fuel_pump", SimpleCollider(FuelPump(), true), "procedural marine fuel pump (VillageAssets.Vehicles)");
            add("armg_crane", ArmgCrane(), "procedural rail-mounted gantry crane (ARMG), 34 m span, 26 m (VillageAssets.Vehicles)");
            add("bench", SimpleCollider(Bench(), true), "procedural park bench 1.8 m (VillageAssets.Vehicles)");
            add("greenhouse_vinyl", Greenhouse(), "procedural vinyl greenhouse tunnel 7 x 3.2 x 40 m (VillageAssets.Vehicles)");
            add("parking_stripes_10", SimpleCollider(ParkingStripes(), true), "procedural parking stall lines, ten 2.5 x 5 m stalls (VillageAssets.Vehicles)");
        }
    }
}
