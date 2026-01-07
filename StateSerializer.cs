using System;
using System.Collections.Generic;
using System.Text;

namespace AethermancerHarness
{
    public static class StateSerializer
    {
        public static string ToJson()
        {
            try
            {
                // Check for skill selection state first (post-combat level up)
                if (IsInSkillSelection())
                {
                    return GetSkillSelectionStateJson();
                }

                if (!GameStateManager.Instance?.IsCombat ?? true)
                {
                    return GetExplorationStateJson();
                }
                return GetCombatStateJson();
            }
            catch (Exception e)
            {
                return $"{{\"error\": \"{EscapeJson(e.Message)}\"}}";
            }
        }

        /// <summary>
        /// Check if we're in the skill selection screen (post-combat level up)
        /// </summary>
        public static bool IsInSkillSelection()
        {
            try
            {
                var skillSelectMenu = UIController.Instance?.PostCombatMenu?.SkillSelectMenu;
                return skillSelectMenu?.IsOpen ?? false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Check if we're in the post-combat menu (worthiness or level up screens)
        /// </summary>
        public static bool IsInPostCombatMenu()
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

        /// <summary>
        /// Get the skill selection state JSON for level up screen
        /// </summary>
        public static string GetSkillSelectionStateJson()
        {
            var sb = new StringBuilder();
            var skillSelectMenu = UIController.Instance.PostCombatMenu.SkillSelectMenu;
            var postCombatMenu = UIController.Instance.PostCombatMenu;

            sb.Append("{");
            sb.Append("\"phase\": \"SKILL_SELECTION\",");

            // Level up type (action or trait)
            var levelUpType = skillSelectMenu.LevelUpType;
            sb.Append($"\"levelUpType\": \"{(levelUpType == SkillPicker.ELevelUpType.PickSpell ? "action" : "trait")}\",");

            // Find which monster is leveling up
            Monster levelingMonster = null;
            int monsterIndex = -1;
            for (int i = 0; i < postCombatMenu.PostCombatMonsterInfos.Count; i++)
            {
                var info = postCombatMenu.PostCombatMonsterInfos[i];
                if (info.monster != null && info.LevelUpUI.LevelsGainedLeft > 0)
                {
                    // Check if this is the one with skill selection open
                    if (info.gameObject.activeSelf && info.transform.localPosition == postCombatMenu.SkillSelectionMonsterPosition.transform.localPosition)
                    {
                        levelingMonster = info.monster;
                        monsterIndex = i;
                        break;
                    }
                }
            }

            // If we didn't find it by position, just use the first active monster
            if (levelingMonster == null && MonsterManager.Instance.Active.Count > 0)
            {
                levelingMonster = MonsterManager.Instance.Active[0];
                monsterIndex = 0;
            }

            // Monster info
            if (levelingMonster != null)
            {
                sb.Append($"\"monster\": {{\"index\": {monsterIndex}, \"name\": \"{EscapeJson(levelingMonster.Name)}\", \"level\": {levelingMonster.Level}}},");
            }
            else
            {
                sb.Append("\"monster\": null,");
            }

            // The 3 skill choices
            sb.Append("\"choices\": [");
            for (int i = 0; i < skillSelectMenu.SkillTooltips.Count && i < 3; i++)
            {
                if (i > 0) sb.Append(",");
                var tooltip = skillSelectMenu.SkillTooltips[i];
                var menuItem = tooltip.GetComponent<MenuListItem>();
                var skillInstance = menuItem?.Displayable as SkillInstance;

                if (skillInstance != null)
                {
                    sb.Append(SkillChoiceToJson(skillInstance, i, levelUpType));
                }
                else
                {
                    sb.Append($"{{\"index\": {i}, \"error\": \"Could not read skill\"}}");
                }
            }
            sb.Append("],");

            // Rerolls available
            var rerolls = InventoryManager.Instance?.SkillRerolls ?? 0;
            sb.Append($"\"rerollsAvailable\": {rerolls},");

            // Can choose max health instead (when at max skills)
            bool canChooseMaxHealth = levelingMonster != null &&
                levelingMonster.SkillManager.GetSkillTypeCount(levelUpType == SkillPicker.ELevelUpType.PickTrait) >= skillSelectMenu.MonsterMaxSkillCount;
            sb.Append($"\"canChooseMaxHealth\": {BoolToJson(canChooseMaxHealth)},");

            // Count pending level ups across all party members
            int pendingLevelUps = 0;
            foreach (var info in postCombatMenu.PostCombatMonsterInfos)
            {
                if (info.monster != null)
                {
                    pendingLevelUps += info.LevelUpUI.LevelsGainedLeft;
                }
            }
            sb.Append($"\"pendingLevelUps\": {pendingLevelUps},");

            // Full party state
            sb.Append("\"party\": ");
            sb.Append(GetPartyStateJson());

            sb.Append("}");
            return sb.ToString();
        }

        private static string SkillChoiceToJson(SkillInstance skill, int index, SkillPicker.ELevelUpType levelUpType)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"index\": {index},");
            sb.Append($"\"name\": \"{EscapeJson(skill.Skill?.Name ?? "Unknown")}\",");

            // Description
            string description = "";
            if (levelUpType == SkillPicker.ELevelUpType.PickSpell && skill.Action != null)
            {
                description = skill.Action.GetDescription(skill) ?? "";
            }
            else if (skill.Source is Trait trait)
            {
                description = trait.GetDescription(skill as PassiveInstance) ?? "";
            }
            sb.Append($"\"description\": \"{EscapeJson(description)}\",");

            // Is maverick
            bool isMaverick = skill.Skill?.MaverickSkill ?? false;
            sb.Append($"\"isMaverick\": {BoolToJson(isMaverick)},");

            if (levelUpType == SkillPicker.ELevelUpType.PickSpell && skill.Action != null)
            {
                // Action-specific fields
                var action = skill.Action;
                sb.Append($"\"cost\": {AetherToJson(skill.GetActionCost())},");

                // Elements
                sb.Append("\"elements\": [");
                for (int i = 0; i < action.Elements.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append($"\"{action.Elements[i]}\"");
                }
                sb.Append("],");

                sb.Append($"\"targetType\": \"{action.TargetType}\",");
                sb.Append("\"isTrait\": false");
            }
            else
            {
                // Trait-specific fields
                var trait = skill.Source as Trait;
                sb.Append($"\"isAura\": {BoolToJson(trait?.IsAura() ?? false)},");
                sb.Append("\"isTrait\": true");
            }

            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>
        /// Get full party state JSON
        /// </summary>
        public static string GetPartyStateJson()
        {
            var sb = new StringBuilder();
            sb.Append("[");

            var party = MonsterManager.Instance?.Active;
            if (party != null)
            {
                for (int i = 0; i < party.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append(PartyMonsterToJson(party[i], i));
                }
            }

            sb.Append("]");
            return sb.ToString();
        }

        private static string PartyMonsterToJson(Monster m, int index)
        {
            var sb = new StringBuilder();
            sb.Append("{");

            sb.Append($"\"index\": {index},");
            sb.Append($"\"name\": \"{EscapeJson(m.Name)}\",");
            sb.Append($"\"level\": {m.Level},");
            sb.Append($"\"hp\": {m.CurrentHealth},");
            sb.Append($"\"maxHp\": {m.Stats?.MaxHealth?.ValueInt ?? 0},");

            // XP progress
            var levelMgr = m.LevelManager;
            sb.Append($"\"currentExp\": {levelMgr?.CurrentExp ?? 0},");
            sb.Append($"\"expNeeded\": {levelMgr?.ExpNeededTotal ?? 0},");

            // Worthiness
            var worthiness = m.Worthiness;
            sb.Append($"\"worthinessLevel\": {worthiness?.WorthinessLevel ?? 0},");
            sb.Append($"\"currentWorthiness\": {worthiness?.CurrentWorthiness ?? 0},");
            sb.Append($"\"worthinessNeeded\": {worthiness?.CurrentRequiredWorthinessTotal ?? 0},");

            // Skills (actions)
            sb.Append("\"skills\": [");
            bool first = true;
            foreach (var skill in m.SkillManager?.Actions ?? new List<SkillInstance>())
            {
                if (!first) sb.Append(",");
                first = false;
                var desc = skill.Action?.GetDescription(skill) ?? "";
                sb.Append($"{{\"name\": \"{EscapeJson(skill.Action?.Name ?? "")}\", ");
                sb.Append($"\"description\": \"{EscapeJson(desc)}\", ");
                sb.Append($"\"cost\": {AetherToJson(skill.GetActionCost())}}}");
            }
            sb.Append("],");

            // Traits
            sb.Append("\"traits\": [");
            first = true;
            if (m.SkillManager?.Traits != null)
            {
                var signatureTraitId = m.SkillManager.SignatureTraitInstance?.Trait?.ID ?? -1;
                foreach (var trait in m.SkillManager.Traits)
                {
                    if (trait.Trait == null) continue;
                    if (!first) sb.Append(",");
                    first = false;
                    var isSignature = trait.Trait.ID == signatureTraitId;
                    var description = trait.Trait.GetDescription(trait) ?? "";
                    sb.Append($"{{\"name\": \"{EscapeJson(trait.Trait.Name ?? "")}\", ");
                    sb.Append($"\"description\": \"{EscapeJson(description)}\", ");
                    sb.Append($"\"isSignature\": {BoolToJson(isSignature)}, ");
                    sb.Append($"\"isAura\": {BoolToJson(trait.Trait.IsAura())}}}");
                }
            }
            sb.Append("]");

            sb.Append("}");
            return sb.ToString();
        }

        public static string ToText()
        {
            try
            {
                if (!GameStateManager.Instance?.IsCombat ?? true)
                {
                    return GetExplorationStateText();
                }
                return GetCombatStateText();
            }
            catch (Exception e)
            {
                return $"Error: {e.Message}";
            }
        }

        public static string GetValidActionsJson()
        {
            try
            {
                if (!GameStateManager.Instance?.IsCombat ?? true)
                {
                    return "{\"actions\": [], \"error\": \"Not in combat\"}";
                }

                var cc = CombatController.Instance;
                var current = cc.CurrentMonster;

                if (current == null || !current.BelongsToPlayer)
                {
                    return "{\"actions\": [], \"waitingFor\": \"enemy_turn\"}";
                }

                var sb = new StringBuilder();
                sb.Append("{\"actions\": [");

                bool first = true;
                int skillIdx = 0;
                foreach (var skill in current.SkillManager.Actions)
                {
                    var canUse = skill.Action?.CanUseAction(skill) ?? false;
                    if (canUse)
                    {
                        if (!first) sb.Append(",");
                        first = false;

                        var targets = GetValidTargets(current, skill);
                        sb.Append($"{{\"skillIndex\": {skillIdx}, \"name\": \"{EscapeJson(skill.Action?.Name ?? "")}\", ");
                        sb.Append($"\"cost\": {AetherToJson(skill.GetActionCost())}, ");
                        sb.Append($"\"targets\": {targets}}}");
                    }
                    skillIdx++;
                }

                sb.Append("]}");
                return sb.ToString();
            }
            catch (Exception e)
            {
                return $"{{\"error\": \"{EscapeJson(e.Message)}\"}}";
            }
        }

        private static string GetValidTargets(Monster caster, SkillInstance skill)
        {
            var targetType = skill.Action?.TargetType ?? ETargetType.SingleEnemy;
            var sb = new StringBuilder();
            sb.Append("[");

            bool first = true;
            var cc = CombatController.Instance;

            switch (targetType)
            {
                case ETargetType.SingleEnemy:
                case ETargetType.AllEnemies:
                    for (int i = 0; i < cc.Enemies.Count; i++)
                    {
                        var e = cc.Enemies[i];
                        if (!e.State.IsDead)
                        {
                            if (!first) sb.Append(",");
                            first = false;
                            sb.Append($"{{\"index\": {i}, \"name\": \"{EscapeJson(e.Name)}\"}}");
                        }
                    }
                    break;

                case ETargetType.SingleAlly:
                case ETargetType.AllAllies:
                    for (int i = 0; i < cc.PlayerMonsters.Count; i++)
                    {
                        var m = cc.PlayerMonsters[i];
                        if (!m.State.IsDead)
                        {
                            if (!first) sb.Append(",");
                            first = false;
                            sb.Append($"{{\"index\": {i}, \"name\": \"{EscapeJson(m.Name)}\"}}");
                        }
                    }
                    break;

                case ETargetType.SelfOrOwner:
                    sb.Append("{\"index\": -1, \"name\": \"self\"}");
                    break;
            }

            sb.Append("]");
            return sb.ToString();
        }

        private static string GetCombatStateJson()
        {
            var cc = CombatController.Instance;
            var sb = new StringBuilder();

            sb.Append("{");
            sb.Append($"\"phase\": \"COMBAT\",");
            sb.Append($"\"round\": {cc.Timeline?.CurrentRound ?? 0},");

            // Combat state and readiness
            var readyForInput = ActionHandler.IsReadyForInput();
            var inputStatus = ActionHandler.GetInputReadyStatus();
            sb.Append($"\"readyForInput\": {BoolToJson(readyForInput)},");
            sb.Append($"\"inputStatus\": \"{inputStatus}\",");

            // Current actor
            var current = cc.CurrentMonster;
            var currentIdx = current != null ? (current.BelongsToPlayer ? cc.PlayerMonsters.IndexOf(current) : -1) : -1;
            sb.Append($"\"currentActorIndex\": {currentIdx},");
            sb.Append($"\"isPlayerTurn\": {BoolToJson(current?.BelongsToPlayer ?? false)},");

            // Aether
            sb.Append($"\"playerAether\": {AetherToJson(cc.PlayerAether?.Aether)},");
            sb.Append($"\"enemyAether\": {AetherToJson(cc.EnemyAether?.Aether)},");

            // Player monsters (allies)
            sb.Append("\"allies\": [");
            for (int i = 0; i < cc.PlayerMonsters.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(MonsterToJson(cc.PlayerMonsters[i], i, true));
            }
            sb.Append("],");

            // Enemies
            sb.Append("\"enemies\": [");
            for (int i = 0; i < cc.Enemies.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(MonsterToJson(cc.Enemies[i], i, false));
            }
            sb.Append("],");

            // Consumables (artifacts)
            sb.Append("\"consumables\": ");
            sb.Append(GetConsumablesJson());
            sb.Append(",");

            // Combat result
            var stateManager = CombatStateManager.Instance;
            string result = "null";
            if (stateManager?.State?.CurrentState?.ID == CombatStateManager.EState.CombatFinished)
            {
                result = stateManager.WonEncounter ? "\"VICTORY\"" : "\"DEFEAT\"";
            }
            sb.Append($"\"combatResult\": {result}");

            sb.Append("}");
            return sb.ToString();
        }

        private static string MonsterToJson(Monster m, int index, bool isPlayer)
        {
            var sb = new StringBuilder();
            sb.Append("{");

            sb.Append($"\"index\": {index},");
            sb.Append($"\"name\": \"{EscapeJson(m.Name)}\",");
            sb.Append($"\"hp\": {m.CurrentHealth},");
            sb.Append($"\"maxHp\": {m.Stats?.MaxHealth?.ValueInt ?? 0},");
            sb.Append($"\"shield\": {m.Shield},");
            sb.Append($"\"corruption\": {m.Stats?.CurrentCorruption ?? 0},");
            sb.Append($"\"isDead\": {BoolToJson(m.State?.IsDead ?? false)},");
            sb.Append($"\"staggered\": {BoolToJson(m.Turn?.WasStaggered ?? false)},");

            // Buffs
            sb.Append("\"buffs\": [");
            bool first = true;
            if (m.BuffManager?.Buffs != null)
            {
                foreach (var buff in m.BuffManager.Buffs)
                {
                    if (buff.Buff?.BuffType == EBuffType.Buff)
                    {
                        if (!first) sb.Append(",");
                        first = false;
                        sb.Append($"{{\"name\": \"{EscapeJson(buff.Buff?.Name ?? "")}\", \"stacks\": {buff.Stacks}}}");
                    }
                }
            }
            sb.Append("],");

            // Debuffs
            sb.Append("\"debuffs\": [");
            first = true;
            if (m.BuffManager?.Buffs != null)
            {
                foreach (var buff in m.BuffManager.Buffs)
                {
                    if (buff.Buff?.BuffType == EBuffType.Debuff)
                    {
                        if (!first) sb.Append(",");
                        first = false;
                        sb.Append($"{{\"name\": \"{EscapeJson(buff.Buff?.Name ?? "")}\", \"stacks\": {buff.Stacks}}}");
                    }
                }
            }
            sb.Append("],");

            // Traits (including signature trait)
            sb.Append("\"traits\": [");
            first = true;
            if (m.SkillManager?.Traits != null)
            {
                var signatureTraitId = m.SkillManager.SignatureTraitInstance?.Trait?.ID ?? -1;
                foreach (var trait in m.SkillManager.Traits)
                {
                    if (trait.Trait == null) continue;
                    if (!first) sb.Append(",");
                    first = false;
                    var isSignature = trait.Trait.ID == signatureTraitId;
                    var description = trait.Trait.GetDescription(trait) ?? "";
                    sb.Append($"{{\"name\": \"{EscapeJson(trait.Trait.Name ?? "")}\", ");
                    sb.Append($"\"description\": \"{EscapeJson(description)}\", ");
                    sb.Append($"\"isSignature\": {BoolToJson(isSignature)}, ");
                    sb.Append($"\"isAura\": {BoolToJson(trait.Trait.IsAura())}}}");
                }
            }
            sb.Append("]");

            // Player-specific: skills
            if (isPlayer)
            {
                sb.Append(",\"skills\": [");
                first = true;
                int skillIdx = 0;
                foreach (var skill in m.SkillManager?.Actions ?? new List<SkillInstance>())
                {
                    if (!first) sb.Append(",");
                    first = false;
                    var canUse = skill.Action?.CanUseAction(skill) ?? false;
                    var description = skill.Action?.GetDescription(skill) ?? "";
                    sb.Append($"{{\"index\": {skillIdx}, \"name\": \"{EscapeJson(skill.Action?.Name ?? "")}\", ");
                    sb.Append($"\"description\": \"{EscapeJson(description)}\", ");
                    sb.Append($"\"cost\": {AetherToJson(skill.GetActionCost())}, ");
                    sb.Append($"\"canUse\": {BoolToJson(canUse)}}}");
                    skillIdx++;
                }
                sb.Append("]");
            }
            else
            {
                // Enemy-specific: poise and intended action
                sb.Append(",\"poise\": [");
                first = true;
                foreach (var poise in m.SkillManager?.Stagger ?? new List<StaggerDefine>())
                {
                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append($"{{\"element\": \"{poise.Element}\", \"current\": {poise.CurrentPoise}, \"max\": {poise.MaxHits}}}");
                }
                sb.Append("]");

                // Intended action
                if (m.AI?.PickedActionList != null && m.AI.PickedActionList.Count > 0)
                {
                    var action = m.AI.PickedActionList[0];
                    var targetName = (action.Target as Monster)?.Name ?? "unknown";
                    sb.Append($",\"intendedAction\": {{\"skill\": \"{EscapeJson(action.Action?.Action?.Name ?? "")}\", \"target\": \"{EscapeJson(targetName)}\"}}");
                }
                else
                {
                    sb.Append(",\"intendedAction\": null");
                }
            }

            sb.Append("}");
            return sb.ToString();
        }

        private static string AetherToJson(Aether aether)
        {
            if (aether == null) return "{}";
            return $"{{\"fire\": {aether.Fire}, \"water\": {aether.Water}, \"earth\": {aether.Earth}, \"wind\": {aether.Wind}, \"neutral\": {aether.Neutral}, \"wild\": {aether.Wild}}}";
        }

        private static string GetCombatStateText()
        {
            var cc = CombatController.Instance;
            var sb = new StringBuilder();

            var current = cc.CurrentMonster;
            sb.AppendLine($"=== COMBAT Round {cc.Timeline?.CurrentRound ?? 0} | Turn: {current?.Name ?? "???"} ===");

            // Player monsters
            sb.AppendLine("PLAYER TEAM:");
            for (int i = 0; i < cc.PlayerMonsters.Count; i++)
            {
                var m = cc.PlayerMonsters[i];
                var status = m.State?.IsDead == true ? "DEAD" : (m.Turn?.WasStaggered == true ? "STAGGERED" : "");
                var shield = m.Shield > 0 ? $" {m.Shield} shield" : "";
                var buffs = GetBuffsText(m);
                sb.AppendLine($"  [{i}] {m.Name,-12} {m.CurrentHealth}/{m.Stats?.MaxHealth?.ValueInt ?? 0} HP{shield} {status} {buffs}");
            }

            // Enemies
            sb.AppendLine("ENEMIES:");
            for (int i = 0; i < cc.Enemies.Count; i++)
            {
                var m = cc.Enemies[i];
                var status = m.State?.IsDead == true ? "DEAD" : (m.Turn?.WasStaggered == true ? "STAGGERED" : "");
                var poise = GetPoiseText(m);
                var intent = GetIntentText(m);
                sb.AppendLine($"  [{i}] {m.Name,-12} {m.CurrentHealth}/{m.Stats?.MaxHealth?.ValueInt ?? 0} HP {poise} {status} {intent}");
            }

            // Aether
            sb.AppendLine($"AETHER: You{AetherToText(cc.PlayerAether?.Aether)} | Enemy{AetherToText(cc.EnemyAether?.Aether)}");

            // Skills (if player turn)
            if (current?.BelongsToPlayer == true)
            {
                sb.AppendLine("SKILLS:");
                int idx = 0;
                foreach (var skill in current.SkillManager?.Actions ?? new List<SkillInstance>())
                {
                    var canUse = skill.Action?.CanUseAction(skill) ?? false;
                    var mark = canUse ? "OK" : "X";
                    var cost = AetherCostToText(skill.GetActionCost());
                    sb.AppendLine($"  [{idx}] {skill.Action?.Name,-16} ({cost}) [{mark}]");
                    idx++;
                }
            }

            return sb.ToString();
        }

        private static string GetBuffsText(Monster m)
        {
            if (m.BuffManager?.Buffs == null) return "";
            var buffs = new List<string>();
            foreach (var buff in m.BuffManager.Buffs)
            {
                if (buff.Stacks > 0)
                {
                    buffs.Add($"{buff.Buff?.Name} x{buff.Stacks}");
                }
            }
            return buffs.Count > 0 ? $"[{string.Join(", ", buffs)}]" : "";
        }

        private static string GetPoiseText(Monster m)
        {
            if (m.SkillManager?.Stagger == null) return "";
            var parts = new List<string>();
            foreach (var p in m.SkillManager.Stagger)
            {
                parts.Add($"{p.Element.ToString()[0]}:{p.CurrentPoise}/{p.MaxHits}");
            }
            return parts.Count > 0 ? $"Poise[{string.Join(" ", parts)}]" : "";
        }

        private static string GetIntentText(Monster m)
        {
            if (m.AI?.PickedActionList == null || m.AI.PickedActionList.Count == 0) return "";
            var action = m.AI.PickedActionList[0];
            var targetName = (action.Target as Monster)?.Name ?? "?";
            return $"-> {action.Action?.Action?.Name} @ {targetName}";
        }

        private static string AetherToText(Aether a)
        {
            if (a == null) return "[?]";
            return $"[F:{a.Fire} W:{a.Water} E:{a.Earth} Wi:{a.Wind} N:{a.Neutral}]";
        }

        private static string AetherCostToText(Aether a)
        {
            if (a == null) return "free";
            var parts = new List<string>();
            if (a.Fire > 0) parts.Add($"F:{a.Fire}");
            if (a.Water > 0) parts.Add($"W:{a.Water}");
            if (a.Earth > 0) parts.Add($"E:{a.Earth}");
            if (a.Wind > 0) parts.Add($"Wi:{a.Wind}");
            if (a.Neutral > 0) parts.Add($"N:{a.Neutral}");
            if (a.Wild > 0) parts.Add($"*:{a.Wild}");
            return parts.Count > 0 ? string.Join(" ", parts) : "free";
        }

        private static string GetExplorationStateJson()
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"phase\": \"EXPLORATION\",");

