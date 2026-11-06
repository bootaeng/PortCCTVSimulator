using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PortCCTV
{
    /// <summary>
    /// Pre-extracted vessel geometry in prefab-root space (yaw-only placement keeps y invariant).
    /// Extracted in the Editor so that labels never need Read/Write-enabled meshes in the player.
    /// </summary>
    public class VesselGeometry : ScriptableObject
    {
        public Vector3[] vertices_root_space;
        public float render_min_y;
        public float hull_reference_min_y;
        public bool hull_reference_found;
        public string hull_reference_node;
        public Vector3 bounds_min;
        public Vector3 bounds_max;
        public int source_vertex_count;
    }
}
