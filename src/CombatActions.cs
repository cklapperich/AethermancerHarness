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

        public static bool WaitForReady(int timeoutMs = 30000)
        {
            var startTime = DateTime.Now;
            while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
            {
                if (IsReadyForInput())
                    return true;
                System.Threading.Thread.Sleep(50);
            }
            return false;
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

        public static string ExecuteCombatAction(int actorIndex, int skillIndex, int targetIndex)
        {
            if (!IsReadyForInput())
            {
                return JsonHelper.Serialize(new
                {
                    success = false,
                    error = "Game not ready for input",
                    status = GetInputReadyStatus()
                });
            }

            var cc = CombatController.Instance;

            if (actorIndex < 0 || actorIndex >= cc.PlayerMonsters.Count)
                return JsonHelper.Serialize(new { success = false, error = $"Invalid actor index: {actorIndex}" });

            var actor = cc.PlayerMonsters[actorIndex];

            if (skillIndex < 0 || skillIndex >= actor.SkillManager.Actions.Count)
                return JsonHelper.Serialize(new { success = false, error = $"Invalid skill index: {skillIndex}" });

            var skill = actor.SkillManager.Actions[skillIndex];

            if (!skill.Action.CanUseAction(skill))
                return JsonHelper.Serialize(new { success = false, error = $"Skill cannot be used: {skill.Action.Name}" });

            var (target, error) = ResolveTarget(skill.Action.TargetType, targetIndex, cc, actor);
            if (target == null)
                return JsonHelper.Serialize(new { success = false, error });

            Plugin.Log.LogInfo($"Executing action: {actor.Name} uses {skill.Action.Name} on {GetTargetName(target)}");
            var snapshot = UseCondensedState ? CombatStateSnapshot.Capture() : null;
            actor.State.StartAction(skill, target, target);

            return FinishCombatAction(skill.Action.Name, actor.Name, target, snapshot: snapshot);
        }

        public static string ExecuteConsumableAction(int consumableIndex, int targetIndex)
        {
            if (!IsReadyForInput())
            {
                return JsonHelper.Serialize(new
                {
                    success = false,
                    error = "Game not ready for input",
                    status = GetInputReadyStatus()
                });
            }

            var cc = CombatController.Instance;
            var consumables = PlayerController.Instance?.Inventory?.GetAllConsumables();

            if (consumables == null || consumableIndex < 0 || consumableIndex >= consumables.Count)
                return JsonHelper.Serialize(new { success = false, error = $"Invalid consumable index: {consumableIndex}" });

            var consumable = consumables[consumableIndex];
            var currentMonster = cc.CurrentMonster;

            if (currentMonster == null)
                return JsonHelper.Serialize(new { success = false, error = "No current monster to use consumable" });

            consumable.Owner = currentMonster;

            if (!consumable.Action.CanUseAction(consumable))
                return JsonHelper.Serialize(new { success = false, error = $"Consumable cannot be used: {consumable.Consumable?.Name}" });

            var (target, error) = ResolveTarget(consumable.Action.TargetType, targetIndex, cc, currentMonster);
            if (target == null)
                return JsonHelper.Serialize(new { success = false, error });

            Plugin.Log.LogInfo($"Using consumable: {consumable.Consumable?.Name} on {GetTargetName(target)}");
            var snapshot = UseCondensedState ? CombatStateSnapshot.Capture() : null;
            currentMonster.State.StartAction(consumable, target, target);

            return FinishCombatAction(consumable.Consumable?.Name ?? "", currentMonster.Name, target, isConsumable: true, snapshot: snapshot);
        }

        private static string FinishCombatAction(string actionName, string actorName, ITargetable target, bool isConsumable = false, CombatStateSnapshot snapshot = null)
        {
            bool ready = WaitForReady(30000);

            var stateManager = CombatStateManager.Instance;
            bool combatEnded = stateManager?.State?.CurrentState?.ID >= CombatStateManager.EState.EndCombatTriggers;

            if (combatEnded && stateManager.WonEncounter)
            {
                Plugin.Log.LogInfo("Combat won, starting post-combat auto-advance");
                return WaitForPostCombatComplete();
            }

            if (combatEnded && !stateManager.WonEncounter)
            {
                Plugin.Log.LogInfo("Combat lost");
                var result = new JObject
                {
                    ["success"] = true,
                    ["action"] = actionName,
                    ["combatResult"] = "DEFEAT",
                    ["state"] = JObject.Parse(StateSerializer.ToJson())
                };
                return result.ToString(Newtonsoft.Json.Formatting.None);
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
                ["target"] = GetTargetName(target),
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

        public static string ExecutePreview(int actorIndex, int skillIndex, int targetIndex)
        {
            if (!(GameStateManager.Instance?.IsCombat ?? false))
                return JsonHelper.Serialize(new { success = false, error = "Not in combat" });

            var cc = CombatController.Instance;

            if (actorIndex < 0 || actorIndex >= cc.PlayerMonsters.Count)
                return JsonHelper.Serialize(new { success = false, error = $"Invalid actor index: {actorIndex}" });

            var actor = cc.PlayerMonsters[actorIndex];

            if (skillIndex < 0 || skillIndex >= actor.SkillManager.Actions.Count)
                return JsonHelper.Serialize(new { success = false, error = $"Invalid skill index: {skillIndex}" });

            var skill = actor.SkillManager.Actions[skillIndex];

            if (!skill.Action.CanUseAction(skill))
                return JsonHelper.Serialize(new { success = false, error = $"Skill cannot be used: {skill.Action.Name}" });

            var (target, error) = ResolveTarget(skill.Action.TargetType, targetIndex, cc, actor);
            if (target == null)
                return JsonHelper.Serialize(new { success = false, error });

            Plugin.Log.LogInfo($"Previewing action: {actor.Name} uses {skill.Action.Name} on {GetTargetName(target)}");
            return ExecutePreviewInternal(cc, actor, skill, target);
        }

        public static string ExecuteConsumablePreview(int consumableIndex, int targetIndex)
        {
            if (!(GameStateManager.Instance?.IsCombat ?? false))
                return JsonHelper.Serialize(new { success = false, error = "Not in combat" });

            var cc = CombatController.Instance;
            var consumables = PlayerController.Instance?.Inventory?.GetAllConsumables();

            if (consumables == null || consumableIndex < 0 || consumableIndex >= consumables.Count)
                return JsonHelper.Serialize(new { success = false, error = $"Invalid consumable index: {consumableIndex}" });

            var consumable = consumables[consumableIndex];
            var currentMonster = cc.CurrentMonster;

            if (currentMonster == null)
                return JsonHelper.Serialize(new { success = false, error = "No current monster for preview" });

            consumable.Owner = currentMonster;

            if (!consumable.Action.CanUseAction(consumable))
                return JsonHelper.Serialize(new { success = false, error = $"Consumable cannot be used: {consumable.Consumable?.Name}" });

            var (target, error) = ResolveTarget(consumable.Action.TargetType, targetIndex, cc, currentMonster);
            if (target == null)
                return JsonHelper.Serialize(new { success = false, error });

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
                return JsonHelper.Serialize(new { success = false, error = "Not in combat" });

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
