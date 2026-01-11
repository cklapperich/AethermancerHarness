using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AethermancerHarness
{
    /// <summary>
    /// Combat-related actions: action execution, previews, enemy actions.
    /// Input readiness is now handled by the unified system in ReadinessActions.cs.
    /// </summary>
    public static partial class ActionHandler
    {
        // =====================================================
        // NAME-BASED RESOLUTION (Identity-based)
        // =====================================================

        private static Func<Monster, string> MonsterNameFunc = m => StateSerializer.StripMarkup(m.Name);
        private static Func<Monster, string> AliveMonsterNameFunc = m => (m.State?.IsDead ?? false) ? null : StateSerializer.StripMarkup(m.Name);

        private static (Monster actor, string error) ResolveActorByName(string name, CombatController cc)
        {
            var actor = StateSerializer.FindByDisplayName(name, cc.PlayerMonsters, MonsterNameFunc);
            if (actor == null)
            {
                var available = StateSerializer.GetAllDisplayNames(cc.PlayerMonsters, MonsterNameFunc);
                return (null, $"No ally named '{name}'. Available: {string.Join(", ", available)}");
            }
            return (actor, null);
        }

        private static (int index, string error) ResolveSkillByName(string name, Monster actor)
        {
            for (int i = 0; i < actor.SkillManager.Actions.Count; i++)
                if (actor.SkillManager.Actions[i].Action.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return (i, null);
            return (-1, $"No skill named '{name}' for {actor.Name}");
        }

        private static (Monster target, string error) ResolveTargetByName(string name, ETargetType targetType, CombatController cc, Monster actor)
        {
            switch (targetType)
            {
                case ETargetType.SingleEnemy:
                case ETargetType.AllEnemies:
                    var aliveEnemies = cc.Enemies.Where(e => !(e.State?.IsDead ?? false)).ToList();
                    var enemy = StateSerializer.FindByDisplayName(name, aliveEnemies, MonsterNameFunc);
                    if (enemy == null)
                    {
                        var available = StateSerializer.GetAllDisplayNames(aliveEnemies, MonsterNameFunc);
                        return (null, $"No enemy named '{name}'. Available: {string.Join(", ", available)}");
                    }
                    return (enemy, null);

                case ETargetType.SingleAlly:
                case ETargetType.AllAllies:
                    var aliveAllies = cc.PlayerMonsters.Where(m => !(m.State?.IsDead ?? false)).ToList();
                    var ally = StateSerializer.FindByDisplayName(name, aliveAllies, MonsterNameFunc);
                    if (ally == null)
                    {
                        var available = StateSerializer.GetAllDisplayNames(aliveAllies, MonsterNameFunc);
                        return (null, $"No ally named '{name}'. Available: {string.Join(", ", available)}");
                    }
                    return (ally, null);

                case ETargetType.SelfOrOwner:
                    return (actor, null);

                default:
                    return (null, $"Cannot resolve target name for target type {targetType}");
            }
        }

        // =====================================================
        // TARGET RESOLUTION
        // =====================================================

        private static (ITargetable target, string error) ResolveTargetable(
            ETargetType targetType, Monster resolvedTarget, CombatController cc, Monster actor)
        {
            switch (targetType)
            {
                case ETargetType.SingleEnemy:
                case ETargetType.SingleAlly:
                    return (resolvedTarget, null);

                case ETargetType.AllEnemies:
                    return (cc.TargetEnemies, null);

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
                var readiness = GetReadinessState();
                if (!readiness.Ready)
                {
                    error = JsonConfig.Error("Game not ready for input", new { status = readiness.BlockReason, phase = readiness.Phase });
                    return;
                }

                var cc = CombatController.Instance;

                // Resolve actor by name (now returns Monster directly)
                var (actor, actorError) = ResolveActorByName(actorName, cc);
                if (actorError != null)
                {
                    error = JsonConfig.Error(actorError);
                    return;
                }

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

                // Resolve target by name (now returns Monster directly)
                var (resolvedMonster, targetError) = ResolveTargetByName(targetName, skill.Action.TargetType, cc, actor);
                if (targetError != null)
                {
                    error = JsonConfig.Error(targetError);
                    return;
                }

                var (target, resolveError) = ResolveTargetable(skill.Action.TargetType, resolvedMonster, cc, actor);
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

            // Phase 2: Wait until ready or combat ends
            WaitUntilReady(30000);

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
                    result = BuildCombatActionResponse(actionName, resolvedActorName, resolvedTargetName, false, snapshot);
                }
            });

            if (result != null)
                return result;

            // Phase 4: Combat was won - handle post-combat on HTTP thread
            Plugin.Log.LogInfo("Combat won, starting post-combat auto-advance");
            return WaitForPostCombatComplete();
        }

        private static Func<ConsumableInstance, string> ConsumableNameFunc = c =>
            StateSerializer.StripMarkup(c.Consumable?.Name ?? c.Skill?.Name ?? "Unknown");

        public static string ExecuteConsumableAction(string consumableName, string targetName)
        {
            // This method can be called from HTTP thread - it handles its own threading

            string error = null;
            string actionName = null;
            string actorName = null;
            string resolvedTargetName = null;
            CombatStateSnapshot snapshot = null;

            // Phase 1: Validate and start action (main thread)
            Plugin.RunOnMainThreadAndWait(() =>
            {
                var readiness = GetReadinessState();
                if (!readiness.Ready)
                {
                    error = JsonConfig.Error("Game not ready for input", new { status = readiness.BlockReason, phase = readiness.Phase });
                    return;
                }

                var cc = CombatController.Instance;
                var consumables = PlayerController.Instance?.Inventory?.GetAllConsumables();

                if (consumables == null)
                {
                    error = JsonConfig.Error("No consumables available");
                    return;
                }

                // Find consumable by name using identity-based lookup
                var consumable = StateSerializer.FindByDisplayName(consumableName, consumables, ConsumableNameFunc);
                if (consumable == null)
                {
                    var available = StateSerializer.GetAllDisplayNames(consumables, ConsumableNameFunc);
                    error = JsonConfig.Error($"No consumable named '{consumableName}'. Available: {string.Join(", ", available)}");
                    return;
                }

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

                var (resolvedMonster, targetError) = ResolveTargetByName(targetName, consumable.Action.TargetType, cc, currentMonster);
                if (targetError != null)
                {
                    error = JsonConfig.Error(targetError);
                    return;
                }

                var (target, resolveError) = ResolveTargetable(consumable.Action.TargetType, resolvedMonster, cc, currentMonster);
                if (target == null)
                {
                    error = JsonConfig.Error(resolveError);
                    return;
                }

                // Capture info needed for later phases
                actionName = consumable.Consumable?.Name ?? "";
                actorName = currentMonster.Name;
                resolvedTargetName = GetTargetName(target);
                snapshot = UseCondensedState ? CombatStateSnapshot.Capture() : null;

                Plugin.Log.LogInfo($"Using consumable: {actionName} on {resolvedTargetName}");
                currentMonster.State.StartAction(consumable, target, target);
            });

            if (error != null)
                return error;

            // Phase 2: Wait until ready or combat ends
            WaitUntilReady(30000);

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
                    result = BuildCombatActionResponse(actionName, actorName, resolvedTargetName, true, snapshot);
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
        private static string BuildCombatActionResponse(string actionName, string actorName, string targetName, bool isConsumable, CombatStateSnapshot snapshot)
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

            // Resolve actor by name (now returns Monster directly)
            var (actor, actorError) = ResolveActorByName(actorName, cc);
            if (actorError != null)
                return JsonConfig.Error(actorError);

            // Resolve skill by name
            var (skillIndex, skillError) = ResolveSkillByName(skillName, actor);
            if (skillError != null)
                return JsonConfig.Error(skillError);

            var skill = actor.SkillManager.Actions[skillIndex];

            if (!skill.Action.CanUseAction(skill))
                return JsonConfig.Error($"Skill cannot be used: {skill.Action.Name}");

            // Resolve target by name (now returns Monster directly)
            var (resolvedMonster, targetError) = ResolveTargetByName(targetName, skill.Action.TargetType, cc, actor);
            if (targetError != null)
                return JsonConfig.Error(targetError);

            var (target, error) = ResolveTargetable(skill.Action.TargetType, resolvedMonster, cc, actor);
            if (target == null)
                return JsonConfig.Error(error);

            Plugin.Log.LogInfo($"Previewing action: {actor.Name} uses {skill.Action.Name} on {GetTargetName(target)}");
            return ExecutePreviewInternal(cc, actor, skill, target);
        }

        public static string ExecuteConsumablePreview(string consumableName, string targetName)
        {
            if (!(GameStateManager.Instance?.IsCombat ?? false))
                return JsonConfig.Error("Not in combat");

            var cc = CombatController.Instance;
            var consumables = PlayerController.Instance?.Inventory?.GetAllConsumables();

            if (consumables == null)
                return JsonConfig.Error("No consumables available");

            // Find consumable by name using identity-based lookup
            var consumable = StateSerializer.FindByDisplayName(consumableName, consumables, ConsumableNameFunc);
            if (consumable == null)
            {
                var available = StateSerializer.GetAllDisplayNames(consumables, ConsumableNameFunc);
                return JsonConfig.Error($"No consumable named '{consumableName}'. Available: {string.Join(", ", available)}");
            }

            var currentMonster = cc.CurrentMonster;

            if (currentMonster == null)
                return JsonConfig.Error("No current monster for preview");

            consumable.Owner = currentMonster;

            if (!consumable.Action.CanUseAction(consumable))
                return JsonConfig.Error($"Consumable cannot be used: {consumable.Consumable?.Name}");

            var (resolvedMonster, targetError) = ResolveTargetByName(targetName, consumable.Action.TargetType, cc, currentMonster);
            if (targetError != null)
                return JsonConfig.Error(targetError);

            var (target, error) = ResolveTargetable(consumable.Action.TargetType, resolvedMonster, cc, currentMonster);
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
