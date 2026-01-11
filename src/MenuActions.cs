using System;
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

            while (true)
            {
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
                    return JsonConfig.Error("Timeout waiting for PostCombatMenu to open", new { phase = GamePhase.Timeout });

                System.Threading.Thread.Sleep(100);
            }

            return ProcessPostCombatStates(startTime, timeoutMs);
        }

        private static string ProcessPostCombatStates(DateTime startTime, int timeoutMs)
        {
            while (true)
            {
                if (TimedOut(startTime, timeoutMs))
                    return JsonConfig.Error("Timeout waiting for post-combat processing", new { phase = GamePhase.Timeout });

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

                System.Threading.Thread.Sleep(100);
            }
        }

        /// <summary>
        /// Check if all worthiness UIs can continue. MUST be called on main thread.
        /// </summary>
        private static bool CheckWorthinessCanContinue(PostCombatMenu postCombatMenu)
        {
            return CheckAllMonsterInfosCanContinue(postCombatMenu, info => info.WorthinessUI.CanContinue);
        }

        /// <summary>
        /// Check if all level up UIs can continue. MUST be called on main thread.
        /// </summary>
        private static bool CheckLevelUpCanContinue(PostCombatMenu postCombatMenu)
        {
            return CheckAllMonsterInfosCanContinue(postCombatMenu, info => info.LevelUpUI.CanContinue);
        }

        private static bool CheckAllMonsterInfosCanContinue(
            PostCombatMenu postCombatMenu,
            System.Func<PostCombatMonsterInfo, bool> canContinueCheck)
        {
            foreach (var info in postCombatMenu.PostCombatMonsterInfos)
            {
                if (info.monster != null && info.gameObject.activeSelf && !canContinueCheck(info))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Wait until a condition is met or timeout. Polls on HTTP thread. Returns true if condition was met.
        /// </summary>
        private static bool WaitUntil(System.Func<bool> condition, int timeoutMs, int pollIntervalMs = 100)
        {
            var startTime = DateTime.Now;
            while (!TimedOut(startTime, timeoutMs))
            {
                if (condition())
                    return true;
                System.Threading.Thread.Sleep(pollIntervalMs);
            }
            return false;
        }

        /// <summary>
        /// Wait until a condition is met or timeout, checking on main thread. Returns true if condition was met.
        /// </summary>
        private static bool WaitUntilOnMainThread(System.Func<bool> condition, int timeoutMs, int pollIntervalMs = 100)
        {
            var startTime = DateTime.Now;
            while (!TimedOut(startTime, timeoutMs))
            {
                bool result = false;
                Plugin.RunOnMainThreadAndWait(() => result = condition());
                if (result)
                    return true;
                System.Threading.Thread.Sleep(pollIntervalMs);
            }
            return false;
        }

        private static bool IsPostCombatMenuOpen()
        {
            return UIController.Instance?.PostCombatMenu?.IsOpen ?? false;
        }

        private static (EMonsterShift shift, string error) ParseShiftParameter(string shift)
        {
            if (string.IsNullOrEmpty(shift) || shift.Equals("normal", StringComparison.OrdinalIgnoreCase))
                return (EMonsterShift.Normal, null);

            if (shift.Equals("shifted", StringComparison.OrdinalIgnoreCase))
                return (EMonsterShift.Shifted, null);

            return (EMonsterShift.Normal, JsonConfig.Error($"Invalid shift value: '{shift}'. Use 'normal' or 'shifted'."));
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
            GetEndOfRunMenu().Close();
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
            int remainingMs = timeoutMs - (int)(DateTime.Now - startTime).TotalMilliseconds;
            return remainingMs > 0 && WaitUntilOnMainThread(StateSerializer.IsInSkillSelection, remainingMs);
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

            if (choiceIndex == -1)
                return HandleSkillReroll(menuList, skillSelectMenu);

            if (choiceIndex == 3)
                return HandleMaxHealthBonus(menuList, skillSelectMenu);

            if (choiceIndex >= 0 && choiceIndex <= 2)
            {
                menuList.SelectByIndex(choiceIndex);
                TriggerMenuConfirm(menuList);
                System.Threading.Thread.Sleep(500);
                return WaitForPostCombatComplete();
            }

            return JsonConfig.Error($"Invalid choice index: {choiceIndex}. Use 0-2 for skills, 3 for max health, -1 for reroll.");
        }

        private static string HandleSkillReroll(MenuList menuList, SkillSelectMenu skillSelectMenu)
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

        private static string HandleMaxHealthBonus(MenuList menuList, SkillSelectMenu skillSelectMenu)
        {
            int bonusIndex = FindMenuItemIndex(menuList, skillSelectMenu.AlternativeBonusButton);
            if (bonusIndex == -1)
                return JsonConfig.Error("Max health option not available");

            menuList.SelectByIndex(bonusIndex);
            TriggerMenuConfirm(menuList);
            System.Threading.Thread.Sleep(500);
            return WaitForPostCombatComplete();
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
            var (choiceIndex, error) = ResolveChoiceName(choiceName);
            if (error != null)
                return JsonConfig.Error(error);

            if (StateSerializer.IsInSkillSelection())
                return ExecuteSkillSelectionChoice(choiceIndex);

            if (StateSerializer.IsInEquipmentSelection())
                return ExecuteEquipmentChoice(choiceIndex);

            if (IsMerchantMenuOpen())
                return ExecuteMerchantChoice(choiceIndex);

            if (StateSerializer.IsInDifficultySelection())
                return ExecuteDifficultyChoice(choiceIndex);

            if (StateSerializer.IsInMonsterSelection())
                return ExecuteMonsterSelectionChoice(choiceIndex, shift);

            if (StateSerializer.IsInAetherSpringMenu())
                return ExecuteAetherSpringChoice(choiceIndex);

            if (IsDialogueOpen())
                return ExecuteDialogueChoice(choiceIndex);

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

            int totalCount = displayedMonsters.Count + (selection.HasRandomMonster ? 1 : 0);
            var indexError = ValidateChoiceIndex(choiceIndex, totalCount, "monster");
            if (choiceIndex != -1 && indexError != null)
                return indexError;

            if (choiceIndex == -1)
                return HandleMonsterReroll(menu);

            var isRandom = selection.HasRandomMonster && choiceIndex == selection.RandomMonsterPosition;
            int adjustedIndex = isRandom ? choiceIndex
                : (selection.HasRandomMonster && choiceIndex > selection.RandomMonsterPosition ? choiceIndex - 1 : choiceIndex);

            string monsterName = isRandom ? "Random Monster" : displayedMonsters[adjustedIndex].Name;
            Monster selectedMonster = isRandom ? null : displayedMonsters[adjustedIndex];

            var (targetShift, shiftError) = ParseShiftParameter(shift);
            if (shiftError != null)
                return shiftError;

            if (targetShift == EMonsterShift.Shifted && !isRandom &&
                !InventoryManager.Instance.HasShiftedMementosOfMonster(selectedMonster))
            {
                return JsonConfig.Error($"Shifted variant not available for {monsterName}");
            }

            try
            {
                Plugin.RunOnMainThreadAndWait(() =>
                {
                    selection.SetSelectedIndex(adjustedIndex + 1);

                    if (!isRandom && targetShift != selection.CurrentShift)
                    {
                        selection.CurrentShift = targetShift;
                        selectedMonster.SetShift(targetShift);
                    }

                    if (ConfirmSelectionMethod != null)
                        ConfirmSelectionMethod.Invoke(menu, null);
                    else
                        menu.OnConfirm();
                });

                System.Threading.Thread.Sleep(500);

                return WaitForMonsterSelectionResult(monsterName, targetShift, isRandom);
            }
            catch (System.Exception ex)
            {
                return JsonConfig.Error($"Exception during monster selection: {ex.Message}");
            }
        }

        private static string WaitForMonsterSelectionResult(string monsterName, EMonsterShift targetShift, bool isRandom)
        {
            var startTime = DateTime.Now;
            while (!TimedOut(startTime, 3000))
            {
                if (StateSerializer.IsInEquipmentSelection())
                {
                    return BuildMonsterSelectedResult(monsterName, targetShift, isRandom,
                        GamePhase.EquipmentSelection, "Select which monster to replace");
                }

                if (StateSerializer.IsInSkillSelection())
                {
                    return BuildMonsterSelectedResult(monsterName, targetShift, isRandom,
                        GamePhase.SkillSelection, "Monster rebirthed with XP - select skill");
                }

                if (!StateSerializer.IsInMonsterSelection())
                    break;

                System.Threading.Thread.Sleep(100);
            }

            if (StateSerializer.IsInSkillSelection())
                return StateSerializer.GetSkillSelectionStateJson();

            return BuildMonsterSelectedResult(monsterName, targetShift, isRandom, GamePhase.Exploration, null);
        }

        private static string BuildMonsterSelectedResult(
            string monsterName,
            EMonsterShift targetShift,
            bool isRandom,
            GamePhase phase,
            string note)
        {
            var result = new JObject
            {
                ["success"] = true,
                ["action"] = "monster_selected",
                ["selected"] = monsterName,
                ["shift"] = targetShift.ToString(),
                ["isRandom"] = isRandom,
                ["phase"] = phase.ToString()
            };

            if (note != null)
                result["note"] = note;

            return result.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string HandleMonsterReroll(MonsterShrineMenu menu)
        {
            if (InventoryManager.Instance.ShrineRerolls <= 0)
                return JsonConfig.Error("No shrine rerolls available");

            if (menu.ShrineSelectionState != EShrineState.NormalShrineSelection)
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

        // =====================================================
        // EQUIPMENT SELECTION
        // =====================================================

        public static string ExecuteEquipmentChoice(int choiceIndex)
        {
            if (!StateSerializer.IsInEquipmentSelection())
                return JsonConfig.Error("Equipment selection menu not open");

            var menu = UIController.Instance.MonsterSelectMenu;
            var party = MonsterManager.Instance.Active;

            if (choiceIndex == party.Count)
                return ExecuteEquipmentScrap(menu);

            return ExecuteEquipmentAssign(menu, party[choiceIndex], choiceIndex);
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

            // Close the popup that appears after scrapping
            if (WaitUntil(() => PopupController.Instance.IsOpen, 2000, 50))
                Plugin.RunOnMainThreadAndWait(() => PopupController.Instance.Close());

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

            if (options == null || options.Length == 0)
            {
                Plugin.RunOnMainThreadAndWait(() => display.OnConfirm(isMouseClick: false));
                System.Threading.Thread.Sleep(200);

                if (!IsDialogueOpen())
                    return JsonConfig.Serialize(new { success = true, phase = GamePhase.Exploration, dialogueComplete = true });

                return StateSerializer.GetDialogueStateJson();
            }

            bool isEndAndForceSkip = false;

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
                dialogueInteractable.SelectDialogueOption(choiceIndex, options.Length, out bool isEnd, out bool forceSkip);

                isEndAndForceSkip = isEnd && forceSkip;
                if (isEndAndForceSkip)
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

            System.Threading.Thread.Sleep(isEndAndForceSkip ? 300 : 200);
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
            WaitUntil(() => !IsMerchantMenuOpen(), 2000, 50);
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
                WaitUntil(IsMerchantMenuOpen, 5000);
            }

            if (shopItem.ItemType == ShopItemType.Equipment)
            {
                if (WaitUntil(StateSerializer.IsInEquipmentSelection, 3000))
                    return StateSerializer.GetEquipmentSelectionStateJson();
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
                {
                    popup.ConfirmMenu.SelectByIndex(0);
                    InputConfirmMethod.Invoke(popup.ConfirmMenu, null);
                }
            });

            if (!WaitUntil(StateSerializer.IsInMonsterSelection, 5000))
                return JsonConfig.Error("Timeout waiting for monster selection after difficulty selection");

            return JsonConfig.Serialize(new
            {
                success = true,
                action = "difficulty_selected",
                difficulty = targetDifficulty,
                phase = GamePhase.MonsterSelection,
                state = JObject.Parse(StateSerializer.GetMonsterSelectionStateJson())
            });
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
                if (choiceIndex == 0)
                    menu.OnGoLeft();
                else
                    menu.OnGoRight();

                menu.OnConfirm();
            });

            WaitUntil(() => !StateSerializer.IsInAetherSpringMenu(), 3000, 50);
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