            // Player position
            var playerPos = PlayerMovementController.Instance?.transform?.position ?? UnityEngine.Vector3.zero;
            sb.Append($"\"player\": {{\"x\": {playerPos.x:F2}, \"y\": {playerPos.y:F2}, \"z\": {playerPos.z:F2}}},");

            // Current area info
            var area = ExplorationController.Instance?.CurrentArea.ToString() ?? "Unknown";
            var zone = ExplorationController.Instance?.CurrentZone ?? 0;
            sb.Append($"\"area\": \"{area}\",");
            sb.Append($"\"zone\": {zone},");

            // Get map if available
            var map = LevelGenerator.Instance?.Map;
            if (map == null)
            {
                // Still provide monster groups even without map
                sb.Append("\"monsterGroups\": [");
                var groups = ExplorationController.Instance?.EncounterGroups;
                if (groups != null)
                {
                    for (int i = 0; i < groups.Count; i++)
                    {
                        var group = groups[i];
                        if (group == null) continue;
                        if (i > 0) sb.Append(",");
                        var pos = group.transform.position;
                        var defeated = group.EncounterDefeated;
                        var canBlitz = !defeated && group.CanBeAetherBlitzed();
                        var encounterType = group.EncounterData?.EncounterType.ToString() ?? "Unknown";
                        var monsterCount = group.OverworldMonsters?.Count ?? 0;
                        sb.Append($"{{\"index\": {i}, \"x\": {pos.x:F2}, \"y\": {pos.y:F2}, \"z\": {pos.z:F2}, \"defeated\": {BoolToJson(defeated)}, \"canVoidBlitz\": {BoolToJson(canBlitz)}, \"encounterType\": \"{encounterType}\", \"monsterCount\": {monsterCount}}}");
                    }
                }
                sb.Append("],");
                sb.Append("\"interactables\": []");
                sb.Append("}");
                return sb.ToString();
            }

