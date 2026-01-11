using System;

namespace AethermancerHarness
{
    /// <summary>
    /// Unified readiness detection - determines when the game is ready to accept player input.
    /// </summary>
    public static partial class ActionHandler
    {
        // =====================================================
        // UNIFIED READINESS DETECTION
        // =====================================================

        /// <summary>
        /// Simple boolean check - is the game ready for input in any phase?
        /// </summary>
        public static bool IsReady()
        {
            return GetReadinessState().Ready;
        }

        /// <summary>
        /// Get comprehensive readiness state with detailed information about why
        /// the game is or isn't ready for input.
        /// </summary>
        public static ReadinessState GetReadinessState()
        {
            var state = new ReadinessState
            {
                Details = new ReadinessDetails()
            };

            try
            {
                // Global state checks
                var gameStateManager = GameStateManager.Instance;
                if (gameStateManager == null)
                {
                    state.Ready = false;
                    state.Phase = "Unknown";
                    state.BlockReason = "GameStateManager not available";
                    return state;
                }

                state.Details.GameState = gameStateManager.CurrentState.ToString();
                state.Details.IsPriorityLayerOpen = gameStateManager.IsPriorityLayerOpen;
                state.Details.IsPaused = gameStateManager.IsPaused;

                // Check for popup blocking (applies to all phases)
                var popupOpen = PopupController.Instance?.IsOpen ?? false;
                state.Details.PopupOpen = popupOpen;

                // Determine phase and check phase-specific readiness
                if (gameStateManager.IsCombat)
                {
                    return GetCombatReadiness(state);
                }

                if (StateSerializer.IsInDialogue())
                {
                    return GetDialogueReadiness(state);
                }

                if (StateSerializer.IsInSkillSelection())
                {
                    return GetMenuReadiness(state, "SkillSelection");
                }

                if (StateSerializer.IsInEquipmentSelection())
                {
                    return GetMenuReadiness(state, "EquipmentSelection");
                }

                if (StateSerializer.IsInMonsterSelection())
                {
                    return GetMenuReadiness(state, "MonsterSelection");
                }

                if (StateSerializer.IsInDifficultySelection())
                {
                    return GetMenuReadiness(state, "DifficultySelection");
                }

                if (StateSerializer.IsInMerchantMenu())
                {
                    return GetMenuReadiness(state, "Merchant");
                }

                if (StateSerializer.IsInAetherSpringMenu())
                {
                    return GetMenuReadiness(state, "AetherSpring");
                }

                if (StateSerializer.IsInEndOfRunMenu())
                {
                    return GetMenuReadiness(state, "EndOfRun");
                }

                if (StateSerializer.IsInPostCombatMenu())
                {
                    return GetPostCombatReadiness(state);
                }

                if (gameStateManager.IsExploring)
                {
                    return GetExplorationReadiness(state);
                }

                // Transition or unknown state
                state.Phase = "Transition";
                state.Ready = false;
                state.BlockReason = $"InTransition:{gameStateManager.CurrentState}";
                return state;
            }
            catch (Exception ex)
            {
                state.Ready = false;
                state.Phase = "Error";
                state.BlockReason = $"Exception:{ex.Message}";
                return state;
            }
        }

        // =====================================================
        // PHASE-SPECIFIC READINESS CHECKS
        // =====================================================

        private static ReadinessState GetCombatReadiness(ReadinessState state)
        {
            state.Phase = "Combat";

            var combatStateManager = CombatStateManager.Instance;
            var cc = CombatController.Instance;

            if (combatStateManager == null || cc == null)
            {
                state.Ready = false;
                state.BlockReason = "CombatController not available";
                return state;
            }

            // Combat state details
            var combatState = combatStateManager.State?.CurrentState?.ID;
            state.Details.CombatState = combatState?.ToString() ?? "Unknown";

            // Check if combat ended
            if (combatState >= CombatStateManager.EState.EndCombatTriggers)
            {
                state.Ready = false;
                state.BlockReason = $"CombatEnded:{combatState}";
                return state;
            }

            // Current monster details
            var currentMonster = cc.CurrentMonster;
            state.Details.CurrentMonster = currentMonster?.Name;
            state.Details.IsPlayerTurn = currentMonster?.BelongsToPlayer;

            if (currentMonster == null)
            {
                state.Ready = false;
                state.BlockReason = "NoCurrentMonster";
                return state;
            }

            if (!currentMonster.BelongsToPlayer)
            {
                state.Ready = false;
                state.BlockReason = $"EnemyTurn:{currentMonster.Name}";
                return state;
            }

            // Pending triggers
            var triggerCount = CombatTimeline.Instance?.TriggerStack?.Count ?? 0;
            state.Details.PendingTriggers = triggerCount;
            if (triggerCount > 0)
            {
                state.Ready = false;
                state.BlockReason = $"TriggersPending:{triggerCount}";
                return state;
            }

            // Action executing
            var actionInstance = currentMonster.State?.ActionInstance;
            state.Details.ExecutingAction = actionInstance?.Action?.Name;
            state.Details.IsMonsterAnimating = currentMonster.State?.IsInActionCycle ?? false;

            if (actionInstance != null)
            {
                state.Ready = false;
                state.BlockReason = $"ActionExecuting:{actionInstance.Action?.Name}";
                return state;
            }

            if (state.Details.IsMonsterAnimating == true)
            {
                state.Ready = false;
                state.BlockReason = "MonsterAnimating";
                return state;
            }

            // Combat state not idle
            if (combatState != CombatStateManager.EState.Idle)
            {
                state.Ready = false;
                state.BlockReason = $"CombatState:{combatState}";
                return state;
            }

            // All checks passed
            state.Ready = true;
            return state;
        }

