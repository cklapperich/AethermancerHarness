using System;
using Newtonsoft.Json.Linq;

namespace AethermancerHarness
{
    /// <summary>
    /// Exploration-related actions: void blitz, teleport, interact, start combat.
    /// </summary>
    public static partial class ActionHandler
    {
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

            // Build monster group names
            var groupNames = new System.Collections.Generic.List<string>();
            for (int i = 0; i < groups.Count; i++)
            {
                var group = groups[i];
                if (group == null) continue;
                var pos = group.transform.position;
                groupNames.Add($"Monster Group at ({pos.x:F0}, {pos.y:F0})");
            }

            var groupDisplayNames = StateSerializer.DeduplicateNames(groupNames);
            var (monsterGroupIndex, groupError) = StateSerializer.ResolveNameToIndex(monsterGroupName, groupDisplayNames, "monster group");

            if (monsterGroupIndex < 0)
                return JsonConfig.Error(groupError);

            var targetGroup = groups[monsterGroupIndex];
            if (targetGroup == null)
                return JsonConfig.Error("Monster group is null");

            if (targetGroup.EncounterDefeated)
                return JsonConfig.Error("Monster group already defeated");

            if (targetGroup.OverworldMonsters == null || targetGroup.OverworldMonsters.Count == 0)
                return JsonConfig.Error("Monster group has no overworld monsters");

            // Find target monster
            OverworldMonster targetMonster = null;

            // If monster name is provided, resolve it
            if (!string.IsNullOrEmpty(monsterName))
            {
                // Build monster names for this group
                var monsterNames = new System.Collections.Generic.List<string>();
                foreach (var m in targetGroup.OverworldMonsters)
                {
                    if (m != null && m.gameObject.activeSelf)
                        monsterNames.Add(m.name);
                }

                var monsterDisplayNames = StateSerializer.DeduplicateNames(monsterNames);
                var (monsterIndex, monsterError) = StateSerializer.ResolveNameToIndex(monsterName, monsterDisplayNames, "monster");

                if (monsterIndex >= 0 && monsterIndex < targetGroup.OverworldMonsters.Count)
                {
                    targetMonster = targetGroup.OverworldMonsters[monsterIndex];
                    if (targetMonster == null || !targetMonster.gameObject.activeSelf)
                        return JsonConfig.Error("Specified monster is not active");
                }
                else
                {
                    return JsonConfig.Error(monsterError);
                }
            }

            // If no monster name provided or not found, use first active monster
            if (targetMonster == null)
            {
                foreach (var monster in targetGroup.OverworldMonsters)
                {
                    if (monster != null && monster.gameObject.activeSelf)
                    {
                        targetMonster = monster;
                        break;
                    }
                }
            }

            if (targetMonster == null)
                return JsonConfig.Error("No active monsters in group");

            Plugin.Log.LogInfo($"ExecuteVoidBlitz: Targeting group {monsterGroupIndex}, monster {targetMonster.name}");

