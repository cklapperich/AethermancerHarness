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

        /// <summary>
        /// Resolves a choice name to an index by parsing the current game state.
        /// Supports both numeric indices (e.g., "0", "1") and text matching.
        /// Returns (index, error) where error is null on success.
        /// </summary>
        public static (int index, string error) ResolveChoiceName(string choiceName)
        {
            if (string.IsNullOrEmpty(choiceName))
                return (-1, "choiceName is required");

            // If it's a numeric index, return it directly
            if (int.TryParse(choiceName, out int directIndex))
                return (directIndex, null);

            // Skill selection
            if (StateSerializer.IsInSkillSelection())
            {
                var stateJson = StateSerializer.GetSkillSelectionStateJson();
                var state = Newtonsoft.Json.JsonConvert.DeserializeObject<SkillSelectionState>(stateJson);

                for (int i = 0; i < state.Choices.Count; i++)
                {
                    if (state.Choices[i].Name != null &&
                        state.Choices[i].Name.Equals(choiceName, StringComparison.OrdinalIgnoreCase))
                        return (i, null);
                }
                return (-1, $"No skill named '{choiceName}'");
            }

            // Equipment selection
            if (StateSerializer.IsInEquipmentSelection())
            {
                var stateJson = StateSerializer.GetEquipmentSelectionStateJson();
                var state = Newtonsoft.Json.JsonConvert.DeserializeObject<EquipmentSelectionState>(stateJson);

                // Check for "Scrap" special name
                if (choiceName.Equals("Scrap", StringComparison.OrdinalIgnoreCase) ||
                    choiceName.Equals("Scrap Equipment", StringComparison.OrdinalIgnoreCase))
                {
                    return (state.Choices.Count - 1, null);
                }

                for (int i = 0; i < state.Choices.Count; i++)
                {
                    if (state.Choices[i].Name != null &&
                        state.Choices[i].Name.Equals(choiceName, StringComparison.OrdinalIgnoreCase))
                        return (i, null);
                }
                return (-1, $"No equipment choice named '{choiceName}'");
            }

            // Merchant menu
            if (IsMerchantMenuOpen())
            {
                var stateJson = StateSerializer.GetMerchantStateJson();
                var state = Newtonsoft.Json.JsonConvert.DeserializeObject<MerchantState>(stateJson);

                for (int i = 0; i < state.Choices.Count; i++)
                {
                    if (state.Choices[i].Name != null &&
                        state.Choices[i].Name.Equals(choiceName, StringComparison.OrdinalIgnoreCase))
                        return (i, null);
                }
                return (-1, $"No merchant item named '{choiceName}'");
            }

            // Difficulty selection
            if (StateSerializer.IsInDifficultySelection())
            {
                var stateJson = StateSerializer.GetDifficultySelectionStateJson();
                var state = Newtonsoft.Json.JsonConvert.DeserializeObject<DifficultySelectionState>(stateJson);

                for (int i = 0; i < state.Choices.Count; i++)
                {
                    if (state.Choices[i].Name != null &&
                        state.Choices[i].Name.Equals(choiceName, StringComparison.OrdinalIgnoreCase))
                        return (i, null);
                }
                return (-1, $"No difficulty named '{choiceName}'");
            }

            // Monster selection
            if (StateSerializer.IsInMonsterSelection())
            {
                var stateJson = StateSerializer.GetMonsterSelectionStateJson();
                var state = Newtonsoft.Json.JsonConvert.DeserializeObject<MonsterSelectionState>(stateJson);

                for (int i = 0; i < state.Choices.Count; i++)
                {
                    if (state.Choices[i].Name != null &&
                        state.Choices[i].Name.Equals(choiceName, StringComparison.OrdinalIgnoreCase))
                        return (i, null);
                }
                return (-1, $"No monster named '{choiceName}'");
            }

            // Aether spring
            if (StateSerializer.IsInAetherSpringMenu())
            {
                var stateJson = StateSerializer.GetAetherSpringStateJson();
                var state = Newtonsoft.Json.JsonConvert.DeserializeObject<AetherSpringState>(stateJson);

                for (int i = 0; i < state.Choices.Count; i++)
                {
                    if (state.Choices[i].Name != null &&
                        state.Choices[i].Name.Equals(choiceName, StringComparison.OrdinalIgnoreCase))
                        return (i, null);
                }
                return (-1, $"No boon named '{choiceName}'");
            }

            // Dialogue
            if (IsDialogueOpen())
            {
                var stateJson = StateSerializer.GetDialogueStateJson();
                var state = Newtonsoft.Json.JsonConvert.DeserializeObject<DialogueState>(stateJson);

                for (int i = 0; i < state.Choices.Count; i++)
                {
                    if (state.Choices[i].Text != null &&
                        state.Choices[i].Text.Equals(choiceName, StringComparison.OrdinalIgnoreCase))
                        return (i, null);
                }
                return (-1, $"No dialogue choice named '{choiceName}'");
            }

            return (-1, "No active choice context");
        }
    }
}