        private static ReadinessState GetExplorationReadiness(ReadinessState state)
        {
            state.Phase = "Exploration";

            var playerMovement = PlayerMovementController.Instance;
            if (playerMovement == null)
            {
                state.Ready = false;
                state.BlockReason = "PlayerMovementController not available";
                return state;
            }

            // Movement state details
            state.Details.CanMove = playerMovement.CanMove;
            state.Details.IsTeleporting = playerMovement.IsTeleporting;
            state.Details.IsDashing = playerMovement.IsDashing;
            state.Details.IsFalling = playerMovement.IsFalling;
            state.Details.InCutscene = playerMovement.InCutSceneWithMovement;

            // Check blocking conditions
            if (state.Details.PopupOpen == true)
            {
                state.Ready = false;
                state.BlockReason = "PopupOpen";
                return state;
            }

            if (playerMovement.IsTeleporting)
            {
                state.Ready = false;
                state.BlockReason = "Teleporting";
                return state;
            }

            if (playerMovement.IsDashing)
            {
                state.Ready = false;
                state.BlockReason = "Dashing";
                return state;
            }

            if (playerMovement.IsFalling)
            {
                state.Ready = false;
                state.BlockReason = "Falling";
                return state;
            }

            if (playerMovement.InCutSceneWithMovement)
            {
                state.Ready = false;
                state.BlockReason = "InCutscene";
                return state;
            }

            if (!playerMovement.CanMove)
            {
                state.Ready = false;
                state.BlockReason = "CannotMove";
                return state;
            }

            // Check for priority state layers (e.g., menus)
            if (state.Details.IsPriorityLayerOpen)
            {
                state.Ready = false;
                state.BlockReason = "PriorityLayerOpen";
                return state;
            }

            // All checks passed
            state.Ready = true;
            return state;
        }

        private static ReadinessState GetDialogueReadiness(ReadinessState state)
        {
            state.Phase = "Dialogue";

            var dialogueDisplay = UIController.Instance?.DialogueDisplay;
            if (dialogueDisplay == null)
            {
                state.Ready = false;
                state.BlockReason = "DialogueDisplay not available";
                return state;
            }

            state.Details.DialogueOpen = dialogueDisplay.IsOpen;
            state.Details.DialogueInputAllowed = dialogueDisplay.InputAllowed;

            if (!dialogueDisplay.IsOpen)
            {
                state.Ready = false;
                state.BlockReason = "DialogueNotOpen";
                return state;
            }

            if (!dialogueDisplay.InputAllowed)
            {
                state.Ready = false;
                state.BlockReason = "DialogueInputNotAllowed";
                return state;
            }

            // Check for popup blocking dialogue input
            if (state.Details.PopupOpen == true)
            {
                state.Ready = false;
                state.BlockReason = "PopupBlockingDialogue";
                return state;
            }

            state.Ready = true;
            return state;
        }

        private static ReadinessState GetMenuReadiness(ReadinessState state, string menuType)
        {
            state.Phase = menuType;
            state.Details.ActiveMenu = menuType;

            // Check for popup blocking
            if (state.Details.PopupOpen == true)
            {
                state.Ready = false;
                state.BlockReason = $"PopupBlocking{menuType}";
                return state;
            }

            // Check menu-specific locked states where available
            MenuList menuList = null;
            switch (menuType)
            {
                case "SkillSelection":
                    menuList = UIController.Instance?.PostCombatMenu?.SkillSelectMenu?.MenuList;
                    break;
                case "Merchant":
                    menuList = GetMerchantMenu()?.MenuList;
                    break;
                // Note: MonsterSelection and DifficultySelection don't expose MenuList
                // They're considered ready if open and no popup is blocking
            }

            if (menuList != null)
            {
                state.Details.MenuLocked = menuList.IsLocked;
                if (menuList.IsLocked)
                {
                    state.Ready = false;
                    state.BlockReason = $"{menuType}MenuLocked";
                    return state;
                }
            }

            // Menu is open and not locked
            state.Ready = true;
            return state;
        }

