using System;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace AethermancerHarness
{
    /// <summary>
    /// Menu-related actions: post-combat, skill selection, choices, dialogue, merchant, difficulty.
    /// </summary>
    public static partial class ActionHandler
    {
        // =====================================================
        // POST-COMBAT AUTO-ADVANCE AND SKILL SELECTION
        // =====================================================

        public static string WaitForPostCombatComplete(int timeoutMs = 60000)
        {
            var startTime = DateTime.Now;
            Plugin.Log.LogInfo("WaitForPostCombatComplete: Starting post-combat auto-advance");

            while (!IsPostCombatMenuOpen())
            {
                if (TimedOut(startTime, timeoutMs))
                {
                    Plugin.Log.LogWarning("WaitForPostCombatComplete: Timeout waiting for PostCombatMenu");
                    return JsonHelper.Serialize(new { success = false, error = "Timeout waiting for PostCombatMenu to open", phase = "TIMEOUT" });
                }

                if (IsInExploration())
                {
                    Plugin.Log.LogInfo("WaitForPostCombatComplete: Back in exploration (no post-combat screens)");
                    return CreateExplorationResult("VICTORY");
                }

                System.Threading.Thread.Sleep(100);
            }

            Plugin.Log.LogInfo("WaitForPostCombatComplete: PostCombatMenu is open");
            return ProcessPostCombatStates(startTime, timeoutMs);
        }

        private static string ProcessPostCombatStates(DateTime startTime, int timeoutMs)
        {
            var postCombatMenu = UIController.Instance.PostCombatMenu;

            while (true)
            {
                if (TimedOut(startTime, timeoutMs))
                    return JsonHelper.Serialize(new { success = false, error = "Timeout waiting for post-combat processing", phase = "TIMEOUT" });

                if (StateSerializer.IsInSkillSelection())
                {
                    Plugin.Log.LogInfo("ProcessPostCombatStates: Skill selection menu is open");
                    return StateSerializer.GetSkillSelectionStateJson();
                }

                // Check for end of run screen and auto-continue
                if (StateSerializer.IsInEndOfRunMenu())
                {
                    Plugin.Log.LogInfo("ProcessPostCombatStates: End of run screen detected, auto-continuing");
                    AutoContinueFromEndOfRun();
                    System.Threading.Thread.Sleep(500);
                    continue;
                }

                if (IsInExploration())
                {
                    Plugin.Log.LogInfo("ProcessPostCombatStates: Back in exploration");
                    return CreateExplorationResult("VICTORY");
                }

                var currentState = postCombatMenu.CurrentState;
                Plugin.Log.LogInfo($"ProcessPostCombatStates: Current state = {currentState}");

                switch (currentState)
                {
                    case PostCombatMenu.EPostCombatMenuState.WorthinessUI:
                    case PostCombatMenu.EPostCombatMenuState.WorthinessUIDetailed:
                        if (WaitForWorthinessCanContinue(postCombatMenu, startTime, timeoutMs))
                        {
                            Plugin.Log.LogInfo("ProcessPostCombatStates: Worthiness CanContinue, triggering Continue");
                            TriggerContinue(postCombatMenu);
                            System.Threading.Thread.Sleep(200);
                        }
                        break;

                    case PostCombatMenu.EPostCombatMenuState.LevelUpUI:
                        if (WaitForLevelUpCanContinue(postCombatMenu, startTime, timeoutMs))
                        {
                            var pendingLevelUp = FindFirstPendingLevelUp(postCombatMenu);
                            if (pendingLevelUp != null)
                            {
                                Plugin.Log.LogInfo("ProcessPostCombatStates: Found pending level up, opening skill selection");
                                OpenSkillSelectionForMonster(pendingLevelUp);

                                if (WaitForSkillSelectionOpen(startTime, timeoutMs))
                                    return StateSerializer.GetSkillSelectionStateJson();
                            }
                            else
                            {
                                Plugin.Log.LogInfo("ProcessPostCombatStates: No pending level ups, continuing");
                                TriggerContinue(postCombatMenu);
                                System.Threading.Thread.Sleep(200);
                            }
                        }
                        break;

                    case PostCombatMenu.EPostCombatMenuState.SkillSelectionUI:
                        Plugin.Log.LogInfo("ProcessPostCombatStates: In SkillSelectionUI state");
                        System.Threading.Thread.Sleep(100);
                        break;
                }

                System.Threading.Thread.Sleep(100);
            }
        }

        private static bool IsPostCombatMenuOpen()
        {
            return UIController.Instance?.PostCombatMenu?.IsOpen ?? false;
        }

        private static bool IsInExploration()
        {
            bool notInCombat = !(GameStateManager.Instance?.IsCombat ?? false);
            bool notInPostCombat = !IsPostCombatMenuOpen();
            bool notInEndOfRun = !StateSerializer.IsInEndOfRunMenu();
            return notInCombat && notInPostCombat && notInEndOfRun;
        }

        private static void AutoContinueFromEndOfRun()
        {
            var menu = GetEndOfRunMenu();
            if (menu == null || !menu.IsOpen)
            {
                Plugin.Log.LogWarning("AutoContinueFromEndOfRun: End of run menu not available");
                return;
            }

            bool isVictory = menu.VictoryBanner?.activeSelf ?? false;
            Plugin.Log.LogInfo($"AutoContinueFromEndOfRun: Closing end-of-run screen ({(isVictory ? "VICTORY" : "DEFEAT")})");

            Plugin.RunOnMainThreadAndWait(() =>
            {
                menu.Close();
            });
        }

        private static bool WaitForWorthinessCanContinue(PostCombatMenu postCombatMenu, DateTime startTime, int timeoutMs)
        {
            while (true)
            {
                if (TimedOut(startTime, timeoutMs)) return false;

                bool allCanContinue = true;
                foreach (var info in postCombatMenu.PostCombatMonsterInfos)
                {
                    if (info.monster != null && info.gameObject.activeSelf && !info.WorthinessUI.CanContinue)
                    {
                        allCanContinue = false;
                        break;
                    }
                }

                if (allCanContinue) return true;
                System.Threading.Thread.Sleep(100);
            }
        }

        private static bool WaitForLevelUpCanContinue(PostCombatMenu postCombatMenu, DateTime startTime, int timeoutMs)
        {
            while (true)
            {
                if (TimedOut(startTime, timeoutMs)) return false;

                bool allCanContinue = true;
                foreach (var info in postCombatMenu.PostCombatMonsterInfos)
                {
                    if (info.monster != null && info.gameObject.activeSelf && !info.LevelUpUI.CanContinue)
                    {
                        allCanContinue = false;
                        break;
                    }
                }

                if (allCanContinue) return true;
                System.Threading.Thread.Sleep(100);
            }
        }

        private static PostCombatMonsterInfo FindFirstPendingLevelUp(PostCombatMenu postCombatMenu)
        {
            foreach (var info in postCombatMenu.PostCombatMonsterInfos)
            {
                if (info.monster != null && info.LevelUpUI.LevelsGainedLeft > 0)
                    return info;
            }
            return null;
        }

        private static void OpenSkillSelectionForMonster(PostCombatMonsterInfo monsterInfo)
        {
            var menuListItem = monsterInfo.LevelUpUI.MenuListItem;
            if (menuListItem != null)
            {
                var postCombatMenu = UIController.Instance.PostCombatMenu;
                postCombatMenu.MenuList.SelectByIndex(0);
                TriggerMenuConfirm(postCombatMenu.MenuList);
                Plugin.Log.LogInfo($"OpenSkillSelectionForMonster: Triggered skill selection for {monsterInfo.monster?.Name}");
            }
        }

        private static bool WaitForSkillSelectionOpen(DateTime startTime, int timeoutMs)
        {
            while (!TimedOut(startTime, timeoutMs))
            {
                if (StateSerializer.IsInSkillSelection())
                    return true;
                System.Threading.Thread.Sleep(100);
            }
            return false;
        }

        private static string CreateExplorationResult(string combatResult)
        {
            var result = new JObject
            {
                ["success"] = true,
                ["combatResult"] = combatResult,
                ["state"] = JObject.Parse(StateSerializer.ToJson())
            };
            return result.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static string ExecuteSkillSelection(int skillIndex, bool reroll = false)
        {
            if (!StateSerializer.IsInSkillSelection())
                return JsonHelper.Serialize(new { success = false, error = "Not in skill selection screen" });

            var skillSelectMenu = UIController.Instance.PostCombatMenu.SkillSelectMenu;

            if (reroll)
            {
                if (InventoryManager.Instance.SkillRerolls <= 0)
                    return JsonHelper.Serialize(new { success = false, error = "No skill rerolls available" });

                var menuList = skillSelectMenu.MenuList;
                for (int i = 0; i < menuList.List.Count; i++)
                {
                    if (menuList.List[i] == skillSelectMenu.RerollSkillsButton)
                    {
                        menuList.SelectByIndex(i);
                        TriggerMenuConfirm(menuList);
                        Plugin.Log.LogInfo("ExecuteSkillSelection: Rerolled skills");
                        System.Threading.Thread.Sleep(500);
                        return StateSerializer.GetSkillSelectionStateJson();
                    }
                }
                return JsonHelper.Serialize(new { success = false, error = "Could not find reroll button" });
            }

            if (skillIndex == -1)
            {
                var menuList = skillSelectMenu.MenuList;
                for (int i = 0; i < menuList.List.Count; i++)
                {
                    if (menuList.List[i] == skillSelectMenu.AlternativeBonusButton)
                    {
                        menuList.SelectByIndex(i);
                        TriggerMenuConfirm(menuList);
                        Plugin.Log.LogInfo("ExecuteSkillSelection: Selected max health bonus");
                        System.Threading.Thread.Sleep(500);
                        return WaitForPostCombatComplete();
                    }
                }
                return JsonHelper.Serialize(new { success = false, error = "Max health option not available" });
            }

            if (skillIndex >= 0 && skillIndex <= 2)
            {
                var menuList = skillSelectMenu.MenuList;
                if (skillIndex < menuList.List.Count)
                {
                    menuList.SelectByIndex(skillIndex);
                    TriggerMenuConfirm(menuList);
                    Plugin.Log.LogInfo($"ExecuteSkillSelection: Selected skill at index {skillIndex}");
                    System.Threading.Thread.Sleep(500);
                    return WaitForPostCombatComplete();
                }
                return JsonHelper.Serialize(new { success = false, error = $"Invalid skill index: {skillIndex}" });
            }

            return JsonHelper.Serialize(new { success = false, error = $"Invalid skill index: {skillIndex}. Use 0-2 for skills, -1 for max health." });
        }

        // =====================================================
        // UNIFIED CHOICE HANDLER
        // =====================================================

        public static string ExecuteChoice(int choiceIndex, string shift = null)
        {
            // Check equipment selection first (after picking equipment from dialogue/loot)
            if (StateSerializer.IsInEquipmentSelection())
            {
                Plugin.Log.LogInfo($"ExecuteChoice: Routing to equipment selection handler (index {choiceIndex})");
                return ExecuteEquipmentChoice(choiceIndex);
            }

            // Check difficulty selection (run start)
            if (StateSerializer.IsInDifficultySelection())
            {
                Plugin.Log.LogInfo($"ExecuteChoice: Routing to difficulty selection handler (index {choiceIndex})");
                return ExecuteDifficultyChoice(choiceIndex);
            }

            // Check monster selection (shrine/starter)
            if (StateSerializer.IsInMonsterSelection())
            {
                Plugin.Log.LogInfo($"ExecuteChoice: Routing to monster selection handler (index {choiceIndex}, shift: {shift ?? "default"})");
                return ExecuteMonsterSelectionChoice(choiceIndex, shift);
            }

            // Check dialogue
            if (IsDialogueOpen())
            {
                Plugin.Log.LogInfo($"ExecuteChoice: Routing to dialogue choice handler (index {choiceIndex})");
                return ExecuteDialogueChoice(choiceIndex);
            }

            return JsonHelper.Serialize(new { success = false, error = "No active choice context (not in dialogue, equipment selection, difficulty selection, or monster selection)" });
        }

        // =====================================================
        // MONSTER SELECTION (Shrine/Starter)
        // =====================================================

        public static string ExecuteMonsterSelectionChoice(int choiceIndex, string shift = null)
        {
            var menu = UIController.Instance?.MonsterShrineMenu;
            if (menu == null || !menu.IsOpen)
                return JsonHelper.Serialize(new { success = false, error = "Monster selection menu not open" });

            var selection = menu.MonsterSelection;
            var displayedMonsters = menu.DisplayedMonsters;

            if (displayedMonsters == null || displayedMonsters.Count == 0)
                return JsonHelper.Serialize(new { success = false, error = "No monsters available" });

            // Calculate total count including random entry
            int totalCount = displayedMonsters.Count + (selection.HasRandomMonster ? 1 : 0);

            // Validate choice index
            if (choiceIndex < 0 || choiceIndex >= totalCount)
                return JsonHelper.Serialize(new { success = false, error = $"Invalid choice index: {choiceIndex}. Valid range: 0-{totalCount - 1}" });

            // Check if selecting random
            var isRandom = selection.HasRandomMonster && choiceIndex == selection.RandomMonsterPosition;

            // Get monster name - for random, use "Random Monster"; otherwise adjust index to skip random
            string monsterName;
            Monster selectedMonster = null;
            if (isRandom)
            {
                monsterName = "Random Monster";
            }
            else
            {
                // Adjust index: if random exists and we're after its position, subtract 1
                int adjustedIndex = choiceIndex;
                if (selection.HasRandomMonster && choiceIndex > selection.RandomMonsterPosition)
                {
                    adjustedIndex = choiceIndex - 1;
                }
                selectedMonster = displayedMonsters[adjustedIndex];
                monsterName = selectedMonster?.Name ?? "Unknown";
            }

            // Parse shift parameter
            EMonsterShift targetShift = EMonsterShift.Normal;
            if (!string.IsNullOrEmpty(shift))
            {
                if (shift.Equals("shifted", System.StringComparison.OrdinalIgnoreCase))
                {
                    targetShift = EMonsterShift.Shifted;
                }
                else if (!shift.Equals("normal", System.StringComparison.OrdinalIgnoreCase))
                {
                    return JsonHelper.Serialize(new { success = false, error = $"Invalid shift value: '{shift}'. Use 'normal' or 'shifted'." });
                }
            }

            // Validate shifted variant is available if requested
            if (targetShift == EMonsterShift.Shifted && !isRandom)
            {
                bool hasShiftedVariant = InventoryManager.Instance?.HasShiftedMementosOfMonster(selectedMonster) ?? false;
                if (!hasShiftedVariant)
                {
                    return JsonHelper.Serialize(new { success = false, error = $"Shifted variant not available for {monsterName}" });
                }
            }

            Plugin.Log.LogInfo($"ExecuteMonsterSelectionChoice: Selecting monster at index {choiceIndex}: {monsterName} (isRandom: {isRandom}, shift: {targetShift})");

            try
            {
                // Run UI operations on main thread
                Plugin.RunOnMainThreadAndWait(() =>
                {
                    // Set the selection index
                    selection.SetSelectedIndex(choiceIndex);

                    // Set the shift (only applies to non-random monsters with shifted variants)
                    if (!isRandom && targetShift != selection.CurrentShift)
                    {
                        selection.CurrentShift = targetShift;
                        selectedMonster.SetShift(targetShift);
                        Plugin.Log.LogInfo($"ExecuteMonsterSelectionChoice: Set shift to {targetShift}");
                    }

                    // Auto-confirm via reflection (skip confirmation popup)
                    if (ConfirmSelectionMethod != null)
                    {
                        ConfirmSelectionMethod.Invoke(menu, null);
                        Plugin.Log.LogInfo("ExecuteMonsterSelectionChoice: Called ConfirmSelection()");
                    }
                    else
                    {
                        Plugin.Log.LogWarning("ExecuteMonsterSelectionChoice: ConfirmSelection method not found, falling back to OnConfirm");
                        menu.OnConfirm();
                    }
                });

                // Wait for the selection to process
                System.Threading.Thread.Sleep(500);

                // Wait a bit more for state transitions
                var startTime = DateTime.Now;
                while (!TimedOut(startTime, 3000))
                {
                    // If equipment selection opened (for replacement), return that state
                    if (StateSerializer.IsInEquipmentSelection())
                    {
                        Plugin.Log.LogInfo("ExecuteMonsterSelectionChoice: Transitioned to equipment selection (monster replacement)");
                        return JsonHelper.Serialize(new
                        {
                            success = true,
                            action = "monster_selected",
                            selected = monsterName,
                            shift = targetShift.ToString(),
                            isRandom = isRandom,
                            phase = "EQUIPMENT_SELECTION",
                            note = "Select which monster to replace"
                        });
                    }

                    // If skill selection opened (post-rebirth XP), return that state
                    if (StateSerializer.IsInSkillSelection())
                    {
                        Plugin.Log.LogInfo("ExecuteMonsterSelectionChoice: Transitioned to skill selection");
                        return JsonHelper.Serialize(new
                        {
                            success = true,
                            action = "monster_selected",
                            selected = monsterName,
                            shift = targetShift.ToString(),
                            isRandom = isRandom,
                            phase = "SKILL_SELECTION",
                            note = "Monster rebirthed with XP - select skill"
                        });
                    }

                    // If still in monster selection, wait a bit more
                    if (StateSerializer.IsInMonsterSelection())
                    {
                        System.Threading.Thread.Sleep(100);
                        continue;
                    }

                    // Otherwise we're done - back to exploration
                    break;
                }

                // Final state check
                if (StateSerializer.IsInSkillSelection())
                {
                    return StateSerializer.GetSkillSelectionStateJson();
                }

                Plugin.Log.LogInfo($"ExecuteMonsterSelectionChoice: Monster '{monsterName}' selected successfully (shift: {targetShift})");
                return JsonHelper.Serialize(new
                {
                    success = true,
                    action = "monster_selected",
                    selected = monsterName,
                    shift = targetShift.ToString(),
                    isRandom = isRandom,
                    phase = "EXPLORATION"
                });
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"ExecuteMonsterSelectionChoice: Exception - {ex.Message}\n{ex.StackTrace}");
                return JsonHelper.Serialize(new { success = false, error = $"Exception during monster selection: {ex.Message}" });
            }
        }

        // =====================================================
        // EQUIPMENT SELECTION
        // =====================================================

        public static string ExecuteEquipmentChoice(int choiceIndex)
        {
            var menu = UIController.Instance?.MonsterSelectMenu;
            if (menu == null || !menu.IsOpen)
                return JsonHelper.Serialize(new { success = false, error = "Equipment selection menu not open" });

            var party = MonsterManager.Instance?.Active;
            if (party == null)
                return JsonHelper.Serialize(new { success = false, error = "No party available" });

            int scrapIndex = party.Count;

            // Validate choice index
            if (choiceIndex < 0 || choiceIndex > scrapIndex)
                return JsonHelper.Serialize(new { success = false, error = $"Invalid choice index: {choiceIndex}. Valid range: 0-{scrapIndex}" });

            // Handle scrap
            if (choiceIndex == scrapIndex)
            {
                Plugin.Log.LogInfo("ExecuteEquipmentChoice: Scrapping equipment");
                return ExecuteEquipmentScrap(menu);
            }

            // Handle assign to monster
            var targetMonster = party[choiceIndex];
            Plugin.Log.LogInfo($"ExecuteEquipmentChoice: Assigning equipment to {targetMonster.Name} (index {choiceIndex})");
            return ExecuteEquipmentAssign(menu, targetMonster, choiceIndex);
        }

        private static string ExecuteEquipmentAssign(MonsterSelectMenu menu, Monster targetMonster, int monsterIndex)
        {
            var newEquipment = menu.NewEquipmentInstance;
            if (newEquipment == null)
                return JsonHelper.Serialize(new { success = false, error = "No equipment to assign" });

            var prevEquipment = targetMonster.Equipment?.Equipment;
            var newEquipName = newEquipment.Equipment?.GetName() ?? "Unknown";

            try
            {
                // Run UI operations on main thread
                Plugin.RunOnMainThreadAndWait(() =>
                {
                    // Select the monster in the menu list
                    menu.MenuList.SelectByIndex(monsterIndex);

                    // Trigger the menu item selection via InputConfirm
                    if (InputConfirmMethod != null)
                    {
                        InputConfirmMethod.Invoke(menu.MenuList, null);
                    }
                });

                // Wait for the UI to process
                System.Threading.Thread.Sleep(600);

                // Check if we're still in equipment selection (happens when monster had equipment - trade scenario)
                if (StateSerializer.IsInEquipmentSelection())
                {
                    var prevEquipName = prevEquipment?.Equipment?.GetName() ?? "Unknown";
                    Plugin.Log.LogInfo($"ExecuteEquipmentAssign: Trade occurred - now holding {prevEquipName}");
                    return JsonHelper.Serialize(new
                    {
                        success = true,
                        action = "equipment_trade",
                        assignedTo = targetMonster.Name,
                        assigned = newEquipName,
                        nowHolding = prevEquipName,
                        phase = "EQUIPMENT_SELECTION",
                        state = JObject.Parse(StateSerializer.GetEquipmentSelectionStateJson())
                    });
                }

                // Equipment assigned, back to exploration
                Plugin.Log.LogInfo($"ExecuteEquipmentAssign: Equipment assigned to {targetMonster.Name}");
                return JsonHelper.Serialize(new
                {
                    success = true,
                    action = "equipment_assigned",
                    assignedTo = targetMonster.Name,
                    equipment = newEquipName,
                    phase = "EXPLORATION"
                });
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"ExecuteEquipmentAssign: Exception - {ex.Message}");
                return JsonHelper.Serialize(new { success = false, error = $"Exception during equipment assignment: {ex.Message}" });
            }
        }

        private static string ExecuteEquipmentScrap(MonsterSelectMenu menu)
        {
            var equipment = menu.NewEquipmentInstance;
            if (equipment == null)
                return JsonHelper.Serialize(new { success = false, error = "No equipment to scrap" });

            var equipName = equipment.Equipment?.GetName() ?? "Unknown";
            var scrapValue = equipment.Equipment?.GetScrapGoldGain() ?? 0;

            try
            {
                // Run UI operations on main thread
                Plugin.RunOnMainThreadAndWait(() =>
                {
                    // Find and select the scrap menu item (it's the last item in the list)
                    var scrapIndex = menu.MenuList.List.Count - 1;
                    menu.MenuList.SelectByIndex(scrapIndex);

                    // Trigger the selection via InputConfirm
                    if (InputConfirmMethod != null)
                    {
                        InputConfirmMethod.Invoke(menu.MenuList, null);
                    }
                });

                // Wait for the scrap animation and popup
                System.Threading.Thread.Sleep(300);

                // The game shows a popup after scrapping - we need to close it
                var startTime = DateTime.Now;
                while ((DateTime.Now - startTime).TotalMilliseconds < 2000)
                {
                    if (PopupController.Instance?.IsOpen ?? false)
                    {
                        Plugin.Log.LogInfo("ExecuteEquipmentScrap: Closing scrap confirmation popup");
                        Plugin.RunOnMainThreadAndWait(() =>
                        {
                            PopupController.Instance.Close();
                        });
                        break;
                    }
                    System.Threading.Thread.Sleep(50);
                }

                System.Threading.Thread.Sleep(300);

                Plugin.Log.LogInfo($"ExecuteEquipmentScrap: Scrapped {equipName} for {scrapValue} gold");
                return JsonHelper.Serialize(new
                {
                    success = true,
                    action = "equipment_scrapped",
                    equipment = equipName,
                    goldGained = scrapValue,
                    goldTotal = InventoryManager.Instance?.Gold ?? 0,
                    phase = "EXPLORATION"
                });
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"ExecuteEquipmentScrap: Exception - {ex.Message}");
                return JsonHelper.Serialize(new { success = false, error = $"Exception during equipment scrap: {ex.Message}" });
            }
        }

        // =====================================================
        // NPC DIALOGUE INTERACTION
        // =====================================================

        // Cached reflection for DialogueDisplay private fields
        private static FieldInfo _dialogueDisplayCurrentDialogueField;
        private static FieldInfo _dialogueDisplayCurrentDataField;

        private static DialogueInteractable GetCurrentDialogueInteractable()
        {
            var display = UIController.Instance?.DialogueDisplay;
            if (display == null) return null;

            if (_dialogueDisplayCurrentDialogueField == null)
            {
                _dialogueDisplayCurrentDialogueField = typeof(DialogueDisplay).GetField("currentDialogue",
                    BindingFlags.NonPublic | BindingFlags.Instance);
            }
            return _dialogueDisplayCurrentDialogueField?.GetValue(display) as DialogueInteractable;
        }

        private static DialogueDisplayData GetCurrentDialogueData()
        {
            var display = UIController.Instance?.DialogueDisplay;
            if (display == null) return null;

            if (_dialogueDisplayCurrentDataField == null)
            {
                _dialogueDisplayCurrentDataField = typeof(DialogueDisplay).GetField("currentDialogueData",
                    BindingFlags.NonPublic | BindingFlags.Instance);
            }
            return _dialogueDisplayCurrentDataField?.GetValue(display) as DialogueDisplayData;
        }

        public static bool IsDialogueOpen()
        {
            return UIController.Instance?.DialogueDisplay?.IsOpen ?? false;
        }

        private static bool HasMeaningfulChoice()
        {
            var data = GetCurrentDialogueData();
            if (data == null) return false;

            if (data.DialogueOptions == null || data.DialogueOptions.Length == 0) return false;
            if (data.DialogueOptions.Length == 1) return false;

            int eventIndex = FindEventOptionIndex(data.DialogueOptions);
            if (eventIndex >= 0)
            {
                return false;
            }

            if (data.IsChoiceEvent) return true;
            if (data.DialogueOptions.Length > 1) return true;

            return false;
        }

        private static int FindEventOptionIndex(string[] options)
        {
            if (options == null) return -1;

            for (int i = 0; i < options.Length; i++)
            {
                if (options[i] != null && options[i].Trim().Equals("Event", System.StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }

        private static void AutoProgressDialogue(int timeoutMs = 10000)
        {
            var startTime = DateTime.Now;
            var display = UIController.Instance?.DialogueDisplay;

            while (!TimedOut(startTime, timeoutMs))
            {
                if (!IsDialogueOpen())
                {
                    Plugin.Log.LogInfo("AutoProgressDialogue: Dialogue closed");
                    return;
                }

                if (StateSerializer.IsInSkillSelection())
                {
                    Plugin.Log.LogInfo("AutoProgressDialogue: Skill selection opened");
                    return;
                }

                var data = GetCurrentDialogueData();
                if (data == null)
                {
                    System.Threading.Thread.Sleep(100);
                    continue;
                }

                if (data.DialogueOptions != null && data.DialogueOptions.Length > 0)
                {
                    int eventIndex = FindEventOptionIndex(data.DialogueOptions);
                    if (eventIndex >= 0)
                    {
                        Plugin.Log.LogInfo($"AutoProgressDialogue: Found 'Event' option at index {eventIndex}, auto-selecting");
                        SelectDialogueOptionInternal(eventIndex);
                        System.Threading.Thread.Sleep(300);
                        continue;
                    }
                }

                if (HasMeaningfulChoice())
                {
                    Plugin.Log.LogInfo($"AutoProgressDialogue: Meaningful choice found with {data.DialogueOptions?.Length ?? 0} options");
                    return;
                }

                Plugin.Log.LogInfo($"AutoProgressDialogue: Auto-advancing past '{data.DialogueText?.Substring(0, Math.Min(50, data.DialogueText?.Length ?? 0))}...'");
                Plugin.RunOnMainThreadAndWait(() => display.OnConfirm(isMouseClick: false));

                System.Threading.Thread.Sleep(150);
            }

            Plugin.Log.LogWarning("AutoProgressDialogue: Timeout");
        }

        private static void SelectDialogueOptionInternal(int choiceIndex)
        {
            var dialogueInteractable = GetCurrentDialogueInteractable();
            var dialogueData = GetCurrentDialogueData();
            var display = UIController.Instance?.DialogueDisplay;

            if (dialogueInteractable == null || dialogueData == null || display == null)
            {
                Plugin.Log.LogWarning("SelectDialogueOptionInternal: Dialogue state not available");
                return;
            }

            var options = dialogueData.DialogueOptions;
            if (options == null || choiceIndex < 0 || choiceIndex >= options.Length)
            {
                Plugin.Log.LogWarning($"SelectDialogueOptionInternal: Invalid choice index {choiceIndex}");
                return;
            }

            Plugin.Log.LogInfo($"SelectDialogueOptionInternal: Selecting option {choiceIndex}: '{options[choiceIndex]}'");

            try
            {
                Plugin.RunOnMainThreadAndWait(() =>
                {
                    dialogueInteractable.TriggerNodeOnCloseEvents();

                    bool isEnd, forceSkip;
                    dialogueInteractable.SelectDialogueOption(choiceIndex, options.Length, out isEnd, out forceSkip);

                    Plugin.Log.LogInfo($"SelectDialogueOptionInternal: isEnd={isEnd}, forceSkip={forceSkip}");

                    if (isEnd && forceSkip)
                    {
                        Plugin.Log.LogInfo("SelectDialogueOptionInternal: Dialogue ending");
                        UIController.Instance.SetDialogueVisibility(visible: false);
                    }
                    else if (IsDialogueOpen())
                    {
                        var nextDialogue = dialogueInteractable.GetNextDialogue();
                        if (nextDialogue != null)
                        {
                            display.ShowDialogue(nextDialogue);
                        }
                    }
                });
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"SelectDialogueOptionInternal: Exception - {ex.Message}");
            }
        }

        public static string ExecuteNpcInteract(int npcIndex)
        {
            if (GameStateManager.Instance?.IsCombat ?? false)
                return JsonHelper.Serialize(new { success = false, error = "Cannot interact with NPCs during combat" });

            if (IsDialogueOpen())
                return JsonHelper.Serialize(new { success = false, error = "Dialogue already open" });

            var map = LevelGenerator.Instance?.Map;
            if (map == null)
                return JsonHelper.Serialize(new { success = false, error = "Map not loaded" });

            var interactables = map.DialogueInteractables;
            if (interactables == null || interactables.Count == 0)
                return JsonHelper.Serialize(new { success = false, error = "No NPCs on map" });

            if (npcIndex < 0 || npcIndex >= interactables.Count)
                return JsonHelper.Serialize(new { success = false, error = $"Invalid NPC index: {npcIndex}. Valid range: 0-{interactables.Count - 1}" });

            var interactable = interactables[npcIndex];
            if (interactable == null)
                return JsonHelper.Serialize(new { success = false, error = "NPC is null" });

            var npc = interactable as DialogueInteractable;
            if (npc == null)
                return JsonHelper.Serialize(new { success = false, error = "Interactable is not a DialogueInteractable" });

            var npcName = npc.DialogueCharacter?.CharacterName ?? "Unknown";
            Plugin.Log.LogInfo($"ExecuteNpcInteract: Starting interaction with {npcName} at index {npcIndex}");

            Plugin.RunOnMainThreadAndWait(() =>
            {
                var npcPos = npc.transform.position;
                var playerMovement = PlayerMovementController.Instance;
                if (playerMovement != null)
                {
                    var targetPos = new UnityEngine.Vector3(npcPos.x - 2f, npcPos.y, npcPos.z);
                    playerMovement.transform.position = targetPos;
                    Plugin.Log.LogInfo($"ExecuteNpcInteract: Teleported player near NPC at ({targetPos.x:F1}, {targetPos.y:F1})");
                }

                npc.ForceStart();
            });

            var startTime = DateTime.Now;
            while (!IsDialogueOpen() && !TimedOut(startTime, 3000))
            {
                System.Threading.Thread.Sleep(50);
            }

            if (!IsDialogueOpen())
            {
                return JsonHelper.Serialize(new { success = false, error = "Dialogue failed to open" });
            }

            System.Threading.Thread.Sleep(200);
            AutoProgressDialogue();

            if (StateSerializer.IsInSkillSelection())
            {
                return JsonHelper.Serialize(new
                {
                    success = true,
                    phase = "SKILL_SELECTION",
                    transitionedFrom = "dialogue",
                    npc = npcName
                });
            }

            if (!IsDialogueOpen())
            {
                return JsonHelper.Serialize(new
                {
                    success = true,
                    phase = "EXPLORATION",
                    dialogueComplete = true,
                    npc = npcName
                });
            }

            return StateSerializer.GetDialogueStateJson();
        }

        public static string ExecuteDialogueChoice(int choiceIndex)
        {
            if (!IsDialogueOpen())
                return JsonHelper.Serialize(new { success = false, error = "No dialogue open" });

            var dialogueInteractable = GetCurrentDialogueInteractable();
            var dialogueData = GetCurrentDialogueData();
            var display = UIController.Instance?.DialogueDisplay;

            if (dialogueInteractable == null || dialogueData == null || display == null)
                return JsonHelper.Serialize(new { success = false, error = "Dialogue state not available" });

            var options = dialogueData.DialogueOptions;
            if (options == null || options.Length == 0)
                return JsonHelper.Serialize(new { success = false, error = "No dialogue options available" });

            if (choiceIndex < 0 || choiceIndex >= options.Length)
                return JsonHelper.Serialize(new { success = false, error = $"Invalid choice index: {choiceIndex}. Valid range: 0-{options.Length - 1}" });

            var selectedOptionText = options[choiceIndex];
            Plugin.Log.LogInfo($"ExecuteDialogueChoice: Selecting option {choiceIndex}: '{selectedOptionText}'");

            try
            {
                bool isEnd = false;
                bool forceSkip = false;

                Plugin.RunOnMainThreadAndWait(() =>
                {
                    try
                    {
                        var characterDisplay = dialogueData.LeftIsSpeaking
                            ? display.LeftCharacterDisplay
                            : display.RightCharacterDisplay;

                        if (characterDisplay != null)
                        {
                            if (dialogueData.IsChoiceEvent && characterDisplay.ChoiceEventOptions != null)
                            {
                                characterDisplay.ChoiceEventOptions.SelectByIndex(choiceIndex);
                            }
                            else if (characterDisplay.DialogOptions != null)
                            {
                                characterDisplay.DialogOptions.SelectByIndex(choiceIndex);
                            }
                        }
                    }
                    catch (System.Exception uiEx)
                    {
                        Plugin.Log.LogWarning($"ExecuteDialogueChoice: UI selection failed (non-fatal): {uiEx.Message}");
                    }

                    dialogueInteractable.TriggerNodeOnCloseEvents();
                    dialogueInteractable.SelectDialogueOption(choiceIndex, options.Length, out isEnd, out forceSkip);

                    Plugin.Log.LogInfo($"ExecuteDialogueChoice: isEnd={isEnd}, forceSkip={forceSkip}");

                    if (isEnd && forceSkip)
                    {
                        Plugin.Log.LogInfo("ExecuteDialogueChoice: Dialogue ending");
                        UIController.Instance.SetDialogueVisibility(visible: false);
                    }
                    else if (IsDialogueOpen())
                    {
                        var nextDialogue = dialogueInteractable.GetNextDialogue();
                        if (nextDialogue != null)
                        {
                            display.ShowDialogue(nextDialogue);
                        }
                    }
                });

                if (isEnd && forceSkip)
                {
                    System.Threading.Thread.Sleep(300);

                    if (StateSerializer.IsInEquipmentSelection())
                    {
                        return JsonHelper.Serialize(new
                        {
                            success = true,
                            phase = "EQUIPMENT_SELECTION",
                            transitionedFrom = "dialogue",
                            state = JObject.Parse(StateSerializer.GetEquipmentSelectionStateJson())
                        });
                    }

                    if (StateSerializer.IsInSkillSelection())
                    {
                        return JsonHelper.Serialize(new
                        {
                            success = true,
                            phase = "SKILL_SELECTION",
                            transitionedFrom = "dialogue"
                        });
                    }

                    return JsonHelper.Serialize(new
                    {
                        success = true,
                        phase = "EXPLORATION",
                        dialogueComplete = true
                    });
                }

                System.Threading.Thread.Sleep(200);
                AutoProgressDialogue();

                if (StateSerializer.IsInEquipmentSelection())
                {
                    return JsonHelper.Serialize(new
                    {
                        success = true,
                        phase = "EQUIPMENT_SELECTION",
                        transitionedFrom = "dialogue",
                        state = JObject.Parse(StateSerializer.GetEquipmentSelectionStateJson())
                    });
                }

                if (StateSerializer.IsInSkillSelection())
                {
                    return JsonHelper.Serialize(new
                    {
                        success = true,
                        phase = "SKILL_SELECTION",
                        transitionedFrom = "dialogue"
                    });
                }

                if (!IsDialogueOpen())
                {
                    return JsonHelper.Serialize(new
                    {
                        success = true,
                        phase = "EXPLORATION",
                        dialogueComplete = true
                    });
                }

                return StateSerializer.GetDialogueStateJson();
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"ExecuteDialogueChoice: Exception - {ex.Message}\n{ex.StackTrace}");
                return JsonHelper.Serialize(new
                {
                    success = false,
                    error = $"Exception during dialogue choice: {ex.Message}",
                    choiceIndex,
                    optionText = selectedOptionText
                });
            }
        }

        // =====================================================
        // MERCHANT SHOP INTERACTION
        // =====================================================

        private static FieldInfo _merchantMenuField;

        private static MerchantMenu GetMerchantMenu()
        {
            var ui = UIController.Instance;
            if (ui == null) return null;

            if (_merchantMenuField == null)
            {
                _merchantMenuField = typeof(UIController).GetField("MerchantMenu",
                    BindingFlags.NonPublic | BindingFlags.Instance);
            }
            return _merchantMenuField?.GetValue(ui) as MerchantMenu;
        }

        public static bool IsMerchantMenuOpen()
        {
            return GetMerchantMenu()?.IsOpen ?? false;
        }

        public static string ExecuteMerchantInteract()
        {
            if (GameStateManager.Instance?.IsCombat ?? false)
                return JsonHelper.Serialize(new { success = false, error = "Cannot shop during combat" });

            if (IsMerchantMenuOpen())
                return StateSerializer.GetMerchantStateJson();

            var map = LevelGenerator.Instance?.Map;
            if (map == null)
                return JsonHelper.Serialize(new { success = false, error = "Map not loaded" });

            var merchantInteractable = map.MerchantInteractable;
            if (merchantInteractable == null)
                return JsonHelper.Serialize(new { success = false, error = "No merchant on this map" });

            var merchant = merchantInteractable as MerchantInteractable;
            if (merchant == null)
                return JsonHelper.Serialize(new { success = false, error = "Merchant is not a MerchantInteractable" });

            Plugin.Log.LogInfo("ExecuteMerchantInteract: Opening merchant shop");

            var merchantPos = merchant.transform.position;
            var playerMovement = PlayerMovementController.Instance;
            if (playerMovement != null)
            {
                var targetPos = new UnityEngine.Vector3(merchantPos.x - 2f, merchantPos.y, merchantPos.z);
                playerMovement.transform.position = targetPos;
                Plugin.Log.LogInfo($"ExecuteMerchantInteract: Teleported player near merchant");
            }

            merchant.StartMerchantInteraction();

            var startTime = DateTime.Now;
            while (!IsMerchantMenuOpen() && !TimedOut(startTime, 3000))
            {
                System.Threading.Thread.Sleep(50);
            }

            if (!IsMerchantMenuOpen())
            {
                return JsonHelper.Serialize(new { success = false, error = "Merchant menu failed to open" });
            }

            System.Threading.Thread.Sleep(200);

            return StateSerializer.GetMerchantStateJson();
        }

        public static string ExecuteMerchantBuy(int itemIndex, int quantity = 1)
        {
            if (!IsMerchantMenuOpen())
                return JsonHelper.Serialize(new { success = false, error = "Merchant menu not open" });

            var merchant = MerchantInteractable.currentMerchant;
            if (merchant == null)
                return JsonHelper.Serialize(new { success = false, error = "No active merchant" });

            var stockedItems = merchant.StockedItems;
            if (itemIndex < 0 || itemIndex >= stockedItems.Count)
                return JsonHelper.Serialize(new { success = false, error = $"Invalid item index: {itemIndex}. Valid range: 0-{stockedItems.Count - 1}" });

            var shopItem = stockedItems[itemIndex];

            if (!merchant.CanBuyItem(shopItem, quantity))
            {
                return JsonHelper.Serialize(new
                {
                    success = false,
                    error = "Cannot afford this item",
                    price = shopItem.Price * quantity,
                    gold = InventoryManager.Instance?.Gold ?? 0
                });
            }

            var itemName = shopItem.GetName();
            var cost = shopItem.Price * quantity;

            Plugin.Log.LogInfo($"ExecuteMerchantBuy: Purchasing {itemName} for {cost} gold");

            merchant.BuyItem(shopItem, quantity);

            if (shopItem.ItemType == ShopItemType.Exp)
            {
                Plugin.Log.LogInfo("ExecuteMerchantBuy: EXP purchase - waiting for distribution to complete");
                var startTime = DateTime.Now;

                while (!TimedOut(startTime, 5000))
                {
                    System.Threading.Thread.Sleep(100);

                    if (IsMerchantMenuOpen())
                    {
                        Plugin.Log.LogInfo("ExecuteMerchantBuy: Back to merchant menu");
                        break;
                    }
                }
            }

            if (shopItem.ItemType == ShopItemType.Equipment)
            {
                Plugin.Log.LogInfo("ExecuteMerchantBuy: Equipment purchase - waiting for equipment selection");
                var startTime = DateTime.Now;

                while (!TimedOut(startTime, 3000))
                {
                    System.Threading.Thread.Sleep(100);

                    if (StateSerializer.IsInEquipmentSelection())
                    {
                        Plugin.Log.LogInfo("ExecuteMerchantBuy: Equipment selection menu opened");
                        return JsonHelper.Serialize(new
                        {
                            success = true,
                            purchased = itemName,
                            cost = cost,
                            goldRemaining = InventoryManager.Instance?.Gold ?? 0,
                            phase = "EQUIPMENT_SELECTION",
                            note = "Use /choice to assign equipment to a monster or scrap",
                            state = JObject.Parse(StateSerializer.GetEquipmentSelectionStateJson())
                        });
                    }
                }
            }

            System.Threading.Thread.Sleep(200);

            return JsonHelper.Serialize(new
            {
                success = true,
                purchased = itemName,
                cost = cost,
                goldRemaining = InventoryManager.Instance?.Gold ?? 0,
                state = JObject.Parse(StateSerializer.GetMerchantStateJson())
            });
        }

        public static string ExecuteMerchantClose()
        {
            if (!IsMerchantMenuOpen())
                return JsonHelper.Serialize(new { success = false, error = "Merchant menu not open" });

            Plugin.Log.LogInfo("ExecuteMerchantClose: Closing merchant menu");

            UIController.Instance.SetMerchantMenuVisibility(visible: false);

            var startTime = DateTime.Now;
            while (IsMerchantMenuOpen() && !TimedOut(startTime, 2000))
            {
                System.Threading.Thread.Sleep(50);
            }

            System.Threading.Thread.Sleep(200);

            return JsonHelper.Serialize(new
            {
                success = true,
                phase = "EXPLORATION"
            });
        }

        // =====================================================
        // RUN START AND DIFFICULTY SELECTION
        // =====================================================

        public static string ExecuteDifficultyChoice(int choiceIndex)
        {
            if (!StateSerializer.IsInDifficultySelection())
                return JsonHelper.Serialize(new { success = false, error = "Not in difficulty selection screen" });

            var menu = UIController.Instance?.DifficultySelectMenu;
            if (menu == null || !menu.IsOpen)
                return JsonHelper.Serialize(new { success = false, error = "Difficulty selection menu not available" });

            EDifficulty targetDifficulty;
            switch (choiceIndex)
            {
                case 0:
                    targetDifficulty = EDifficulty.Normal;
                    break;
                case 1:
                    targetDifficulty = EDifficulty.Heroic;
                    break;
                case 2:
                    targetDifficulty = EDifficulty.Mythic;
                    break;
                default:
                    return JsonHelper.Serialize(new { success = false, error = $"Invalid difficulty index: {choiceIndex}. Valid: 0=Normal, 1=Heroic, 2=Mythic" });
            }

            var maxUnlocked = ProgressManager.Instance?.UnlockedDifficulty ?? 1;
            if ((int)targetDifficulty > maxUnlocked)
            {
                return JsonHelper.Serialize(new
                {
                    success = false,
                    error = $"Difficulty '{targetDifficulty}' is not unlocked. Max unlocked level: {maxUnlocked}"
                });
            }

            Plugin.Log.LogInfo($"ExecuteDifficultyChoice: Selecting difficulty {targetDifficulty} (index {choiceIndex})");

            try
            {
                Plugin.RunOnMainThreadAndWait(() =>
                {
                    while (menu.CurrentDifficulty != targetDifficulty)
                    {
                        if ((int)menu.CurrentDifficulty < (int)targetDifficulty)
                            menu.OnGoRight();
                        else
                            menu.OnGoLeft();
                    }

                    menu.OnConfirm();
                });

                System.Threading.Thread.Sleep(300);

                Plugin.RunOnMainThreadAndWait(() =>
                {
                    var popup = PopupController.Instance;
                    if (popup?.IsOpen ?? false)
                    {
                        Plugin.Log.LogInfo("ExecuteDifficultyChoice: Confirming popup");
                        popup.ConfirmMenu.SelectByIndex(0);
                        if (InputConfirmMethod != null)
                        {
                            InputConfirmMethod.Invoke(popup.ConfirmMenu, null);
                        }
                    }
                });

                var startTime = DateTime.Now;
                while (!TimedOut(startTime, 5000))
                {
                    System.Threading.Thread.Sleep(100);

                    if (StateSerializer.IsInMonsterSelection())
                    {
                        Plugin.Log.LogInfo($"ExecuteDifficultyChoice: Monster selection opened with difficulty {targetDifficulty}");
                        return JsonHelper.Serialize(new
                        {
                            success = true,
                            action = "difficulty_selected",
                            difficulty = targetDifficulty.ToString(),
                            phase = "MONSTER_SELECTION",
                            state = JObject.Parse(StateSerializer.GetMonsterSelectionStateJson())
                        });
                    }
                }

                return JsonHelper.Serialize(new { success = false, error = "Timeout waiting for monster selection after difficulty selection" });
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"ExecuteDifficultyChoice: Exception - {ex.Message}\n{ex.StackTrace}");
                return JsonHelper.Serialize(new { success = false, error = $"Exception during difficulty selection: {ex.Message}" });
            }
        }

        // =====================================================
        // END OF RUN (VICTORY/DEFEAT SCREEN) - Internal helpers
        // =====================================================

        private static FieldInfo _endOfRunMenuField;

        private static EndOfRunMenu GetEndOfRunMenu()
        {
            var ui = UIController.Instance;
            if (ui == null) return null;

            if (_endOfRunMenuField == null)
            {
                _endOfRunMenuField = typeof(UIController).GetField("EndOfRunMenu",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            }
            return _endOfRunMenuField?.GetValue(ui) as EndOfRunMenu;
        }
    }
}
