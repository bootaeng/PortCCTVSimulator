using System.IO;
using UnityEngine;

namespace PortCCTV.EditorTools
{
    /// <summary>Dev tool: writes each map's HarborDefinition (elements, water regions) to Logs/harbor_dump/&lt;id&gt;.json, for the
    /// offline layout checks in Tools/check_layout.py (dead-end roads, overlapping roads, lighthouse access). Batch:
    /// -executeMethod PortCCTV.EditorTools.HarborDump.RunBatch</summary>
    public static class HarborDump
    {
        public static void RunBatch()
        {
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "harbor_dump");
            Directory.CreateDirectory(dir);
            foreach (var id in new[] { ContainerTerminal01.Id, FishingPort01.Id, Marina01.Id })
            {
                var def = HarborLibrary.Get(id);
                File.WriteAllText(Path.Combine(dir, id + ".json"), JsonUtility.ToJson(def));
                Debug.Log($"[HarborDump] {id}: {def.elements.Count} elements -> {dir}");
            }
            EditorApplicationExit(0);
        }

        static void EditorApplicationExit(int code) => UnityEditor.EditorApplication.Exit(code);
    }
}
