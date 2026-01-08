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
        public static bool IsActive { get; set; }
        public static MonsterGroup TargetGroup { get; set; }
        public static OverworldMonster TargetMonster { get; set; }

        // Cached reflection method
        private static readonly MethodInfo ConfirmVoidBlitzTargetMethod;

        static VoidBlitzBypass()
        {
            ConfirmVoidBlitzTargetMethod = typeof(PlayerMovementController).GetMethod(
                "ConfirmVoidBlitzTarget",
                BindingFlags.NonPublic | BindingFlags.Instance);
        }

        public static void Reset()
        {
            IsActive = false;
            TargetGroup = null;
            TargetMonster = null;
        }

        public static void ConfirmVoidBlitz(PlayerMovementController instance)
        {
            if (ConfirmVoidBlitzTargetMethod != null)
            {
                ConfirmVoidBlitzTargetMethod.Invoke(instance, null);
                Plugin.Log.LogInfo("VoidBlitzBypass: ConfirmVoidBlitzTarget called successfully");
            }
            else
            {
                Plugin.Log.LogError("VoidBlitzBypass: Could not find ConfirmVoidBlitzTarget method");
            }
        }
    }

    /// <summary>
    /// Harmony patches for void blitz bypass functionality.
    /// </summary>
    public static class VoidBlitzPatches
    {
        [HarmonyPatch(typeof(MonsterGroup), "CanBeAetherBlitzed")]
        public static class CanBeAetherBlitzedPatch
        {
            static bool Prefix(MonsterGroup __instance, ref bool __result)
            {
                if (VoidBlitzBypass.IsActive && __instance == VoidBlitzBypass.TargetGroup)
                {
                    Plugin.Log.LogInfo($"VoidBlitzBypass: Bypassing CanBeAetherBlitzed for {__instance.name}");
                    __result = true;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(PlayerMovementController), "StartVoidBlitz")]
        public static class StartVoidBlitzPatch
        {
            static void Postfix(PlayerMovementController __instance, MonsterGroup targetGroup, OverworldMonster nearestMonster)
            {
                if (VoidBlitzBypass.IsActive && targetGroup == VoidBlitzBypass.TargetGroup)
                {
                    Plugin.Log.LogInfo("VoidBlitzBypass: Auto-confirming void blitz target");
                    VoidBlitzBypass.ConfirmVoidBlitz(__instance);
                    VoidBlitzBypass.Reset();
                }
            }
        }

        [HarmonyPatch(typeof(PlayerController), "GetNearestMonsterInRange")]
        public static class GetNearestMonsterInRangePatch
        {
            static bool Prefix(MonsterGroup group, ref OverworldMonster __result)
            {
                if (VoidBlitzBypass.IsActive &&
                    group == VoidBlitzBypass.TargetGroup &&
                    VoidBlitzBypass.TargetMonster != null)
                {
                    Plugin.Log.LogInfo($"VoidBlitzBypass: Returning target monster {VoidBlitzBypass.TargetMonster.name} regardless of distance");
                    __result = VoidBlitzBypass.TargetMonster;
                    return false;
                }
                return true;
            }
        }
    }
}
