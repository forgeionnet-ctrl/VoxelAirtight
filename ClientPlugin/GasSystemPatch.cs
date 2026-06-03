using HarmonyLib;
using Sandbox.Game.GameSystems;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;
using VRage.Voxels;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace ProjectExtensio.VoxelAirtight
{
    /// <summary>
    /// Patches MyGridGasSystem so voxel walls count as airtight during room flood-fill.
    ///
    /// When the game's BFS hits a grid face that has no block and would mark it "open to
    /// space", we additionally check if there is solid voxel on the other side. If yes, we
    /// flip the result to "sealed" so pressurised rooms can form inside hollowed asteroids.
    ///
    /// IF THIS BREAKS AFTER A GAME UPDATE:
    ///   Open Sandbox.Game.dll in ILSpy/dnSpy → find MyGridGasSystem → look for the method
    ///   that checks airtightness between two adjacent grid positions → update
    ///   TARGET_METHOD_NAME below to match the new name.
    /// </summary>
    [HarmonyPatch]
    public static class GasSystemPatch
    {
        private const string TARGET_METHOD_NAME = "IsAirtightBetweenPositions";

        // Sample point slightly inside the neighbouring cell so we don't hit the face itself.
        private const double SAMPLE_OFFSET = 0.6;

        // Position → hasSolidVoxel cache. Cleared every CACHE_TTL ticks (~5 s at 60 UPS).
        private static readonly Dictionary<long, bool> _cache = new Dictionary<long, bool>();
        private static int _tick;
        private const int CACHE_TTL = 300;

        // ── Harmony target ────────────────────────────────────────────────────────────

        static MethodBase TargetMethod()
        {
            var type = typeof(MyGridGasSystem);

            // Primary: exact name match.
            var method = AccessTools.Method(type, TARGET_METHOD_NAME);

            // Fallback: find a private bool method that takes a MyCubeGrid and at least
            // one Vector3I — the face-check signature we expect.
            if (method == null)
            {
                foreach (var m in AccessTools.GetDeclaredMethods(type))
                {
                    if (m.IsPublic || m.ReturnType != typeof(bool)) continue;
                    bool hasGrid = false, hasVec = false;
                    foreach (var p in m.GetParameters())
                    {
                        if (p.ParameterType == typeof(MyCubeGrid)) hasGrid = true;
                        if (p.ParameterType == typeof(Vector3I))   hasVec  = true;
                    }
                    if (hasGrid && hasVec) { method = m; break; }
                }
            }

            if (method == null)
                Plugin.Log("WARNING: Target method not found. Check TARGET_METHOD_NAME.");
            else
                Plugin.Log("Patching " + method.DeclaringType?.Name + "." + method.Name);

            return method;
        }

        // ── Postfix ───────────────────────────────────────────────────────────────────

        static void Postfix(ref bool __result, object[] __args)
        {
            if (__result) return; // already sealed — nothing to do

            try
            {
                MyCubeGrid grid    = null;
                Vector3I   cellPos = Vector3I.Zero;
                Vector3I   dir     = Vector3I.Zero;
                bool       gotCell = false;

                foreach (var arg in __args)
                {
                    if (arg is MyCubeGrid g)
                    {
                        grid = g;
                    }
                    else if (arg is Vector3I v)
                    {
                        if (!gotCell) { cellPos = v; gotCell = true; }
                        else          { dir     = v; }
                    }
                }

                if (grid == null) return;

                // World position just inside the neighbouring cell.
                Vector3D worldPos = grid.GridIntegerToWorld(cellPos + dir)
                                  + (Vector3D)dir * (grid.GridSize * SAMPLE_OFFSET);

                if (HasVoxel(worldPos))
                    __result = true;
            }
            catch (Exception ex)
            {
                Plugin.Log("Postfix error: " + ex.Message);
            }
        }

        // ── Voxel sampling ────────────────────────────────────────────────────────────

        private static bool HasVoxel(Vector3D worldPos)
        {
            long key = Quantise(worldPos);

            bool cached;
            if (_cache.TryGetValue(key, out cached)) return cached;

            if (++_tick % CACHE_TTL == 0) _cache.Clear();

            bool result = SampleVoxel(worldPos);
            _cache[key] = result;
            return result;
        }

        private static bool SampleVoxel(Vector3D worldPos)
        {
            var sphere   = new BoundingSphereD(worldPos, 1.0);
            var entities = new List<IMyEntity>();
            MyAPIGateway.Entities.GetEntitiesInSphere(ref sphere, entities);

            foreach (var entity in entities)
            {
                var voxelMap = entity as IMyVoxelMap;
                if (voxelMap?.Storage == null) continue;

                Vector3D local    = worldPos - voxelMap.PositionLeftBottomCorner;
                var      voxCoord = new Vector3I((int)local.X, (int)local.Y, (int)local.Z);
                Vector3I size     = voxelMap.Storage.Size;

                if (voxCoord.X < 0 || voxCoord.Y < 0 || voxCoord.Z < 0 ||
                    voxCoord.X >= size.X || voxCoord.Y >= size.Y || voxCoord.Z >= size.Z)
                    continue;

                var data = new MyStorageData();
                data.Resize(Vector3I.One);
                voxelMap.Storage.ReadRange(data, MyStorageDataTypeFlags.Content, 0,
                    voxCoord, voxCoord);

                if (data.Content(0) > 0) return true;
            }

            return false;
        }

        private static long Quantise(Vector3D pos)
        {
            int x = (int)Math.Round(pos.X);
            int y = (int)Math.Round(pos.Y);
            int z = (int)Math.Round(pos.Z);
            const long M = 0x1FFFFF; // 21 bits, ±1 million metres
            return ((long)(x & M) << 42) | ((long)(y & M) << 21) | (long)(z & M);
        }
    }
}
