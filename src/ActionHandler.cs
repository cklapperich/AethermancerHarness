using System;
using System.Reflection;

namespace AethermancerHarness
{
    /// <summary>
    /// Main action handler - core shared state and utilities.
    /// Combat, exploration, and menu actions are in separate partial class files.
    /// </summary>
    public static partial class ActionHandler
    {
        // Flag to toggle condensed state responses
        public static bool UseCondensedState { get; set; } = true;

        // Cached reflection methods
        internal static readonly MethodInfo GetDescriptionDamageMethod;
        internal static readonly MethodInfo ContinueMethod;
        internal static readonly MethodInfo InputConfirmMethod;
        internal static readonly MethodInfo ConfirmVoidBlitzTargetMethod;
        internal static readonly MethodInfo ConfirmSelectionMethod;

        static ActionHandler()
        {
            GetDescriptionDamageMethod = typeof(ActionDamage).GetMethod(
                "GetDescriptionDamage",
                BindingFlags.NonPublic | BindingFlags.Instance);

            ContinueMethod = typeof(PostCombatMenu).GetMethod(
                "Continue",
                BindingFlags.NonPublic | BindingFlags.Instance);

            InputConfirmMethod = typeof(MenuList).GetMethod(
                "InputConfirm",
                BindingFlags.NonPublic | BindingFlags.Instance);

            ConfirmVoidBlitzTargetMethod = typeof(PlayerMovementController).GetMethod(
                "ConfirmVoidBlitzTarget",
                BindingFlags.NonPublic | BindingFlags.Instance);

            ConfirmSelectionMethod = typeof(MonsterShrineMenu).GetMethod(
                "ConfirmSelection",
                BindingFlags.NonPublic | BindingFlags.Instance);
        }

        // =====================================================
        // SHARED UTILITIES
        // =====================================================

        internal static bool TimedOut(DateTime startTime, int timeoutMs)
        {
            return (DateTime.Now - startTime).TotalMilliseconds >= timeoutMs;
        }

        internal static string GetTargetName(ITargetable target)
        {
            if (target is Monster m)
                return m.Name;
            if (target is MonsterList ml)
                return $"[{ml.Monsters.Count} targets]";
            return "unknown";
        }

        internal static void TriggerMenuConfirm(MenuList menuList)
        {
            if (InputConfirmMethod != null)
            {
                InputConfirmMethod.Invoke(menuList, null);
                Plugin.Log.LogInfo("TriggerMenuConfirm: Called InputConfirm()");
            }
        }

        internal static void TriggerContinue(PostCombatMenu postCombatMenu)
        {
            if (ContinueMethod != null)
            {
                ContinueMethod.Invoke(postCombatMenu, null);
                Plugin.Log.LogInfo("TriggerContinue: Called Continue()");
            }
        }
    }
}
