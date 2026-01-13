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
            // All validation and Unity lookups in one main thread call
            object interactable = null;
            InteractableType interactableType = default;
            string error = null;
            UnityEngine.Vector3 targetPos = UnityEngine.Vector3.zero;

            Plugin.RunOnMainThreadAndWait(() =>
            {
                // Validate not in combat
                if (GameStateManager.Instance?.IsCombat ?? false)
                {
                    error = "Cannot perform this action during combat";
                    return;
                }

                // Find the interactable
                (interactable, interactableType, error) = FindInteractableByName(interactableName);
                if (interactable != null)
                    targetPos = ((UnityEngine.Component)interactable).transform.position;
            });

            if (error != null)
                return JsonConfig.Error(error);

            // Teleport to the interactable (main thread)
            Plugin.RunOnMainThreadAndWait(() =>
            {
                TeleportActions.TeleportInternal(targetPos);
            });

            // Wait for teleport to complete before interacting
            WaitUntilReady(5000);

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
        /// Uses unified readiness detection for consistency with the rest of the system.
        /// </summary>
        private static string WaitForInteractionResult(InteractableType expectedType, string interactableName, int timeoutMs = 3000)
        {
            // Portal returns immediately - scene transition
            if (expectedType == InteractableType.Portal)
                return JsonConfig.Serialize(new { success = true, action = "portal_enter", type = expectedType, name = interactableName });

            // Chest returns immediately - loot is instant
            if (expectedType == InteractableType.Chest)
            {
                // Brief wait to check if equipment selection opens
                Plugin.SafeSleep(100);
                if (StateSerializer.IsInEquipmentSelection())
                    return StateSerializer.GetEquipmentSelectionStateJson();
                return JsonConfig.Serialize(new { success = true, action = "chest_opened", type = expectedType, name = interactableName });
            }

            // Use unified readiness for all other interactions
            var readinessState = WaitUntilReadyWithState(timeoutMs);

            // Map expected type to readiness phase and return appropriate state
            switch (expectedType)
            {
                case InteractableType.Npc:
                case InteractableType.Event:
                    if (readinessState.Phase == "Dialogue" && readinessState.Ready)
                        return StateSerializer.GetDialogueStateJson();
                    break;

                case InteractableType.MonsterShrine:
                    if (readinessState.Phase == "MonsterSelection" && readinessState.Ready)
                        return JsonConfig.Serialize(new { success = true, action = "shrine_interact", type = expectedType, name = interactableName });
                    break;

                case InteractableType.Merchant:
                    if (readinessState.Phase == "Merchant" && readinessState.Ready)
                        return JsonConfig.Serialize(new { success = true, action = "merchant_interact", type = expectedType, name = interactableName });
                    break;

                case InteractableType.AetherSpring:
                    if (readinessState.Phase == "AetherSpring" && readinessState.Ready)
                        return JsonConfig.Serialize(new { success = true, action = "aether_spring_interact", type = expectedType, name = interactableName });
                    break;

                case InteractableType.StartRun:
                    if (readinessState.Phase == "DifficultySelection" && readinessState.Ready)
                        return StateSerializer.GetDifficultySelectionStateJson();
                    if (readinessState.Phase == "MonsterSelection" && readinessState.Ready)
                        return StateSerializer.GetMonsterSelectionStateJson();
                    break;
            }

            // Timeout or unexpected state
            Plugin.Log.LogWarning($"WaitForInteractionResult: Expected {expectedType}, got Phase={readinessState.Phase}, Ready={readinessState.Ready}, BlockReason={readinessState.BlockReason}");
            return JsonConfig.Serialize(new
            {
                success = true,
                action = "interact_pending",
                type = expectedType,
                name = interactableName,
                note = $"Interaction triggered, current phase: {readinessState.Phase}, ready: {readinessState.Ready}"
            });
        }

        // =====================================================
        // VOID BLITZ
        // =====================================================

        public static string ExecuteVoidBlitz(string monsterGroupName, string monsterName = null)
        {
            var (notInCombat, combatError) = ValidateNotInCombat();
            if (!notInCombat)
                return JsonConfig.Error(combatError);

            var (isExploring, exploringError) = ValidateExploring();
            if (!isExploring)
                return JsonConfig.Error(exploringError);

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

            Plugin.RunOnMainThreadAndWait(() =>
            {
                VoidBlitzBypass.IsActive = true;
                VoidBlitzBypass.TargetGroup = targetGroup;
                VoidBlitzBypass.TargetMonster = targetMonster;

                PlayerController.Instance.AetherBlitzTargetGroup = targetGroup;
                PlayerController.Instance.TryToStartAetherBlitz(targetMonster);
            });

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
        // LOOT ALL (BREAK DESTRUCTIBLES + COLLECT)
        // =====================================================

        public static string ExecuteLootAll()
        {
            var (valid, validError) = ValidateNotInCombat();
            if (!valid)
                return JsonConfig.Error(validError);

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

            return JsonConfig.Serialize(new { success = true, action = "loot_all", brokenCount });
        }

    }
}
