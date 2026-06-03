using HarmonyLib;
using VRage.Plugins;
using System;

namespace ProjectExtensio.VoxelAirtight
{
    // ReSharper disable once UnusedType.Global
    public class Plugin : IPlugin
    {
        private Harmony _harmony;

        public void Init(object gameInstance)
        {
            _harmony = new Harmony("ProjectExtensio.VoxelAirtight");
            try
            {
                _harmony.PatchAll(typeof(Plugin).Assembly);
                Log("Patches applied.");
            }
            catch (Exception ex)
            {
                Log("ERROR applying patches: " + ex);
            }
        }

        public void Dispose()
        {
            _harmony?.UnpatchAll("ProjectExtensio.VoxelAirtight");
            Log("Patches removed.");
        }

        public void Update() { }

        internal static void Log(string msg)
        {
            VRage.Utils.MyLog.Default?.WriteLineAndConsole("[VoxelAirtight] " + msg);
        }
    }
}
