using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace PortCCTV
{
    /// <summary>
    /// Render-consistent visible mask. The capture camera renders one extra frame in which a custom pass (after post-processing)
    /// clears the colour buffer and draws every vessel renderer with an unlit material carrying its instance index, depth-tested
    /// against that frame's camera depth buffer (which already holds water, quays, buildings and the hulls themselves). A pixel gets
    /// a vessel's index exactly where that vessel is the front-most opaque surface in the rendered image: rendered waves, the hull
    /// below the water line and every occluder are taken into account, unlike the CPU ray mask on the mean water plane.
    /// Transparent effects (rain, snow, fog, decals) do not hide a vessel in this mask.
    /// </summary>
    public sealed class GpuInstanceMask
    {
        sealed class IdPass : CustomPass
        {
            public Camera camera;
            public readonly List<(Renderer r, Material m, int sub)> draws = new List<(Renderer, Material, int)>();
            protected override void Execute(CustomPassContext ctx)
            {
                if (camera == null || ctx.hdCamera.camera != camera) return;
                CoreUtils.SetRenderTarget(ctx.cmd, ctx.cameraColorBuffer, ctx.cameraDepthBuffer, ClearFlag.Color, Color.clear);
                foreach (var d in draws)
                    if (d.r != null && d.r.enabled && d.r.gameObject.activeInHierarchy) ctx.cmd.DrawRenderer(d.r, d.m, d.sub, 0);
            }
        }

        public const string Status = "gpu_instance_id_pass_depth_tested";
        readonly GameObject _go;
        readonly IdPass _pass;
        readonly Shader _shader;
        readonly Dictionary<int, Material> _mats = new Dictionary<int, Material>();
        public bool Available => _shader != null && _shader.isSupported;

        public GpuInstanceMask(Transform parent, Shader shader)
        {
            _shader = shader;
            _go = new GameObject("GpuInstanceIdPass");
            _go.transform.SetParent(parent, false);
            var vol = _go.AddComponent<CustomPassVolume>();
            vol.isGlobal = true;
            vol.injectionPoint = CustomPassInjectionPoint.AfterPostProcess;
            _pass = new IdPass { name = "PortCCTV instance id", enabled = false };
            vol.customPasses.Add(_pass);
        }

        Material MatFor(int id)
        {
            if (_mats.TryGetValue(id, out var m)) return m;
            m = new Material(_shader) { name = "instance_id_" + id, hideFlags = HideFlags.DontSave };
            m.SetFloat("_InstanceId", id);
            _mats[id] = m;
            return m;
        }

        /// <summary>Renders <paramref name="cam"/> once more at W x H and returns the instance index per pixel, rows from the top
        /// (same layout as the CPU mask). The camera's target texture and enabled state are restored.</summary>
        public byte[] Render(Camera cam, IList<PlacedVessel> vessels, int width, int height)
        {
            if (!Available) return null;
            _pass.draws.Clear();
            foreach (var v in vessels)
            {
                if (v.root == null || v.instance_index <= 0 || v.instance_index > 255) continue;
                var mat = MatFor(v.instance_index);
                foreach (var r in v.root.GetComponentsInChildren<Renderer>())
                {
                    if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer || LightRig.IsBulb(r)) continue;
                    var mf = r.GetComponent<MeshFilter>();
                    int subs = mf != null && mf.sharedMesh != null ? mf.sharedMesh.subMeshCount
                        : r is SkinnedMeshRenderer smr && smr.sharedMesh != null ? smr.sharedMesh.subMeshCount : 1;
                    for (int s = 0; s < subs; s++) _pass.draws.Add((r, mat, s));
                }
            }
            var rt = new RenderTexture(new RenderTextureDescriptor(width, height, GraphicsFormat.R8G8B8A8_UNorm, 24) { msaaSamples = 1 }) { name = "PortCCTV_InstanceId" };
            rt.Create();
            var prevTarget = cam.targetTexture; bool prevEnabled = cam.enabled;
            var prevActive = RenderTexture.active;
            var mask = new byte[width * height];
            try
            {
                _pass.camera = cam; _pass.enabled = true;
                cam.enabled = true; cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply(false);
                var px = tex.GetPixels32();                         // bottom-left origin
                for (int y = 0; y < height; y++)
                {
                    int src = y * width, dst = (height - 1 - y) * width;
                    for (int x = 0; x < width; x++) mask[dst + x] = px[src + x].r;
                }
                Util.SafeDestroy(tex);
            }
            finally
            {
                _pass.enabled = false; _pass.camera = null; _pass.draws.Clear();
                RenderTexture.active = prevActive;
                cam.targetTexture = prevTarget; cam.enabled = prevEnabled;
                rt.Release(); Util.SafeDestroy(rt);
            }
            return mask;
        }

        public void Destroy()
        {
            foreach (var m in _mats.Values) Util.SafeDestroy(m);
            _mats.Clear();
            if (_go != null) Util.SafeDestroy(_go);
        }
    }
}
