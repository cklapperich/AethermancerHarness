using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace AethermancerHarness
{
    /// <summary>
    /// Menu-related actions: post-combat, skill selection, choices, dialogue, merchant, difficulty.
    /// </summary>
    public static partial class ActionHandler
    {
        // =====================================================
        // DIALOGUE STEP RESULT (for coroutine-based advancement)
        // =====================================================

        private class DialogueStepResult
        {
            public bool IsOpen { get; set; }
            public DialogueDisplayData Data { get; set; }
        }
        // =====================================================
        // POST-COMBAT AUTO-ADVANCE AND SKILL SELECTION
        // =====================================================

        public static string WaitForPostCombatComplete(int timeoutMs = 60000)
        {
            // This method can be called from HTTP thread - it handles its own threading
            var startTime = DateTime.Now;
            Plugin.Log.LogInfo("WaitForPostCombatComplete: Starting post-combat auto-advance");

            while (true)
            {
                // Check states on main thread
                bool postCombatOpen = false;
                bool inExploration = false;
                string explorationResult = null;

                Plugin.RunOnMainThreadAndWait(() =>
                {
                    postCombatOpen = IsPostCombatMenuOpen();
                    if (!postCombatOpen)
                    {
                        inExploration = IsInExploration();
                        if (inExploration)
                            explorationResult = CreateExplorationResult(CombatResult.Victory);
                    }
                });

                if (postCombatOpen)
                    break;

                if (inExploration)
                {
                    Plugin.Log.LogInfo("WaitForPostCombatComplete: Back in exploration (no post-combat screens)");
                    return explorationResult;
                }

                if (TimedOut(startTime, timeoutMs))
                {
                    Plugin.Log.LogWarning("WaitForPostCombatComplete: Timeout waiting for PostCombatMenu");
                    return JsonConfig.Error("Timeout waiting for PostCombatMenu to open", new { phase = GamePhase.Timeout });
                }

                // Sleep on HTTP thread - doesn't block Unity
                System.Threading.Thread.Sleep(100);
            }

            Plugin.Log.LogInfo("WaitForPostCombatComplete: PostCombatMenu is open");
            return ProcessPostCombatStates(startTime, timeoutMs);
        }

        private static string ProcessPostCombatStates(DateTime startTime, int timeoutMs)
        {
            // This method can be called from HTTP thread - it handles its own threading

            while (true)
            {
                if (TimedOut(startTime, timeoutMs))
                    return JsonConfig.Error("Timeout waiting for post-combat processing", new { phase = GamePhase.Timeout });

                // Check all state conditions on main thread
                bool inSkillSelection = false;
                bool inEndOfRun = false;
                bool inExploration = false;
                string skillSelectionResult = null;
                string explorationResult = null;
                PostCombatMenu.EPostCombatMenuState currentState = default;
                bool worthinessCanContinue = false;
                bool levelUpCanContinue = false;
                bool hasPendingLevelUp = false;

                Plugin.RunOnMainThreadAndWait(() =>
                {
                    inSkillSelection = StateSerializer.IsInSkillSelection();
                    if (inSkillSelection)
                    {
                        skillSelectionResult = StateSerializer.GetSkillSelectionStateJson();
                        return;
                    }

                    inEndOfRun = StateSerializer.IsInEndOfRunMenu();
                    if (inEndOfRun)
                        return;

                    inExploration = IsInExploration();
                    if (inExploration)
                    {
                        explorationResult = CreateExplorationResult(CombatResult.Victory);
                        return;
                    }

                    var postCombatMenu = UIController.Instance?.PostCombatMenu;
                    if (postCombatMenu == null)
                        return;

                    currentState = postCombatMenu.CurrentState;

                    switch (currentState)
                    {
                        case PostCombatMenu.EPostCombatMenuState.WorthinessUI:
                        case PostCombatMenu.EPostCombatMenuState.WorthinessUIDetailed:
                            worthinessCanContinue = CheckWorthinessCanContinue(postCombatMenu);
                            break;

                        case PostCombatMenu.EPostCombatMenuState.LevelUpUI:
                            levelUpCanContinue = CheckLevelUpCanContinue(postCombatMenu);
                            hasPendingLevelUp = levelUpCanContinue && FindFirstPendingLevelUp(postCombatMenu) != null;
                            break;
                    }
                });

                // Handle results outside main thread dispatch
                if (inSkillSelection)
                {
                    Plugin.Log.LogInfo("ProcessPostCombatStates: Skill selection menu is open");
                    return skillSelectionResult;
                }

                if (inEndOfRun)
                {
                    Plugin.Log.LogInfo("ProcessPostCombatStates: End of run screen detected, auto-continuing");
                    Plugin.RunOnMainThreadAndWait(() => AutoContinueFromEndOfRun());
                    System.Threading.Thread.Sleep(500);
                    continue;
                }

                if (inExploration)
                {
                    Plugin.Log.LogInfo("ProcessPostCombatStates: Back in exploration");
                    return explorationResult;
                }

                Plugin.Log.LogInfo($"ProcessPostCombatStates: Current state = {currentState}");

                switch (currentState)
                {
                    case PostCombatMenu.EPostCombatMenuState.WorthinessUI:
                    case PostCombatMenu.EPostCombatMenuState.WorthinessUIDetailed:
                        if (worthinessCanContinue)
                        {
                            Plugin.Log.LogInfo("ProcessPostCombatStates: Worthiness ready, triggering Continue");
                            Plugin.RunOnMainThreadAndWait(() =>
                            {
                                var menu = UIController.Instance?.PostCombatMenu;
                                if (menu != null) TriggerContinue(menu);
                            });
                            System.Threading.Thread.Sleep(200);
                        }
                        // If not ready, loop continues and checks again
                        break;

                    case PostCombatMenu.EPostCombatMenuState.LevelUpUI:
                        if (levelUpCanContinue)
                        {
                            if (hasPendingLevelUp)
                            {
                                Plugin.Log.LogInfo("ProcessPostCombatStates: Found pending level up, opening skill selection");
                                Plugin.RunOnMainThreadAndWait(() =>
                                {
                                    var menu = UIController.Instance?.PostCombatMenu;
                                    var pendingLevelUp = menu != null ? FindFirstPendingLevelUp(menu) : null;
                                    if (pendingLevelUp != null)
                                        OpenSkillSelectionForMonster(pendingLevelUp);
                                });

                                if (WaitForSkillSelectionOpen(startTime, timeoutMs))
                                {
                                    string result = null;
                                    Plugin.RunOnMainThreadAndWait(() => result = StateSerializer.GetSkillSelectionStateJson());
                                    return result;
                                }
                            }
                            else
                            {
                                Plugin.Log.LogInfo("ProcessPostCombatStates: No pending level ups, continuing");
                                Plugin.RunOnMainThreadAndWait(() =>
                                {
                                    var menu = UIController.Instance?.PostCombatMenu;
                                    if (menu != null) TriggerContinue(menu);
                                });
                                System.Threading.Thread.Sleep(200);
                            }
                        }
                        break;

                    case PostCombatMenu.EPostCombatMenuState.SkillSelectionUI:
                        Plugin.Log.LogInfo("ProcessPostCombatStates: In SkillSelectionUI state");
                        break;
                }

                // Sleep on HTTP thread - doesn't block Unity
                System.Threading.Thread.Sleep(100);
            }
        }

        /// <summary>
        /// Quick check if all worthiness UIs can continue. MUST be called on main thread.
        /// </summary>
        private static bool CheckWorthinessCanContinue(PostCombatMenu postCombatMenu)
        {
            foreach (var info in postCombatMenu.PostCombatMonsterInfos)
            {
                if (info.monster != null && info.gameObject.activeSelf && !info.WorthinessUI.CanContinue)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Quick check if all level up UIs can continue. MUST be called on main thread.
        /// </summary>
        private static bool CheckLevelUpCanContinue(PostCombatMenu postCombatMenu)
        {
            foreach (var info in postCombatMenu.PostCombatMonsterInfos)
            {
                if (info.monster != null && info.gameObject.activeSelf && !info.LevelUpUI.CanContinue)
                    return false;
            }
            return true;
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

        /// <summary>
        /// Auto-continue from end of run screen. MUST be called on main thread.
        /// </summary>
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
            menu.Close();
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
            // This method can be called from HTTP thread - it handles its own threading
            while (!TimedOut(startTime, timeoutMs))
            {
                bool isOpen = false;
                Plugin.RunOnMainThreadAndWait(() => isOpen = StateSerializer.IsInSkillSelection());
                if (isOpen)
                    return true;
                // Sleep on HTTP thread - doesn't block Unity
                System.Threading.Thread.Sleep(100);
            }
            return false;
        }

        private static string CreateExplorationResult(CombatResult combatResult)
        {
            var result = new JObject
            {
                ["success"] = true,
                ["combatResult"] = combatResult.ToString(),
                ["state"] = JObject.Parse(StateSerializer.ToJson())
            };
            return result.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string ExecuteSkillSelectionChoice(int choiceIndex)
        {
            if (!StateSerializer.IsInSkillSelection())
                return JsonConfig.Error("Not in skill selection screen");

            var skillSelectMenu = UIController.Instance.PostCombatMenu.SkillSelectMenu;

            // Handle reroll (choiceIndex == -1)
            if (choiceIndex == -1)
            {
                if (InventoryManager.Instance.SkillRerolls <= 0)
                    return JsonConfig.Error("No skill rerolls available");

                var menuList = skillSelectMenu.MenuList;
                for (int i = 0; i < menuList.List.Count; i++)
                {
                    if (menuList.List[i] == skillSelectMenu.RerollSkillsButton)
                    {
                        menuList.SelectByIndex(i);
                        TriggerMenuConfirm(menuList);
                        Plugin.Log.LogInfo("ExecuteSkillSelectionChoice: Rerolled skills");
                        System.Threading.Thread.Sleep(500);
                        return StateSerializer.GetSkillSelectionStateJson();
                    }
                }
                return JsonConfig.Error("Could not find reroll button");
            }

            // Handle max health bonus (choiceIndex == 3)
            if (choiceIndex == 3)
            {
                var menuList = skillSelectMenu.MenuList;
                for (int i = 0; i < menuList.List.Count; i++)
                {
                    if (menuList.List[i] == skillSelectMenu.AlternativeBonusButton)
                    {
                        menuList.SelectByIndex(i);
                        TriggerMenuConfirm(menuList);
                        Plugin.Log.LogInfo("ExecuteSkillSelectionChoice: Selected max health bonus");
                        System.Threading.Thread.Sleep(500);
                        return WaitForPostCombatComplete();
                    }
                }
                return JsonConfig.Error("Max health option not available");
            }

            // Handle skill selection (choiceIndex 0-2)
            if (choiceIndex >= 0 && choiceIndex <= 2)
            {
                var menuList = skillSelectMenu.MenuList;
                if (choiceIndex < menuList.List.Count)
                {
                    menuList.SelectByIndex(choiceIndex);
                    TriggerMenuConfirm(menuList);
                    Plugin.Log.LogInfo($"ExecuteSkillSelectionChoice: Selected skill at index {choiceIndex}");
                    System.Threading.Thread.Sleep(500);
                    return WaitForPostCombatComplete();
                }
                return JsonConfig.Error($"Invalid skill index: {choiceIndex}");
            }

            return JsonConfig.Error($"Invalid choice index: {choiceIndex}. Use 0-2 for skills, 3 for max health, -1 for reroll.");
        }

        // =====================================================
        // UNIFIED CHOICE HANDLER
        // =====================================================

        public static string ExecuteChoice(string choiceName, string shift = null)
        {
            // Resolve choice name to index based on current context
            var (choiceIndex, error) = ResolveChoiceName(choiceName);
            if (error != null)
                return JsonConfig.Error(error);

            // Check skill selection
            if (StateSerializer.IsInSkillSelection())
            {
                Plugin.Log.LogInfo($"ExecuteChoice: Routing to skill selection handler ('{choiceName}' -> index {choiceIndex})");
                return ExecuteSkillSelectionChoice(choiceIndex);
            }

            // Check equipment selection first (after picking equipment from dialogue/loot)
            if (StateSerializer.IsInEquipmentSelection())
            {
                Plugin.Log.LogInfo($"ExecuteChoice: Routing to equipment selection handler ('{choiceName}' -> index {choiceIndex})");
                return ExecuteEquipmentChoice(choiceIndex);
            }

            // Check merchant menu
            if (IsMerchantMenuOpen())
            {
                Plugin.Log.LogInfo($"ExecuteChoice: Routing to merchant handler ('{choiceName}' -> index {choiceIndex})");
                return ExecuteMerchantChoice(choiceIndex);
            }

            // Check difficulty selection (run start)
            if (StateSerializer.IsInDifficultySelection())
            {
                Plugin.Log.LogInfo($"ExecuteChoice: Routing to difficulty selection handler ('{choiceName}' -> index {choiceIndex})");
                return ExecuteDifficultyChoice(choiceIndex);
            }

            // Check monster selection (shrine/starter)
            if (StateSerializer.IsInMonsterSelection())
            {
                Plugin.Log.LogInfo($"ExecuteChoice: Routing to monster selection handler ('{choiceName}' -> index {choiceIndex}, shift: {shift ?? "default"})");
                return ExecuteMonsterSelectionChoice(choiceIndex, shift);
            }

            // Check aether spring menu
            if (StateSerializer.IsInAetherSpringMenu())
            {
                Plugin.Log.LogInfo($"ExecuteChoice: Routing to aether spring handler ('{choiceName}' -> index {choiceIndex})");
                return ExecuteAetherSpringChoice(choiceIndex);
            }

            // Check dialogue
            if (IsDialogueOpen())
            {
                Plugin.Log.LogInfo($"ExecuteChoice: Routing to dialogue choice handler ('{choiceName}' -> index {choiceIndex})");
                return ExecuteDialogueChoice(choiceIndex);
            }

            return JsonConfig.Error("No active choice context (not in dialogue, equipment selection, difficulty selection, merchant, or monster selection)");
        }

        // =====================================================
        // MONSTER SELECTION (Shrine/Starter)
        // =====================================================

        public static string ExecuteMonsterSelectionChoice(int choiceIndex, string shift = null)
        {
            var menu = UIController.Instance?.MonsterShrineMenu;
            if (menu == null || !menu.IsOpen)
                return JsonConfig.Error("Monster selection menu not open");

            var selection = menu.MonsterSelection;
            var displayedMonsters = menu.DisplayedMonsters;

            if (displayedMonsters == null || displayedMonsters.Count == 0)
                return JsonConfig.Error("No monsters available");

            // Handle reroll (choiceIndex == -1)
            if (choiceIndex == -1)
            {
                var shrineRerolls = InventoryManager.Instance?.ShrineRerolls ?? 0;
                var shrineState = menu.ShrineSelectionState;

                if (shrineRerolls <= 0)
                    return JsonConfig.Error("No shrine rerolls available");

                if (shrineState != EShrineState.NormalShrineSelection)
                    return JsonConfig.Error("Reroll not available for this shrine type");

                Plugin.Log.LogInfo("ExecuteMonsterSelectionChoice: Rerolling monsters");

                try
                {
                    Plugin.RunOnMainThreadAndWait(() =>
                    {
                        // Remove a shrine reroll
                        InventoryManager.Instance.RemoveShrineReroll();

                        // Trigger monster regeneration
                        var map = LevelGenerator.Instance?.Map;
                        if (map != null && map.MonsterShrine != null)
                        {
                            var shrineTrigger = map.MonsterShrine as MonsterShrineTrigger;
                            if (shrineTrigger != null)
                            {
                                shrineTrigger.GenerateMementosForShrine(ignoreHasData: true, isReroll: true);
                            }
                        }
                    });

                    System.Threading.Thread.Sleep(500);

                    // Return updated monster selection state
                    return StateSerializer.GetMonsterSelectionStateJson();
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogError($"ExecuteMonsterSelectionChoice: Reroll failed - {ex.Message}");
                    return JsonConfig.Error($"Monster reroll failed: {ex.Message}");
                }
            }

            // Calculate total count including random entry
            int totalCount = displayedMonsters.Count + (selection.HasRandomMonster ? 1 : 0);

            // Validate choice index
            if (choiceIndex < 0 || choiceIndex >= totalCount)
                return JsonConfig.Error($"Invalid choice index: {choiceIndex}. Valid range: 0-{totalCount - 1}");

            // Check if selecting random
            var isRandom = selection.HasRandomMonster && choiceIndex == selection.RandomMonsterPosition;

            // Calculate adjusted index for displayedMonsters (skip random option position)
            int adjustedIndex = choiceIndex;
            if (!isRandom && selection.HasRandomMonster && choiceIndex > selection.RandomMonsterPosition)
            {
                adjustedIndex = choiceIndex - 1;
            }

            // Get monster name - for random, use "Random Monster"; otherwise use adjusted index
            string monsterName;
            Monster selectedMonster = null;
            if (isRandom)
            {
                monsterName = "Random Monster";
            }
            else
            {
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
                    return JsonConfig.Error($"Invalid shift value: '{shift}'. Use 'normal' or 'shifted'.");
                }
            }

            // Validate shifted variant is available if requested
            if (targetShift == EMonsterShift.Shifted && !isRandom)
            {
                bool hasShiftedVariant = InventoryManager.Instance?.HasShiftedMementosOfMonster(selectedMonster) ?? false;
                if (!hasShiftedVariant)
                {
                    return JsonConfig.Error($"Shifted variant not available for {monsterName}");
                }
            }

            Plugin.Log.LogInfo($"ExecuteMonsterSelectionChoice: Selecting monster at index {choiceIndex}: {monsterName} (isRandom: {isRandom}, shift: {targetShift})");

            try
            {
                // Run UI operations on main thread
                Plugin.RunOnMainThreadAndWait(() =>
                {
                    // Set the selection index (use adjustedIndex + 1 because game is 1-indexed)
                    selection.SetSelectedIndex(adjustedIndex + 1);

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
                        return JsonConfig.Serialize(new
                        {
                            success = true,
                            action = "monster_selected",
                            selected = monsterName,
                            shift = targetShift,
                            isRandom = isRandom,
                            phase = GamePhase.EquipmentSelection,
                            note = "Select which monster to replace"
                        });
                    }

                    // If skill selection opened (post-rebirth XP), return that state
                    if (StateSerializer.IsInSkillSelection())
                    {
                        Plugin.Log.LogInfo("ExecuteMonsterSelectionChoice: Transitioned to skill selection");
                        return JsonConfig.Serialize(new
                        {
                            success = true,
                            action = "monster_selected",
                            selected = monsterName,
                            shift = targetShift,
                            isRandom = isRandom,
                            phase = GamePhase.SkillSelection,
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
                return JsonConfig.Serialize(new
                {
                    success = true,
                    action = "monster_selected",
                    selected = monsterName,
                    shift = targetShift,
                    isRandom = isRandom,
                    phase = GamePhase.Exploration
                });
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"ExecuteMonsterSelectionChoice: Exception - {ex.Message}\n{ex.StackTrace}");
                return JsonConfig.Error($"Exception during monster selection: {ex.Message}");
            }
        }

        // =====================================================
        // EQUIPMENT SELECTION
        // =====================================================

        public static string ExecuteEquipmentChoice(int choiceIndex)
        {
            var menu = UIController.Instance?.MonsterSelectMenu;
            if (menu == null || !menu.IsOpen)
                return JsonConfig.Error("Equipment selection menu not open");

            var party = MonsterManager.Instance?.Active;
            if (party == null)
                return JsonConfig.Error("No party available");

            int scrapIndex = party.Count;

            // Validate choice index
            if (choiceIndex < 0 || choiceIndex > scrapIndex)
                return JsonConfig.Error($"Invalid choice index: {choiceIndex}. Valid range: 0-{scrapIndex}");

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
                return JsonConfig.Error("No equipment to assign");

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
                    return JsonConfig.Serialize(new
                    {
                        success = true,
                        action = "equipment_trade",
                        assignedTo = targetMonster.Name,
                        assigned = newEquipName,
                        nowHolding = prevEquipName,
                        phase = GamePhase.EquipmentSelection,
                        state = JObject.Parse(StateSerializer.GetEquipmentSelectionStateJson())
                    });
                }

                // Equipment assigned, back to exploration
                Plugin.Log.LogInfo($"ExecuteEquipmentAssign: Equipment assigned to {targetMonster.Name}");
                return JsonConfig.Serialize(new
                {
                    success = true,
                    action = "equipment_assigned",
                    assignedTo = targetMonster.Name,
                    equipment = newEquipName,
                    phase = GamePhase.Exploration
                });
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"ExecuteEquipmentAssign: Exception - {ex.Message}");
                return JsonConfig.Error($"Exception during equipment assignment: {ex.Message}");
            }
        }

        private static string ExecuteEquipmentScrap(MonsterSelectMenu menu)
        {
            var equipment = menu.NewEquipmentInstance;
            if (equipment == null)
                return JsonConfig.Error("No equipment to scrap");

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
                return JsonConfig.Serialize(new
                {
                    success = true,
                    action = "equipment_scrapped",
                    equipment = equipName,
                    goldGained = scrapValue,
                    goldTotal = InventoryManager.Instance?.Gold ?? 0,
                    phase = GamePhase.Exploration
                });
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"ExecuteEquipmentScrap: Exception - {ex.Message}");
                return JsonConfig.Error($"Exception during equipment scrap: {ex.Message}");
            }
        }

        // =====================================================
        // NPC DIALOGUE INTERACTION
        // =====================================================

        public static bool IsDialogueOpen()
        {
            return UIController.Instance?.DialogueDisplay?.IsOpen ?? false;
        }

        private static bool HasMeaningfulChoice()
        {
            var data = GetCurrentDialogueData();
            if (data == null) return false;

            if (data.DialogueOptions == null || data.DialogueOptions.Length == 0)
            {
                Plugin.Log.LogInfo($"HasMeaningfulChoice: No options, returning false");
                return false;
            }

            if (data.DialogueOptions.Length == 1)
            {
                Plugin.Log.LogInfo($"HasMeaningfulChoice: Single option, returning false");
                return false;
            }

            int eventIndex = FindEventOptionIndex(data.DialogueOptions);
            if (eventIndex >= 0)
            {
                Plugin.Log.LogInfo($"HasMeaningfulChoice: Event option found, returning false");
                return false;
            }

            if (data.IsChoiceEvent)
            {
                Plugin.Log.LogInfo($"HasMeaningfulChoice: IsChoiceEvent=true, returning true");
                return true;
            }

            if (data.DialogueOptions.Length > 1)
            {
                Plugin.Log.LogInfo($"HasMeaningfulChoice: Multiple options ({data.DialogueOptions.Length}), returning true");
                return true;
            }

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

        // =====================================================
        // COROUTINE-BASED SINGLE-STEP DIALOGUE ADVANCEMENT
        // =====================================================

        /// <summary>
        /// Coroutine that advances dialogue by one step and waits for Unity frames to process.
        /// This allows Unity's frame loop to continue, preventing the deadlock.
        /// </summary>
        private static IEnumerator AdvanceDialogueOneStepCoroutine(Action<DialogueStepResult> callback)
        {
            var display = UIController.Instance?.DialogueDisplay;

            if (display != null && IsDialogueOpen())
            {
                // Perform the advancement
                display.OnConfirm(isMouseClick: false);

                // Wait for frames to process (let Unity update the dialogue state)
                yield return null;  // Wait 1 frame
                yield return null;  // Wait another frame for state to stabilize
            }

            // Gather result
            var result = new DialogueStepResult
            {
                IsOpen = IsDialogueOpen(),
                Data = GetCurrentDialogueData()
            };

            callback(result);
        }

        /// <summary>
        /// Wrapper that HTTP thread can call (blocking on HTTP thread, but doesn't block main thread).
        /// Advances dialogue by one step using coroutines.
        /// </summary>
        private static DialogueStepResult AdvanceDialogueOneStep(int timeoutMs = 3000)
        {
            DialogueStepResult result = null;
            var completedEvent = new ManualResetEventSlim(false);

            Plugin.RunOnMainThread(() =>
            {
                Plugin.Instance.StartCoroutine(AdvanceDialogueOneStepCoroutine((stepResult) =>
                {
                    result = stepResult;
                    completedEvent.Set();
                }));
            });

            // HTTP thread blocks here, but main thread is free to process frames
            if (!completedEvent.Wait(timeoutMs))
            {
                throw new TimeoutException("Dialogue step timed out");
            }

            return result;
        }

        /// <summary>
        /// Coroutine that selects a dialogue choice and waits for Unity frames to process.
        /// </summary>
        private static IEnumerator SelectChoiceCoroutine(int choiceIndex, Action<DialogueStepResult> callback)
        {
            var dialogueInteractable = GetCurrentDialogue();
            var display = UIController.Instance?.DialogueDisplay;

            if (dialogueInteractable != null && display != null)
            {
                var dialogueData = GetCurrentDialogueData();
                if (dialogueData != null && dialogueData.DialogueOptions != null &&
                    choiceIndex >= 0 && choiceIndex < dialogueData.DialogueOptions.Length)
                {
                    try
                    {
                        // Select the option in the UI
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

                        // Trigger the selection
                        dialogueInteractable.TriggerNodeOnCloseEvents();

                        bool isEnd, forceSkip;
                        dialogueInteractable.SelectDialogueOption(choiceIndex, dialogueData.DialogueOptions.Length,
                            out isEnd, out forceSkip);

                        if (isEnd && forceSkip)
                        {
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
                    }
                    catch (System.Exception ex)
                    {
                        Plugin.Log.LogError($"SelectChoiceCoroutine: Exception - {ex.Message}");
                    }
                }

                yield return null;
                yield return null;
            }

            var result = new DialogueStepResult
            {
                IsOpen = IsDialogueOpen(),
                Data = GetCurrentDialogueData()
            };

            callback(result);
        }

        /// <summary>
        /// Wrapper that HTTP thread can call to select a dialogue choice using coroutines.
        /// </summary>
        private static DialogueStepResult AdvanceDialogueOneStepWithChoice(int choiceIndex, int timeoutMs = 3000)
        {
            DialogueStepResult result = null;
            var completedEvent = new ManualResetEventSlim(false);

            Plugin.RunOnMainThread(() =>
            {
                Plugin.Instance.StartCoroutine(SelectChoiceCoroutine(choiceIndex, (stepResult) =>
                {
                    result = stepResult;
                    completedEvent.Set();
                }));
            });

            if (!completedEvent.Wait(timeoutMs))
            {
                throw new TimeoutException("Choice selection timed out");
            }

            return result;
        }

        private static void AutoProgressDialogue(int maxSteps = 50)
        {
            Plugin.Log.LogInfo($"AutoProgressDialogue: Starting step-by-step progression (maxSteps: {maxSteps})");

            for (int step = 0; step < maxSteps; step++)
            {
                // Check if dialogue closed
                if (!IsDialogueOpen())
                {
                    Plugin.Log.LogInfo("AutoProgressDialogue: Dialogue closed");
                    return;
                }

                // Check if skill selection opened
                if (StateSerializer.IsInSkillSelection())
                {
                    Plugin.Log.LogInfo("AutoProgressDialogue: Skill selection opened");
                    return;
                }

                // Get current dialogue data
                var data = GetCurrentDialogueData();
                if (data == null)
                {
                    Plugin.Log.LogInfo("AutoProgressDialogue: No dialogue data, stopping");
                    return;
                }

                Plugin.Log.LogInfo($"AutoProgressDialogue: Step {step}: DialogueOptions.Length={data?.DialogueOptions?.Length ?? -1}, IsChoiceEvent={data?.IsChoiceEvent}");

                // Check for "Event" option that should be auto-selected
                if (data.DialogueOptions != null && data.DialogueOptions.Length > 0)
                {
                    int eventIndex = FindEventOptionIndex(data.DialogueOptions);
                    if (eventIndex >= 0)
                    {
                        Plugin.Log.LogInfo($"AutoProgressDialogue: Auto-selecting Event option at index {eventIndex}");
                        try
                        {
                            var stepResult = AdvanceDialogueOneStepWithChoice(eventIndex);
                            // Optional delay for watchable mode
                            if (Plugin.WatchableMode)
                            {
                                System.Threading.Thread.Sleep(Plugin.WatchableDelayMs);
                            }
                            continue;  // Loop to next step
                        }
                        catch (System.Exception ex)
                        {
                            Plugin.Log.LogError($"AutoProgressDialogue: Failed to select Event option - {ex.Message}");
                            return;
                        }
                    }
                }

                // Check if this is a decision point
                if (HasMeaningfulChoice())
                {
                    Plugin.Log.LogInfo($"AutoProgressDialogue: Meaningful choice found, stopping");
                    return;
                }

                // Handle single-choice dialogues
                if (data.DialogueOptions != null && data.DialogueOptions.Length == 1)
                {
                    Plugin.Log.LogInfo($"AutoProgressDialogue: Single option '{data.DialogueOptions[0]}', auto-selecting");
                    try
                    {
                        var stepResult = AdvanceDialogueOneStepWithChoice(0);
                        // Optional delay for watchable mode
                        if (Plugin.WatchableMode)
                        {
                            System.Threading.Thread.Sleep(Plugin.WatchableDelayMs);
                        }
                        continue;  // Loop to next step
                    }
                    catch (System.Exception ex)
                    {
                        Plugin.Log.LogError($"AutoProgressDialogue: Failed to select single option - {ex.Message}");
                        return;
                    }
                }

                // Handle text-only dialogues (no options)
                if (data.DialogueOptions == null || data.DialogueOptions.Length == 0)
                {
                    Plugin.Log.LogInfo($"AutoProgressDialogue: Text-only dialogue, advancing");
                    try
                    {
                        var stepResult = AdvanceDialogueOneStep();

                        if (!stepResult.IsOpen)
                        {
                            Plugin.Log.LogInfo("AutoProgressDialogue: Dialogue closed after advancement");
                            return;
                        }

                        // Optional delay for watchable mode
                        if (Plugin.WatchableMode)
                        {
                            System.Threading.Thread.Sleep(Plugin.WatchableDelayMs);
                        }
                        continue;  // Loop to next step
                    }
                    catch (System.Exception ex)
                    {
                        Plugin.Log.LogError($"AutoProgressDialogue: Failed to advance text-only dialogue - {ex.Message}");
                        return;
                    }
                }

                // Fallback: shouldn't reach here
                Plugin.Log.LogError("AutoProgressDialogue: Unexpected dialogue state");
                throw new InvalidOperationException($"Unexpected dialogue state: {data.DialogueOptions?.Length ?? 0} options, IsChoiceEvent={data.IsChoiceEvent}");
            }

            Plugin.Log.LogError($"AutoProgressDialogue: Max steps ({maxSteps}) reached - dialogue did not complete");
            throw new TimeoutException($"Dialogue progression exceeded maximum steps ({maxSteps}) without reaching a decision point or completion");
        }

        private static void SelectDialogueOptionInternal(int choiceIndex)
        {
            var dialogueData = GetCurrentDialogueData();

            if (dialogueData == null)
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
                // Use coroutine-based approach to avoid blocking main thread
                var stepResult = AdvanceDialogueOneStepWithChoice(choiceIndex);
                Plugin.Log.LogInfo($"SelectDialogueOptionInternal: Choice selection completed, dialogue open: {stepResult.IsOpen}");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"SelectDialogueOptionInternal: Exception - {ex.Message}");
            }
        }

        /// <summary>
        /// NPC interaction - now uses unified TeleportAndInteract.
        /// </summary>
        public static string ExecuteNpcInteract(string npcName)
        {
            if (GameStateManager.Instance?.IsCombat ?? false)
                return JsonConfig.Error("Cannot interact with NPCs during combat");

            if (IsDialogueOpen())
                return JsonConfig.Error("Dialogue already open");

            if (string.IsNullOrEmpty(npcName))
                return JsonConfig.Error("npcName is required");

            // Use unified teleport-and-interact
            Plugin.Log.LogInfo($"ExecuteNpcInteract: Delegating to TeleportAndInteract for '{npcName}'");
            return TeleportAndInteract(npcName);
        }

        public static string ExecuteDialogueChoice(int choiceIndex)
        {
            if (!IsDialogueOpen())
                return JsonConfig.Error("No dialogue open");

            var dialogueInteractable = GetCurrentDialogue();
            var dialogueData = GetCurrentDialogueData();
            var display = UIController.Instance?.DialogueDisplay;

            if (dialogueInteractable == null || dialogueData == null || display == null)
                return JsonConfig.Error("Dialogue state not available");

            var options = dialogueData.DialogueOptions;
            if (options == null || options.Length == 0)
                return JsonConfig.Error("No dialogue options available");

            if (choiceIndex < 0 || choiceIndex >= options.Length)
                return JsonConfig.Error($"Invalid choice index: {choiceIndex}. Valid range: 0-{options.Length - 1}");

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
                        return JsonConfig.Serialize(new
                        {
                            success = true,
                            phase = GamePhase.EquipmentSelection,
                            transitionedFrom = "dialogue",
                            state = JObject.Parse(StateSerializer.GetEquipmentSelectionStateJson())
                        });
                    }

                    if (StateSerializer.IsInSkillSelection())
                    {
                        return JsonConfig.Serialize(new
                        {
                            success = true,
                            phase = GamePhase.SkillSelection,
                            transitionedFrom = "dialogue"
                        });
                    }

                    return JsonConfig.Serialize(new
                    {
                        success = true,
                        phase = GamePhase.Exploration,
                        dialogueComplete = true
                    });
                }

                System.Threading.Thread.Sleep(200);
                Plugin.Log.LogInfo($"ExecuteDialogueChoice: About to call AutoProgressDialogue");
                AutoProgressDialogue();

                if (StateSerializer.IsInEquipmentSelection())
                {
                    return JsonConfig.Serialize(new
                    {
                        success = true,
                        phase = GamePhase.EquipmentSelection,
                        transitionedFrom = "dialogue",
                        state = JObject.Parse(StateSerializer.GetEquipmentSelectionStateJson())
                    });
                }

                if (StateSerializer.IsInSkillSelection())
                {
                    return JsonConfig.Serialize(new
                    {
                        success = true,
                        phase = GamePhase.SkillSelection,
                        transitionedFrom = "dialogue"
                    });
                }

                if (!IsDialogueOpen())
                {
                    return JsonConfig.Serialize(new
                    {
                        success = true,
                        phase = GamePhase.Exploration,
                        dialogueComplete = true
                    });
                }

                return StateSerializer.GetDialogueStateJson();
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"ExecuteDialogueChoice: Exception - {ex.Message}\n{ex.StackTrace}");
                return JsonConfig.Serialize(new
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

        public static bool IsMerchantMenuOpen()
        {
            return GetMerchantMenu()?.IsOpen ?? false;
        }

        public static string ExecuteMerchantClose()
        {
            if (!IsMerchantMenuOpen())
                return JsonConfig.Error("Merchant menu not open");

            Plugin.Log.LogInfo("ExecuteMerchantClose: Closing merchant menu");

            UIController.Instance.SetMerchantMenuVisibility(visible: false);

            var startTime = DateTime.Now;
            while (IsMerchantMenuOpen() && !TimedOut(startTime, 2000))
            {
                System.Threading.Thread.Sleep(50);
            }

            System.Threading.Thread.Sleep(200);

            return JsonConfig.Serialize(new
            {
                success = true,
                phase = GamePhase.Exploration
            });
        }

        public static string ExecuteMerchantChoice(int choiceIndex)
        {
            if (!IsMerchantMenuOpen())
                return JsonConfig.Error("Merchant menu not open");

            var merchant = MerchantInteractable.currentMerchant;
            if (merchant == null)
                return JsonConfig.Error("No active merchant");

            var stockedItems = merchant.StockedItems;

            // Check if this is the "close" pseudo-choice (last index)
            if (choiceIndex == stockedItems.Count)
            {
                Plugin.Log.LogInfo("ExecuteMerchantChoice: Close shop selected");
                return ExecuteMerchantClose();
            }

            // Otherwise treat as a buy action
            if (choiceIndex < 0 || choiceIndex >= stockedItems.Count)
                return JsonConfig.Error($"Invalid choice index: {choiceIndex}. Valid range: 0-{stockedItems.Count} (last is 'Leave Shop')");

            var shopItem = stockedItems[choiceIndex];

            if (!merchant.CanBuyItem(shopItem, 1))
            {
                return JsonConfig.Serialize(new
                {
                    success = false,
                    error = "Cannot afford this item",
                    price = shopItem.Price,
                    gold = InventoryManager.Instance?.Gold ?? 0
                });
            }

            var itemName = shopItem.GetName();
            var cost = shopItem.Price;

            Plugin.Log.LogInfo($"ExecuteMerchantChoice: Purchasing {itemName} for {cost} gold");

            merchant.BuyItem(shopItem, 1);

            if (shopItem.ItemType == ShopItemType.Exp)
            {
                Plugin.Log.LogInfo("ExecuteMerchantChoice: EXP purchase - waiting for distribution to complete");
                var startTime = DateTime.Now;

                while (!TimedOut(startTime, 5000))
                {
                    System.Threading.Thread.Sleep(100);

                    if (IsMerchantMenuOpen())
                    {
                        Plugin.Log.LogInfo("ExecuteMerchantChoice: Back to merchant menu");
                        break;
                    }
                }
            }

            if (shopItem.ItemType == ShopItemType.Equipment)
            {
                Plugin.Log.LogInfo("ExecuteMerchantChoice: Equipment purchase - waiting for equipment selection");
                var startTime = DateTime.Now;

                while (!TimedOut(startTime, 3000))
                {
                    System.Threading.Thread.Sleep(100);

                    if (StateSerializer.IsInEquipmentSelection())
                    {
                        Plugin.Log.LogInfo("ExecuteMerchantChoice: Equipment selection menu opened");
                        return StateSerializer.GetEquipmentSelectionStateJson();
                    }
                }
            }

            System.Threading.Thread.Sleep(200);

            return StateSerializer.GetMerchantStateJson();
        }

        // =====================================================
        // RUN START AND DIFFICULTY SELECTION
        // =====================================================

        public static string ExecuteDifficultyChoice(int choiceIndex)
        {
            if (!StateSerializer.IsInDifficultySelection())
                return JsonConfig.Error("Not in difficulty selection screen");

            var menu = UIController.Instance?.DifficultySelectMenu;
            if (menu == null || !menu.IsOpen)
                return JsonConfig.Error("Difficulty selection menu not available");

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
                    return JsonConfig.Error($"Invalid difficulty index: {choiceIndex}. Valid: 0=Normal, 1=Heroic, 2=Mythic");
            }

            var maxUnlocked = ProgressManager.Instance?.UnlockedDifficulty ?? 1;
            if ((int)targetDifficulty > maxUnlocked)
            {
                return JsonConfig.Serialize(new
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
                        return JsonConfig.Serialize(new
                        {
                            success = true,
                            action = "difficulty_selected",
                            difficulty = targetDifficulty,
                            phase = GamePhase.MonsterSelection,
                            state = JObject.Parse(StateSerializer.GetMonsterSelectionStateJson())
                        });
                    }
                }

                return JsonConfig.Error("Timeout waiting for monster selection after difficulty selection");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"ExecuteDifficultyChoice: Exception - {ex.Message}\n{ex.StackTrace}");
                return JsonConfig.Error($"Exception during difficulty selection: {ex.Message}");
            }
        }

        // =====================================================
        // AETHER SPRING INTERACTION
        // =====================================================

        public static string ExecuteAetherSpringChoice(int choiceIndex)
        {
            if (!StateSerializer.IsInAetherSpringMenu())
                return JsonConfig.Error("No aether spring menu open");

            if (choiceIndex < 0 || choiceIndex > 1)
                return JsonConfig.Error($"Invalid choice index: {choiceIndex}. Must be 0 or 1");

            var menu = GetAetherSpringMenu();
            if (menu == null)
                return JsonConfig.Error("Aether spring menu not available");

            Plugin.Log.LogInfo($"ExecuteAetherSpringChoice: Selecting boon at index {choiceIndex}");

            Plugin.RunOnMainThreadAndWait(() =>
            {
                // Navigate to the correct selection (0 = left, 1 = right)
                if (choiceIndex == 0)
                    menu.OnGoLeft();
                else
                    menu.OnGoRight();

                // Confirm the selection
                menu.OnConfirm();
            });

            var startTime = DateTime.Now;
            while (StateSerializer.IsInAetherSpringMenu() && !TimedOut(startTime, 3000))
            {
                System.Threading.Thread.Sleep(50);
            }

            System.Threading.Thread.Sleep(300);

            return JsonConfig.Serialize(new
            {
                success = true,
                action = "boon_selected",
                choiceIndex = choiceIndex,
                phase = GamePhase.Exploration
            });
        }

    }
}
