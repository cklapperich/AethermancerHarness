using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AethermancerHarness
{
    /// <summary>
    /// Exploration-related actions: void blitz, teleport, interact, start combat.
    /// </summary>
    public static partial class ActionHandler
    {
        // =====================================================
        // UNIFIED TELEPORT AND INTERACT
        // =====================================================

        /// <summary>
        /// Unified interaction: teleport to interactable by name, press F key, return result.
        /// Works for all interactable types: NPCs, shrines, merchants, aether springs, portals, events.
        /// </summary>
        public static string TeleportAndInteract(string interactableName)
        {
            if (GameStateManager.Instance?.IsCombat ?? false)
                return JsonConfig.Error("Cannot interact during combat");

            // Find the interactable
            var (interactable, interactableType, error) = FindInteractableByName(interactableName);
            if (error != null)
                return JsonConfig.Error(error);

            Plugin.Log.LogInfo($"TeleportAndInteract: Found {interactableType} named '{interactableName}'");

            // Get position
            UnityEngine.Vector3 targetPos = UnityEngine.Vector3.zero;
            Plugin.RunOnMainThreadAndWait(() =>
            {
                targetPos = ((UnityEngine.Component)interactable).transform.position;
            });

            // Teleport to the interactable (main thread)
            Plugin.RunOnMainThreadAndWait(() =>
            {
                TeleportActions.TeleportInternal(targetPos);
                Plugin.Log.LogInfo($"TeleportAndInteract: Teleported to ({targetPos.x:F1}, {targetPos.y:F1})");
            });

            // Wait for teleport animation to complete (HTTP thread - safe)
            if (Plugin.WatchableMode)
            {
                WaitUntilReady(5000);
            }

            // Press F key - the game's unified interaction handler (main thread)
            Plugin.RunOnMainThreadAndWait(() =>
            {
                PlayerController.Instance?.OnInteract();
            });

            // Wait for and return the resulting state
            return WaitForInteractionResult(interactableType, interactableName);
        }

        /// <summary>
        /// Find an interactable by its display name from the exploration state.
        /// Returns (interactable, type, error).
        /// </summary>
        private static (object interactable, InteractableType type, string error) FindInteractableByName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return (null, default(InteractableType), "Interactable name is required");

            // Build interactables list with names
            var interactables = new List<(object obj, InteractableType type, string displayName)>();

            // NPCs and Events (DialogueInteractable) - using identity-based naming
            var allDialogue = UnityEngine.Object.FindObjectsByType<DialogueInteractable>(UnityEngine.FindObjectsSortMode.None);
            var npcObjects = allDialogue.Where(di => di != null).ToList();

            Func<DialogueInteractable, string> getNpcBaseName = di =>
            {
                var goName = di.gameObject.name;
                if (goName.Contains("SmallEvent_NPC_Collectable"))
                    return "Care Chest";
                return di.DialogueCharacter?.CharacterName ?? goName;
            };

            foreach (var di in npcObjects)
            {
                var goName = di.gameObject.name;
                var iType = (goName.Contains("Collectable") || goName.Contains("SmallEvent"))
                    ? InteractableType.Event : InteractableType.Npc;
                interactables.Add((di, iType, StateSerializer.GetDisplayName(di, npcObjects, getNpcBaseName)));
            }

            // Map-based interactables
            var map = LevelGenerator.Instance?.Map;
            if (map != null)
            {
                // Monster Shrine
                if (map.MonsterShrine != null && !map.MonsterShrine.WasUsedUp)
                    interactables.Add((map.MonsterShrine, InteractableType.MonsterShrine, "MonsterShrine"));

                // Merchant
                if (map.MerchantInteractable != null)
                    interactables.Add((map.MerchantInteractable, InteractableType.Merchant, "Merchant"));

                // Aether Springs - using identity-based naming
                if (map.AetherSpringInteractables != null)
                {
                    var activeSprings = map.AetherSpringInteractables
                        .Where(s => s != null && s is AetherSpringInteractable asi && !asi.WasUsedUp)
                        .Cast<AetherSpringInteractable>()
                        .ToList();
                    Func<AetherSpringInteractable, string> getSpringName = s => "AetherSpring";

                    foreach (var spring in activeSprings)
                        interactables.Add((spring, InteractableType.AetherSpring, StateSerializer.GetDisplayName(spring, activeSprings, getSpringName)));
                }
            }

            // Portals - using identity-based naming
            var propGen = LevelGenerator.Instance?.PropGenerator;
            if (propGen?.ExitInteractables != null)
            {
                var nextMapBubbleField = typeof(ExitInteractable).GetField(
                    "nextMapBubble",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                var portalObjects = new List<ExitInteractable>();
                foreach (var go in propGen.ExitInteractables)
                {
                    if (go == null) continue;
                    var exitInteractable = go.GetComponent<ExitInteractable>();
                    if (exitInteractable != null)
                        portalObjects.Add(exitInteractable);
                }

                Func<ExitInteractable, string> getPortalName = ei =>
                {
                    string portalType = "None";
                    if (nextMapBubbleField != null)
                    {
                        var mapBubble = nextMapBubbleField.GetValue(ei) as MapBubble;
                        if (mapBubble != null)
                            portalType = mapBubble.Customization.ToString();
                    }
                    return $"Portal ({portalType})";
                };

                foreach (var portal in portalObjects)
                    interactables.Add((portal, InteractableType.Portal, StateSerializer.GetDisplayName(portal, portalObjects, getPortalName)));
            }

            // Start Run (in Pilgrim's Rest)
            var currentArea = ExplorationController.Instance?.CurrentArea ?? EArea.PilgrimsRest;
            if (currentArea == EArea.PilgrimsRest)
            {
                var startRun = StateSerializer.FindStartRunInteractable();
                if (startRun != null)
                    interactables.Add((startRun, InteractableType.StartRun, "StartRun"));
            }

            // Chests
            var allChests = UnityEngine.Object.FindObjectsByType<ChestInteractible>(UnityEngine.FindObjectsSortMode.None);
            int chestIdx = 0;
            foreach (var chest in allChests)
            {
                if (chest == null || chest.WasUsedUp) continue;
                var chestName = chestIdx == 0 ? "Chest" : $"Chest {chestIdx + 1}";
                interactables.Add((chest, InteractableType.Chest, chestName));
                chestIdx++;
            }

            // Find match (case-insensitive)
            foreach (var (obj, iType, displayName) in interactables)
            {
                if (displayName.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return (obj, iType, null);
            }

            // Build available names for error message
            var available = new List<string>();
            foreach (var (_, _, displayName) in interactables)
                available.Add(displayName);

            return (null, default(InteractableType),
                $"No interactable named '{name}'. Available: {string.Join(", ", available)}");
        }

        /// <summary>
        /// Wait for interaction result based on expected type.
        /// </summary>
        private static string WaitForInteractionResult(InteractableType expectedType, string interactableName, int timeoutMs = 3000)
        {
            var startTime = DateTime.Now;

            while (!TimedOut(startTime, timeoutMs))
            {
                System.Threading.Thread.Sleep(50);

                // Check for menus/states that might have opened
                switch (expectedType)
                {
                    case InteractableType.MonsterShrine:
                        if (StateSerializer.IsInMonsterSelection())
                            return JsonConfig.Serialize(new { success = true, action = "shrine_interact", type = expectedType, name = interactableName });
                        break;

                    case InteractableType.Merchant:
                        if (IsMerchantMenuOpen())
                        {
                            System.Threading.Thread.Sleep(200);
                            return JsonConfig.Serialize(new { success = true, action = "merchant_interact", type = expectedType, name = interactableName });
                        }
                        break;

                    case InteractableType.AetherSpring:
                        if (StateSerializer.IsInAetherSpringMenu())
                        {
                            System.Threading.Thread.Sleep(200);
                            return JsonConfig.Serialize(new { success = true, action = "aether_spring_interact", type = expectedType, name = interactableName });
                        }
                        break;

                    case InteractableType.Portal:
                        // Portal triggers scene transition - just return success
                        return JsonConfig.Serialize(new { success = true, action = "portal_enter", type = expectedType, name = interactableName });

                    case InteractableType.Npc:
                    case InteractableType.Event:
                        if (IsDialogueOpen())
                        {
                            System.Threading.Thread.Sleep(200);
                            // Auto-progress dialogue to first decision point
                            AutoProgressDialogue();

                            if (StateSerializer.IsInSkillSelection())
                                return JsonConfig.Serialize(new { success = true, phase = GamePhase.SkillSelection, transitionedFrom = "dialogue", name = interactableName });

                            if (!IsDialogueOpen())
                                return JsonConfig.Serialize(new { success = true, phase = GamePhase.Exploration, dialogueComplete = true, name = interactableName });

                            return StateSerializer.GetDialogueStateJson();
                        }
                        break;

                    case InteractableType.StartRun:
                        if (StateSerializer.IsInDifficultySelection())
                            return StateSerializer.GetDifficultySelectionStateJson();
                        if (StateSerializer.IsInMonsterSelection())
                            return StateSerializer.GetMonsterSelectionStateJson();
                        break;

                    case InteractableType.Chest:
                        // Chests drop loot immediately - check if equipment selection opened
                        if (StateSerializer.IsInEquipmentSelection())
                            return StateSerializer.GetEquipmentSelectionStateJson();
                        // Otherwise just return success
                        return JsonConfig.Serialize(new { success = true, action = "chest_opened", type = expectedType, name = interactableName });
                }
            }

            // Timeout - return current state
            Plugin.Log.LogWarning($"WaitForInteractionResult: Timeout waiting for {expectedType} menu");
            return JsonConfig.Serialize(new
            {
                success = true,
                action = "interact_pending",
                type = expectedType,
                name = interactableName,
                note = "Interaction triggered, menu may still be opening"
            });
        }

        // =====================================================
        // VOID BLITZ
        // =====================================================

        public static string ExecuteVoidBlitz(string monsterGroupName, string monsterName = null)
        {
            if (GameStateManager.Instance?.IsCombat ?? true)
                return JsonConfig.Error("Cannot void blitz during combat");

            if (!GameStateManager.Instance.IsExploring)
                return JsonConfig.Error("Must be in exploration mode to void blitz");

            var groups = ExplorationController.Instance?.EncounterGroups;
            if (groups == null || groups.Count == 0)
                return JsonConfig.Error("No monster groups available");

            // Filter valid groups and use identity-based naming
            var validGroups = groups.Where(g => g != null).ToList();
            Func<MonsterGroup, string> getGroupName = g =>
            {
                var pos = g.transform.position;
                return $"Monster Group at ({pos.x:F0}, {pos.y:F0})";
            };

            // Find target group using identity-based naming
            var targetGroup = StateSerializer.FindByDisplayName(monsterGroupName, validGroups, getGroupName);
            if (targetGroup == null)
            {
                var availableGroupNames = StateSerializer.GetAllDisplayNames(validGroups, getGroupName);
                return JsonConfig.Error($"No monster group named '{monsterGroupName}'. Available: {string.Join(", ", availableGroupNames)}");
            }

            if (targetGroup.EncounterDefeated)
                return JsonConfig.Error("Monster group already defeated");

            if (targetGroup.OverworldMonsters == null || targetGroup.OverworldMonsters.Count == 0)
                return JsonConfig.Error("Monster group has no overworld monsters");

            // Filter active monsters and use identity-based naming
            var activeMonsters = targetGroup.OverworldMonsters
                .Where(m => m != null && m.gameObject.activeSelf)
                .ToList();

            if (activeMonsters.Count == 0)
                return JsonConfig.Error("No active monsters in group");

            Func<OverworldMonster, string> getMonsterName = m =>
                StateSerializer.StripMarkup(m.Monster?.Name ?? m.name);

            // Find target monster using identity-based naming
            OverworldMonster targetMonster = null;
            string targetMonsterDisplayName = null;

            if (!string.IsNullOrEmpty(monsterName))
            {
                targetMonster = StateSerializer.FindByDisplayName(monsterName, activeMonsters, getMonsterName);
                if (targetMonster == null)
                {
                    var availableMonsterNames = StateSerializer.GetAllDisplayNames(activeMonsters, getMonsterName);
                    return JsonConfig.Error($"No monster named '{monsterName}'. Available: {string.Join(", ", availableMonsterNames)}");
                }
                targetMonsterDisplayName = StateSerializer.GetDisplayName(targetMonster, activeMonsters, getMonsterName);
            }
            else
            {
                // Use first active monster
                targetMonster = activeMonsters[0];
                targetMonsterDisplayName = StateSerializer.GetDisplayName(targetMonster, activeMonsters, getMonsterName);
            }

            var targetGroupIndex = validGroups.IndexOf(targetGroup);

            Plugin.Log.LogInfo($"ExecuteVoidBlitz: Targeting group {targetGroupIndex}, monster '{targetMonsterDisplayName}'");

            // Run void blitz trigger on main thread (UI operations like poise preview require main thread)
            Plugin.RunOnMainThreadAndWait(() =>
            {
                VoidBlitzBypass.IsActive = true;
                VoidBlitzBypass.TargetGroup = targetGroup;
                VoidBlitzBypass.TargetMonster = targetMonster;

                PlayerController.Instance.AetherBlitzTargetGroup = targetGroup;
                PlayerController.Instance.TryToStartAetherBlitz(targetMonster);
            });

            Plugin.Log.LogInfo("ExecuteVoidBlitz: Void blitz triggered successfully");

            return JsonConfig.Serialize(new
            {
                success = true,
                action = "void_blitz",
                monsterGroup = monsterGroupName,
                targetMonster = targetMonsterDisplayName,
                note = "Animation playing, combat will start shortly"
            });
        }
        
        // =====================================================
        // SIMPLIFIED INTERACT (backwards compatible)
        // =====================================================

        /// <summary>
        /// Parameterless interact - finds first available interactable in priority order.
        /// Kept for backwards compatibility.
        /// </summary>
        public static string ExecuteInteract()
        {
            if (GameStateManager.Instance?.IsCombat ?? false)
                return JsonConfig.Error("Cannot interact during combat");

            // Priority order for auto-selection
            var currentArea = ExplorationController.Instance?.CurrentArea ?? EArea.PilgrimsRest;

            // 1. Start Run in Pilgrim's Rest
            if (currentArea == EArea.PilgrimsRest)
            {
                var startRunInteractable = StateSerializer.FindStartRunInteractable();
                if (startRunInteractable != null)
                {
                    Plugin.Log.LogInfo("ExecuteInteract: Found StartRun, using unified interact");
                    return TeleportAndInteract("StartRun");
                }
            }

            // 2. Monster Shrine
            var map = LevelGenerator.Instance?.Map;
            if (map?.MonsterShrine != null && !map.MonsterShrine.WasUsedUp)
            {
                Plugin.Log.LogInfo("ExecuteInteract: Found MonsterShrine, using unified interact");
                return TeleportAndInteract("MonsterShrine");
            }

            // 3. Merchant
            if (map?.MerchantInteractable != null)
            {
                Plugin.Log.LogInfo("ExecuteInteract: Found Merchant, using unified interact");
                return TeleportAndInteract("Merchant");
            }

            // 4. Aether Spring
            if (map?.AetherSpringInteractables != null)
            {
                foreach (var s in map.AetherSpringInteractables)
                {
                    var spring = s as AetherSpringInteractable;
                    if (spring != null && !spring.WasUsedUp)
                    {
                        Plugin.Log.LogInfo("ExecuteInteract: Found AetherSpring, using unified interact");
                        return TeleportAndInteract("AetherSpring");
                    }
                }
            }

            // 5. Fallback to generic interact for nearest interactable
            Plugin.Log.LogInfo("ExecuteInteract: No priority interactable found, using OnInteract()");
            Plugin.RunOnMainThreadAndWait(() =>
            {
                PlayerController.Instance?.OnInteract();
            });
            return JsonConfig.Serialize(new { success = true, action = "interact" });
        }

        /// <summary>
        /// Named interact - uses unified TeleportAndInteract.
        /// The 'type' parameter is now ignored; name is used directly.
        /// </summary>
        public static string ExecuteInteract(string type, string name)
        {
            // Type parameter kept for backwards compatibility but ignored
            // All resolution is by name now
            if (string.IsNullOrEmpty(name))
                return JsonConfig.Error("name is required");

            return TeleportAndInteract(name);
        }

        // =====================================================
        // LOOT ALL (BREAK DESTRUCTIBLES + COLLECT)
        // =====================================================

        public static string ExecuteLootAll()
        {
            if (GameStateManager.Instance?.IsCombat ?? false)
                return JsonConfig.Error("Cannot loot during combat");

            int brokenCount = 0;
            string error = null;

            Plugin.RunOnMainThreadAndWait(() =>
            {
                try
                {
                    var propGen = LevelGenerator.Instance?.PropGenerator;
                    if (propGen == null)
                    {
                        error = "PropGenerator not available";
                        return;
                    }

                    // Break all destructibles (boxes, plants, barrels)
                    // Force break ALL breakables regardless of CanBeInteracted()
                    var breakables = propGen.GeneratedBreakableObjects;
                    var playerMovement = PlayerMovementController.Instance;

                    if (breakables != null)
                    {
                        foreach (var breakable in breakables)
                        {
                            if (breakable == null || breakable.WasUsedUp) continue;

                            try
                            {
                                // Teleport player to breakable position (always instant for batch operation)
                                var breakablePos = breakable.transform.position;
                                var targetPos = new UnityEngine.Vector3(breakablePos.x, breakablePos.y - 2f, breakablePos.z);
                                playerMovement.transform.position = targetPos;

                                // Force the interaction regardless of CanBeInteracted()
                                breakable.StartBaseInteraction();
                                brokenCount++;
                            }
                            catch { /* Skip failures */ }
                        }
                    }

                    // Instantly collect all dropped loot on the map
                    propGen.CollectCollectibles();
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
            });

            if (error != null)
                return JsonConfig.Error(error);

            Plugin.Log.LogInfo($"ExecuteLootAll: Broke {brokenCount} destructibles and collected loot");
            return JsonConfig.Serialize(new { success = true, action = "loot_all", brokenCount });
        }

    }
}