            // Monster Groups (for /combat/start API)
            sb.Append("\"monsterGroups\": [");
            var encounterGroups = ExplorationController.Instance?.EncounterGroups;
            if (encounterGroups != null)
            {
                for (int i = 0; i < encounterGroups.Count; i++)
                {
                    var group = encounterGroups[i];
                    if (group == null) continue;
                    if (i > 0) sb.Append(",");
                    var pos = group.transform.position;
                    var defeated = group.EncounterDefeated;
                    var canBlitz = !defeated && group.CanBeAetherBlitzed();
                    var encounterType = group.EncounterData?.EncounterType.ToString() ?? "Unknown";
                    var monsterCount = group.OverworldMonsters?.Count ?? 0;
                    sb.Append($"{{\"index\": {i}, \"x\": {pos.x:F2}, \"y\": {pos.y:F2}, \"z\": {pos.z:F2}, \"defeated\": {BoolToJson(defeated)}, \"canVoidBlitz\": {BoolToJson(canBlitz)}, \"encounterType\": \"{encounterType}\", \"monsterCount\": {monsterCount}}}");
                }
            }
            sb.Append("],");

            sb.Append("\"interactables\": [");
            bool first = true;

            // Aether Springs
            foreach (var spring in map.AetherSpringInteractables)
            {
                if (spring == null) continue;
                if (!first) sb.Append(",");
                first = false;
                var pos = spring.transform.position;
                var used = spring.WasUsedUp;
                sb.Append($"{{\"type\": \"AETHER_SPRING\", \"x\": {pos.x:F2}, \"y\": {pos.y:F2}, \"z\": {pos.z:F2}, \"used\": {BoolToJson(used)}}}");
            }

