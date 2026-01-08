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

        public static string ExecuteVoidBlitz(int monsterGroupIndex, int monsterIndex = 0)
        {
            if (GameStateManager.Instance?.IsCombat ?? true)
                return JsonConfig.Error("Cannot void blitz during combat");

            if (!GameStateManager.Instance.IsExploring)
                return JsonConfig.Error("Must be in exploration mode to void blitz");

            var groups = ExplorationController.Instance?.EncounterGroups;
            if (groups == null || groups.Count == 0)
                return JsonConfig.Error("No monster groups available");

            if (monsterGroupIndex < 0 || monsterGroupIndex >= groups.Count)
                return JsonConfig.Error($"Invalid monster group index: {monsterGroupIndex}. Valid range: 0-{groups.Count - 1}");

            var targetGroup = groups[monsterGroupIndex];
            if (targetGroup == null)
                return JsonConfig.Error("Monster group is null");

            if (targetGroup.EncounterDefeated)
                return JsonConfig.Error("Monster group already defeated");

            if (targetGroup.OverworldMonsters == null || targetGroup.OverworldMonsters.Count == 0)
                return JsonConfig.Error("Monster group has no overworld monsters");

            // Find target monster
            OverworldMonster targetMonster = null;
            if (monsterIndex >= 0 && monsterIndex < targetGroup.OverworldMonsters.Count)
            {
                targetMonster = targetGroup.OverworldMonsters[monsterIndex];
                if (targetMonster == null || !targetMonster.gameObject.activeSelf)
                    targetMonster = null;
            }

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
                monsterGroupIndex,
                targetMonster = targetMonsterName,
                note = "Animation playing, combat will start shortly"
            });
        }

        // =====================================================
        // START COMBAT (WITHOUT VOID BLITZ)
        // =====================================================

        public static string ExecuteStartCombat(int monsterGroupIndex)
        {
            if (GameStateManager.Instance?.IsCombat ?? true)
                return JsonConfig.Error("Already in combat");

            var groups = ExplorationController.Instance?.EncounterGroups;
            if (groups == null || groups.Count == 0)
                return JsonConfig.Error("No monster groups available");

            if (monsterGroupIndex < 0 || monsterGroupIndex >= groups.Count)
                return JsonConfig.Error($"Invalid monster group index: {monsterGroupIndex}. Valid range: 0-{groups.Count - 1}");

            var targetGroup = groups[monsterGroupIndex];
            if (targetGroup == null)
                return JsonConfig.Error("Monster group is null");

            if (targetGroup.EncounterDefeated)
                return JsonConfig.Error("Monster group already defeated");

            Plugin.Log.LogInfo($"ExecuteStartCombat: Starting combat with group {monsterGroupIndex}");
            targetGroup.StartCombat(aetherBlitzed: false, null, ignoreGameState: true);

            return JsonConfig.Serialize(new { success = true, action = "start_combat", monsterGroupIndex });
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

            // Fallback to generic interact for other interactables
            PlayerController.Instance?.OnInteract();
            return JsonConfig.Serialize(new { success = true, action = "interact" });
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
                    var breakables = propGen.GeneratedBreakableObjects;
                    if (breakables != null)
                    {
                        foreach (var breakable in breakables)
                        {
                            if (breakable != null && breakable.CanBeInteracted())
                            {
                                breakable.StartBaseInteraction();
                                brokenCount++;
                            }
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
