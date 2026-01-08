using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace AethermancerHarness
{
    public static class ActionHandler
    {
        // Flag to toggle condensed state responses
        public static bool UseCondensedState { get; set; } = true;

        // Cached reflection methods
        private static readonly MethodInfo GetDescriptionDamageMethod;
        private static readonly MethodInfo ContinueMethod;
        private static readonly MethodInfo InputConfirmMethod;
        private static readonly MethodInfo ConfirmVoidBlitzTargetMethod;
        private static readonly MethodInfo ConfirmSelectionMethod;

        static ActionHandler()
        {
            GetDescriptionDamageMethod = typeof(ActionDamage).GetMethod(
                "GetDescriptionDamage",
                BindingFlags.NonPublic | BindingFlags.Instance);

            ContinueMethod = typeof(PostCombatMenu).GetMethod(
                "Continue",
                BindingFlags.NonPublic | BindingFlags.Instance);

            InputConfirmMethod = typeof(MenuList).GetMethod(
                "InputConfirm",
                BindingFlags.NonPublic | BindingFlags.Instance);

            ConfirmVoidBlitzTargetMethod = typeof(PlayerMovementController).GetMethod(
                "ConfirmVoidBlitzTarget",
                BindingFlags.NonPublic | BindingFlags.Instance);

            ConfirmSelectionMethod = typeof(MonsterShrineMenu).GetMethod(
                "ConfirmSelection",
                BindingFlags.NonPublic | BindingFlags.Instance);
        }

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

        /// <summary>
        /// Resolve target based on target type. Returns null with error message if invalid.
        /// </summary>
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

        private static string GetTargetName(ITargetable target)
        {
            if (target is Monster m)
                return m.Name;
            if (target is MonsterList ml)
                return $"[{ml.Monsters.Count} targets]";
            return "unknown";
        }

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

        public static string ExecuteVoidBlitz(int monsterGroupIndex, int monsterIndex = 0)
        {
            if (GameStateManager.Instance?.IsCombat ?? true)
                return JsonHelper.Serialize(new { success = false, error = "Cannot void blitz during combat" });

            if (!GameStateManager.Instance.IsExploring)
                return JsonHelper.Serialize(new { success = false, error = "Must be in exploration mode to void blitz" });

            var groups = ExplorationController.Instance?.EncounterGroups;
            if (groups == null || groups.Count == 0)
                return JsonHelper.Serialize(new { success = false, error = "No monster groups available" });

            if (monsterGroupIndex < 0 || monsterGroupIndex >= groups.Count)
                return JsonHelper.Serialize(new { success = false, error = $"Invalid monster group index: {monsterGroupIndex}. Valid range: 0-{groups.Count - 1}" });

            var targetGroup = groups[monsterGroupIndex];
            if (targetGroup == null)
                return JsonHelper.Serialize(new { success = false, error = "Monster group is null" });

            if (targetGroup.EncounterDefeated)
                return JsonHelper.Serialize(new { success = false, error = "Monster group already defeated" });

            if (targetGroup.OverworldMonsters == null || targetGroup.OverworldMonsters.Count == 0)
                return JsonHelper.Serialize(new { success = false, error = "Monster group has no overworld monsters" });

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
                return JsonHelper.Serialize(new { success = false, error = "No active monsters in group" });

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

            return JsonHelper.Serialize(new
            {
                success = true,
                action = "void_blitz",
                monsterGroupIndex,
                targetMonster = targetMonsterName,
                note = "Animation playing, combat will start shortly"
            });
        }

        public static string ExecuteStartCombat(int monsterGroupIndex)
        {
            if (GameStateManager.Instance?.IsCombat ?? true)
                return JsonHelper.Serialize(new { success = false, error = "Already in combat" });

            var groups = ExplorationController.Instance?.EncounterGroups;
            if (groups == null || groups.Count == 0)
                return JsonHelper.Serialize(new { success = false, error = "No monster groups available" });

            if (monsterGroupIndex < 0 || monsterGroupIndex >= groups.Count)
                return JsonHelper.Serialize(new { success = false, error = $"Invalid monster group index: {monsterGroupIndex}. Valid range: 0-{groups.Count - 1}" });

            var targetGroup = groups[monsterGroupIndex];
            if (targetGroup == null)
                return JsonHelper.Serialize(new { success = false, error = "Monster group is null" });

            if (targetGroup.EncounterDefeated)
                return JsonHelper.Serialize(new { success = false, error = "Monster group already defeated" });

            Plugin.Log.LogInfo($"ExecuteStartCombat: Starting combat with group {monsterGroupIndex}");
            targetGroup.StartCombat(aetherBlitzed: false, null, ignoreGameState: true);

            return JsonHelper.Serialize(new { success = true, action = "start_combat", monsterGroupIndex });
        }

        public static string ExecuteTeleport(float x, float y, float z)
        {
            if (GameStateManager.Instance?.IsCombat ?? false)
                return JsonHelper.Serialize(new { success = false, error = "Cannot teleport during combat" });

            var playerMovement = PlayerMovementController.Instance;
            if (playerMovement == null)
                return JsonHelper.Serialize(new { success = false, error = "PlayerMovementController not available" });

            var oldPos = playerMovement.transform.position;
            var newPos = new UnityEngine.Vector3(x, y, z);
            playerMovement.transform.position = newPos;

            Plugin.Log.LogInfo($"Teleported player from ({oldPos.x:F1}, {oldPos.y:F1}, {oldPos.z:F1}) to ({x:F1}, {y:F1}, {z:F1})");

            return JsonHelper.Serialize(new
            {
                success = true,
                from = new { x = (double)oldPos.x, y = (double)oldPos.y, z = (double)oldPos.z },
                to = new { x = (double)x, y = (double)y, z = (double)z }
            });
        }

        public static string ExecuteInteract()
        {
            if (GameStateManager.Instance?.IsCombat ?? false)
                return JsonHelper.Serialize(new { success = false, error = "Cannot interact during combat" });

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
                        return JsonHelper.Serialize(new { success = true, action = "shrine_interact", type = "MONSTER_SHRINE" });
                    }
                    else
                    {
                        return JsonHelper.Serialize(new { success = true, action = "shrine_interact_pending", type = "MONSTER_SHRINE", note = "Shrine triggered, menu may still be opening" });
                    }
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogError($"ExecuteInteract: Shrine interaction failed: {ex.Message}");
                    return JsonHelper.Serialize(new { success = false, error = $"Shrine interaction failed: {ex.Message}" });
                }
            }

            // Fallback to generic interact for other interactables
            PlayerController.Instance?.OnInteract();
            return JsonHelper.Serialize(new { success = true, action = "interact" });
        }

        // =====================================================
        // POST-COMBAT AUTO-ADVANCE AND SKILL SELECTION
        // =====================================================

        public static string WaitForPostCombatComplete(int timeoutMs = 60000)
        {
            var startTime = DateTime.Now;
            Plugin.Log.LogInfo("WaitForPostCombatComplete: Starting post-combat auto-advance");

            while (!IsPostCombatMenuOpen())
            {
                if (TimedOut(startTime, timeoutMs))
                {
                    Plugin.Log.LogWarning("WaitForPostCombatComplete: Timeout waiting for PostCombatMenu");
                    return JsonHelper.Serialize(new { success = false, error = "Timeout waiting for PostCombatMenu to open", phase = "TIMEOUT" });
                }

                if (IsInExploration())
                {
                    Plugin.Log.LogInfo("WaitForPostCombatComplete: Back in exploration (no post-combat screens)");
                    return CreateExplorationResult("VICTORY");
                }

                System.Threading.Thread.Sleep(100);
            }

            Plugin.Log.LogInfo("WaitForPostCombatComplete: PostCombatMenu is open");
            return ProcessPostCombatStates(startTime, timeoutMs);
        }

        private static string ProcessPostCombatStates(DateTime startTime, int timeoutMs)
        {
            var postCombatMenu = UIController.Instance.PostCombatMenu;

            while (true)
            {
                if (TimedOut(startTime, timeoutMs))
                    return JsonHelper.Serialize(new { success = false, error = "Timeout waiting for post-combat processing", phase = "TIMEOUT" });

                if (StateSerializer.IsInSkillSelection())
                {
                    Plugin.Log.LogInfo("ProcessPostCombatStates: Skill selection menu is open");
                    return StateSerializer.GetSkillSelectionStateJson();
                }

                if (IsInExploration())
                {
                    Plugin.Log.LogInfo("ProcessPostCombatStates: Back in exploration");
                    return CreateExplorationResult("VICTORY");
                }

                var currentState = postCombatMenu.CurrentState;
                Plugin.Log.LogInfo($"ProcessPostCombatStates: Current state = {currentState}");

                switch (currentState)
                {
                    case PostCombatMenu.EPostCombatMenuState.WorthinessUI:
                    case PostCombatMenu.EPostCombatMenuState.WorthinessUIDetailed:
                        if (WaitForWorthinessCanContinue(postCombatMenu, startTime, timeoutMs))
                        {
                            Plugin.Log.LogInfo("ProcessPostCombatStates: Worthiness CanContinue, triggering Continue");
                            TriggerContinue(postCombatMenu);
                            System.Threading.Thread.Sleep(200);
                        }
                        break;

                    case PostCombatMenu.EPostCombatMenuState.LevelUpUI:
                        if (WaitForLevelUpCanContinue(postCombatMenu, startTime, timeoutMs))
                        {
                            var pendingLevelUp = FindFirstPendingLevelUp(postCombatMenu);
                            if (pendingLevelUp != null)
                            {
                                Plugin.Log.LogInfo("ProcessPostCombatStates: Found pending level up, opening skill selection");
                                OpenSkillSelectionForMonster(pendingLevelUp);

                                if (WaitForSkillSelectionOpen(startTime, timeoutMs))
                                    return StateSerializer.GetSkillSelectionStateJson();
                            }
                            else
                            {
                                Plugin.Log.LogInfo("ProcessPostCombatStates: No pending level ups, continuing");
                                TriggerContinue(postCombatMenu);
                                System.Threading.Thread.Sleep(200);
                            }
                        }
                        break;

                    case PostCombatMenu.EPostCombatMenuState.SkillSelectionUI:
                        Plugin.Log.LogInfo("ProcessPostCombatStates: In SkillSelectionUI state");
                        System.Threading.Thread.Sleep(100);
                        break;
                }

                System.Threading.Thread.Sleep(100);
            }
        }

        private static bool IsPostCombatMenuOpen()
        {
            return UIController.Instance?.PostCombatMenu?.IsOpen ?? false;
        }

        private static bool IsInExploration()
        {
            bool notInCombat = !(GameStateManager.Instance?.IsCombat ?? false);
            bool notInPostCombat = !IsPostCombatMenuOpen();
            return notInCombat && notInPostCombat;
        }

        private static bool WaitForWorthinessCanContinue(PostCombatMenu postCombatMenu, DateTime startTime, int timeoutMs)
        {
            while (true)
            {
                if (TimedOut(startTime, timeoutMs)) return false;

                bool allCanContinue = true;
                foreach (var info in postCombatMenu.PostCombatMonsterInfos)
                {
                    if (info.monster != null && info.gameObject.activeSelf && !info.WorthinessUI.CanContinue)
                    {
                        allCanContinue = false;
                        break;
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

                bool allCanContinue = true;
                foreach (var info in postCombatMenu.PostCombatMonsterInfos)
                {
                    if (info.monster != null && info.gameObject.activeSelf && !info.LevelUpUI.CanContinue)
                    {
                        allCanContinue = false;
                        break;
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
                    return info;
            }
            return null;
        }

        private static void OpenSkillSelectionForMonster(PostCombatMonsterInfo monsterInfo)
        {
            var menuListItem = monsterInfo.LevelUpUI.MenuListItem;
            if (menuListItem != null)
            {
                var postCombatMenu = UIController.Instance.PostCombatMenu;
                postCombatMenu.MenuList.SelectByIndex(0);
                TriggerMenuConfirm(postCombatMenu.MenuList);
                Plugin.Log.LogInfo($"OpenSkillSelectionForMonster: Triggered skill selection for {monsterInfo.monster?.Name}");
            }
        }

        private static void TriggerMenuConfirm(MenuList menuList)
        {
            if (InputConfirmMethod != null)
            {
                InputConfirmMethod.Invoke(menuList, null);
                Plugin.Log.LogInfo("TriggerMenuConfirm: Called InputConfirm()");
            }
        }

        private static bool WaitForSkillSelectionOpen(DateTime startTime, int timeoutMs)
        {
            while (!TimedOut(startTime, timeoutMs))
            {
                if (StateSerializer.IsInSkillSelection())
                    return true;
                System.Threading.Thread.Sleep(100);
            }
            return false;
        }

        private static void TriggerContinue(PostCombatMenu postCombatMenu)
        {
            if (ContinueMethod != null)
            {
                ContinueMethod.Invoke(postCombatMenu, null);
                Plugin.Log.LogInfo("TriggerContinue: Called Continue()");
            }
        }

        private static bool TimedOut(DateTime startTime, int timeoutMs)
        {
            return (DateTime.Now - startTime).TotalMilliseconds >= timeoutMs;
        }

        private static string CreateExplorationResult(string combatResult)
        {
            var result = new JObject
            {
                ["success"] = true,
                ["combatResult"] = combatResult,
                ["state"] = JObject.Parse(StateSerializer.ToJson())
            };
            return result.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static string ExecuteSkillSelection(int skillIndex, bool reroll = false)
        {
            if (!StateSerializer.IsInSkillSelection())
                return JsonHelper.Serialize(new { success = false, error = "Not in skill selection screen" });

            var skillSelectMenu = UIController.Instance.PostCombatMenu.SkillSelectMenu;

            if (reroll)
            {
                if (InventoryManager.Instance.SkillRerolls <= 0)
                    return JsonHelper.Serialize(new { success = false, error = "No skill rerolls available" });

                var menuList = skillSelectMenu.MenuList;
                for (int i = 0; i < menuList.List.Count; i++)
                {
                    if (menuList.List[i] == skillSelectMenu.RerollSkillsButton)
                    {
                        menuList.SelectByIndex(i);
                        TriggerMenuConfirm(menuList);
                        Plugin.Log.LogInfo("ExecuteSkillSelection: Rerolled skills");
                        System.Threading.Thread.Sleep(500);
                        return StateSerializer.GetSkillSelectionStateJson();
                    }
                }
                return JsonHelper.Serialize(new { success = false, error = "Could not find reroll button" });
            }

            if (skillIndex == -1)
            {
                var menuList = skillSelectMenu.MenuList;
                for (int i = 0; i < menuList.List.Count; i++)
                {
                    if (menuList.List[i] == skillSelectMenu.AlternativeBonusButton)
                    {
                        menuList.SelectByIndex(i);
                        TriggerMenuConfirm(menuList);
                        Plugin.Log.LogInfo("ExecuteSkillSelection: Selected max health bonus");
                        System.Threading.Thread.Sleep(500);
                        return WaitForPostCombatComplete();
                    }
                }
                return JsonHelper.Serialize(new { success = false, error = "Max health option not available" });
            }

            if (skillIndex >= 0 && skillIndex <= 2)
            {
                var menuList = skillSelectMenu.MenuList;
                if (skillIndex < menuList.List.Count)
                {
                    menuList.SelectByIndex(skillIndex);
                    TriggerMenuConfirm(menuList);
                    Plugin.Log.LogInfo($"ExecuteSkillSelection: Selected skill at index {skillIndex}");
                    System.Threading.Thread.Sleep(500);
                    return WaitForPostCombatComplete();
                }
                return JsonHelper.Serialize(new { success = false, error = $"Invalid skill index: {skillIndex}" });
            }

            return JsonHelper.Serialize(new { success = false, error = $"Invalid skill index: {skillIndex}. Use 0-2 for skills, -1 for max health." });
        }

        // =====================================================
        // UNIFIED CHOICE HANDLER
        // =====================================================

        /// <summary>
        /// Unified choice handler that routes to the appropriate handler based on game state.
        /// Handles dialogue choices, equipment selection, monster selection, and other choice-based interactions.
        /// </summary>
        /// <param name="choiceIndex">Index of the choice to select</param>
        /// <param name="shift">Optional shift for monster selection: "normal" or "shifted"</param>
        public static string ExecuteChoice(int choiceIndex, string shift = null)
        {
            // Check equipment selection first (after picking equipment from dialogue/loot)
            if (StateSerializer.IsInEquipmentSelection())
            {
                Plugin.Log.LogInfo($"ExecuteChoice: Routing to equipment selection handler (index {choiceIndex})");
                return ExecuteEquipmentChoice(choiceIndex);
            }

            // Check monster selection (shrine/starter)
            if (StateSerializer.IsInMonsterSelection())
            {
                Plugin.Log.LogInfo($"ExecuteChoice: Routing to monster selection handler (index {choiceIndex}, shift: {shift ?? "default"})");
                return ExecuteMonsterSelectionChoice(choiceIndex, shift);
            }

            // Check dialogue
            if (IsDialogueOpen())
            {
                Plugin.Log.LogInfo($"ExecuteChoice: Routing to dialogue choice handler (index {choiceIndex})");
                return ExecuteDialogueChoice(choiceIndex);
            }

            return JsonHelper.Serialize(new { success = false, error = "No active choice context (not in dialogue, equipment selection, or monster selection)" });
        }

        // =====================================================
        // MONSTER SELECTION (Shrine/Starter)
        // =====================================================

        /// <summary>
        /// Handle monster selection from shrine or starter selection screen.
        /// Auto-confirms the selection (skips confirmation popup).
        /// </summary>
        /// <param name="choiceIndex">Index of the monster choice</param>
        /// <param name="shift">Optional shift: "normal" or "shifted". If not specified, uses normal.</param>
        public static string ExecuteMonsterSelectionChoice(int choiceIndex, string shift = null)
        {
            var menu = UIController.Instance?.MonsterShrineMenu;
            if (menu == null || !menu.IsOpen)
                return JsonHelper.Serialize(new { success = false, error = "Monster selection menu not open" });

            var selection = menu.MonsterSelection;
            var displayedMonsters = menu.DisplayedMonsters;

            if (displayedMonsters == null || displayedMonsters.Count == 0)
                return JsonHelper.Serialize(new { success = false, error = "No monsters available" });

            // Calculate total count including random entry
            int totalCount = displayedMonsters.Count + (selection.HasRandomMonster ? 1 : 0);

            // Validate choice index
            if (choiceIndex < 0 || choiceIndex >= totalCount)
                return JsonHelper.Serialize(new { success = false, error = $"Invalid choice index: {choiceIndex}. Valid range: 0-{totalCount - 1}" });

            // Check if selecting random
            var isRandom = selection.HasRandomMonster && choiceIndex == selection.RandomMonsterPosition;

            // Get monster name - for random, use "Random Monster"; otherwise adjust index to skip random
            string monsterName;
            Monster selectedMonster = null;
            if (isRandom)
            {
                monsterName = "Random Monster";
            }
            else
            {
                // Adjust index: if random exists and we're after its position, subtract 1
                int adjustedIndex = choiceIndex;
                if (selection.HasRandomMonster && choiceIndex > selection.RandomMonsterPosition)
                {
                    adjustedIndex = choiceIndex - 1;
                }
                selectedMonster = displayedMonsters[adjustedIndex];
                monsterName = selectedMonster?.Name ?? "Unknown";
            }

            // Parse shift parameter
            EMonsterShift targetShift = EMonsterShift.Normal;
            if (!string.IsNullOrEmpty(shift))
            {
                if (shift.Equals("shifted", System.StringComparison.OrdinalIgnoreCase))
                {
                    targetShift = EMonsterShift.Shifted;
                }
                else if (!shift.Equals("normal", System.StringComparison.OrdinalIgnoreCase))
                {
                    return JsonHelper.Serialize(new { success = false, error = $"Invalid shift value: '{shift}'. Use 'normal' or 'shifted'." });
                }
            }

            // Validate shifted variant is available if requested
            if (targetShift == EMonsterShift.Shifted && !isRandom)
            {
                bool hasShiftedVariant = InventoryManager.Instance?.HasShiftedMementosOfMonster(selectedMonster) ?? false;
                if (!hasShiftedVariant)
                {
                    return JsonHelper.Serialize(new { success = false, error = $"Shifted variant not available for {monsterName}" });
                }
            }

            Plugin.Log.LogInfo($"ExecuteMonsterSelectionChoice: Selecting monster at index {choiceIndex}: {monsterName} (isRandom: {isRandom}, shift: {targetShift})");

            try
            {
                // Run UI operations on main thread
                Plugin.RunOnMainThreadAndWait(() =>
                {
                    // Set the selection index
                    selection.SetSelectedIndex(choiceIndex);

                    // Set the shift (only applies to non-random monsters with shifted variants)
                    if (!isRandom && targetShift != selection.CurrentShift)
                    {
                        selection.CurrentShift = targetShift;
                        selectedMonster.SetShift(targetShift);
                        Plugin.Log.LogInfo($"ExecuteMonsterSelectionChoice: Set shift to {targetShift}");
                    }

                    // Auto-confirm via reflection (skip confirmation popup)
                    if (ConfirmSelectionMethod != null)
                    {
                        ConfirmSelectionMethod.Invoke(menu, null);
                        Plugin.Log.LogInfo("ExecuteMonsterSelectionChoice: Called ConfirmSelection()");
                    }
                    else
                    {
                        Plugin.Log.LogWarning("ExecuteMonsterSelectionChoice: ConfirmSelection method not found, falling back to OnConfirm");
                        menu.OnConfirm();
                    }
                });

                // Wait for the selection to process
                System.Threading.Thread.Sleep(500);

                // Check what state we transitioned to
                // Monster selection can lead to:
                // 1. MonsterSelectMenu opening (if player has 3 monsters - replacement needed)
                // 2. PostCombatMenu for XP distribution
                // 3. Back to exploration

                // Wait a bit more for state transitions
                var startTime = DateTime.Now;
                while (!TimedOut(startTime, 3000))
                {
                    // If equipment selection opened (for replacement), return that state
                    if (StateSerializer.IsInEquipmentSelection())
                    {
                        Plugin.Log.LogInfo("ExecuteMonsterSelectionChoice: Transitioned to equipment selection (monster replacement)");
                        // Note: This is actually MonsterSelectMenu with ShrineMonsterToReplace type
                        return JsonHelper.Serialize(new
                        {
                            success = true,
                            action = "monster_selected",
                            selected = monsterName,
                            shift = targetShift.ToString(),
                            isRandom = isRandom,
                            phase = "EQUIPMENT_SELECTION",
                            note = "Select which monster to replace"
                        });
                    }

                    // If skill selection opened (post-rebirth XP), return that state
                    if (StateSerializer.IsInSkillSelection())
                    {
                        Plugin.Log.LogInfo("ExecuteMonsterSelectionChoice: Transitioned to skill selection");
                        return JsonHelper.Serialize(new
                        {
                            success = true,
                            action = "monster_selected",
                            selected = monsterName,
                            shift = targetShift.ToString(),
                            isRandom = isRandom,
                            phase = "SKILL_SELECTION",
                            note = "Monster rebirthed with XP - select skill"
                        });
                    }

                    // If still in monster selection, wait a bit more
                    if (StateSerializer.IsInMonsterSelection())
                    {
                        System.Threading.Thread.Sleep(100);
                        continue;
                    }

                    // Otherwise we're done - back to exploration
                    break;
                }

                // Final state check
                if (StateSerializer.IsInSkillSelection())
                {
                    return StateSerializer.GetSkillSelectionStateJson();
                }

                Plugin.Log.LogInfo($"ExecuteMonsterSelectionChoice: Monster '{monsterName}' selected successfully (shift: {targetShift})");
                return JsonHelper.Serialize(new
                {
                    success = true,
                    action = "monster_selected",
                    selected = monsterName,
                    shift = targetShift.ToString(),
                    isRandom = isRandom,
                    phase = "EXPLORATION"
                });
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"ExecuteMonsterSelectionChoice: Exception - {ex.Message}\n{ex.StackTrace}");
                return JsonHelper.Serialize(new { success = false, error = $"Exception during monster selection: {ex.Message}" });
            }
        }

        // =====================================================
        // EQUIPMENT SELECTION
        // =====================================================

        /// <summary>
        /// Handle equipment selection choice. Monsters are indices 0 to party.Count-1,
        /// and scrap is index party.Count.
        /// </summary>
        public static string ExecuteEquipmentChoice(int choiceIndex)
        {
            var menu = UIController.Instance?.MonsterSelectMenu;
            if (menu == null || !menu.IsOpen)
                return JsonHelper.Serialize(new { success = false, error = "Equipment selection menu not open" });

            var party = MonsterManager.Instance?.Active;
            if (party == null)
                return JsonHelper.Serialize(new { success = false, error = "No party available" });

            int scrapIndex = party.Count;

            // Validate choice index
            if (choiceIndex < 0 || choiceIndex > scrapIndex)
                return JsonHelper.Serialize(new { success = false, error = $"Invalid choice index: {choiceIndex}. Valid range: 0-{scrapIndex}" });

            // Handle scrap
            if (choiceIndex == scrapIndex)
            {
                Plugin.Log.LogInfo("ExecuteEquipmentChoice: Scrapping equipment");
                return ExecuteEquipmentScrap(menu);
            }

            // Handle assign to monster
            var targetMonster = party[choiceIndex];
            Plugin.Log.LogInfo($"ExecuteEquipmentChoice: Assigning equipment to {targetMonster.Name} (index {choiceIndex})");
            return ExecuteEquipmentAssign(menu, targetMonster, choiceIndex);
        }

        private static string ExecuteEquipmentAssign(MonsterSelectMenu menu, Monster targetMonster, int monsterIndex)
        {
            var newEquipment = menu.NewEquipmentInstance;
            if (newEquipment == null)
                return JsonHelper.Serialize(new { success = false, error = "No equipment to assign" });

            var prevEquipment = targetMonster.Equipment?.Equipment;
            var newEquipName = newEquipment.Equipment?.GetName() ?? "Unknown";

            try
            {
                // Run UI operations on main thread
                Plugin.RunOnMainThreadAndWait(() =>
                {
                    // Select the monster in the menu list
                    menu.MenuList.SelectByIndex(monsterIndex);

                    // Trigger the menu item selection via InputConfirm
                    if (InputConfirmMethod != null)
                    {
                        InputConfirmMethod.Invoke(menu.MenuList, null);
                    }
                });

                // Wait for the UI to process
                System.Threading.Thread.Sleep(600);

                // Check if we're still in equipment selection (happens when monster had equipment - trade scenario)
                if (StateSerializer.IsInEquipmentSelection())
                {
                    var prevEquipName = prevEquipment?.Equipment?.GetName() ?? "Unknown";
                    Plugin.Log.LogInfo($"ExecuteEquipmentAssign: Trade occurred - now holding {prevEquipName}");
                    return JsonHelper.Serialize(new
                    {
                        success = true,
                        action = "equipment_trade",
                        assignedTo = targetMonster.Name,
                        assigned = newEquipName,
                        nowHolding = prevEquipName,
                        phase = "EQUIPMENT_SELECTION",
                        state = JObject.Parse(StateSerializer.GetEquipmentSelectionStateJson())
                    });
                }

                // Equipment assigned, back to exploration
                Plugin.Log.LogInfo($"ExecuteEquipmentAssign: Equipment assigned to {targetMonster.Name}");
                return JsonHelper.Serialize(new
                {
                    success = true,
                    action = "equipment_assigned",
                    assignedTo = targetMonster.Name,
                    equipment = newEquipName,
                    phase = "EXPLORATION"
                });
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"ExecuteEquipmentAssign: Exception - {ex.Message}");
                return JsonHelper.Serialize(new { success = false, error = $"Exception during equipment assignment: {ex.Message}" });
            }
        }

        private static string ExecuteEquipmentScrap(MonsterSelectMenu menu)
        {
            var equipment = menu.NewEquipmentInstance;
            if (equipment == null)
                return JsonHelper.Serialize(new { success = false, error = "No equipment to scrap" });

            var equipName = equipment.Equipment?.GetName() ?? "Unknown";
            var scrapValue = equipment.Equipment?.GetScrapGoldGain() ?? 0;

            try
            {
                // Run UI operations on main thread
                Plugin.RunOnMainThreadAndWait(() =>
                {
                    // Find and select the scrap menu item (it's the last item in the list)
                    var scrapIndex = menu.MenuList.List.Count - 1;
                    menu.MenuList.SelectByIndex(scrapIndex);

                    // Trigger the selection via InputConfirm
                    if (InputConfirmMethod != null)
                    {
                        InputConfirmMethod.Invoke(menu.MenuList, null);
                    }
                });

                // Wait for the scrap animation and popup
                System.Threading.Thread.Sleep(300);

                // The game shows a popup after scrapping - we need to close it
                // Wait for popup and auto-confirm it
                var startTime = DateTime.Now;
                while ((DateTime.Now - startTime).TotalMilliseconds < 2000)
                {
                    if (PopupController.Instance?.IsOpen ?? false)
                    {
                        Plugin.Log.LogInfo("ExecuteEquipmentScrap: Closing scrap confirmation popup");
                        Plugin.RunOnMainThreadAndWait(() =>
                        {
                            PopupController.Instance.Close();
                        });
                        break;
                    }
                    System.Threading.Thread.Sleep(50);
                }

                System.Threading.Thread.Sleep(300);

                Plugin.Log.LogInfo($"ExecuteEquipmentScrap: Scrapped {equipName} for {scrapValue} gold");
                return JsonHelper.Serialize(new
                {
                    success = true,
                    action = "equipment_scrapped",
                    equipment = equipName,
                    goldGained = scrapValue,
                    goldTotal = InventoryManager.Instance?.Gold ?? 0,
                    phase = "EXPLORATION"
                });
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"ExecuteEquipmentScrap: Exception - {ex.Message}");
                return JsonHelper.Serialize(new { success = false, error = $"Exception during equipment scrap: {ex.Message}" });
            }
        }

        // =====================================================
        // NPC DIALOGUE INTERACTION
        // =====================================================

        // Cached reflection for DialogueDisplay private fields
        private static FieldInfo _dialogueDisplayCurrentDialogueField;
        private static FieldInfo _dialogueDisplayCurrentDataField;

        private static DialogueInteractable GetCurrentDialogueInteractable()
        {
            var display = UIController.Instance?.DialogueDisplay;
            if (display == null) return null;

            if (_dialogueDisplayCurrentDialogueField == null)
            {
                _dialogueDisplayCurrentDialogueField = typeof(DialogueDisplay).GetField("currentDialogue",
                    BindingFlags.NonPublic | BindingFlags.Instance);
            }
            return _dialogueDisplayCurrentDialogueField?.GetValue(display) as DialogueInteractable;
        }

        private static DialogueDisplayData GetCurrentDialogueData()
        {
            var display = UIController.Instance?.DialogueDisplay;
            if (display == null) return null;

            if (_dialogueDisplayCurrentDataField == null)
            {
                _dialogueDisplayCurrentDataField = typeof(DialogueDisplay).GetField("currentDialogueData",
                    BindingFlags.NonPublic | BindingFlags.Instance);
            }
            return _dialogueDisplayCurrentDataField?.GetValue(display) as DialogueDisplayData;
        }

        public static bool IsDialogueOpen()
        {
            return UIController.Instance?.DialogueDisplay?.IsOpen ?? false;
        }

        /// <summary>
        /// Checks if the current dialogue state has a meaningful choice that requires user input.
        /// Returns false if the only meaningful option is "Event" (which should be auto-selected).
        /// </summary>
        private static bool HasMeaningfulChoice()
        {
            var data = GetCurrentDialogueData();
            if (data == null) return false;

            // Check if we have dialogue options
            if (data.DialogueOptions == null || data.DialogueOptions.Length == 0) return false;

            // If there's only one option, no real choice needed
            if (data.DialogueOptions.Length == 1) return false;

            // Check if "Event" is one of the options - if so, we'll auto-select it
            int eventIndex = FindEventOptionIndex(data.DialogueOptions);
            if (eventIndex >= 0)
            {
                // Event option exists - this is NOT a meaningful choice, we'll auto-select Event
                return false;
            }

            // Choice events with no Event option require user input
            if (data.IsChoiceEvent) return true;

            // Multiple options (without Event) require user input
            if (data.DialogueOptions.Length > 1) return true;

            return false;
        }

        /// <summary>
        /// Find the index of an "Event" option in the dialogue options.
        /// Returns -1 if not found.
        /// </summary>
        private static int FindEventOptionIndex(string[] options)
        {
            if (options == null) return -1;

            for (int i = 0; i < options.Length; i++)
            {
                if (options[i] != null && options[i].Trim().Equals("Event", System.StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Auto-progress through dialogue until a meaningful choice appears or dialogue ends.
        /// Auto-selects "Event" option when available.
        /// </summary>
        private static void AutoProgressDialogue(int timeoutMs = 10000)
        {
            var startTime = DateTime.Now;
            var display = UIController.Instance?.DialogueDisplay;

            while (!TimedOut(startTime, timeoutMs))
            {
                // Check if dialogue closed
                if (!IsDialogueOpen())
                {
                    Plugin.Log.LogInfo("AutoProgressDialogue: Dialogue closed");
                    return;
                }

                // Check if skill selection opened (e.g., Witch dialogue)
                if (StateSerializer.IsInSkillSelection())
                {
                    Plugin.Log.LogInfo("AutoProgressDialogue: Skill selection opened");
                    return;
                }

                var data = GetCurrentDialogueData();
                if (data == null)
                {
                    System.Threading.Thread.Sleep(100);
                    continue;
                }

                // Check if "Event" is an available option - auto-select it
                if (data.DialogueOptions != null && data.DialogueOptions.Length > 0)
                {
                    int eventIndex = FindEventOptionIndex(data.DialogueOptions);
                    if (eventIndex >= 0)
                    {
                        Plugin.Log.LogInfo($"AutoProgressDialogue: Found 'Event' option at index {eventIndex}, auto-selecting");
                        SelectDialogueOptionInternal(eventIndex);
                        System.Threading.Thread.Sleep(300); // Wait for event to process
                        continue;
                    }
                }

                // Stop if we have a meaningful choice (no Event option)
                if (HasMeaningfulChoice())
                {
                    Plugin.Log.LogInfo($"AutoProgressDialogue: Meaningful choice found with {data.DialogueOptions?.Length ?? 0} options");
                    return;
                }

                // Auto-advance: simulate confirm (must run on main thread)
                Plugin.Log.LogInfo($"AutoProgressDialogue: Auto-advancing past '{data.DialogueText?.Substring(0, Math.Min(50, data.DialogueText?.Length ?? 0))}...'");
                Plugin.RunOnMainThreadAndWait(() => display.OnConfirm(isMouseClick: false));

                System.Threading.Thread.Sleep(150); // Wait for UI update
            }

            Plugin.Log.LogWarning("AutoProgressDialogue: Timeout");
        }

        /// <summary>
        /// Internal method to select a dialogue option without returning a result.
        /// Used for auto-selecting Event options.
        /// Must run UI operations on main thread.
        /// </summary>
        private static void SelectDialogueOptionInternal(int choiceIndex)
        {
            var dialogueInteractable = GetCurrentDialogueInteractable();
            var dialogueData = GetCurrentDialogueData();
            var display = UIController.Instance?.DialogueDisplay;

            if (dialogueInteractable == null || dialogueData == null || display == null)
            {
                Plugin.Log.LogWarning("SelectDialogueOptionInternal: Dialogue state not available");
                return;
            }

            var options = dialogueData.DialogueOptions;
            if (options == null || choiceIndex < 0 || choiceIndex >= options.Length)
            {
                Plugin.Log.LogWarning($"SelectDialogueOptionInternal: Invalid choice index {choiceIndex}");
                return;
            }

            Plugin.Log.LogInfo($"SelectDialogueOptionInternal: Selecting option {choiceIndex}: '{options[choiceIndex]}'");

            try
            {
                // Run UI operations on main thread
                Plugin.RunOnMainThreadAndWait(() =>
                {
                    // Trigger node close events first
                    dialogueInteractable.TriggerNodeOnCloseEvents();

                    // Select the dialogue option
                    bool isEnd, forceSkip;
                    dialogueInteractable.SelectDialogueOption(choiceIndex, options.Length, out isEnd, out forceSkip);

                    Plugin.Log.LogInfo($"SelectDialogueOptionInternal: isEnd={isEnd}, forceSkip={forceSkip}");

                    if (isEnd && forceSkip)
                    {
                        // Dialogue is ending - close the UI
                        Plugin.Log.LogInfo("SelectDialogueOptionInternal: Dialogue ending");
                        UIController.Instance.SetDialogueVisibility(visible: false);
                    }
                    else if (IsDialogueOpen())
                    {
                        // Show next dialogue if still open
                        var nextDialogue = dialogueInteractable.GetNextDialogue();
                        if (nextDialogue != null)
                        {
                            display.ShowDialogue(nextDialogue);
                        }
                    }
                });
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"SelectDialogueOptionInternal: Exception - {ex.Message}");
            }
        }

        /// <summary>
        /// Start dialogue with an NPC by index from the map's DialogueInteractables.
        /// Auto-teleports to NPC and auto-progresses until meaningful choice or dialogue end.
        /// </summary>
        public static string ExecuteNpcInteract(int npcIndex)
        {
            if (GameStateManager.Instance?.IsCombat ?? false)
                return JsonHelper.Serialize(new { success = false, error = "Cannot interact with NPCs during combat" });

            if (IsDialogueOpen())
                return JsonHelper.Serialize(new { success = false, error = "Dialogue already open" });

            var map = LevelGenerator.Instance?.Map;
            if (map == null)
                return JsonHelper.Serialize(new { success = false, error = "Map not loaded" });

            var interactables = map.DialogueInteractables;
            if (interactables == null || interactables.Count == 0)
                return JsonHelper.Serialize(new { success = false, error = "No NPCs on map" });

            if (npcIndex < 0 || npcIndex >= interactables.Count)
                return JsonHelper.Serialize(new { success = false, error = $"Invalid NPC index: {npcIndex}. Valid range: 0-{interactables.Count - 1}" });

            var interactable = interactables[npcIndex];
            if (interactable == null)
                return JsonHelper.Serialize(new { success = false, error = "NPC is null" });

            var npc = interactable as DialogueInteractable;
            if (npc == null)
                return JsonHelper.Serialize(new { success = false, error = "Interactable is not a DialogueInteractable" });

            var npcName = npc.DialogueCharacter?.CharacterName ?? "Unknown";
            Plugin.Log.LogInfo($"ExecuteNpcInteract: Starting interaction with {npcName} at index {npcIndex}");

            // Run UI/transform operations on main thread
            Plugin.RunOnMainThreadAndWait(() =>
            {
                // Teleport player near NPC
                var npcPos = npc.transform.position;
                var playerMovement = PlayerMovementController.Instance;
                if (playerMovement != null)
                {
                    // Teleport 2 units away in x direction for interaction range
                    var targetPos = new UnityEngine.Vector3(npcPos.x - 2f, npcPos.y, npcPos.z);
                    playerMovement.transform.position = targetPos;
                    Plugin.Log.LogInfo($"ExecuteNpcInteract: Teleported player near NPC at ({targetPos.x:F1}, {targetPos.y:F1})");
                }

                // Trigger dialogue
                npc.ForceStart();
            });

            // Wait for dialogue to open
            var startTime = DateTime.Now;
            while (!IsDialogueOpen() && !TimedOut(startTime, 3000))
            {
                System.Threading.Thread.Sleep(50);
            }

            if (!IsDialogueOpen())
            {
                return JsonHelper.Serialize(new { success = false, error = "Dialogue failed to open" });
            }

            // Small delay for dialogue to initialize
            System.Threading.Thread.Sleep(200);

            // Auto-progress through non-choice dialogue
            AutoProgressDialogue();

            // Check what state we're in now
            if (StateSerializer.IsInSkillSelection())
            {
                return JsonHelper.Serialize(new
                {
                    success = true,
                    phase = "SKILL_SELECTION",
                    transitionedFrom = "dialogue",
                    npc = npcName
                });
            }

            if (!IsDialogueOpen())
            {
                // Dialogue ended without requiring input
                return JsonHelper.Serialize(new
                {
                    success = true,
                    phase = "EXPLORATION",
                    dialogueComplete = true,
                    npc = npcName
                });
            }

            // Return current dialogue state with choices
            return StateSerializer.GetDialogueStateJson();
        }

        /// <summary>
        /// Select a dialogue choice by index and auto-progress until next choice or dialogue end.
        /// </summary>
        public static string ExecuteDialogueChoice(int choiceIndex)
        {
            if (!IsDialogueOpen())
                return JsonHelper.Serialize(new { success = false, error = "No dialogue open" });

            var dialogueInteractable = GetCurrentDialogueInteractable();
            var dialogueData = GetCurrentDialogueData();
            var display = UIController.Instance?.DialogueDisplay;

            if (dialogueInteractable == null || dialogueData == null || display == null)
                return JsonHelper.Serialize(new { success = false, error = "Dialogue state not available" });

            var options = dialogueData.DialogueOptions;
            if (options == null || options.Length == 0)
                return JsonHelper.Serialize(new { success = false, error = "No dialogue options available" });

            if (choiceIndex < 0 || choiceIndex >= options.Length)
                return JsonHelper.Serialize(new { success = false, error = $"Invalid choice index: {choiceIndex}. Valid range: 0-{options.Length - 1}" });

            var selectedOptionText = options[choiceIndex];
            Plugin.Log.LogInfo($"ExecuteDialogueChoice: Selecting option {choiceIndex}: '{selectedOptionText}'");

            try
            {
                bool isEnd = false;
                bool forceSkip = false;

                // Run all UI operations on main thread
                Plugin.RunOnMainThreadAndWait(() =>
                {
                    // Select the option in the UI if possible (wrapped in try-catch for safety)
                    try
                    {
                        var characterDisplay = dialogueData.LeftIsSpeaking
                            ? display.LeftCharacterDisplay
                            : display.RightCharacterDisplay;

                        if (characterDisplay != null)
                        {
                            if (dialogueData.IsChoiceEvent && characterDisplay.ChoiceEventOptions != null)
                            {
                                characterDisplay.ChoiceEventOptions.SelectByIndex(choiceIndex);
                            }
                            else if (characterDisplay.DialogOptions != null)
                            {
                                characterDisplay.DialogOptions.SelectByIndex(choiceIndex);
                            }
                        }
                    }
                    catch (System.Exception uiEx)
                    {
                        Plugin.Log.LogWarning($"ExecuteDialogueChoice: UI selection failed (non-fatal): {uiEx.Message}");
                    }

                    // Trigger node close events
                    dialogueInteractable.TriggerNodeOnCloseEvents();

                    // Select the dialogue option
                    dialogueInteractable.SelectDialogueOption(choiceIndex, options.Length, out isEnd, out forceSkip);

                    Plugin.Log.LogInfo($"ExecuteDialogueChoice: isEnd={isEnd}, forceSkip={forceSkip}");

                    if (isEnd && forceSkip)
                    {
                        // Dialogue is ending
                        Plugin.Log.LogInfo("ExecuteDialogueChoice: Dialogue ending");
                        UIController.Instance.SetDialogueVisibility(visible: false);
                    }
                    else if (IsDialogueOpen())
                    {
                        // Show next dialogue if still open
                        var nextDialogue = dialogueInteractable.GetNextDialogue();
                        if (nextDialogue != null)
                        {
                            display.ShowDialogue(nextDialogue);
                        }
                    }
                });

                if (isEnd && forceSkip)
                {
                    System.Threading.Thread.Sleep(300);

                    // Check what state we transitioned to
                    if (StateSerializer.IsInEquipmentSelection())
                    {
                        return JsonHelper.Serialize(new
                        {
                            success = true,
                            phase = "EQUIPMENT_SELECTION",
                            transitionedFrom = "dialogue",
                            state = JObject.Parse(StateSerializer.GetEquipmentSelectionStateJson())
                        });
                    }

                    if (StateSerializer.IsInSkillSelection())
                    {
                        return JsonHelper.Serialize(new
                        {
                            success = true,
                            phase = "SKILL_SELECTION",
                            transitionedFrom = "dialogue"
                        });
                    }

                    return JsonHelper.Serialize(new
                    {
                        success = true,
                        phase = "EXPLORATION",
                        dialogueComplete = true
                    });
                }

                System.Threading.Thread.Sleep(200);

                // Auto-progress through non-choice dialogue (including auto-selecting Event if present)
                AutoProgressDialogue();

                // Return current state after auto-progression
                if (StateSerializer.IsInEquipmentSelection())
                {
                    return JsonHelper.Serialize(new
                    {
                        success = true,
                        phase = "EQUIPMENT_SELECTION",
                        transitionedFrom = "dialogue",
                        state = JObject.Parse(StateSerializer.GetEquipmentSelectionStateJson())
                    });
                }

                if (StateSerializer.IsInSkillSelection())
                {
                    return JsonHelper.Serialize(new
                    {
                        success = true,
                        phase = "SKILL_SELECTION",
                        transitionedFrom = "dialogue"
                    });
                }

                if (!IsDialogueOpen())
                {
                    return JsonHelper.Serialize(new
                    {
                        success = true,
                        phase = "EXPLORATION",
                        dialogueComplete = true
                    });
                }

                return StateSerializer.GetDialogueStateJson();
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"ExecuteDialogueChoice: Exception - {ex.Message}\n{ex.StackTrace}");
                return JsonHelper.Serialize(new
                {
                    success = false,
                    error = $"Exception during dialogue choice: {ex.Message}",
                    choiceIndex,
                    optionText = selectedOptionText
                });
            }
        }

        // =====================================================
        // MERCHANT SHOP INTERACTION
        // =====================================================

        // Cached reflection for merchant menu access
        private static FieldInfo _merchantMenuField;

        private static MerchantMenu GetMerchantMenu()
        {
            var ui = UIController.Instance;
            if (ui == null) return null;

            if (_merchantMenuField == null)
            {
                _merchantMenuField = typeof(UIController).GetField("MerchantMenu",
                    BindingFlags.NonPublic | BindingFlags.Instance);
            }
            return _merchantMenuField?.GetValue(ui) as MerchantMenu;
        }

        public static bool IsMerchantMenuOpen()
        {
            return GetMerchantMenu()?.IsOpen ?? false;
        }

        /// <summary>
        /// Open the merchant shop. Auto-teleports to merchant.
        /// </summary>
        public static string ExecuteMerchantInteract()
        {
            if (GameStateManager.Instance?.IsCombat ?? false)
                return JsonHelper.Serialize(new { success = false, error = "Cannot shop during combat" });

            if (IsMerchantMenuOpen())
                return StateSerializer.GetMerchantStateJson();

            var map = LevelGenerator.Instance?.Map;
            if (map == null)
                return JsonHelper.Serialize(new { success = false, error = "Map not loaded" });

            var merchantInteractable = map.MerchantInteractable;
            if (merchantInteractable == null)
                return JsonHelper.Serialize(new { success = false, error = "No merchant on this map" });

            var merchant = merchantInteractable as MerchantInteractable;
            if (merchant == null)
                return JsonHelper.Serialize(new { success = false, error = "Merchant is not a MerchantInteractable" });

            Plugin.Log.LogInfo("ExecuteMerchantInteract: Opening merchant shop");

            // Teleport player near merchant
            var merchantPos = merchant.transform.position;
            var playerMovement = PlayerMovementController.Instance;
            if (playerMovement != null)
            {
                var targetPos = new UnityEngine.Vector3(merchantPos.x - 2f, merchantPos.y, merchantPos.z);
                playerMovement.transform.position = targetPos;
                Plugin.Log.LogInfo($"ExecuteMerchantInteract: Teleported player near merchant");
            }

            // Start merchant interaction
            merchant.StartMerchantInteraction();

            // Wait for menu to open
            var startTime = DateTime.Now;
            while (!IsMerchantMenuOpen() && !TimedOut(startTime, 3000))
            {
                System.Threading.Thread.Sleep(50);
            }

            if (!IsMerchantMenuOpen())
            {
                return JsonHelper.Serialize(new { success = false, error = "Merchant menu failed to open" });
            }

            System.Threading.Thread.Sleep(200); // Let UI initialize

            return StateSerializer.GetMerchantStateJson();
        }

        /// <summary>
        /// Buy an item from the merchant by index.
        /// </summary>
        public static string ExecuteMerchantBuy(int itemIndex, int quantity = 1)
        {
            if (!IsMerchantMenuOpen())
                return JsonHelper.Serialize(new { success = false, error = "Merchant menu not open" });

            var merchant = MerchantInteractable.currentMerchant;
            if (merchant == null)
                return JsonHelper.Serialize(new { success = false, error = "No active merchant" });

            var stockedItems = merchant.StockedItems;
            if (itemIndex < 0 || itemIndex >= stockedItems.Count)
                return JsonHelper.Serialize(new { success = false, error = $"Invalid item index: {itemIndex}. Valid range: 0-{stockedItems.Count - 1}" });

            var shopItem = stockedItems[itemIndex];

            // Check affordability
            if (!merchant.CanBuyItem(shopItem, quantity))
            {
                return JsonHelper.Serialize(new
                {
                    success = false,
                    error = "Cannot afford this item",
                    price = shopItem.Price * quantity,
                    gold = InventoryManager.Instance?.Gold ?? 0
                });
            }

            var itemName = shopItem.GetName();
            var cost = shopItem.Price * quantity;

            Plugin.Log.LogInfo($"ExecuteMerchantBuy: Purchasing {itemName} for {cost} gold");

            // Make the purchase
            merchant.BuyItem(shopItem, quantity);

            // For EXP purchases, the game opens a post-combat menu to distribute XP
            // Wait for it to complete
            if (shopItem.ItemType == ShopItemType.Exp)
            {
                Plugin.Log.LogInfo("ExecuteMerchantBuy: EXP purchase - waiting for distribution to complete");
                var startTime = DateTime.Now;

                // Wait for post-combat menu to open and close
                while (!TimedOut(startTime, 5000))
                {
                    System.Threading.Thread.Sleep(100);

                    // Check if we're back to merchant menu
                    if (IsMerchantMenuOpen())
                    {
                        Plugin.Log.LogInfo("ExecuteMerchantBuy: Back to merchant menu");
                        break;
                    }
                }
            }

            // For equipment purchases, the game opens equipment selection menu
            if (shopItem.ItemType == ShopItemType.Equipment)
            {
                Plugin.Log.LogInfo("ExecuteMerchantBuy: Equipment purchase - waiting for equipment selection");
                var startTime = DateTime.Now;

                // Wait for equipment selection menu to open
                while (!TimedOut(startTime, 3000))
                {
                    System.Threading.Thread.Sleep(100);

                    if (StateSerializer.IsInEquipmentSelection())
                    {
                        Plugin.Log.LogInfo("ExecuteMerchantBuy: Equipment selection menu opened");
                        return JsonHelper.Serialize(new
                        {
                            success = true,
                            purchased = itemName,
                            cost = cost,
                            goldRemaining = InventoryManager.Instance?.Gold ?? 0,
                            phase = "EQUIPMENT_SELECTION",
                            note = "Use /choice to assign equipment to a monster or scrap",
                            state = JObject.Parse(StateSerializer.GetEquipmentSelectionStateJson())
                        });
                    }
                }
            }

            System.Threading.Thread.Sleep(200); // Let UI update

            return JsonHelper.Serialize(new
            {
                success = true,
                purchased = itemName,
                cost = cost,
                goldRemaining = InventoryManager.Instance?.Gold ?? 0,
                state = JObject.Parse(StateSerializer.GetMerchantStateJson())
            });
        }

        /// <summary>
        /// Close the merchant menu.
        /// </summary>
        public static string ExecuteMerchantClose()
        {
            if (!IsMerchantMenuOpen())
                return JsonHelper.Serialize(new { success = false, error = "Merchant menu not open" });

            Plugin.Log.LogInfo("ExecuteMerchantClose: Closing merchant menu");

            UIController.Instance.SetMerchantMenuVisibility(visible: false);

            // Wait for menu to close
            var startTime = DateTime.Now;
            while (IsMerchantMenuOpen() && !TimedOut(startTime, 2000))
            {
                System.Threading.Thread.Sleep(50);
            }

            System.Threading.Thread.Sleep(200);

            return JsonHelper.Serialize(new
            {
                success = true,
                phase = "EXPLORATION"
            });
        }

        // =====================================================
        // RUN START AND DIFFICULTY SELECTION
        // =====================================================

        /// <summary>
        /// Start a new run from Pilgrim's Rest. Opens difficulty selection if unlocked,
        /// otherwise proceeds directly to monster selection.
        /// </summary>
        public static string ExecuteStartRun()
        {
            // Validate we're in exploration mode
            if (GameStateManager.Instance?.IsCombat ?? false)
                return JsonHelper.Serialize(new { success = false, error = "Cannot start run during combat" });

            // Check if we're in Pilgrim's Rest
            var currentArea = ExplorationController.Instance?.CurrentArea ?? EArea.PilgrimsRest;
            if (currentArea != EArea.PilgrimsRest)
                return JsonHelper.Serialize(new { success = false, error = $"Must be in Pilgrim's Rest to start run. Current area: {currentArea}" });

            // Find the start run interactable
            var startRunInteractable = StateSerializer.FindStartRunInteractable();
            if (startRunInteractable == null)
                return JsonHelper.Serialize(new { success = false, error = "Start run interactable not found in Pilgrim's Rest" });

            Plugin.Log.LogInfo("ExecuteStartRun: Triggering start run interaction");

            try
            {
                // Run on main thread
                Plugin.RunOnMainThreadAndWait(() =>
                {
                    // Use ForceStartInteraction which handles difficulty check internally
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
                        Plugin.Log.LogInfo("ExecuteStartRun: Difficulty selection opened");
                        return StateSerializer.GetDifficultySelectionStateJson();
                    }

                    // Check if monster selection opened (no difficulty selection needed)
                    if (StateSerializer.IsInMonsterSelection())
                    {
                        Plugin.Log.LogInfo("ExecuteStartRun: Monster selection opened (difficulties not unlocked)");
                        return StateSerializer.GetMonsterSelectionStateJson();
                    }
                }

                return JsonHelper.Serialize(new { success = false, error = "Timeout waiting for run start menu to open" });
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"ExecuteStartRun: Exception - {ex.Message}\n{ex.StackTrace}");
                return JsonHelper.Serialize(new { success = false, error = $"Exception during run start: {ex.Message}" });
            }
        }

        /// <summary>
        /// Select a difficulty level during run start. Valid values: "Normal", "Heroic", "Mythic"
        /// </summary>
        public static string ExecuteSelectDifficulty(string difficulty)
        {
            if (!StateSerializer.IsInDifficultySelection())
                return JsonHelper.Serialize(new { success = false, error = "Not in difficulty selection screen" });

            var menu = UIController.Instance?.DifficultySelectMenu;
            if (menu == null || !menu.IsOpen)
                return JsonHelper.Serialize(new { success = false, error = "Difficulty selection menu not available" });

            // Parse difficulty
            EDifficulty targetDifficulty;
            switch (difficulty?.ToLower())
            {
                case "normal":
                    targetDifficulty = EDifficulty.Normal;
                    break;
                case "heroic":
                    targetDifficulty = EDifficulty.Heroic;
                    break;
                case "mythic":
                    targetDifficulty = EDifficulty.Mythic;
                    break;
                default:
                    return JsonHelper.Serialize(new { success = false, error = $"Invalid difficulty: '{difficulty}'. Valid values: Normal, Heroic, Mythic" });
            }

            // Check if difficulty is unlocked
            var maxUnlocked = ProgressManager.Instance?.UnlockedDifficulty ?? 1;
            if ((int)targetDifficulty > maxUnlocked)
            {
                return JsonHelper.Serialize(new
                {
                    success = false,
                    error = $"Difficulty '{difficulty}' is not unlocked. Max unlocked level: {maxUnlocked}"
                });
            }

            Plugin.Log.LogInfo($"ExecuteSelectDifficulty: Selecting difficulty {targetDifficulty}");

            try
            {
                // Navigate to the correct difficulty and confirm
                Plugin.RunOnMainThreadAndWait(() =>
                {
                    // Navigate to target difficulty
                    while (menu.CurrentDifficulty != targetDifficulty)
                    {
                        if ((int)menu.CurrentDifficulty < (int)targetDifficulty)
                            menu.OnGoRight();
                        else
                            menu.OnGoLeft();
                    }

                    // Trigger confirm (opens popup)
                    menu.OnConfirm();
                });

                // Wait for popup to appear and confirm it
                System.Threading.Thread.Sleep(300);

                Plugin.RunOnMainThreadAndWait(() =>
                {
                    // The popup asks for confirmation - select the confirm button (index 0) and trigger it
                    var popup = PopupController.Instance;
                    if (popup?.IsOpen ?? false)
                    {
                        Plugin.Log.LogInfo("ExecuteSelectDifficulty: Confirming popup");
                        // Select the first button (Confirm) and trigger via InputConfirm on the menu
                        popup.ConfirmMenu.SelectByIndex(0);
                        if (InputConfirmMethod != null)
                        {
                            InputConfirmMethod.Invoke(popup.ConfirmMenu, null);
                        }
                    }
                });

                // Wait for monster selection to open
                var startTime = DateTime.Now;
                while (!TimedOut(startTime, 5000))
                {
                    System.Threading.Thread.Sleep(100);

                    if (StateSerializer.IsInMonsterSelection())
                    {
                        Plugin.Log.LogInfo($"ExecuteSelectDifficulty: Monster selection opened with difficulty {targetDifficulty}");
                        return JsonHelper.Serialize(new
                        {
                            success = true,
                            action = "difficulty_selected",
                            difficulty = targetDifficulty.ToString(),
                            phase = "MONSTER_SELECTION",
                            state = JObject.Parse(StateSerializer.GetMonsterSelectionStateJson())
                        });
                    }
                }

                return JsonHelper.Serialize(new { success = false, error = "Timeout waiting for monster selection after difficulty selection" });
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"ExecuteSelectDifficulty: Exception - {ex.Message}\n{ex.StackTrace}");
                return JsonHelper.Serialize(new { success = false, error = $"Exception during difficulty selection: {ex.Message}" });
            }
        }

        // =====================================================
        // END OF RUN (VICTORY/DEFEAT SCREEN)
        // =====================================================

        // Cached reflection for EndOfRunMenu access
        private static FieldInfo _endOfRunMenuField;

        private static EndOfRunMenu GetEndOfRunMenu()
        {
            var ui = UIController.Instance;
            if (ui == null) return null;

            if (_endOfRunMenuField == null)
            {
                _endOfRunMenuField = typeof(UIController).GetField("EndOfRunMenu",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            }
            return _endOfRunMenuField?.GetValue(ui) as EndOfRunMenu;
        }

        /// <summary>
        /// Advance past the end-of-run screen (victory or defeat) back to Pilgrim's Rest.
        /// </summary>
        public static string ExecuteContinueFromEndOfRun()
        {
            if (!StateSerializer.IsInEndOfRunMenu())
                return JsonHelper.Serialize(new { success = false, error = "Not in end-of-run screen" });

            var menu = GetEndOfRunMenu();
            if (menu == null || !menu.IsOpen)
                return JsonHelper.Serialize(new { success = false, error = "End of run menu not available" });

            // Determine if victory or defeat
            bool isVictory = menu.VictoryBanner?.activeSelf ?? false;
            string result = isVictory ? "VICTORY" : "DEFEAT";

            Plugin.Log.LogInfo($"ExecuteContinueFromEndOfRun: Closing end-of-run screen ({result})");

            try
            {
                // Close the menu
                Plugin.RunOnMainThreadAndWait(() =>
                {
                    menu.Close();
                });

                // Wait for transition back to Pilgrim's Rest
                var startTime = DateTime.Now;
                while (!TimedOut(startTime, 10000))
                {
                    System.Threading.Thread.Sleep(200);

                    // Check if we're back in exploration
                    if (!(GameStateManager.Instance?.IsCombat ?? true) &&
                        !StateSerializer.IsInEndOfRunMenu() &&
                        !StateSerializer.IsInPostCombatMenu())
                    {
                        // Give scene transition time to complete
                        System.Threading.Thread.Sleep(1000);

                        Plugin.Log.LogInfo("ExecuteContinueFromEndOfRun: Returned to exploration");
                        return JsonHelper.Serialize(new
                        {
                            success = true,
                            action = "continue_from_end_of_run",
                            runResult = result,
                            phase = "EXPLORATION",
                            state = JObject.Parse(StateSerializer.ToJson())
                        });
                    }
                }

                return JsonHelper.Serialize(new { success = false, error = "Timeout waiting for return to exploration" });
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"ExecuteContinueFromEndOfRun: Exception - {ex.Message}\n{ex.StackTrace}");
                return JsonHelper.Serialize(new { success = false, error = $"Exception during continue: {ex.Message}" });
            }
        }
    }
}
