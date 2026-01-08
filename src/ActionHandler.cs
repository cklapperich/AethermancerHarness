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
        internal static readonly MethodInfo ConfirmSelectionMethod;

        // Cached reflection fields for UI access
        internal static readonly FieldInfo EndOfRunMenuField;
        internal static readonly FieldInfo MerchantMenuField;
        internal static readonly FieldInfo DialogueCurrentField;
        internal static readonly FieldInfo DialogueDataField;
        internal static readonly FieldInfo AetherSpringMenuField;
        internal static readonly FieldInfo AetherSpringField;

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

            ConfirmSelectionMethod = typeof(MonsterShrineMenu).GetMethod(
                "ConfirmSelection",
                BindingFlags.NonPublic | BindingFlags.Instance);

            EndOfRunMenuField = typeof(UIController).GetField("EndOfRunMenu",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

            MerchantMenuField = typeof(UIController).GetField("MerchantMenu",
                BindingFlags.NonPublic | BindingFlags.Instance);

            DialogueCurrentField = typeof(DialogueDisplay).GetField("currentDialogue",
                BindingFlags.NonPublic | BindingFlags.Instance);

            DialogueDataField = typeof(DialogueDisplay).GetField("currentDialogueData",
                BindingFlags.NonPublic | BindingFlags.Instance);

            AetherSpringMenuField = typeof(UIController).GetField("AetherSpringMenu",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

            AetherSpringField = typeof(AetherSpringMenu).GetField("AetherSpring",
                BindingFlags.NonPublic | BindingFlags.Instance);
        }

        // =====================================================
        // SHARED UTILITIES
        // =====================================================

        internal static EndOfRunMenu GetEndOfRunMenu()
        {
            var ui = UIController.Instance;
            if (ui == null) return null;
            return EndOfRunMenuField?.GetValue(ui) as EndOfRunMenu;
        }

        internal static MerchantMenu GetMerchantMenu()
        {
            var ui = UIController.Instance;
            if (ui == null) return null;
            return MerchantMenuField?.GetValue(ui) as MerchantMenu;
        }

        internal static AetherSpringMenu GetAetherSpringMenu()
        {
            var ui = UIController.Instance;
            if (ui == null) return null;
            return AetherSpringMenuField?.GetValue(ui) as AetherSpringMenu;
        }

        internal static AetherSpringInteractable GetCurrentAetherSpring()
        {
            var menu = GetAetherSpringMenu();
            if (menu == null) return null;
            return AetherSpringField?.GetValue(menu) as AetherSpringInteractable;
        }

        internal static DialogueInteractable GetCurrentDialogue()
        {
            var display = UIController.Instance?.DialogueDisplay;
            if (display == null) return null;
            return DialogueCurrentField?.GetValue(display) as DialogueInteractable;
        }

        internal static DialogueDisplayData GetCurrentDialogueData()
        {
            var display = UIController.Instance?.DialogueDisplay;
            if (display == null) return null;
            return DialogueDataField?.GetValue(display) as DialogueDisplayData;
        }

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
