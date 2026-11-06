using UnityEngine;

namespace PortCCTV
{
    public static class Util
    {
        /// <summary>Destroy in play mode / player, DestroyImmediate in edit mode (editor batch tools).</summary>
        public static void SafeDestroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }
    }
}
