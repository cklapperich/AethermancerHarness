using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace AethermancerHarness
{
    /// <summary>
    /// Combat-related actions: input readiness, action execution, previews, enemy actions.
    /// </summary>
    public static partial class ActionHandler
    {
        // =====================================================
        // COMBAT INPUT READINESS
        // =====================================================

        public static bool IsReadyForInput()
        {
            if (!(GameStateManager.Instance?.IsCombat ?? false))
                return false;

            var currentState = CombatStateManager.Instance?.State?.CurrentState?.ID;
            if (currentState >= CombatStateManager.EState.EndCombatTriggers)
                return false;

            var cc = CombatController.Instance;
            if (cc == null)
                return false;

            if (cc.CurrentMonster == null || !cc.CurrentMonster.BelongsToPlayer)
                return false;

            if (CombatTimeline.Instance?.TriggerStack?.Count > 0)
                return false;

            if (cc.CurrentMonster.State?.ActionInstance != null)
                return false;

            return true;
        }

        public static string GetInputReadyStatus()
        {
            if (!(GameStateManager.Instance?.IsCombat ?? false))
                return InputReadyStatus.NotInCombat.ToString();

            var currentState = CombatStateManager.Instance?.State?.CurrentState?.ID;
            if (currentState >= CombatStateManager.EState.EndCombatTriggers)
                return $"{InputReadyStatus.CombatEnded.ToString()}:{currentState}";

            var cc = CombatController.Instance;
            if (cc == null)
                return InputReadyStatus.NoCombatController.ToString();

            if (cc.CurrentMonster == null)
                return InputReadyStatus.NoCurrentMonster.ToString();
            if (!cc.CurrentMonster.BelongsToPlayer)
                return $"{InputReadyStatus.EnemyTurn.ToString()}:{cc.CurrentMonster.Name}";
            if (CombatTimeline.Instance?.TriggerStack?.Count > 0)
                return $"{InputReadyStatus.TriggersPending.ToString()}:{CombatTimeline.Instance.TriggerStack.Count}";
            if (cc.CurrentMonster.State?.ActionInstance != null)
                return $"{InputReadyStatus.ActionExecuting.ToString()}:{cc.CurrentMonster.State.ActionInstance.Action?.Name}";

            return InputReadyStatus.Ready.ToString();
        }

        public static bool WaitForReady(int timeoutMs = 30000)
        {
            var startTime = DateTime.Now;
            while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
            {
                // Check readiness on main thread, but sleep on calling thread
                bool ready = false;
                if (Plugin.IsMainThread)
                {
                    ready = IsReadyForInput();
                }
                else
                {
                    Plugin.RunOnMainThreadAndWait(() => ready = IsReadyForInput());
                }

                if (ready)
                    return true;

                // Sleep on calling thread (HTTP thread) - this doesn't block Unity
                System.Threading.Thread.Sleep(50);
            }
            return false;
        }

        // =====================================================
        // NAME-BASED RESOLUTION
        // =====================================================

        private static (int index, string error) ResolveActorByName(string name, CombatController cc)
        {
            var displayNames = StateSerializer.GetCombatMonsterNames(cc.PlayerMonsters);
            return StateSerializer.ResolveNameToIndex(name, displayNames, "ally");
        }

        private static (int index, string error) ResolveSkillByName(string name, Monster actor)
        {
            for (int i = 0; i < actor.SkillManager.Actions.Count; i++)
                if (actor.SkillManager.Actions[i].Action.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return (i, null);
            return (-1, $"No skill named '{name}' for {actor.Name}");
        }

        private static (int index, string error) ResolveTargetByName(string name, ETargetType targetType, CombatController cc)
        {
            switch (targetType)
            {
                case ETargetType.SingleEnemy:
                case ETargetType.AllEnemies:
                    var enemyNames = StateSerializer.GetCombatMonsterNames(cc.Enemies, onlyAlive: true);
                    return StateSerializer.ResolveNameToIndex(name, enemyNames, "enemy");

                case ETargetType.SingleAlly:
                case ETargetType.AllAllies:
                    var allyNames = StateSerializer.GetCombatMonsterNames(cc.PlayerMonsters, onlyAlive: true);
                    return StateSerializer.ResolveNameToIndex(name, allyNames, "ally");

                case ETargetType.SelfOrOwner:
                    return (-1, null);

                default:
                    return (-1, $"Cannot resolve target name for target type {targetType}");
            }
        }

        // =====================================================
        // TARGET RESOLUTION
        // =====================================================

        private static (ITargetable target, string error) ResolveTarget(
            ETargetType targetType, int targetIndex, CombatController cc, Monster actor)
        {
            switch (targetType)
            {
                case ETargetType.SingleEnemy:
                    if (targetIndex < 0 || targetIndex >= cc.Enemies.Count)
                        return (null, $"Invalid target index: {targetIndex}");
                    return (cc.Enemies[targetIndex], null);

                case ETargetType.AllEnemies:
                    return (cc.TargetEnemies, null);

                case ETargetType.SingleAlly:
                    if (targetIndex < 0 || targetIndex >= cc.PlayerMonsters.Count)
                        return (null, $"Invalid ally target index: {targetIndex}");
                    return (cc.PlayerMonsters[targetIndex], null);

                case ETargetType.AllAllies:
                    return (cc.TargetPlayerMonsters, null);

                case ETargetType.SelfOrOwner:
                    return (actor, null);

                case ETargetType.AllMonsters:
                    return (cc.TargetAllMonsters, null);

                default:
                    return (null, $"Unsupported target type: {targetType}");
            }
        }

        // =====================================================
        // COMBAT ACTION EXECUTION
        // =====================================================

        public static string ExecuteCombatAction(string actorName, string skillName, string targetName)
        {
            // This method can be called from HTTP thread - it handles its own threading

            string error = null;
            string actionName = null;
            string resolvedActorName = null;
            string resolvedTargetName = null;
            CombatStateSnapshot snapshot = null;

            // Phase 1: Validate and start action (main thread)
            Plugin.RunOnMainThreadAndWait(() =>
            {
                if (!IsReadyForInput())
                {
                    error = JsonConfig.Error("Game not ready for input", new { status = GetInputReadyStatus() });
                    return;
                }

                var cc = CombatController.Instance;

                // Resolve actor by name
                var (actorIndex, actorError) = ResolveActorByName(actorName, cc);
                if (actorError != null)
                {
                    error = JsonConfig.Error(actorError);
                    return;
                }

                var actor = cc.PlayerMonsters[actorIndex];

                // Resolve skill by name
                var (skillIndex, skillError) = ResolveSkillByName(skillName, actor);
                if (skillError != null)
                {
                    error = JsonConfig.Error(skillError);
                    return;
                }

                var skill = actor.SkillManager.Actions[skillIndex];

                if (!skill.Action.CanUseAction(skill))
                {
                    error = JsonConfig.Error($"Skill cannot be used: {skill.Action.Name}");
                    return;
                }

                // Resolve target by name
                var (targetIndex, targetError) = ResolveTargetByName(targetName, skill.Action.TargetType, cc);
                if (targetError != null)
                {
                    error = JsonConfig.Error(targetError);
                    return;
                }

                var (target, resolveError) = ResolveTarget(skill.Action.TargetType, targetIndex, cc, actor);
                if (target == null)
                {
                    error = JsonConfig.Error(resolveError);
                    return;
                }

                // Capture info needed for later phases
                actionName = skill.Action.Name;
                resolvedActorName = actor.Name;
                resolvedTargetName = GetTargetName(target);
                snapshot = UseCondensedState ? CombatStateSnapshot.Capture() : null;

                Plugin.Log.LogInfo($"Executing action: {actor.Name} uses {skill.Action.Name} on {resolvedTargetName}");
                actor.State.StartAction(skill, target, target);
            });

            if (error != null)
                return error;

            // Phase 2: Wait for ready (HTTP thread with main thread checks)
            bool ready = WaitForReady(30000);

            // Phase 3: Check combat result and capture state (main thread)
            string result = null;
            bool combatWon = false;
            Plugin.RunOnMainThreadAndWait(() =>
            {
                var stateManager = CombatStateManager.Instance;
                bool combatEnded = stateManager?.State?.CurrentState?.ID >= CombatStateManager.EState.EndCombatTriggers;
                combatWon = combatEnded && stateManager.WonEncounter;

                if (!combatWon)
                {
                    // Combat not won - build response immediately
                    result = BuildCombatActionResponse(actionName, resolvedActorName, resolvedTargetName, ready, false, snapshot);
                }
            });

            if (result != null)
                return result;

            // Phase 4: Combat was won - handle post-combat on HTTP thread
            Plugin.Log.LogInfo("Combat won, starting post-combat auto-advance");
            return WaitForPostCombatComplete();
        }

        public static string ExecuteConsumableAction(int consumableIndex, int targetIndex)
        {
            // This method can be called from HTTP thread - it handles its own threading

            string error = null;
            string actionName = null;
            string actorName = null;
            string targetName = null;
            CombatStateSnapshot snapshot = null;

            // Phase 1: Validate and start action (main thread)
            Plugin.RunOnMainThreadAndWait(() =>
            {
                if (!IsReadyForInput())
                {
                    error = JsonConfig.Error("Game not ready for input", new { status = GetInputReadyStatus() });
                    return;
                }

                var cc = CombatController.Instance;
                var consumables = PlayerController.Instance?.Inventory?.GetAllConsumables();

                if (consumables == null || consumableIndex < 0 || consumableIndex >= consumables.Count)
                {
                    error = JsonConfig.Error($"Invalid consumable index: {consumableIndex}");
                    return;
                }

                var consumable = consumables[consumableIndex];
                var currentMonster = cc.CurrentMonster;

                if (currentMonster == null)
                {
                    error = JsonConfig.Error("No current monster to use consumable");
                    return;
                }

                consumable.Owner = currentMonster;

                if (!consumable.Action.CanUseAction(consumable))
                {
                    error = JsonConfig.Error($"Consumable cannot be used: {consumable.Consumable?.Name}");
                    return;
                }

                var (target, targetError) = ResolveTarget(consumable.Action.TargetType, targetIndex, cc, currentMonster);
                if (target == null)
                {
                    error = JsonConfig.Error(targetError);
                    return;
                }

                // Capture info needed for later phases
                actionName = consumable.Consumable?.Name ?? "";
                actorName = currentMonster.Name;
                targetName = GetTargetName(target);
                snapshot = UseCondensedState ? CombatStateSnapshot.Capture() : null;

                Plugin.Log.LogInfo($"Using consumable: {actionName} on {targetName}");
                currentMonster.State.StartAction(consumable, target, target);
            });

            if (error != null)
                return error;

            // Phase 2: Wait for ready (HTTP thread with main thread checks)
            bool ready = WaitForReady(30000);

            // Phase 3: Check combat result and capture state (main thread)
            string result = null;
            bool combatWon = false;
            Plugin.RunOnMainThreadAndWait(() =>
            {
                var stateManager = CombatStateManager.Instance;
                bool combatEnded = stateManager?.State?.CurrentState?.ID >= CombatStateManager.EState.EndCombatTriggers;
                combatWon = combatEnded && stateManager.WonEncounter;

                if (!combatWon)
                {
                    // Combat not won - build response immediately
                    result = BuildCombatActionResponse(actionName, actorName, targetName, ready, true, snapshot);
                }
            });

            if (result != null)
                return result;

            // Phase 4: Combat was won - handle post-combat on HTTP thread
            Plugin.Log.LogInfo("Combat won, starting post-combat auto-advance");
            return WaitForPostCombatComplete();
        }

        /// <summary>
        /// Build the response JSON after a combat action completes.
        /// MUST be called on main thread. Does NOT handle combat won case.
        /// </summary>
        private static string BuildCombatActionResponse(string actionName, string actorName, string targetName, bool ready, bool isConsumable, CombatStateSnapshot snapshot)
        {
            var stateManager = CombatStateManager.Instance;
            bool combatEnded = stateManager?.State?.CurrentState?.ID >= CombatStateManager.EState.EndCombatTriggers;

            // Combat won case is handled by caller on HTTP thread
            if (combatEnded && !stateManager.WonEncounter)
            {
                Plugin.Log.LogInfo("Combat lost");
                var defeatResult = new JObject
                {
                    ["success"] = true,
                    ["action"] = actionName,
                    ["combatResult"] = CombatResult.Defeat.ToString(),
                    ["state"] = JObject.Parse(StateSerializer.ToJson())
                };
                return defeatResult.ToString(Newtonsoft.Json.Formatting.None);
            }

            // Check if round changed - if so, use full state (condensed would include enemy action effects)
            var currentRound = CombatController.Instance?.Timeline?.CurrentRound ?? 0;
            bool roundChanged = snapshot != null && snapshot.Round != currentRound;
            bool useCondensed = snapshot != null && !roundChanged;

            var response = new JObject
            {
                ["success"] = true,
                ["action"] = actionName,
                ["actor"] = actorName,
                ["target"] = targetName,
                ["waitedForReady"] = ready,
                ["state"] = useCondensed
                    ? JObject.Parse(StateSerializer.BuildCondensedCombatStateJson(snapshot))
                    : JObject.Parse(StateSerializer.ToJson())
            };
            if (useCondensed)
                response["condensed"] = true;
            if (roundChanged)
                response["roundChanged"] = true;
            if (isConsumable)
                response["isConsumable"] = true;

            return response.ToString(Newtonsoft.Json.Formatting.None);
        }

        // =====================================================
        // PREVIEW SYSTEM
        // =====================================================

        public static string ExecutePreview(string actorName, string skillName, string targetName)
        {
            if (!(GameStateManager.Instance?.IsCombat ?? false))
                return JsonConfig.Error("Not in combat");

            var cc = CombatController.Instance;

            // Resolve actor by name
            var (actorIndex, actorError) = ResolveActorByName(actorName, cc);
            if (actorError != null)
                return JsonConfig.Error(actorError);

            var actor = cc.PlayerMonsters[actorIndex];

            // Resolve skill by name
            var (skillIndex, skillError) = ResolveSkillByName(skillName, actor);
            if (skillError != null)
                return JsonConfig.Error(skillError);

            var skill = actor.SkillManager.Actions[skillIndex];

            if (!skill.Action.CanUseAction(skill))
                return JsonConfig.Error($"Skill cannot be used: {skill.Action.Name}");

            // Resolve target by name
            var (targetIndex, targetError) = ResolveTargetByName(targetName, skill.Action.TargetType, cc);
            if (targetError != null)
                return JsonConfig.Error(targetError);

            var (target, error) = ResolveTarget(skill.Action.TargetType, targetIndex, cc, actor);
            if (target == null)
                return JsonConfig.Error(error);

            Plugin.Log.LogInfo($"Previewing action: {actor.Name} uses {skill.Action.Name} on {GetTargetName(target)}");
            return ExecutePreviewInternal(cc, actor, skill, target);
        }

        public static string ExecuteConsumablePreview(int consumableIndex, int targetIndex)
        {
            if (!(GameStateManager.Instance?.IsCombat ?? false))
                return JsonConfig.Error("Not in combat");

            var cc = CombatController.Instance;
            var consumables = PlayerController.Instance?.Inventory?.GetAllConsumables();

            if (consumables == null || consumableIndex < 0 || consumableIndex >= consumables.Count)
                return JsonConfig.Error($"Invalid consumable index: {consumableIndex}");

            var consumable = consumables[consumableIndex];
            var currentMonster = cc.CurrentMonster;

            if (currentMonster == null)
                return JsonConfig.Error("No current monster for preview");

            consumable.Owner = currentMonster;

            if (!consumable.Action.CanUseAction(consumable))
                return JsonConfig.Error($"Consumable cannot be used: {consumable.Consumable?.Name}");

            var (target, error) = ResolveTarget(consumable.Action.TargetType, targetIndex, cc, currentMonster);
            if (target == null)
                return JsonConfig.Error(error);

            Plugin.Log.LogInfo($"Previewing consumable: {consumable.Consumable?.Name} on {GetTargetName(target)}");
            return ExecutePreviewInternal(cc, currentMonster, consumable, target);
        }

        private static string ExecutePreviewInternal(CombatController cc, Monster actor, SkillInstance actionInstance, ITargetable target)
        {
            var currentMonster = cc.CurrentMonster;

            // Store poise values BEFORE preview
            var poiseBeforePreview = CapturePoiseState(cc);

            // Setup preview state
            cc.CurrentMonster = actor;
            cc.StartPreview();
            CombatVariablesManager.Instance.StartPreview();
            CombatStateManager.Instance.PreviewActionInstance = actionInstance;
            CombatStateManager.Instance.PreviewAction = actionInstance.Action;
            CombatStateManager.Instance.IsPreview = true;
            CombatStateManager.Instance.IsEnemyPreview = false;

            foreach (var m in cc.AllMonsters)
                m.InitializePreview();
            foreach (var s in cc.AllSummons)
                s.InitializePreview();

            // Handle aether cost
            if (actionInstance.GetActionCost().TotalCurrentAether > 0)
            {
                var finalCost = cc.GetAether(actor).GetFinalAetherCost(actionInstance.GetActionCost());
                cc.GetAether(actor).ConsumeAether(finalCost, actor, actionInstance);
            }

            actor.State.StartActionSetup(actionInstance, target);
            CombatTimeline.Instance.ProgressActionPreview();

            // Collect preview data BEFORE cleanup
            var previewResults = CollectPreviewData(cc, poiseBeforePreview);

            // Cleanup
            CombatStateManager.Instance.IsPreview = false;
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
                m.ClearPreview();
            foreach (var s in cc.AllSummons)
                s.ClearPreview();
            foreach (var m in cc.AllMonsters)
                m.Stats.Calculate(updateHealth: false);
            foreach (var s in cc.AllSummons)
                s.Stats.Calculate(updateHealth: false);

            return previewResults;
        }

        private static Dictionary<int, List<(EElement element, int current, int max)>> CapturePoiseState(CombatController cc)
        {
            var result = new Dictionary<int, List<(EElement, int, int)>>();
            for (int i = 0; i < cc.Enemies.Count; i++)
            {
                var enemy = cc.Enemies[i];
                var poiseList = new List<(EElement, int, int)>();
                if (enemy.SkillManager?.Stagger != null)
                {
                    foreach (var stagger in enemy.SkillManager.Stagger)
                        poiseList.Add((stagger.Element, stagger.CurrentPoise, stagger.MaxHits));
                }
                result[i] = poiseList;
            }
            return result;
        }

        private static string CollectPreviewData(CombatController cc, Dictionary<int, List<(EElement, int, int)>> poiseBeforePreview)
        {
            int totalKills = 0, totalBreaks = 0, totalPurgeCancels = 0, totalInterrupts = 0;

            var enemies = new JArray();
            for (int i = 0; i < cc.Enemies.Count; i++)
            {
                var enemy = cc.Enemies[i];
                bool willKill = enemy.Stats.PreviewHealth.ValueInt <= 0;
                int breaks = 0, purgeCancels = 0;

                if (enemy.AI?.PickedActionList != null)
                {
                    foreach (var picked in enemy.AI.PickedActionList)
                    {
                        if (picked.PreviewStaggered) breaks++;
                        if (picked.PreviewPurged) purgeCancels++;
                    }
                }

                if (willKill) totalKills++;
                totalBreaks += breaks;
                totalPurgeCancels += purgeCancels;
                totalInterrupts += (willKill ? (enemy.AI?.PickedActionList?.Count ?? 1) : 0) + breaks + purgeCancels;

                var poiseBefore = poiseBeforePreview.ContainsKey(i) ? poiseBeforePreview[i] : null;
                enemies.Add(BuildEnemyPreview(enemy, i, willKill, breaks, purgeCancels, poiseBefore));
            }

            var allies = new JArray();
            for (int i = 0; i < cc.PlayerMonsters.Count; i++)
                allies.Add(BuildMonsterPreview(cc.PlayerMonsters[i], i));

            var result = new JObject
            {
                ["success"] = true,
                ["preview"] = new JObject
                {
                    ["enemies"] = enemies,
                    ["allies"] = allies,
                    ["interruptSummary"] = new JObject
                    {
                        ["kills"] = totalKills,
                        ["breaks"] = totalBreaks,
                        ["purgeCancels"] = totalPurgeCancels,
                        ["totalInterrupts"] = totalInterrupts
                    }
                }
            };

            return result.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static JObject BuildEnemyPreview(Monster enemy, int index, bool willKill, int breaks, int purgeCancels,
            List<(EElement element, int current, int max)> poiseBefore)
        {
            var obj = BuildMonsterPreview(enemy, index);
            obj["willKill"] = willKill;
            obj["willBreak"] = breaks > 0;
            obj["willPurgeCancel"] = purgeCancels > 0;
            obj["breaksCount"] = breaks;
            obj["purgeCancelsCount"] = purgeCancels;

            var poise = new JArray();
            if (enemy.SkillManager?.Stagger != null && poiseBefore != null)
            {
                int poiseIdx = 0;
                foreach (var stagger in enemy.SkillManager.Stagger)
                {
                    int beforeValue = poiseIdx < poiseBefore.Count ? poiseBefore[poiseIdx].current : stagger.MaxHits;
                    poise.Add(new JObject
                    {
                        ["element"] = stagger.Element.ToString(),
                        ["before"] = beforeValue,
                        ["after"] = stagger.PreviewPoise,
                        ["max"] = stagger.MaxHits
                    });
                    poiseIdx++;
                }
            }
            obj["poise"] = poise;

            return obj;
        }

        private static JObject BuildMonsterPreview(Monster monster, int index)
        {
            int totalDamage = 0, totalHeal = 0, totalShield = 0, hitCount = 0;
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
                        if (!string.IsNullOrEmpty(preview.Text)) buffs.Add(preview.Text);
                        break;
                    case EDamageNumberType.Debuff:
                        if (!string.IsNullOrEmpty(preview.Text)) debuffs.Add(preview.Text);
                        break;
                }
            }

            return new JObject
            {
                ["index"] = index,
                ["name"] = monster.Name,
                ["damage"] = totalDamage,
                ["heal"] = totalHeal,
                ["shield"] = totalShield,
                ["hits"] = hitCount,
                ["hasCrit"] = hasCrit,
                ["currentHp"] = monster.CurrentHealth,
                ["previewHp"] = monster.Stats.PreviewHealth.ValueInt,
                ["buffsApplied"] = new JArray(buffs),
                ["debuffsApplied"] = new JArray(debuffs)
            };
        }

        // =====================================================
        // ENEMY ACTIONS
        // =====================================================

        public static string GetEnemyActions()
        {
            if (!(GameStateManager.Instance?.IsCombat ?? false))
                return JsonConfig.Error("Not in combat");

            var cc = CombatController.Instance;
            var enemies = new JArray();

            for (int e = 0; e < cc.Enemies.Count; e++)
            {
                var enemy = cc.Enemies[e];
                var intentions = new JArray();

                if (enemy.AI?.PickedActionList != null)
                {
                    foreach (var picked in enemy.AI.PickedActionList)
                    {
                        var intent = new JObject
                        {
                            ["skillName"] = picked.Action?.Action?.Name ?? "Unknown",
                            ["targetName"] = GetTargetName(picked.Target),
                            ["targetIndex"] = GetTargetIndex(picked.Target, cc),
                            ["isPurged"] = picked.PreviewPurged,
                            ["isStaggered"] = picked.PreviewStaggered
                        };

                        var actionDamage = picked.Action?.Action?.GetComponent<ActionDamage>();
                        if (actionDamage != null)
                        {
                            int dmgPerHit = GetCalculatedDamage(actionDamage, picked.Action);
                            intent["damagePerHit"] = dmgPerHit;
                            intent["hitCount"] = actionDamage.HitCount;
                            intent["totalDamage"] = dmgPerHit * actionDamage.HitCount;
                        }

                        intentions.Add(intent);
                    }
                }

                var skills = new JArray();
                for (int s = 0; s < enemy.SkillManager.Actions.Count; s++)
                {
                    var skill = enemy.SkillManager.Actions[s];
                    var skillObj = new JObject
                    {
                        ["index"] = s,
                        ["name"] = skill.Action.Name,
                        ["description"] = skill.Action.GetDescription(skill) ?? "",
                        ["targetType"] = skill.Action.TargetType.ToString(),
                        ["elements"] = new JArray(skill.Action.Elements.ConvertAll(el => el.ToString())),
                        ["canUse"] = skill.Action.CanUseAction(skill)
                    };

                    var dmg = skill.Action.GetComponent<ActionDamage>();
                    if (dmg != null)
                    {
                        int dmgPerHit = GetCalculatedDamage(dmg, skill);
                        skillObj["damagePerHit"] = dmgPerHit;
                        skillObj["hitCount"] = dmg.HitCount;
                        skillObj["totalDamage"] = dmgPerHit * dmg.HitCount;
                    }
                    else
                    {
                        skillObj["damagePerHit"] = 0;
                        skillObj["hitCount"] = 0;
                        skillObj["totalDamage"] = 0;
                    }

                    skills.Add(skillObj);
                }

                enemies.Add(new JObject
                {
                    ["index"] = e,
                    ["name"] = enemy.Name,
                    ["hp"] = enemy.CurrentHealth,
                    ["maxHp"] = enemy.Stats.MaxHealth.ValueInt,
                    ["intentions"] = intentions,
                    ["skills"] = skills
                });
            }

            return new JObject { ["success"] = true, ["enemies"] = enemies }.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static int GetTargetIndex(ITargetable target, CombatController cc)
        {
            if (target is Monster m)
            {
                for (int i = 0; i < cc.PlayerMonsters.Count; i++)
                    if (cc.PlayerMonsters[i] == m) return i;
                for (int i = 0; i < cc.Enemies.Count; i++)
                    if (cc.Enemies[i] == m) return i;
            }
            return -1;
        }

        private static int GetCalculatedDamage(ActionDamage actionDamage, SkillInstance skill)
        {
            if (GetDescriptionDamageMethod == null) return 0;
            var result = GetDescriptionDamageMethod.Invoke(actionDamage, new object[] { skill });
            var valueInt = result.GetType().GetProperty("ValueInt").GetValue(result);
            return (int)valueInt;
        }
    }
}