            // Store monster name before main thread call (can't access Unity objects across threads safely)
            var targetMonsterName = targetMonster.name;

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
                targetMonster = targetMonsterName,
                note = "Animation playing, combat will start shortly"
            });
        }

        // =====================================================
        // START COMBAT (WITHOUT VOID BLITZ)
        // =====================================================

        public static string ExecuteStartCombat(string monsterGroupName)
        {
            if (GameStateManager.Instance?.IsCombat ?? true)
                return JsonConfig.Error("Already in combat");

            var groups = ExplorationController.Instance?.EncounterGroups;
            if (groups == null || groups.Count == 0)
                return JsonConfig.Error("No monster groups available");

            // Build monster group names
            var groupNames = new System.Collections.Generic.List<string>();
            for (int i = 0; i < groups.Count; i++)
            {
                var group = groups[i];
                if (group == null) continue;
                var pos = group.transform.position;
                groupNames.Add($"Monster Group at ({pos.x:F0}, {pos.y:F0})");
            }

            var groupDisplayNames = StateSerializer.DeduplicateNames(groupNames);
            var (monsterGroupIndex, groupError) = StateSerializer.ResolveNameToIndex(monsterGroupName, groupDisplayNames, "monster group");

            if (monsterGroupIndex < 0)
                return JsonConfig.Error(groupError);

            var targetGroup = groups[monsterGroupIndex];
            if (targetGroup == null)
                return JsonConfig.Error("Monster group is null");

            if (targetGroup.EncounterDefeated)
                return JsonConfig.Error("Monster group already defeated");

            Plugin.Log.LogInfo($"ExecuteStartCombat: Starting combat with group {monsterGroupName}");
            targetGroup.StartCombat(aetherBlitzed: false, null, ignoreGameState: true);

            return JsonConfig.Serialize(new { success = true, action = "start_combat", monsterGroup = monsterGroupName });
        }

        // =====================================================
        // TELEPORT
        // =====================================================

        public static string ExecuteTeleport(float x, float y, float z)
        {
            if (GameStateManager.Instance?.IsCombat ?? false)
                return JsonConfig.Error("Cannot teleport during combat");

            var playerMovement = PlayerMovementController.Instance;
            if (playerMovement == null)
                return JsonConfig.Error("PlayerMovementController not available");

            var oldPos = playerMovement.transform.position;
            var newPos = new UnityEngine.Vector3(x, y, z);
            playerMovement.transform.position = newPos;

            Plugin.Log.LogInfo($"Teleported player from ({oldPos.x:F1}, {oldPos.y:F1}, {oldPos.z:F1}) to ({x:F1}, {y:F1}, {z:F1})");

            return JsonConfig.Serialize(new
            {
                success = true,
                from = new { x = (double)oldPos.x, y = (double)oldPos.y, z = (double)oldPos.z },
                to = new { x = (double)x, y = (double)y, z = (double)z }
            });
        }

        // =====================================================
        // GENERIC INTERACT
        // =====================================================

        public static string ExecuteInteract()
        {
            if (GameStateManager.Instance?.IsCombat ?? false)
                return JsonConfig.Error("Cannot interact during combat");

            // Check for start run interactable in Pilgrim's Rest
            var currentArea = ExplorationController.Instance?.CurrentArea ?? EArea.PilgrimsRest;
            if (currentArea == EArea.PilgrimsRest)
            {
                var startRunInteractable = StateSerializer.FindStartRunInteractable();
                if (startRunInteractable != null)
                {
                    Plugin.Log.LogInfo("ExecuteInteract: Found start run interactable, triggering");
                    return ExecuteStartRunInteraction(startRunInteractable);
                }
            }

            // Check for monster shrine - teleport and interact regardless of distance
            var shrine = LevelGenerator.Instance?.Map?.MonsterShrine;
            if (shrine != null && !shrine.WasUsedUp)
            {
                Plugin.Log.LogInfo($"ExecuteInteract: Found unused monster shrine, teleporting and interacting");
                try
                {
                    Plugin.RunOnMainThreadAndWait(() =>
                    {
                        // Teleport player near shrine
                        var shrinePos = shrine.transform.position;
                        var playerMovement = PlayerMovementController.Instance;
                        if (playerMovement != null)
                        {
                            var targetPos = new UnityEngine.Vector3(shrinePos.x, shrinePos.y - 5f, shrinePos.z);
                            playerMovement.transform.position = targetPos;
                            Plugin.Log.LogInfo($"ExecuteInteract: Teleported player to shrine at ({targetPos.x:F1}, {targetPos.y:F1})");
                        }

                        shrine.StartBaseInteraction();
                    });

                    // Wait for shrine menu to open
                    var startTime = DateTime.Now;
                    while (!StateSerializer.IsInMonsterSelection() && !TimedOut(startTime, 3000))
                    {
                        System.Threading.Thread.Sleep(50);
                    }

                    if (StateSerializer.IsInMonsterSelection())
                    {
                        return JsonConfig.Serialize(new { success = true, action = "shrine_interact", type = InteractableType.MonsterShrine });
                    }
                    else
                    {
                        return JsonConfig.Serialize(new { success = true, action = "shrine_interact_pending", type = InteractableType.MonsterShrine, note = "Shrine triggered, menu may still be opening" });
                    }
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogError($"ExecuteInteract: Shrine interaction failed: {ex.Message}");
                    return JsonConfig.Error($"Shrine interaction failed: {ex.Message}");
                }
            }

            // Check for merchant - teleport and interact
            var map = LevelGenerator.Instance?.Map;
            if (map != null)
            {
                var merchantInteractable = map.MerchantInteractable;
                if (merchantInteractable != null)
                {
                    var merchant = merchantInteractable as MerchantInteractable;
                    if (merchant != null)
                    {
                        Plugin.Log.LogInfo("ExecuteInteract: Found merchant, teleporting and interacting");
                        try
                        {
                            Plugin.RunOnMainThreadAndWait(() =>
                            {
                                var merchantPos = merchant.transform.position;
                                var playerMovement = PlayerMovementController.Instance;
                                if (playerMovement != null)
                                {
                                    var targetPos = new UnityEngine.Vector3(merchantPos.x - 2f, merchantPos.y, merchantPos.z);
                                    playerMovement.transform.position = targetPos;
                                    Plugin.Log.LogInfo($"ExecuteInteract: Teleported player near merchant at ({targetPos.x:F1}, {targetPos.y:F1})");
                                }

                                merchant.StartMerchantInteraction();
                            });

                            // Wait for merchant menu to open
                            var startTime = DateTime.Now;
                            while (!ActionHandler.IsMerchantMenuOpen() && !TimedOut(startTime, 3000))
                            {
                                System.Threading.Thread.Sleep(50);
                            }

                            if (ActionHandler.IsMerchantMenuOpen())
                            {
                                System.Threading.Thread.Sleep(200);
                                return JsonConfig.Serialize(new { success = true, action = "merchant_interact", type = InteractableType.Merchant });
                            }
                            else
                            {
                                return JsonConfig.Serialize(new { success = true, action = "merchant_interact_pending", type = InteractableType.Merchant, note = "Merchant triggered, menu may still be opening" });
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Plugin.Log.LogError($"ExecuteInteract: Merchant interaction failed: {ex.Message}");
                            return JsonConfig.Error($"Merchant interaction failed: {ex.Message}");
                        }
                    }
                }

                // Check for aether spring - teleport and interact
                var aetherSpringInteractables = map.AetherSpringInteractables;
                if (aetherSpringInteractables != null)
                {
                    foreach (var s in aetherSpringInteractables)
                    {
                        var spring = s as AetherSpringInteractable;
                        if (spring != null && !spring.WasUsedUp)
                        {
                            Plugin.Log.LogInfo("ExecuteInteract: Found unused aether spring, teleporting and interacting");
                            try
                            {
                                Plugin.RunOnMainThreadAndWait(() =>
                                {
                                    var springPos = spring.transform.position;
                                    var playerMovement = PlayerMovementController.Instance;
                                    if (playerMovement != null)
                                    {
                                        var targetPos = new UnityEngine.Vector3(springPos.x - 2f, springPos.y, springPos.z);
                                        playerMovement.transform.position = targetPos;
                                        Plugin.Log.LogInfo($"ExecuteInteract: Teleported player near aether spring at ({targetPos.x:F1}, {targetPos.y:F1})");
                                    }

                                    spring.StartBaseInteraction();
                                });

                                // Wait for aether spring menu to open
                                var startTime = DateTime.Now;
                                while (!StateSerializer.IsInAetherSpringMenu() && !TimedOut(startTime, 3000))
                                {
                                    System.Threading.Thread.Sleep(50);
                                }

                                if (StateSerializer.IsInAetherSpringMenu())
                                {
                                    System.Threading.Thread.Sleep(200);
                                    return JsonConfig.Serialize(new { success = true, action = "aether_spring_interact", type = InteractableType.AetherSpring });
                                }
                                else
                                {
                                    return JsonConfig.Serialize(new { success = true, action = "aether_spring_interact_pending", type = InteractableType.AetherSpring, note = "Aether spring triggered, menu may still be opening" });
                                }
                            }
                            catch (System.Exception ex)
                            {
                                Plugin.Log.LogError($"ExecuteInteract: Aether spring interaction failed: {ex.Message}");
                                return JsonConfig.Error($"Aether spring interaction failed: {ex.Message}");
                            }
                        }
                    }
                }
            }

            // Fallback to generic interact for other interactables
            PlayerController.Instance?.OnInteract();
            return JsonConfig.Serialize(new { success = true, action = "interact" });
        }

        // =====================================================
        // TYPED INTERACT (with type and index)
        // =====================================================

        public static string ExecuteInteract(string type, string name)
        {
            if (GameStateManager.Instance?.IsCombat ?? false)
                return JsonConfig.Error("Cannot interact during combat");

            // Handle Portal interaction
            if (type == "portal" || type == "Portal")
            {
                var propGen = LevelGenerator.Instance?.PropGenerator;
                if (propGen == null)
                    return JsonConfig.Error("PropGenerator not available");

                var exitInteractables = propGen.ExitInteractables;
                if (exitInteractables == null || exitInteractables.Count == 0)
                    return JsonConfig.Error("No portals available");

                // Build portal names
                var portalNames = new System.Collections.Generic.List<string>();
                var nextMapBubbleField = typeof(ExitInteractable).GetField(
                    "nextMapBubble",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                for (int i = 0; i < exitInteractables.Count; i++)
                {
                    var go = exitInteractables[i];
                    if (go == null) continue;

                    var exitInteractable = go.GetComponent<ExitInteractable>();
                    if (exitInteractable == null) continue;

                    string portalType = "None";
                    if (nextMapBubbleField != null)
                    {
                        var mapBubble = nextMapBubbleField.GetValue(exitInteractable) as MapBubble;
                        if (mapBubble != null)
                            portalType = mapBubble.Customization.ToString();
                    }

                    portalNames.Add($"Portal ({portalType})");
                }

                var portalDisplayNames = StateSerializer.DeduplicateNames(portalNames);
                var (index, portalError) = StateSerializer.ResolveNameToIndex(name, portalDisplayNames, "portal");

                if (index < 0)
                    return JsonConfig.Error(portalError);

                var portalGO = exitInteractables[index];
                if (portalGO == null)
                    return JsonConfig.Error("Portal is null");

                var portalComponent = portalGO.GetComponent<ExitInteractable>();
                if (portalComponent == null)
                    return JsonConfig.Error("Portal component not found");

                Plugin.RunOnMainThreadAndWait(() =>
                {
                    // Teleport to portal and interact
                    var portalPos = portalGO.transform.position;
                    PlayerMovementController.Instance.transform.position = portalPos;
                    portalComponent.StartBaseInteraction();
                });

                return JsonConfig.Serialize(new { success = true, action = "portal_enter", portal = name });
            }

            return JsonConfig.Error($"Unknown interactable type: {type}");
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
                                // Teleport player to breakable position
                                if (playerMovement != null)
                                {
                                    var breakablePos = breakable.transform.position;
                                    var targetPos = new UnityEngine.Vector3(breakablePos.x, breakablePos.y - 2f, breakablePos.z);
                                    playerMovement.transform.position = targetPos;
                                }

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

        /// <summary>
        /// Handle start run interaction - opens difficulty selection or monster selection.
        /// </summary>
        private static string ExecuteStartRunInteraction(NextAreaInteractable startRunInteractable)
        {
            try
            {
                Plugin.RunOnMainThreadAndWait(() =>
                {
                    startRunInteractable.ForceStartInteraction();
                });

                // Wait for state transition
                var startTime = DateTime.Now;
                while (!TimedOut(startTime, 5000))
                {
                    System.Threading.Thread.Sleep(100);

                    // Check if difficulty selection opened
                    if (StateSerializer.IsInDifficultySelection())
                    {
                        Plugin.Log.LogInfo("ExecuteStartRunInteraction: Difficulty selection opened");
                        return StateSerializer.GetDifficultySelectionStateJson();
                    }

                    // Check if monster selection opened (no difficulty selection needed)
                    if (StateSerializer.IsInMonsterSelection())
                    {
                        Plugin.Log.LogInfo("ExecuteStartRunInteraction: Monster selection opened");
                        return StateSerializer.GetMonsterSelectionStateJson();
                    }
                }

                return JsonConfig.Error("Timeout waiting for run start menu to open");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"ExecuteStartRunInteraction: Exception - {ex.Message}\n{ex.StackTrace}");
                return JsonConfig.Error($"Exception during run start: {ex.Message}");
            }
        }

    }
}