            // Monster Groups
            foreach (var mg in map.MonsterGroupInteractables)
            {
                if (mg == null) continue;
                if (!first) sb.Append(",");
                first = false;
                var pos = mg.transform.position;
                var used = mg.WasUsedUp;
                sb.Append($"{{\"type\": \"MONSTER_GROUP\", \"x\": {pos.x:F2}, \"y\": {pos.y:F2}, \"z\": {pos.z:F2}, \"defeated\": {BoolToJson(used)}}}");
            }

            // Chests
            foreach (var chest in map.ChestInteractables)
            {
                if (chest == null) continue;
                if (!first) sb.Append(",");
                first = false;
                var pos = chest.transform.position;
                var used = chest.WasUsedUp;
                sb.Append($"{{\"type\": \"CHEST\", \"x\": {pos.x:F2}, \"y\": {pos.y:F2}, \"z\": {pos.z:F2}, \"opened\": {BoolToJson(used)}}}");
            }

            // Merchant
            if (map.MerchantInteractable != null)
            {
                if (!first) sb.Append(",");
                first = false;
                var pos = map.MerchantInteractable.transform.position;
                sb.Append($"{{\"type\": \"MERCHANT\", \"x\": {pos.x:F2}, \"y\": {pos.y:F2}, \"z\": {pos.z:F2}}}");
            }

