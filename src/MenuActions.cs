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
                    return JsonConfig.Error("Timeout waiting for PostCombatMenu to open", new { phase = GamePhase.Timeout });
                }

                // Sleep on HTTP thread - doesn't block Unity
                System.Threading.Thread.Sleep(100);
            }

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

                    var postCombatMenu = UIController.Instance.PostCombatMenu;
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
                    return skillSelectionResult;
                }

                if (inEndOfRun)
                {
                    Plugin.RunOnMainThreadAndWait(() => AutoContinueFromEndOfRun());
                    System.Threading.Thread.Sleep(500);
                    continue;
                }

                if (inExploration)
                {
                    return explorationResult;
                }

                switch (currentState)
                {
                    case PostCombatMenu.EPostCombatMenuState.WorthinessUI:
                    case PostCombatMenu.EPostCombatMenuState.WorthinessUIDetailed:
                        if (worthinessCanContinue)
                        {
                            Plugin.RunOnMainThreadAndWait(() => TriggerContinue(UIController.Instance.PostCombatMenu));
                            System.Threading.Thread.Sleep(200);
                        }
                        break;

                    case PostCombatMenu.EPostCombatMenuState.LevelUpUI:
                        if (levelUpCanContinue)
                        {
                            if (hasPendingLevelUp)
                            {
                                Plugin.RunOnMainThreadAndWait(() =>
                                {
                                    var pendingLevelUp = FindFirstPendingLevelUp(UIController.Instance.PostCombatMenu);
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
                                Plugin.RunOnMainThreadAndWait(() => TriggerContinue(UIController.Instance.PostCombatMenu));
                                System.Threading.Thread.Sleep(200);
                            }
                        }
                        break;

                    case PostCombatMenu.EPostCombatMenuState.SkillSelectionUI:
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
            bool isVictory = menu.VictoryBanner.activeSelf;
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
            var postCombatMenu = UIController.Instance.PostCombatMenu;
            postCombatMenu.MenuList.SelectByIndex(0);
            TriggerMenuConfirm(postCombatMenu.MenuList);
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
            var menuList = skillSelectMenu.MenuList;

            // Handle reroll (choiceIndex == -1)
            if (choiceIndex == -1)
            {
                if (InventoryManager.Instance.SkillRerolls <= 0)
                    return JsonConfig.Error("No skill rerolls available");

                int rerollIndex = FindMenuItemIndex(menuList, skillSelectMenu.RerollSkillsButton);
                if (rerollIndex == -1)
                    return JsonConfig.Error("Could not find reroll button");

                menuList.SelectByIndex(rerollIndex);
                TriggerMenuConfirm(menuList);
                System.Threading.Thread.Sleep(500);
                return StateSerializer.GetSkillSelectionStateJson();
            }

            // Handle max health bonus (choiceIndex == 3)
            if (choiceIndex == 3)
            {
                int bonusIndex = FindMenuItemIndex(menuList, skillSelectMenu.AlternativeBonusButton);
                if (bonusIndex == -1)
                    return JsonConfig.Error("Max health option not available");

                menuList.SelectByIndex(bonusIndex);
                TriggerMenuConfirm(menuList);
                System.Threading.Thread.Sleep(500);
                return WaitForPostCombatComplete();
            }

            // Handle skill selection (choiceIndex 0-2)
            if (choiceIndex >= 0 && choiceIndex <= 2)
            {
                menuList.SelectByIndex(choiceIndex);
                TriggerMenuConfirm(menuList);
                System.Threading.Thread.Sleep(500);
                return WaitForPostCombatComplete();
            }

            return JsonConfig.Error($"Invalid choice index: {choiceIndex}. Use 0-2 for skills, 3 for max health, -1 for reroll.");
        }

        private static int FindMenuItemIndex(MenuList menuList, UnityEngine.MonoBehaviour targetItem)
        {
            for (int i = 0; i < menuList.List.Count; i++)
            {
                if (menuList.List[i] == targetItem)
                    return i;
            }
            return -1;
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
                return ExecuteSkillSelectionChoice(choiceIndex);
            }

            // Check equipment selection first (after picking equipment from dialogue/loot)
            if (StateSerializer.IsInEquipmentSelection())
            {
                return ExecuteEquipmentChoice(choiceIndex);
            }

            // Check merchant menu
            if (IsMerchantMenuOpen())
            {
                return ExecuteMerchantChoice(choiceIndex);
            }

            // Check difficulty selection (run start)
            if (StateSerializer.IsInDifficultySelection())
            {
                return ExecuteDifficultyChoice(choiceIndex);
            }

            // Check monster selection (shrine/starter)
            if (StateSerializer.IsInMonsterSelection())
            {
                return ExecuteMonsterSelectionChoice(choiceIndex, shift);
            }

            // Check aether spring menu
            if (StateSerializer.IsInAetherSpringMenu())
            {
                return ExecuteAetherSpringChoice(choiceIndex);
            }

            // Check dialogue
            if (IsDialogueOpen())
            {
                return ExecuteDialogueChoice(choiceIndex);
            }

            return JsonConfig.Error("No active choice context (not in dialogue, equipment selection, difficulty selection, merchant, or monster selection)");
        }

        // =====================================================
        // MONSTER SELECTION (Shrine/Starter)
        // =====================================================

        public static string ExecuteMonsterSelectionChoice(int choiceIndex, string shift = null)
        {
            if (!StateSerializer.IsInMonsterSelection())
                return JsonConfig.Error("Monster selection menu not open");

            var menu = UIController.Instance.MonsterShrineMenu;
            var selection = menu.MonsterSelection;
            var displayedMonsters = menu.DisplayedMonsters;

            // Validate choice index (including random option)
            int totalCount = displayedMonsters.Count + (selection.HasRandomMonster ? 1 : 0);
            var indexError = ValidateChoiceIndex(choiceIndex, totalCount, "monster");
            if (choiceIndex != -1 && indexError != null)
                return indexError;

            // Handle reroll (choiceIndex == -1)
            if (choiceIndex == -1)
            {
                var shrineRerolls = InventoryManager.Instance.ShrineRerolls;
                var shrineState = menu.ShrineSelectionState;

                if (shrineRerolls <= 0)
                    return JsonConfig.Error("No shrine rerolls available");

                if (shrineState != EShrineState.NormalShrineSelection)
                    return JsonConfig.Error("Reroll not available for this shrine type");

                Plugin.RunOnMainThreadAndWait(() =>
                {
                    InventoryManager.Instance.RemoveShrineReroll();
                    var shrineTrigger = LevelGenerator.Instance.Map.MonsterShrine as MonsterShrineTrigger;
                    shrineTrigger.GenerateMementosForShrine(ignoreHasData: true, isReroll: true);
                });

                System.Threading.Thread.Sleep(500);
                return StateSerializer.GetMonsterSelectionStateJson();
            }


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
                monsterName = selectedMonster.Name;
            }

            // Parse shift parameter
            EMonsterShift targetShift = EMonsterShift.Normal;
            if (!string.IsNullOrEmpty(shift))
            {
                if (shift.Equals("shifted", System.StringComparison.OrdinalIgnoreCase))
                    targetShift = EMonsterShift.Shifted;
                else if (!shift.Equals("normal", System.StringComparison.OrdinalIgnoreCase))
                    return JsonConfig.Error($"Invalid shift value: '{shift}'. Use 'normal' or 'shifted'.");
            }

            // Validate shifted variant is available if requested
            if (targetShift == EMonsterShift.Shifted && !isRandom)
            {
                bool hasShiftedVariant = InventoryManager.Instance.HasShiftedMementosOfMonster(selectedMonster);
                if (!hasShiftedVariant)
                    return JsonConfig.Error($"Shifted variant not available for {monsterName}");
            }

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
                    }

                    // Auto-confirm via reflection (skip confirmation popup)
                    if (ConfirmSelectionMethod != null)
                    {
                        ConfirmSelectionMethod.Invoke(menu, null);
                    }
                    else
                    {
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
                return JsonConfig.Error($"Exception during monster selection: {ex.Message}");
            }
        }

        // =====================================================
        // EQUIPMENT SELECTION
        // =====================================================

        public static string ExecuteEquipmentChoice(int choiceIndex)
        {
            if (!StateSerializer.IsInEquipmentSelection())
                return JsonConfig.Error("Equipment selection menu not open");

            var menu = UIController.Instance.MonsterSelectMenu;
            var party = MonsterManager.Instance.Active;
            int scrapIndex = party.Count;

            // Handle scrap
            if (choiceIndex == scrapIndex)
            {
                return ExecuteEquipmentScrap(menu);
            }

            // Handle assign to monster
            var targetMonster = party[choiceIndex];
            return ExecuteEquipmentAssign(menu, targetMonster, choiceIndex);
        }

        private static string ExecuteEquipmentAssign(MonsterSelectMenu menu, Monster targetMonster, int monsterIndex)
        {
            var newEquipment = menu.NewEquipmentInstance;
            var prevEquipment = targetMonster.Equipment?.Equipment;
            var newEquipName = newEquipment.Equipment.GetName();

            Plugin.RunOnMainThreadAndWait(() =>
            {
                menu.MenuList.SelectByIndex(monsterIndex);
                InputConfirmMethod.Invoke(menu.MenuList, null);
            });

            System.Threading.Thread.Sleep(600);

            // Check if we're still in equipment selection (happens when monster had equipment - trade scenario)
            if (StateSerializer.IsInEquipmentSelection())
            {
                var prevEquipName = prevEquipment?.Equipment?.GetName() ?? "Unknown";
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

            return JsonConfig.Serialize(new
            {
                success = true,
                action = "equipment_assigned",
                assignedTo = targetMonster.Name,
                equipment = newEquipName,
                phase = GamePhase.Exploration
            });
        }

        private static string ExecuteEquipmentScrap(MonsterSelectMenu menu)
        {
            var equipment = menu.NewEquipmentInstance;
            var equipName = equipment.Equipment.GetName();
            var scrapValue = equipment.Equipment.GetScrapGoldGain();

            Plugin.RunOnMainThreadAndWait(() =>
            {
                var scrapIndex = menu.MenuList.List.Count - 1;
                menu.MenuList.SelectByIndex(scrapIndex);
                InputConfirmMethod.Invoke(menu.MenuList, null);
            });

            System.Threading.Thread.Sleep(300);

            // The game shows a popup after scrapping - we need to close it
            var startTime = DateTime.Now;
            while ((DateTime.Now - startTime).TotalMilliseconds < 2000)
            {
                if (PopupController.Instance.IsOpen)
                {
                    Plugin.RunOnMainThreadAndWait(() => PopupController.Instance.Close());
                    break;
                }
                System.Threading.Thread.Sleep(50);
            }

            System.Threading.Thread.Sleep(300);

            return JsonConfig.Serialize(new
            {
                success = true,
                action = "equipment_scrapped",
                equipment = equipName,
                goldGained = scrapValue,
                goldTotal = InventoryManager.Instance.Gold,
                phase = GamePhase.Exploration
            });
        }

        // =====================================================
        // NPC DIALOGUE INTERACTION
        // =====================================================

        public static bool IsDialogueOpen()
        {
            return UIController.Instance?.DialogueDisplay?.IsOpen ?? false;
        }

        /// <summary>
        /// NPC interaction - now uses unified TeleportAndInteract.
        /// </summary>
        public static string ExecuteNpcInteract(string npcName)
        {
            if (GameStateManager.Instance.IsCombat)
                return JsonConfig.Error("Cannot interact with NPCs during combat");

            if (IsDialogueOpen())
                return JsonConfig.Error("Dialogue already open");

            return TeleportAndInteract(npcName);
        }

        public static string ExecuteDialogueChoice(int choiceIndex)
        {
            if (!IsDialogueOpen())
                return JsonConfig.Error("No dialogue open");

            var dialogueInteractable = GetCurrentDialogue();
            var dialogueData = GetCurrentDialogueData();
            var display = UIController.Instance.DialogueDisplay;
            var options = dialogueData.DialogueOptions;

            // Handle text-only dialogue (no options) - just advance
            // The caller can send any value for choiceIndex here, it ignores the parameter. This behavior is UNDOCUMENTED
            if (options == null || options.Length == 0)
            {
                Plugin.RunOnMainThreadAndWait(() => display.OnConfirm(isMouseClick: false));
                System.Threading.Thread.Sleep(200);

                if (!IsDialogueOpen())
                    return JsonConfig.Serialize(new { success = true, phase = GamePhase.Exploration, dialogueComplete = true });

                return StateSerializer.GetDialogueStateJson();
            }

            var selectedOptionText = options[choiceIndex];

            bool isEnd = false;
            bool forceSkip = false;

            Plugin.RunOnMainThreadAndWait(() =>
            {
                var characterDisplay = dialogueData.LeftIsSpeaking
                    ? display.LeftCharacterDisplay
                    : display.RightCharacterDisplay;

                if (dialogueData.IsChoiceEvent)
                    characterDisplay.ChoiceEventOptions.SelectByIndex(choiceIndex);
                else
                    characterDisplay.DialogOptions.SelectByIndex(choiceIndex);

                dialogueInteractable.TriggerNodeOnCloseEvents();
                dialogueInteractable.SelectDialogueOption(choiceIndex, options.Length, out isEnd, out forceSkip);

                if (isEnd && forceSkip)
                {
                    UIController.Instance.SetDialogueVisibility(visible: false);
                }
                else if (IsDialogueOpen())
                {
                    var nextDialogue = dialogueInteractable.GetNextDialogue();
                    if (nextDialogue != null)
                        display.ShowDialogue(nextDialogue);
                }
            });

            if (isEnd && forceSkip)
            {
                System.Threading.Thread.Sleep(300);
                return GetPostDialogueState();
            }

            System.Threading.Thread.Sleep(200);
            return GetPostDialogueState();
        }

        private static string GetPostDialogueState()
        {
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
            var stockedItems = merchant.StockedItems;

            // Check if this is the "close" pseudo-choice (last index, inclusive)
            if (choiceIndex == stockedItems.Count)
            {
                return ExecuteMerchantClose();
            }

            var shopItem = stockedItems[choiceIndex];

            if (!merchant.CanBuyItem(shopItem, 1))
            {
                return JsonConfig.Serialize(new
                {
                    success = false,
                    error = "Cannot afford this item",
                    price = shopItem.Price,
                    gold = InventoryManager.Instance.Gold
                });
            }

            merchant.BuyItem(shopItem, 1);

            if (shopItem.ItemType == ShopItemType.Exp)
            {
                var startTime = DateTime.Now;

                while (!TimedOut(startTime, 5000))
                {
                    System.Threading.Thread.Sleep(100);
                    if (IsMerchantMenuOpen())
                    {
                        break;
                    }
                }
            }

            if (shopItem.ItemType == ShopItemType.Equipment)
            {
                var startTime = DateTime.Now;

                while (!TimedOut(startTime, 3000))
                {
                    System.Threading.Thread.Sleep(100);
                    if (StateSerializer.IsInEquipmentSelection())
                    {
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

            var menu = UIController.Instance.DifficultySelectMenu;

            EDifficulty targetDifficulty = choiceIndex switch
            {
                0 => EDifficulty.Normal,
                1 => EDifficulty.Heroic,
                2 => EDifficulty.Mythic,
                _ => EDifficulty.Normal
            };

            var maxUnlocked = ProgressManager.Instance.UnlockedDifficulty;
            if ((int)targetDifficulty > maxUnlocked)
            {
                return JsonConfig.Serialize(new
                {
                    success = false,
                    error = $"Difficulty '{targetDifficulty}' is not unlocked. Max unlocked level: {maxUnlocked}"
                });
            }

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
                if (popup.IsOpen)
                {                    popup.ConfirmMenu.SelectByIndex(0);
                    InputConfirmMethod.Invoke(popup.ConfirmMenu, null);
                }
            });

            var startTime = DateTime.Now;
            while (!TimedOut(startTime, 5000))
            {
                System.Threading.Thread.Sleep(100);

                if (StateSerializer.IsInMonsterSelection())
                {
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

        // =====================================================
        // AETHER SPRING INTERACTION
        // =====================================================

        public static string ExecuteAetherSpringChoice(int choiceIndex)
        {
            if (!StateSerializer.IsInAetherSpringMenu())
                return JsonConfig.Error("No aether spring menu open");

            var menu = GetAetherSpringMenu();
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