        private static ReadinessState GetPostCombatReadiness(ReadinessState state)
        {
            state.Phase = "PostCombat";
            state.Details.ActiveMenu = "PostCombat";

            var postCombatMenu = UIController.Instance?.PostCombatMenu;
            if (postCombatMenu == null || !postCombatMenu.IsOpen)
            {
                state.Ready = false;
                state.BlockReason = "PostCombatMenuNotOpen";
                return state;
            }

            // Post-combat auto-advances, so it's "ready" when we can see the menu
            // But specific states may require waiting
            var currentState = postCombatMenu.CurrentState;

            switch (currentState)
            {
                case PostCombatMenu.EPostCombatMenuState.WorthinessUI:
                case PostCombatMenu.EPostCombatMenuState.WorthinessUIDetailed:
                    // Check if worthiness animations are done
                    bool worthinessReady = true;
                    foreach (var info in postCombatMenu.PostCombatMonsterInfos)
                    {
                        if (info.monster != null && info.gameObject.activeSelf && !info.WorthinessUI.CanContinue)
                        {
                            worthinessReady = false;
                            break;
                        }
                    }
                    if (!worthinessReady)
                    {
                        state.Ready = false;
                        state.BlockReason = "WorthinessAnimating";
                        return state;
                    }
                    break;

                case PostCombatMenu.EPostCombatMenuState.LevelUpUI:
                    // Check if level up animations are done
                    bool levelUpReady = true;
                    foreach (var info in postCombatMenu.PostCombatMonsterInfos)
                    {
                        if (info.monster != null && info.gameObject.activeSelf && !info.LevelUpUI.CanContinue)
                        {
                            levelUpReady = false;
                            break;
                        }
                    }
                    if (!levelUpReady)
                    {
                        state.Ready = false;
                        state.BlockReason = "LevelUpAnimating";
                        return state;
                    }
                    break;
            }

            state.Ready = true;
            return state;
        }

        // =====================================================
        // WAIT FOR READY
        // =====================================================

        /// <summary>
        /// Check if a BlockReason represents a terminal state (should exit wait early).
        /// Terminal states are conditions where waiting longer won't help.
        /// </summary>
        private static bool IsTerminalBlockReason(string blockReason)
        {
            if (string.IsNullOrEmpty(blockReason))
                return false;

            // Combat ending is terminal - waiting won't make it "ready" again
            if (blockReason.StartsWith("CombatEnded:"))
                return true;

            return false;
        }

        /// <summary>
        /// Wait until the game is ready for input, or a terminal state, or timeout.
        /// Returns true if ready or terminal state reached, false if timed out.
        /// Can be called from HTTP thread.
        /// </summary>
        public static bool WaitUntilReady(int timeoutMs = 5000, int pollIntervalMs = 50)
        {
            var state = WaitUntilReadyWithState(timeoutMs, pollIntervalMs);
            // Ready, or terminal (e.g. combat ended), or timeout
            return state.Ready || IsTerminalBlockReason(state.BlockReason);
        }

        /// <summary>
        /// Wait until the game is ready or reaches a terminal state, returning detailed state when done.
        /// Terminal states (like combat ending) will exit early even though Ready=false.
        /// Can be called from HTTP thread.
        /// </summary>
        public static ReadinessState WaitUntilReadyWithState(int timeoutMs = 5000, int pollIntervalMs = 50)
        {
            var startTime = DateTime.Now;
            ReadinessState lastState = null;

            while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
            {
                if (Plugin.IsMainThread)
                {
                    lastState = GetReadinessState();
                }
                else
                {
                    Plugin.RunOnMainThreadAndWait(() =>
                    {
                        lastState = GetReadinessState();
                    });
                }

                // Exit if ready
                if (lastState.Ready)
                    return lastState;

                // Exit if terminal state (e.g. combat ended)
                if (IsTerminalBlockReason(lastState.BlockReason))
                    return lastState;

                System.Threading.Thread.Sleep(pollIntervalMs);
            }

            // Return last state with timeout indication
            if (lastState != null)
            {
                lastState.BlockReason = $"Timeout:{lastState.BlockReason}";
            }
            return lastState ?? new ReadinessState
            {
                Ready = false,
                Phase = "Unknown",
                BlockReason = "Timeout"
            };
        }
    }
}
