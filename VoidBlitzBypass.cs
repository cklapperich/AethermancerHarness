using System;
using System.Reflection;
using HarmonyLib;

namespace AethermancerHarness
{
    /// <summary>
    /// Manages the void blitz bypass state for API-triggered void blitz attacks.
    /// When active, bypasses distance checks and auto-confirms targeting.
    /// </summary>
    public static class VoidBlitzBypass
    {
        /// <summary>
        /// When true, distance checks are bypassed and void blitz auto-confirms.
        /// </summary>
        public static bool IsActive { get; set; }

        /// <summary>
        /// The target monster group for the bypass.
        /// </summary>
        public static MonsterGroup TargetGroup { get; set; }

        /// <summary>
        /// The specific monster to target within the group.
        /// </summary>
        public static OverworldMonster TargetMonster { get; set; }

        /// <summary>
        /// Reset the bypass state.
        /// </summary>
        public static void Reset()
        {
            IsActive = false;
            TargetGroup = null;
            TargetMonster = null;
        }
    }

    /// <summary>
    /// Harmony patches for void blitz bypass functionality.
    /// </summary>
    public static class VoidBlitzPatches
    {
        /// <summary>
        /// Patch MonsterGroup.CanBeAetherBlitzed to return true when bypass is active.
        /// </summary>
        [HarmonyPatch(typeof(MonsterGroup), "CanBeAetherBlitzed")]
        public static class CanBeAetherBlitzedPatch
        {
            static bool Prefix(MonsterGroup __instance, ref bool __result)
            {
                if (VoidBlitzBypass.IsActive && __instance == VoidBlitzBypass.TargetGroup)
                {
                    Plugin.Log.LogInfo($"VoidBlitzBypass: Bypassing CanBeAetherBlitzed for {__instance.name}");
                    __result = true;
                    return false; // Skip original method
                }
                return true; // Run original method
            }
        }

        /// <summary>
        /// Patch PlayerMovementController.StartVoidBlitz to auto-confirm when bypass is active.
        /// </summary>
        [HarmonyPatch(typeof(PlayerMovementController), "StartVoidBlitz")]
        public static class StartVoidBlitzPatch
        {
            static void Postfix(PlayerMovementController __instance, MonsterGroup targetGroup, OverworldMonster nearestMonster)
            {
                if (VoidBlitzBypass.IsActive && targetGroup == VoidBlitzBypass.TargetGroup)
                {
                    Plugin.Log.LogInfo("VoidBlitzBypass: Auto-confirming void blitz target");

                    // Use reflection to call the private ConfirmVoidBlitzTarget method
                    try
                    {
                        var confirmMethod = typeof(PlayerMovementController).GetMethod(
                            "ConfirmVoidBlitzTarget",
                            BindingFlags.NonPublic | BindingFlags.Instance);

                        if (confirmMethod != null)
                        {
                            confirmMethod.Invoke(__instance, null);
                            Plugin.Log.LogInfo("VoidBlitzBypass: ConfirmVoidBlitzTarget called successfully");
                        }
                        else
                        {
                            Plugin.Log.LogError("VoidBlitzBypass: Could not find ConfirmVoidBlitzTarget method");
                        }
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogError($"VoidBlitzBypass: Error calling ConfirmVoidBlitzTarget: {e}");
                    }
                    finally
                    {
                        // Reset bypass state
                        VoidBlitzBypass.Reset();
                    }
                }
            }
        }

        /// <summary>
        /// Patch PlayerController.GetNearestMonsterInRange to return our target when bypass is active.
        /// This bypasses the distance check.
        /// </summary>
        [HarmonyPatch(typeof(PlayerController), "GetNearestMonsterInRange")]
        public static class GetNearestMonsterInRangePatch
        {
            static bool Prefix(MonsterGroup targetGroup, ref OverworldMonster __result)
            {
                if (VoidBlitzBypass.IsActive &&
                    targetGroup == VoidBlitzBypass.TargetGroup &&
                    VoidBlitzBypass.TargetMonster != null)
                {
                    Plugin.Log.LogInfo($"VoidBlitzBypass: Returning target monster {VoidBlitzBypass.TargetMonster.name} regardless of distance");
                    __result = VoidBlitzBypass.TargetMonster;
                    return false; // Skip original method
                }
                return true; // Run original method
            }
        }
    }
}