            // Monster Shrine
            if (map.MonsterShrine != null)
            {
                if (!first) sb.Append(",");
                first = false;
                var pos = map.MonsterShrine.transform.position;
                var used = map.MonsterShrine.WasUsedUp;
                sb.Append($"{{\"type\": \"MONSTER_SHRINE\", \"x\": {pos.x:F2}, \"y\": {pos.y:F2}, \"z\": {pos.z:F2}, \"used\": {BoolToJson(used)}}}");
            }

            // NPCs/Dialogue
            foreach (var npc in map.DialogueInteractables)
            {
                if (npc == null) continue;
                if (!first) sb.Append(",");
                first = false;
                var pos = npc.transform.position;
                var used = npc.WasUsedUp;
                sb.Append($"{{\"type\": \"NPC\", \"x\": {pos.x:F2}, \"y\": {pos.y:F2}, \"z\": {pos.z:F2}, \"talked\": {BoolToJson(used)}}}");
            }

            // Small Events
            foreach (var evt in map.SmallEventInteractables)
            {
                if (evt == null) continue;
                if (!first) sb.Append(",");
                first = false;
                var pos = evt.transform.position;
                var used = evt.WasUsedUp;
                sb.Append($"{{\"type\": \"EVENT\", \"x\": {pos.x:F2}, \"y\": {pos.y:F2}, \"z\": {pos.z:F2}, \"completed\": {BoolToJson(used)}}}");
            }

