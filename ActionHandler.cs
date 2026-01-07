using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace AethermancerHarness
{
    public static class ActionHandler
    {
        /// <summary>
        /// Check if the game is ready to accept player input.
        /// Uses game-state checks (works in both headed and headless modes).
        /// </summary>
        public static bool IsReadyForInput()
        {
            // Must be in combat
            if (!GameStateManager.Instance?.IsCombat ?? true)
                return false;

            // Combat must not have ended
            var currentState = CombatStateManager.Instance?.State?.CurrentState?.ID;
            if (currentState >= CombatStateManager.EState.EndCombatTriggers)
                return false;

            var cc = CombatController.Instance;
            if (cc == null)
                return false;

            // 1. Must be player's phase (current monster belongs to player)
            if (cc.CurrentMonster == null || !cc.CurrentMonster.BelongsToPlayer)
                return false;

            // 2. No triggers/effects queued
            if (CombatTimeline.Instance?.TriggerStack?.Count > 0)
                return false;

            // 3. Current monster not mid-action
            if (cc.CurrentMonster.State?.ActionInstance != null)
                return false;

            return true;
        }

        /// <summary>
        /// Returns detailed info about why input might not be ready.
        /// </summary>
        public static string GetInputReadyStatus()
        {
            if (!GameStateManager.Instance?.IsCombat ?? true)
                return "NotInCombat";

            var currentState = CombatStateManager.Instance?.State?.CurrentState?.ID;
            if (currentState >= CombatStateManager.EState.EndCombatTriggers)
                return $"CombatEnded:{currentState}";

            var cc = CombatController.Instance;
            if (cc == null)
                return "NoCombatController";

            if (cc.CurrentMonster == null)
                return "NoCurrentMonster";
            if (!cc.CurrentMonster.BelongsToPlayer)
                return $"EnemyTurn:{cc.CurrentMonster.Name}";
            if (CombatTimeline.Instance?.TriggerStack?.Count > 0)
                return $"TriggersPending:{CombatTimeline.Instance.TriggerStack.Count}";
            if (cc.CurrentMonster.State?.ActionInstance != null)
                return $"ActionExecuting:{cc.CurrentMonster.State.ActionInstance.Action?.Name}";

            return "Ready";
        }

        /// <summary>
        /// Wait for the game to be ready for input (blocking, with timeout).
        /// </summary>
        public static bool WaitForReady(int timeoutMs = 30000)
        {
            var startTime = DateTime.Now;
            while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
            {
                if (IsReadyForInput())
                    return true;
                System.Threading.Thread.Sleep(50); // Poll every 50ms
            }
            return false;
        }

        public static string ExecuteCombatAction(int actorIndex, int skillIndex, int targetIndex)
        {
            try
            {
                // Check if game is ready for input
                if (!IsReadyForInput())
                {
                    var status = GetInputReadyStatus();
                    return $"{{\"success\": false, \"error\": \"Game not ready for input\", \"status\": \"{status}\"}}";
                }

                var cc = CombatController.Instance;

                // Get the actor
                if (actorIndex < 0 || actorIndex >= cc.PlayerMonsters.Count)
                {
                    return $"{{\"success\": false, \"error\": \"Invalid actor index: {actorIndex}\"}}";
                }
                var actor = cc.PlayerMonsters[actorIndex];

                // Get the skill
                if (skillIndex < 0 || skillIndex >= actor.SkillManager.Actions.Count)
                {
                    return $"{{\"success\": false, \"error\": \"Invalid skill index: {skillIndex}\"}}";
                }
                var skill = actor.SkillManager.Actions[skillIndex];

                // Verify skill can be used
                if (!skill.Action.CanUseAction(skill))
                {
                    return $"{{\"success\": false, \"error\": \"Skill cannot be used: {skill.Action.Name}\"}}";
                }

                // Get the target
                ITargetable target = null;
                var targetType = skill.Action.TargetType;

                switch (targetType)
                {
                    case ETargetType.SingleEnemy:
                        if (targetIndex < 0 || targetIndex >= cc.Enemies.Count)
                        {
                            return $"{{\"success\": false, \"error\": \"Invalid target index: {targetIndex}\"}}";
                        }
                        target = cc.Enemies[targetIndex];
                        break;

                    case ETargetType.AllEnemies:
                        target = cc.TargetEnemies;
                        break;

                    case ETargetType.SingleAlly:
                        if (targetIndex < 0 || targetIndex >= cc.PlayerMonsters.Count)
                        {
                            return $"{{\"success\": false, \"error\": \"Invalid ally target index: {targetIndex}\"}}";
                        }
                        target = cc.PlayerMonsters[targetIndex];
                        break;

                    case ETargetType.AllAllies:
                        target = cc.TargetPlayerMonsters;
                        break;

                    case ETargetType.SelfOrOwner:
                        target = actor;
                        break;

                    case ETargetType.AllMonsters:
                        target = cc.TargetAllMonsters;
                        break;

                    default:
                        // Default to first enemy for unknown types
                        if (cc.Enemies.Count > 0)
                        {
                            target = cc.Enemies[0];
                        }
                        break;
                }

                if (target == null)
                {
                    return "{\"success\": false, \"error\": \"Could not determine target\"}";
                }

                // Execute the action!
                Plugin.Log.LogInfo($"Executing action: {actor.Name} uses {skill.Action.Name} on {GetTargetName(target)}");

                // This is the key call - same as what CombatMenu.SelectTarget does
                actor.State.StartAction(skill, target, target);

                // Wait for game to be ready for input again (action animation completes, enemy turns finish, etc.)
                bool ready = WaitForReady(30000);

                // Check if combat ended
                var stateManager = CombatStateManager.Instance;
                bool combatEnded = stateManager?.State?.CurrentState?.ID >= CombatStateManager.EState.EndCombatTriggers;

                if (combatEnded && stateManager.WonEncounter)
                {
                    // Victory! Auto-advance through post-combat screens
                    Plugin.Log.LogInfo("ExecuteCombatAction: Combat won, starting post-combat auto-advance");
                    return WaitForPostCombatComplete();
                }
                else if (combatEnded && !stateManager.WonEncounter)
                {
                    // Defeat
                    Plugin.Log.LogInfo("ExecuteCombatAction: Combat lost");
                    return $"{{\"success\": true, \"action\": \"{EscapeJson(skill.Action.Name)}\", \"combatResult\": \"DEFEAT\", \"state\": {StateSerializer.ToJson()}}}";
                }

                // Combat continues - return normal state
                var state = StateSerializer.ToJson();
                return $"{{\"success\": true, \"action\": \"{EscapeJson(skill.Action.Name)}\", \"actor\": \"{EscapeJson(actor.Name)}\", \"target\": \"{EscapeJson(GetTargetName(target))}\", \"waitedForReady\": {(ready ? "true" : "false")}, \"state\": {state}}}";
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Error executing action: {e}");
                return $"{{\"success\": false, \"error\": \"{EscapeJson(e.Message)}\"}}";
            }
        }

        public static string ExecuteConsumableAction(int consumableIndex, int targetIndex)
        {
            try
            {
                if (!IsReadyForInput())
                {
                    var status = GetInputReadyStatus();
                    return $"{{\"success\": false, \"error\": \"Game not ready for input\", \"status\": \"{status}\"}}";
                }

                var cc = CombatController.Instance;

                // Get the consumable
                var consumables = PlayerController.Instance?.Inventory?.GetAllConsumables();
                if (consumables == null || consumableIndex < 0 || consumableIndex >= consumables.Count)
                    return $"{{\"success\": false, \"error\": \"Invalid consumable index: {consumableIndex}\"}}";

                var consumable = consumables[consumableIndex];

                // Set owner to current monster (required for CanUseAction check)
                var currentMonster = cc.CurrentMonster;
                if (currentMonster == null)
                    return "{\"success\": false, \"error\": \"No current monster to use consumable\"}";

                consumable.Owner = currentMonster;

                // Verify consumable can be used
                if (!consumable.Action.CanUseAction(consumable))
                    return $"{{\"success\": false, \"error\": \"Consumable cannot be used: {consumable.Consumable?.Name}\"}}";

                // Get target based on consumable's target type
                ITargetable target = GetConsumableTarget(consumable.Action.TargetType, targetIndex, cc);
                if (target == null)
                    return "{\"success\": false, \"error\": \"Could not determine target\"}";

                // Execute the consumable action
                Plugin.Log.LogInfo($"Using consumable: {consumable.Consumable?.Name} on {GetTargetName(target)}");
                currentMonster.State.StartAction(consumable, target, target);

                // Wait for completion
                bool ready = WaitForReady(30000);

                // Handle combat end (same as skills)
                var stateManager = CombatStateManager.Instance;
                bool combatEnded = stateManager?.State?.CurrentState?.ID >= CombatStateManager.EState.EndCombatTriggers;
                if (combatEnded && stateManager.WonEncounter)
                    return WaitForPostCombatComplete();
                else if (combatEnded && !stateManager.WonEncounter)
                    return $"{{\"success\": true, \"action\": \"{EscapeJson(consumable.Consumable?.Name ?? "")}\", \"combatResult\": \"DEFEAT\", \"state\": {StateSerializer.ToJson()}}}";

                var state = StateSerializer.ToJson();
                return $"{{\"success\": true, \"action\": \"{EscapeJson(consumable.Consumable?.Name ?? "")}\", \"target\": \"{EscapeJson(GetTargetName(target))}\", \"isConsumable\": true, \"waitedForReady\": {(ready ? "true" : "false")}, \"state\": {state}}}";
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Error executing consumable: {e}");
                return $"{{\"success\": false, \"error\": \"{EscapeJson(e.Message)}\"}}";
            }
        }

        private static ITargetable GetConsumableTarget(ETargetType targetType, int targetIndex, CombatController cc)
        {
            switch (targetType)
            {
                case ETargetType.SingleEnemy:
                    return (targetIndex >= 0 && targetIndex < cc.Enemies.Count) ? cc.Enemies[targetIndex] : null;
                case ETargetType.AllEnemies:
                    return cc.TargetEnemies;
                case ETargetType.SingleAlly:
                    return (targetIndex >= 0 && targetIndex < cc.PlayerMonsters.Count) ? cc.PlayerMonsters[targetIndex] : null;
                case ETargetType.AllAllies:
                    return cc.TargetPlayerMonsters;
                case ETargetType.SelfOrOwner:
                    return cc.CurrentMonster;
                case ETargetType.AllMonsters:
                    return cc.TargetAllMonsters;
                default:
                    return cc.Enemies.Count > 0 ? cc.Enemies[0] : null;
            }
        }

        public static string ExecutePreview(int actorIndex, int skillIndex, int targetIndex)
        {
            try
            {
                // Validate we're in combat
                if (!GameStateManager.Instance?.IsCombat ?? true)
                {
                    return "{\"success\": false, \"error\": \"Not in combat\"}";
                }

                var cc = CombatController.Instance;

                // Get the actor
                if (actorIndex < 0 || actorIndex >= cc.PlayerMonsters.Count)
                {
                    return $"{{\"success\": false, \"error\": \"Invalid actor index: {actorIndex}\"}}";
                }
                var actor = cc.PlayerMonsters[actorIndex];

                // Get the skill
                if (skillIndex < 0 || skillIndex >= actor.SkillManager.Actions.Count)
                {
                    return $"{{\"success\": false, \"error\": \"Invalid skill index: {skillIndex}\"}}";
                }
                var skill = actor.SkillManager.Actions[skillIndex];

                // Verify skill can be used (for preview we still want to check this)
                if (!skill.Action.CanUseAction(skill))
                {
                    return $"{{\"success\": false, \"error\": \"Skill cannot be used: {skill.Action.Name}\"}}";
                }

                // Get the target
                ITargetable target = null;
                var targetType = skill.Action.TargetType;

                switch (targetType)
                {
                    case ETargetType.SingleEnemy:
                        if (targetIndex < 0 || targetIndex >= cc.Enemies.Count)
                        {
                            return $"{{\"success\": false, \"error\": \"Invalid target index: {targetIndex}\"}}";
                        }
                        target = cc.Enemies[targetIndex];
                        break;

                    case ETargetType.AllEnemies:
                        target = cc.TargetEnemies;
                        break;

                    case ETargetType.SingleAlly:
                        if (targetIndex < 0 || targetIndex >= cc.PlayerMonsters.Count)
                        {
                            return $"{{\"success\": false, \"error\": \"Invalid ally target index: {targetIndex}\"}}";
                        }
                        target = cc.PlayerMonsters[targetIndex];
                        break;

                    case ETargetType.AllAllies:
                        target = cc.TargetPlayerMonsters;
                        break;

                    case ETargetType.SelfOrOwner:
                        target = actor;
                        break;

                    case ETargetType.AllMonsters:
                        target = cc.TargetAllMonsters;
                        break;

                    default:
                        if (cc.Enemies.Count > 0)
                        {
                            target = cc.Enemies[0];
                        }
                        break;
                }

                if (target == null)
                {
                    return "{\"success\": false, \"error\": \"Could not determine target\"}";
                }

                Plugin.Log.LogInfo($"Previewing action: {actor.Name} uses {skill.Action.Name} on {GetTargetName(target)}");

                // Store current monster to restore later
                var currentMonster = cc.CurrentMonster;

                // Store poise values BEFORE preview for comparison later
                var poiseBeforePreview = new Dictionary<int, List<(EElement element, int current, int max)>>();
                for (int i = 0; i < cc.Enemies.Count; i++)
                {
                    var enemy = cc.Enemies[i];
                    var poiseList = new List<(EElement element, int current, int max)>();
                    if (enemy.SkillManager?.Stagger != null)
                    {
                        foreach (var stagger in enemy.SkillManager.Stagger)
                        {
                            // Access the actual current poise (not preview) before we start
                            poiseList.Add((stagger.Element, stagger.CurrentPoise, stagger.MaxHits));
                        }
                    }
                    poiseBeforePreview[i] = poiseList;
                }

                // Set up preview state (mimicking Monster.PreviewAction)
                cc.CurrentMonster = actor;
                cc.StartPreview();
                CombatVariablesManager.Instance.StartPreview();
                CombatStateManager.Instance.PreviewActionInstance = skill;
                CombatStateManager.Instance.PreviewAction = skill.Action;
                CombatStateManager.Instance.IsPreview = true;
                CombatStateManager.Instance.IsEnemyPreview = false;

                // Initialize all monsters for preview
                foreach (var m in cc.AllMonsters)
                {
                    m.InitializePreview();
                }
                foreach (var s in cc.AllSummons)
                {
                    s.InitializePreview();
                }

                // Handle aether cost for preview
                if (skill.GetActionCost().TotalCurrentAether > 0)
                {
                    var finalCost = cc.GetAether(actor).GetFinalAetherCost(skill.GetActionCost());
                    cc.GetAether(actor).ConsumeAether(finalCost, actor, skill);
                }

                // Set up the action
                actor.State.StartActionSetup(skill, target);

                // Execute the preview simulation
                CombatTimeline.Instance.ProgressActionPreview();

                // *** COLLECT PREVIEW DATA BEFORE CLEANUP ***
                var previewResults = CollectPreviewData(cc, poiseBeforePreview);

                // Turn off preview mode
                CombatStateManager.Instance.IsPreview = false;

                // Cleanup (restore original state)
                CombatStateManager.Instance.PreviewAction = null;
                CombatVariablesManager.Instance.ClearPreviews();
                CombatTimeline.Instance.ClearTriggers();
                cc.EnemyAether.Aether.ClearPreview();
                cc.PlayerAether.Aether.ClearPreview();
                cc.ClearPreview();
                cc.CurrentMonster = currentMonster;
                cc.UpdateEnemyAuras();
                CombatStateManager.Instance.IsEnemyPreview = false;

                foreach (var m in cc.AllMonsters)
                {
                    m.ClearPreview();
                }
                foreach (var s in cc.AllSummons)
                {
                    s.ClearPreview();
                }
                foreach (var m in cc.AllMonsters)
                {
                    m.Stats.Calculate(updateHealth: false);
                }
                foreach (var s in cc.AllSummons)
                {
                    s.Stats.Calculate(updateHealth: false);
                }

                return previewResults;
            }
            catch (Exception e)
            {
                // Make sure to turn off preview mode even on error
                CombatStateManager.Instance.IsPreview = false;
                Plugin.Log.LogError($"Error previewing action: {e}");
                return $"{{\"success\": false, \"error\": \"{EscapeJson(e.Message)}\"}}";
            }
        }

        public static string ExecuteConsumablePreview(int consumableIndex, int targetIndex)
        {
            try
            {
                // Validate we're in combat
                if (!GameStateManager.Instance?.IsCombat ?? true)
                {
                    return "{\"success\": false, \"error\": \"Not in combat\"}";
                }

                var cc = CombatController.Instance;

                // Get the consumable
                var consumables = PlayerController.Instance?.Inventory?.GetAllConsumables();
                if (consumables == null || consumableIndex < 0 || consumableIndex >= consumables.Count)
                    return $"{{\"success\": false, \"error\": \"Invalid consumable index: {consumableIndex}\"}}";

                var consumable = consumables[consumableIndex];

                // Set owner to current monster (required for CanUseAction check)
                var currentMonster = cc.CurrentMonster;
                if (currentMonster == null)
                    return "{\"success\": false, \"error\": \"No current monster for preview\"}";

                consumable.Owner = currentMonster;

                // Verify consumable can be used (for preview we still want to check this)
                if (!consumable.Action.CanUseAction(consumable))
                {
                    return $"{{\"success\": false, \"error\": \"Consumable cannot be used: {consumable.Consumable?.Name}\"}}";
                }

                // Get target based on consumable's target type
                ITargetable target = GetConsumableTarget(consumable.Action.TargetType, targetIndex, cc);
                if (target == null)
                    return "{\"success\": false, \"error\": \"Could not determine target\"}";

                Plugin.Log.LogInfo($"Previewing consumable: {consumable.Consumable?.Name} on {GetTargetName(target)}");

                // Store poise values BEFORE preview for comparison later
                var poiseBeforePreview = new Dictionary<int, List<(EElement element, int current, int max)>>();
                for (int i = 0; i < cc.Enemies.Count; i++)
                {
                    var enemy = cc.Enemies[i];
                    var poiseList = new List<(EElement element, int current, int max)>();
                    if (enemy.SkillManager?.Stagger != null)
                    {
                        foreach (var stagger in enemy.SkillManager.Stagger)
                        {
                            poiseList.Add((stagger.Element, stagger.CurrentPoise, stagger.MaxHits));
                        }
                    }
                    poiseBeforePreview[i] = poiseList;
                }

                // Set up preview state (mimicking Monster.PreviewAction)
                cc.CurrentMonster = currentMonster;
                cc.StartPreview();
                CombatVariablesManager.Instance.StartPreview();
                CombatStateManager.Instance.PreviewActionInstance = consumable;
                CombatStateManager.Instance.PreviewAction = consumable.Action;
                CombatStateManager.Instance.IsPreview = true;
                CombatStateManager.Instance.IsEnemyPreview = false;

                // Initialize all monsters for preview
                foreach (var m in cc.AllMonsters)
                {
                    m.InitializePreview();
                }
                foreach (var s in cc.AllSummons)
                {
                    s.InitializePreview();
                }

                // Handle aether cost for preview
                if (consumable.GetActionCost().TotalCurrentAether > 0)
                {
                    var finalCost = cc.GetAether(currentMonster).GetFinalAetherCost(consumable.GetActionCost());
                    cc.GetAether(currentMonster).ConsumeAether(finalCost, currentMonster, consumable);
                }

                // Set up the action
                currentMonster.State.StartActionSetup(consumable, target);

                // Execute the preview simulation
                CombatTimeline.Instance.ProgressActionPreview();

                // Collect preview data BEFORE cleanup
                var previewResults = CollectPreviewData(cc, poiseBeforePreview);

                // Turn off preview mode
                CombatStateManager.Instance.IsPreview = false;

                // Cleanup (restore original state)
                CombatStateManager.Instance.PreviewAction = null;
                CombatVariablesManager.Instance.ClearPreviews();
                CombatTimeline.Instance.ClearTriggers();
                cc.EnemyAether.Aether.ClearPreview();
                cc.PlayerAether.Aether.ClearPreview();
                cc.ClearPreview();
                cc.CurrentMonster = currentMonster;
                cc.UpdateEnemyAuras();
                CombatStateManager.Instance.IsEnemyPreview = false;

                foreach (var m in cc.AllMonsters)
                {
                    m.ClearPreview();
                }
                foreach (var s in cc.AllSummons)
                {
                    s.ClearPreview();
                }
                foreach (var m in cc.AllMonsters)
                {
                    m.Stats.Calculate(updateHealth: false);
                }
                foreach (var s in cc.AllSummons)
                {
                    s.Stats.Calculate(updateHealth: false);
                }

                return previewResults;
            }
            catch (Exception e)
            {
                // Make sure to turn off preview mode even on error
                CombatStateManager.Instance.IsPreview = false;
                Plugin.Log.LogError($"Error previewing consumable: {e}");
                return $"{{\"success\": false, \"error\": \"{EscapeJson(e.Message)}\"}}";
            }
        }

        private static string CollectPreviewData(CombatController cc, Dictionary<int, List<(EElement element, int current, int max)>> poiseBeforePreview)
        {
            var sb = new StringBuilder();
            sb.Append("{\"success\": true, \"preview\": {");

            // Track interrupt counts across all enemies
            int totalKills = 0;
            int totalBreaks = 0;  // poise breaks (staggers)
            int totalPurgeCancels = 0;  // actions cancelled by aether purge
            int totalInterrupts = 0;

            // Collect effects on enemies (including action interruption status)
            sb.Append("\"enemies\": [");
            for (int i = 0; i < cc.Enemies.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var enemy = cc.Enemies[i];

                // Count interrupts for this enemy
                bool willKill = enemy.Stats.PreviewHealth.ValueInt <= 0;
                int breaks = 0;
                int purgeCancels = 0;

                if (enemy.AI?.PickedActionList != null)
                {
                    foreach (var picked in enemy.AI.PickedActionList)
                    {
                        if (picked.PreviewStaggered) breaks++;
                        if (picked.PreviewPurged) purgeCancels++;
                    }
                }

                // Each kill/break/purgeCancel is an interrupt
                if (willKill) totalKills++;
                totalBreaks += breaks;
                totalPurgeCancels += purgeCancels;

                // Interrupts: kills + breaks + purgeCancels (kill counts as interrupting all their actions)
                int enemyInterrupts = (willKill ? (enemy.AI?.PickedActionList?.Count ?? 1) : 0) + breaks + purgeCancels;
                totalInterrupts += enemyInterrupts;

                // Get poise before values for this enemy
                var poiseBefore = poiseBeforePreview.ContainsKey(i) ? poiseBeforePreview[i] : null;
                sb.Append(FormatEnemyPreview(enemy, i, willKill, breaks, purgeCancels, poiseBefore));
            }
            sb.Append("],");

            // Collect effects on player monsters
            sb.Append("\"allies\": [");
            for (int i = 0; i < cc.PlayerMonsters.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var ally = cc.PlayerMonsters[i];
                sb.Append(FormatMonsterPreview(ally, i));
            }
            sb.Append("],");

            // Summary of interrupts
            sb.Append("\"interruptSummary\": {");
            sb.Append($"\"kills\": {totalKills},");
            sb.Append($"\"breaks\": {totalBreaks},");
            sb.Append($"\"purgeCancels\": {totalPurgeCancels},");
            sb.Append($"\"totalInterrupts\": {totalInterrupts}");
            sb.Append("}");

            sb.Append("}}");
            return sb.ToString();
        }

        private static string FormatEnemyPreview(Monster enemy, int index, bool willKill, int breaks, int purgeCancels, List<(EElement element, int current, int max)> poiseBefore)
        {
            var sb = new StringBuilder();
            // Get base monster preview data
            var basePreview = FormatMonsterPreview(enemy, index);

            // Remove closing brace to append more data
            sb.Append(basePreview.Substring(0, basePreview.Length - 1));

            sb.Append($",\"willKill\": {(willKill ? "true" : "false")}");
            sb.Append($",\"willBreak\": {(breaks > 0 ? "true" : "false")}");
            sb.Append($",\"willPurgeCancel\": {(purgeCancels > 0 ? "true" : "false")}");
            sb.Append($",\"breaksCount\": {breaks}");
            sb.Append($",\"purgeCancelsCount\": {purgeCancels}");

            // Poise before/after - uses game's built-in preview system
            sb.Append(",\"poise\": [");
            if (enemy.SkillManager?.Stagger != null && poiseBefore != null)
            {
                int poiseIdx = 0;
                foreach (var stagger in enemy.SkillManager.Stagger)
                {
                    if (poiseIdx > 0) sb.Append(",");
                    // poiseBefore contains values before preview started
                    // stagger.PreviewPoise has the post-preview value (since we're still in preview mode)
                    int beforeValue = poiseIdx < poiseBefore.Count ? poiseBefore[poiseIdx].current : stagger.MaxHits;
                    int afterValue = stagger.PreviewPoise;
                    sb.Append($"{{\"element\": \"{stagger.Element}\", ");
                    sb.Append($"\"before\": {beforeValue}, ");
                    sb.Append($"\"after\": {afterValue}, ");
                    sb.Append($"\"max\": {stagger.MaxHits}}}");
                    poiseIdx++;
                }
            }
            sb.Append("]");

            sb.Append("}");
            return sb.ToString();
        }

        private static string FormatMonsterPreview(Monster monster, int index)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"index\": {index},");
            sb.Append($"\"name\": \"{EscapeJson(monster.Name)}\",");

            // Aggregate preview numbers by type
            int totalDamage = 0;
            int totalHeal = 0;
            int totalShield = 0;
            int hitCount = 0;
            bool hasCrit = false;
            var buffs = new List<string>();
            var debuffs = new List<string>();

            foreach (var preview in monster.Numbers.PreviewNumbers)
            {
                switch (preview.NumberType)
                {
                    case EDamageNumberType.Damage:
                        totalDamage += preview.Number;
                        hitCount++;
                        if (preview.IsCritical) hasCrit = true;
                        break;
                    case EDamageNumberType.Heal:
                        totalHeal += preview.Number;
                        break;
                    case EDamageNumberType.Shield:
                        totalShield += preview.Number;
                        break;
                    case EDamageNumberType.Buff:
                        if (!string.IsNullOrEmpty(preview.Text))
                            buffs.Add(preview.Text);
                        break;
                    case EDamageNumberType.Debuff:
                        if (!string.IsNullOrEmpty(preview.Text))
                            debuffs.Add(preview.Text);
                        break;
                }
            }

            sb.Append($"\"damage\": {totalDamage},");
            sb.Append($"\"heal\": {totalHeal},");
            sb.Append($"\"shield\": {totalShield},");
            sb.Append($"\"hits\": {hitCount},");
            sb.Append($"\"hasCrit\": {(hasCrit ? "true" : "false")},");

            // Current and preview HP
            sb.Append($"\"currentHp\": {monster.CurrentHealth},");
            sb.Append($"\"previewHp\": {monster.Stats.PreviewHealth.ValueInt},");

            // Buffs and debuffs
            sb.Append("\"buffsApplied\": [");
            for (int i = 0; i < buffs.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append($"\"{EscapeJson(buffs[i])}\"");
            }
            sb.Append("],");

            sb.Append("\"debuffsApplied\": [");
            for (int i = 0; i < debuffs.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append($"\"{EscapeJson(debuffs[i])}\"");
            }
            sb.Append("]");

            sb.Append("}");
            return sb.ToString();
        }

        private static string GetTargetName(ITargetable target)
        {
            if (target is Monster m)
            {
                return m.Name;
            }
            if (target is MonsterList ml)
            {
                return $"[{ml.Monsters.Count} targets]";
            }
            return "unknown";
        }

        public static string GetEnemyActions()
        {
            try
            {
                if (!GameStateManager.Instance?.IsCombat ?? true)
                {
                    return "{\"success\": false, \"error\": \"Not in combat\"}";
                }

                var cc = CombatController.Instance;
                var sb = new StringBuilder();
                sb.Append("{\"success\": true, \"enemies\": [");

                for (int e = 0; e < cc.Enemies.Count; e++)
                {
                    if (e > 0) sb.Append(",");
                    var enemy = cc.Enemies[e];
                    sb.Append("{");
                    sb.Append($"\"index\": {e},");
                    sb.Append($"\"name\": \"{EscapeJson(enemy.Name)}\",");
                    sb.Append($"\"hp\": {enemy.CurrentHealth},");
                    sb.Append($"\"maxHp\": {enemy.Stats.MaxHealth.ValueInt},");

                    // Current intention (what they've picked)
                    sb.Append("\"intentions\": [");
                    if (enemy.AI?.PickedActionList != null)
                    {
                        for (int i = 0; i < enemy.AI.PickedActionList.Count; i++)
                        {
                            if (i > 0) sb.Append(",");
                            var picked = enemy.AI.PickedActionList[i];
                            sb.Append("{");
                            sb.Append($"\"skillName\": \"{EscapeJson(picked.Action?.Action?.Name ?? "Unknown")}\",");
                            sb.Append($"\"targetName\": \"{EscapeJson(GetTargetName(picked.Target))}\",");
                            sb.Append($"\"targetIndex\": {GetTargetIndex(picked.Target, cc)},");
                            sb.Append($"\"isPurged\": {(picked.PreviewPurged ? "true" : "false")},");
                            sb.Append($"\"isStaggered\": {(picked.PreviewStaggered ? "true" : "false")}");

                            // Get damage info with caster modifiers via reflection
                            var actionDamage = picked.Action?.Action?.GetComponent<ActionDamage>();
                            if (actionDamage != null)
                            {
                                int dmgPerHit = GetCalculatedDamage(actionDamage, picked.Action);
                                sb.Append($",\"damagePerHit\": {dmgPerHit}");
                                sb.Append($",\"hitCount\": {actionDamage.HitCount}");
                                sb.Append($",\"totalDamage\": {dmgPerHit * actionDamage.HitCount}");
                            }

                            sb.Append("}");
                        }
                    }
                    sb.Append("],");

                    // All available skills
                    sb.Append("\"skills\": [");
                    for (int s = 0; s < enemy.SkillManager.Actions.Count; s++)
                    {
                        if (s > 0) sb.Append(",");
                        var skill = enemy.SkillManager.Actions[s];
                        var description = skill.Action?.GetDescription(skill) ?? "";
                        sb.Append("{");
                        sb.Append($"\"index\": {s},");
                        sb.Append($"\"name\": \"{EscapeJson(skill.Action.Name)}\",");
                        sb.Append($"\"description\": \"{EscapeJson(description)}\",");
                        sb.Append($"\"targetType\": \"{skill.Action.TargetType}\",");

                        // Elements
                        sb.Append("\"elements\": [");
                        for (int el = 0; el < skill.Action.Elements.Count; el++)
                        {
                            if (el > 0) sb.Append(",");
                            sb.Append($"\"{skill.Action.Elements[el]}\"");
                        }
                        sb.Append("],");

                        // Damage info (with caster modifiers via reflection)
                        var dmg = skill.Action.GetComponent<ActionDamage>();
                        if (dmg != null)
                        {
                            int dmgPerHit = GetCalculatedDamage(dmg, skill);
                            sb.Append($"\"damagePerHit\": {dmgPerHit},");
                            sb.Append($"\"hitCount\": {dmg.HitCount},");
                            sb.Append($"\"totalDamage\": {dmgPerHit * dmg.HitCount},");
                        }
                        else
                        {
                            sb.Append("\"damagePerHit\": 0,");
                            sb.Append("\"hitCount\": 0,");
                            sb.Append("\"totalDamage\": 0,");
                        }

                        sb.Append($"\"canUse\": {(skill.Action.CanUseAction(skill) ? "true" : "false")}");
                        sb.Append("}");
                    }
                    sb.Append("]");

                    sb.Append("}");
                }

                sb.Append("]}");
                return sb.ToString();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Error getting enemy actions: {e}");
                return $"{{\"success\": false, \"error\": \"{EscapeJson(e.Message)}\"}}";
            }
        }

        private static int GetTargetIndex(ITargetable target, CombatController cc)
        {
            if (target is Monster m)
            {
                // Check if it's a player monster
                for (int i = 0; i < cc.PlayerMonsters.Count; i++)
                {
                    if (cc.PlayerMonsters[i] == m) return i;
                }
                // Check if it's an enemy
                for (int i = 0; i < cc.Enemies.Count; i++)
                {
                    if (cc.Enemies[i] == m) return i;
                }
            }
            return -1;
        }

        // Cache the reflection method for performance
        private static MethodInfo _getDescriptionDamageMethod;

        private static int GetCalculatedDamage(ActionDamage actionDamage, SkillInstance skill)
        {
            if (_getDescriptionDamageMethod == null)
            {
                _getDescriptionDamageMethod = typeof(ActionDamage).GetMethod(
                    "GetDescriptionDamage",
                    BindingFlags.NonPublic | BindingFlags.Instance);
            }

            var result = _getDescriptionDamageMethod.Invoke(actionDamage, new object[] { skill });
            var valueInt = result.GetType().GetProperty("ValueInt").GetValue(result);
            return (int)valueInt;
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r");
        }

        /// <summary>
        /// Execute a void blitz on a monster group, bypassing distance checks.
        /// Plays the full teleport animation and starts combat with poise break.
        /// </summary>
        /// <param name="monsterGroupIndex">Index into ExplorationController.Instance.EncounterGroups</param>
        /// <param name="monsterIndex">Index of specific monster in group to target (default 0)</param>
        public static string ExecuteVoidBlitz(int monsterGroupIndex, int monsterIndex = 0)
        {
            try
            {
                // Verify we're in exploration mode
                if (GameStateManager.Instance?.IsCombat ?? true)
                {
                    return "{\"success\": false, \"error\": \"Cannot void blitz during combat\"}";
                }

                if (!GameStateManager.Instance.IsExploring)
                {
                    return "{\"success\": false, \"error\": \"Must be in exploration mode to void blitz\"}";
                }

                // Get the monster groups
                var groups = ExplorationController.Instance?.EncounterGroups;
                if (groups == null || groups.Count == 0)
                {
                    return "{\"success\": false, \"error\": \"No monster groups available\"}";
                }

                if (monsterGroupIndex < 0 || monsterGroupIndex >= groups.Count)
                {
                    return $"{{\"success\": false, \"error\": \"Invalid monster group index: {monsterGroupIndex}. Valid range: 0-{groups.Count - 1}\"}}";
                }

                var targetGroup = groups[monsterGroupIndex];
                if (targetGroup == null)
                {
                    return "{\"success\": false, \"error\": \"Monster group is null\"}";
                }

                if (targetGroup.EncounterDefeated)
                {
                    return "{\"success\": false, \"error\": \"Monster group already defeated\"}";
                }

                // Get the target monster within the group
                if (targetGroup.OverworldMonsters == null || targetGroup.OverworldMonsters.Count == 0)
                {
                    return "{\"success\": false, \"error\": \"Monster group has no overworld monsters\"}";
                }

                // Find an active monster
                OverworldMonster targetMonster = null;
                if (monsterIndex >= 0 && monsterIndex < targetGroup.OverworldMonsters.Count)
                {
                    targetMonster = targetGroup.OverworldMonsters[monsterIndex];
                    if (targetMonster == null || !targetMonster.gameObject.activeSelf)
                    {
                        targetMonster = null;
                    }
                }

                // Fallback to first active monster
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
                {
                    return "{\"success\": false, \"error\": \"No active monsters in group\"}";
                }

                Plugin.Log.LogInfo($"ExecuteVoidBlitz: Targeting group {monsterGroupIndex}, monster {targetMonster.name}");

                // Set up the bypass
                VoidBlitzBypass.IsActive = true;
                VoidBlitzBypass.TargetGroup = targetGroup;
                VoidBlitzBypass.TargetMonster = targetMonster;

                // Set the target group on player controller (this normally requires being in range)
                PlayerController.Instance.AetherBlitzTargetGroup = targetGroup;

                // Trigger the void blitz
                // TryToStartAetherBlitz will call StartVoidBlitz, and our Harmony patch will auto-confirm
                PlayerController.Instance.TryToStartAetherBlitz(targetMonster);

                Plugin.Log.LogInfo("ExecuteVoidBlitz: Void blitz triggered successfully");

                // The animation plays asynchronously - combat will start after animation completes
                // Return immediately with success - the combat start will be handled by the game
                return $"{{\"success\": true, \"action\": \"void_blitz\", \"monsterGroupIndex\": {monsterGroupIndex}, \"targetMonster\": \"{EscapeJson(targetMonster.name)}\", \"note\": \"Animation playing, combat will start shortly\"}}";
            }
            catch (Exception e)
            {
                // Reset bypass state on error
                VoidBlitzBypass.Reset();
                Plugin.Log.LogError($"ExecuteVoidBlitz error: {e}");
                return $"{{\"success\": false, \"error\": \"{EscapeJson(e.Message)}\"}}";
            }
        }

        /// <summary>
        /// Start combat directly without void blitz animation.
        /// </summary>
        public static string ExecuteStartCombat(int monsterGroupIndex)
        {
            try
            {
                // Verify we're in exploration mode
                if (GameStateManager.Instance?.IsCombat ?? true)
                {
                    return "{\"success\": false, \"error\": \"Already in combat\"}";
                }

                // Get the monster groups
                var groups = ExplorationController.Instance?.EncounterGroups;
                if (groups == null || groups.Count == 0)
                {
                    return "{\"success\": false, \"error\": \"No monster groups available\"}";
                }

                if (monsterGroupIndex < 0 || monsterGroupIndex >= groups.Count)
                {
                    return $"{{\"success\": false, \"error\": \"Invalid monster group index: {monsterGroupIndex}. Valid range: 0-{groups.Count - 1}\"}}";
                }

                var targetGroup = groups[monsterGroupIndex];
                if (targetGroup == null)
                {
                    return "{\"success\": false, \"error\": \"Monster group is null\"}";
                }

                if (targetGroup.EncounterDefeated)
                {
                    return "{\"success\": false, \"error\": \"Monster group already defeated\"}";
                }

                Plugin.Log.LogInfo($"ExecuteStartCombat: Starting combat with group {monsterGroupIndex}");

                // Start combat directly (no void blitz)
                targetGroup.StartCombat(aetherBlitzed: false, null, ignoreGameState: true);

                return $"{{\"success\": true, \"action\": \"start_combat\", \"monsterGroupIndex\": {monsterGroupIndex}}}";
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"ExecuteStartCombat error: {e}");
                return $"{{\"success\": false, \"error\": \"{EscapeJson(e.Message)}\"}}";
            }
        }

        public static string ExecuteTeleport(float x, float y, float z)
        {
            try
            {
                // Check we're in exploration
                if (GameStateManager.Instance?.IsCombat ?? false)
                {
                    return "{\"success\": false, \"error\": \"Cannot teleport during combat\"}";
                }

                var playerMovement = PlayerMovementController.Instance;
                if (playerMovement == null)
                {
                    return "{\"success\": false, \"error\": \"PlayerMovementController not available\"}";
                }

                var oldPos = playerMovement.transform.position;
                var newPos = new UnityEngine.Vector3(x, y, z);

                // Set the position directly
                playerMovement.transform.position = newPos;

                Plugin.Log.LogInfo($"Teleported player from ({oldPos.x:F1}, {oldPos.y:F1}, {oldPos.z:F1}) to ({x:F1}, {y:F1}, {z:F1})");

                return $"{{\"success\": true, \"from\": {{\"x\": {oldPos.x:F2}, \"y\": {oldPos.y:F2}, \"z\": {oldPos.z:F2}}}, \"to\": {{\"x\": {x:F2}, \"y\": {y:F2}, \"z\": {z:F2}}}}}";
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Error teleporting: {e}");
                return $"{{\"success\": false, \"error\": \"{EscapeJson(e.Message)}\"}}";
            }
        }

        public static string ExecuteInteract()
        {
            try
            {
                // Check we're in exploration
                if (GameStateManager.Instance?.IsCombat ?? false)
                {
                    return "{\"success\": false, \"error\": \"Cannot interact during combat\"}";
                }

                // Trigger interaction
                PlayerController.Instance?.OnInteract();

                return "{\"success\": true, \"action\": \"interact\"}";
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Error interacting: {e}");
                return $"{{\"success\": false, \"error\": \"{EscapeJson(e.Message)}\"}}";
            }
        }

        // =====================================================
        // POST-COMBAT AUTO-ADVANCE AND SKILL SELECTION
        // =====================================================

        /// <summary>
        /// After combat ends, auto-advance through worthiness and level up screens
        /// until either skill selection is open or we're back in exploration.
        /// </summary>
        public static string WaitForPostCombatComplete(int timeoutMs = 60000)
        {
            var startTime = DateTime.Now;
            Plugin.Log.LogInfo("WaitForPostCombatComplete: Starting post-combat auto-advance");

            try
            {
                // Wait for PostCombatMenu to open (after victory banner)
                while (!IsPostCombatMenuOpen())
                {
                    if (TimedOut(startTime, timeoutMs))
                    {
                        Plugin.Log.LogWarning("WaitForPostCombatComplete: Timeout waiting for PostCombatMenu");
                        return CreateTimeoutError("PostCombatMenu to open");
                    }

                    // Check if we went back to exploration (no post-combat screens)
                    if (IsInExploration())
                    {
                        Plugin.Log.LogInfo("WaitForPostCombatComplete: Back in exploration (no post-combat screens)");
                        return CreateExplorationResult("VICTORY");
                    }

                    System.Threading.Thread.Sleep(100);
                }

                Plugin.Log.LogInfo("WaitForPostCombatComplete: PostCombatMenu is open");

                // Now process through the post-combat states
                return ProcessPostCombatStates(startTime, timeoutMs);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"WaitForPostCombatComplete error: {e}");
                return $"{{\"success\": false, \"error\": \"{EscapeJson(e.Message)}\"}}";
            }
        }

        private static string ProcessPostCombatStates(DateTime startTime, int timeoutMs)
        {
            var postCombatMenu = UIController.Instance.PostCombatMenu;

            while (true)
            {
                if (TimedOut(startTime, timeoutMs))
                {
                    return CreateTimeoutError("post-combat processing");
                }

                // Check if skill selection opened
                if (StateSerializer.IsInSkillSelection())
                {
                    Plugin.Log.LogInfo("ProcessPostCombatStates: Skill selection menu is open");
                    return StateSerializer.GetSkillSelectionStateJson();
                }

                // Check if back in exploration
                if (IsInExploration())
                {
                    Plugin.Log.LogInfo("ProcessPostCombatStates: Back in exploration");
                    return CreateExplorationResult("VICTORY");
                }

                // Handle current state
                var currentState = postCombatMenu.CurrentState;
                Plugin.Log.LogInfo($"ProcessPostCombatStates: Current state = {currentState}");

                switch (currentState)
                {
                    case PostCombatMenu.EPostCombatMenuState.WorthinessUI:
                    case PostCombatMenu.EPostCombatMenuState.WorthinessUIDetailed:
                        // Wait for worthiness animations to complete
                        if (WaitForWorthinessCanContinue(postCombatMenu, startTime, timeoutMs))
                        {
                            Plugin.Log.LogInfo("ProcessPostCombatStates: Worthiness CanContinue, triggering Continue");
                            TriggerContinue(postCombatMenu);
                            System.Threading.Thread.Sleep(200); // Brief pause for state transition
                        }
                        break;

                    case PostCombatMenu.EPostCombatMenuState.LevelUpUI:
                        // Wait for level up animations to complete
                        if (WaitForLevelUpCanContinue(postCombatMenu, startTime, timeoutMs))
                        {
                            // Check if any monster has pending level ups
                            var pendingLevelUp = FindFirstPendingLevelUp(postCombatMenu);
                            if (pendingLevelUp != null)
                            {
                                Plugin.Log.LogInfo($"ProcessPostCombatStates: Found pending level up for monster, opening skill selection");
                                OpenSkillSelectionForMonster(pendingLevelUp);

                                // Wait for skill selection menu to open
                                if (WaitForSkillSelectionOpen(startTime, timeoutMs))
                                {
                                    return StateSerializer.GetSkillSelectionStateJson();
                                }
                            }
                            else
                            {
                                // No level ups, continue to close menu
                                Plugin.Log.LogInfo("ProcessPostCombatStates: No pending level ups, continuing");
                                TriggerContinue(postCombatMenu);
                                System.Threading.Thread.Sleep(200);
                            }
                        }
                        break;

                    case PostCombatMenu.EPostCombatMenuState.SkillSelectionUI:
                        // Should be handled by skill selection check above
                        Plugin.Log.LogInfo("ProcessPostCombatStates: In SkillSelectionUI state");
                        System.Threading.Thread.Sleep(100);
                        break;
                }

                System.Threading.Thread.Sleep(100);
            }
        }

        private static bool IsPostCombatMenuOpen()
        {
            try
            {
                return UIController.Instance?.PostCombatMenu?.IsOpen ?? false;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsInExploration()
        {
            try
            {
                var gameState = GameStateManager.Instance?.CurrentState;
                // Check if not in combat and not in post-combat menu
                bool notInCombat = !GameStateManager.Instance?.IsCombat ?? true;
                bool notInPostCombat = !IsPostCombatMenuOpen();
                return notInCombat && notInPostCombat;
            }
            catch
            {
                return false;
            }
        }

        private static bool WaitForWorthinessCanContinue(PostCombatMenu postCombatMenu, DateTime startTime, int timeoutMs)
        {
            while (true)
            {
                if (TimedOut(startTime, timeoutMs)) return false;

                // Check if all monsters' worthiness UI is ready to continue
                bool allCanContinue = true;
                foreach (var info in postCombatMenu.PostCombatMonsterInfos)
                {
                    if (info.monster != null && info.gameObject.activeSelf)
                    {
                        if (!info.WorthinessUI.CanContinue)
                        {
                            allCanContinue = false;
                            break;
                        }
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

                // Check if all monsters' level up UI is ready
                bool allCanContinue = true;
                foreach (var info in postCombatMenu.PostCombatMonsterInfos)
                {
                    if (info.monster != null && info.gameObject.activeSelf)
                    {
                        if (!info.LevelUpUI.CanContinue)
                        {
                            allCanContinue = false;
                            break;
                        }
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
                {
                    return info;
                }
            }
            return null;
        }

        private static void OpenSkillSelectionForMonster(PostCombatMonsterInfo monsterInfo)
        {
            try
            {
                // Get the menu list item for this monster's level up
                var menuListItem = monsterInfo.LevelUpUI.MenuListItem;
                if (menuListItem != null)
                {
                    // Select this item in the menu list
                    var postCombatMenu = UIController.Instance.PostCombatMenu;
                    postCombatMenu.MenuList.SelectByIndex(0);

                    // Trigger the selection (this should open skill selection) using reflection
                    TriggerMenuConfirm(postCombatMenu.MenuList);

                    Plugin.Log.LogInfo($"OpenSkillSelectionForMonster: Triggered skill selection for {monsterInfo.monster?.Name}");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"OpenSkillSelectionForMonster error: {e}");
            }
        }

        private static void TriggerMenuConfirm(MenuList menuList)
        {
            try
            {
                // Use reflection to call the internal InputConfirm method
                var inputConfirmMethod = typeof(MenuList).GetMethod("InputConfirm",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (inputConfirmMethod != null)
                {
                    inputConfirmMethod.Invoke(menuList, null);
                    Plugin.Log.LogInfo("TriggerMenuConfirm: Called InputConfirm()");
                }
                else
                {
                    // Fallback: directly call ItemSelected on the current item
                    var itemSelectedMethod = typeof(MenuList).GetMethod("ItemSelected",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (itemSelectedMethod != null && menuList.CurrentSelected != null)
                    {
                        itemSelectedMethod.Invoke(menuList, new object[] { menuList.CurrentSelected });
                        Plugin.Log.LogInfo("TriggerMenuConfirm: Called ItemSelected() as fallback");
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"TriggerMenuConfirm error: {e}");
            }
        }

        private static bool WaitForSkillSelectionOpen(DateTime startTime, int timeoutMs)
        {
            while (!TimedOut(startTime, timeoutMs))
            {
                if (StateSerializer.IsInSkillSelection())
                {
                    return true;
                }
                System.Threading.Thread.Sleep(100);
            }
            return false;
        }

        private static void TriggerContinue(PostCombatMenu postCombatMenu)
        {
            try
            {
                // Use reflection to call the private Continue method
                var continueMethod = typeof(PostCombatMenu).GetMethod("Continue",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (continueMethod != null)
                {
                    continueMethod.Invoke(postCombatMenu, null);
                    Plugin.Log.LogInfo("TriggerContinue: Called Continue()");
                }
                else
                {
                    Plugin.Log.LogWarning("TriggerContinue: Could not find Continue method");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"TriggerContinue error: {e}");
            }
        }

        private static bool TimedOut(DateTime startTime, int timeoutMs)
        {
            return (DateTime.Now - startTime).TotalMilliseconds >= timeoutMs;
        }

        private static string CreateTimeoutError(string waiting)
        {
            return $"{{\"success\": false, \"error\": \"Timeout waiting for {waiting}\", \"phase\": \"TIMEOUT\"}}";
        }

        private static string CreateExplorationResult(string combatResult)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"success\": true,");
            sb.Append($"\"combatResult\": \"{combatResult}\",");
            sb.Append($"\"state\": {StateSerializer.ToJson()}");
            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>
        /// Execute skill selection from the level up menu.
        /// </summary>
        /// <param name="skillIndex">0-2 for skill choice, or -1 for max health</param>
        /// <param name="reroll">True to reroll instead of selecting</param>
        public static string ExecuteSkillSelection(int skillIndex, bool reroll = false)
        {
            try
            {
                // Verify we're in skill selection
                if (!StateSerializer.IsInSkillSelection())
                {
                    return "{\"success\": false, \"error\": \"Not in skill selection screen\"}";
                }

                var skillSelectMenu = UIController.Instance.PostCombatMenu.SkillSelectMenu;

                if (reroll)
                {
                    // Check if rerolls available
                    if (InventoryManager.Instance.SkillRerolls <= 0)
                    {
                        return "{\"success\": false, \"error\": \"No skill rerolls available\"}";
                    }

                    // Select the reroll button
                    // Find reroll button index in menu list
                    var menuList = skillSelectMenu.MenuList;
                    for (int i = 0; i < menuList.List.Count; i++)
                    {
                        if (menuList.List[i] == skillSelectMenu.RerollSkillsButton)
                        {
                            menuList.SelectByIndex(i);
                            TriggerMenuConfirm(menuList);
                            Plugin.Log.LogInfo("ExecuteSkillSelection: Rerolled skills");

                            // Wait briefly for reroll to complete
                            System.Threading.Thread.Sleep(500);

                            // Return new skill selection state
                            return StateSerializer.GetSkillSelectionStateJson();
                        }
                    }

                    return "{\"success\": false, \"error\": \"Could not find reroll button\"}";
                }
                else if (skillIndex == -1)
                {
                    // Max health option
                    var menuList = skillSelectMenu.MenuList;
                    for (int i = 0; i < menuList.List.Count; i++)
                    {
                        if (menuList.List[i] == skillSelectMenu.AlternativeBonusButton)
                        {
                            menuList.SelectByIndex(i);
                            TriggerMenuConfirm(menuList);
                            Plugin.Log.LogInfo("ExecuteSkillSelection: Selected max health bonus");

                            // Wait for menu to close and process
                            System.Threading.Thread.Sleep(500);

                            // Continue with post-combat flow
                            return WaitForPostCombatComplete();
                        }
                    }

                    return "{\"success\": false, \"error\": \"Max health option not available\"}";
                }
                else if (skillIndex >= 0 && skillIndex <= 2)
                {
                    // Select a skill
                    var menuList = skillSelectMenu.MenuList;
                    if (skillIndex < menuList.List.Count)
                    {
                        menuList.SelectByIndex(skillIndex);
                        TriggerMenuConfirm(menuList);

                        Plugin.Log.LogInfo($"ExecuteSkillSelection: Selected skill at index {skillIndex}");

                        // Wait for selection to process
                        System.Threading.Thread.Sleep(500);

                        // Check if skill replacement menu opened (when at max skills)
                        // For now, just continue with post-combat flow
                        return WaitForPostCombatComplete();
                    }
                    else
                    {
                        return $"{{\"success\": false, \"error\": \"Invalid skill index: {skillIndex}\"}}";
                    }
                }
                else
                {
                    return $"{{\"success\": false, \"error\": \"Invalid skill index: {skillIndex}. Use 0-2 for skills, -1 for max health.\"}}";
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"ExecuteSkillSelection error: {e}");
                return $"{{\"success\": false, \"error\": \"{EscapeJson(e.Message)}\"}}";
            }
        }
    }
}