            // Secret Rooms
            foreach (var secret in map.SecretRoomInteractables)
            {
                if (secret == null) continue;
                if (!first) sb.Append(",");
                first = false;
                var pos = secret.transform.position;
                var used = secret.WasUsedUp;
                sb.Append($"{{\"type\": \"SECRET_ROOM\", \"x\": {pos.x:F2}, \"y\": {pos.y:F2}, \"z\": {pos.z:F2}, \"found\": {BoolToJson(used)}}}");
            }

            sb.Append("]");
            sb.Append("}");
            return sb.ToString();
        }

        private static string GetExplorationStateText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== EXPLORATION ===");

            var playerPos = PlayerMovementController.Instance?.transform?.position ?? UnityEngine.Vector3.zero;
            sb.AppendLine($"Player: ({playerPos.x:F1}, {playerPos.y:F1}, {playerPos.z:F1})");

            var area = ExplorationController.Instance?.CurrentArea.ToString() ?? "Unknown";
            var zone = ExplorationController.Instance?.CurrentZone ?? 0;
            sb.AppendLine($"Area: {area} Zone {zone}");

            var map = LevelGenerator.Instance?.Map;
            if (map == null)
            {
                sb.AppendLine("(Map not loaded)");
                return sb.ToString();
            }

            sb.AppendLine("INTERACTABLES:");

            int idx = 0;
            foreach (var spring in map.AetherSpringInteractables)
            {
                if (spring == null) continue;
                var pos = spring.transform.position;
                var status = spring.WasUsedUp ? "USED" : "";
                sb.AppendLine($"  [{idx++}] SPRING     ({pos.x:F0},{pos.y:F0}) {status}");
            }

            foreach (var mg in map.MonsterGroupInteractables)
            {
                if (mg == null) continue;
                var pos = mg.transform.position;
                var status = mg.WasUsedUp ? "DEFEATED" : "";
                sb.AppendLine($"  [{idx++}] MONSTERS   ({pos.x:F0},{pos.y:F0}) {status}");
            }

            foreach (var chest in map.ChestInteractables)
            {
                if (chest == null) continue;
                var pos = chest.transform.position;
                var status = chest.WasUsedUp ? "OPENED" : "";
                sb.AppendLine($"  [{idx++}] CHEST      ({pos.x:F0},{pos.y:F0}) {status}");
            }

            if (map.MerchantInteractable != null)
            {
                var pos = map.MerchantInteractable.transform.position;
                sb.AppendLine($"  [{idx++}] MERCHANT   ({pos.x:F0},{pos.y:F0})");
            }

            if (map.MonsterShrine != null)
            {
                var pos = map.MonsterShrine.transform.position;
                var status = map.MonsterShrine.WasUsedUp ? "USED" : "";
                sb.AppendLine($"  [{idx++}] SHRINE     ({pos.x:F0},{pos.y:F0}) {status}");
            }

            return sb.ToString();
        }

        private static string BoolToJson(bool b) => b ? "true" : "false";

        private static string GetConsumablesJson()
        {
            var sb = new StringBuilder();
            sb.Append("[");

            try
            {
                var consumables = PlayerController.Instance?.Inventory?.GetAllConsumables();
                if (consumables != null)
                {
                    for (int i = 0; i < consumables.Count; i++)
                    {
                        if (i > 0) sb.Append(",");
                        var c = consumables[i];

                        // Set owner for CanUseAction check (required)
                        var currentMonster = CombatController.Instance?.CurrentMonster;
                        if (currentMonster != null) c.Owner = currentMonster;

                        sb.Append("{");
                        sb.Append($"\"index\": {i},");
                        sb.Append($"\"name\": \"{EscapeJson(c.Consumable?.Name ?? c.Skill?.Name ?? "Unknown")}\",");
                        sb.Append($"\"description\": \"{EscapeJson(c.Action?.GetDescription(c) ?? "")}\",");
                        sb.Append($"\"currentCharges\": {c.Charges},");
                        sb.Append($"\"maxCharges\": {c.GetMaxCharges()},");
                        sb.Append($"\"canUse\": {BoolToJson(c.Action?.CanUseAction(c) ?? false)},");
                        sb.Append($"\"targetType\": \"{c.Action?.TargetType ?? ETargetType.SelfOrOwner}\"");
                        sb.Append("}");
                    }
                }
            }
            catch
            {
                // If consumables can't be accessed, return empty array
            }

            sb.Append("]");
            return sb.ToString();
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t");
        }
    }
}
