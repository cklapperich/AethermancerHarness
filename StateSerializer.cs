using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;

namespace AethermancerHarness
{
    public static class StateSerializer
    {
        public static string ToJson()
        {
            // Check for skill selection state first (post-combat level up)
            if (IsInSkillSelection())
                return GetSkillSelectionStateJson();

            if (!(GameStateManager.Instance?.IsCombat ?? false))
                return GetExplorationStateJson();

            return GetCombatStateJson();
        }

        public static bool IsInSkillSelection()
        {
            return UIController.Instance?.PostCombatMenu?.SkillSelectMenu?.IsOpen ?? false;
        }

        public static bool IsInPostCombatMenu()
        {
            return UIController.Instance?.PostCombatMenu?.IsOpen ?? false;
        }

        public static string GetSkillSelectionStateJson()
        {
            var skillSelectMenu = UIController.Instance.PostCombatMenu.SkillSelectMenu;
            var postCombatMenu = UIController.Instance.PostCombatMenu;
            var levelUpType = skillSelectMenu.LevelUpType;

            // Find which monster is leveling up
            Monster levelingMonster = null;
            int monsterIndex = -1;
            foreach (var info in postCombatMenu.PostCombatMonsterInfos)
            {
                if (info.monster != null && info.LevelUpUI.LevelsGainedLeft > 0 &&
                    info.gameObject.activeSelf &&
                    info.transform.localPosition == postCombatMenu.SkillSelectionMonsterPosition.transform.localPosition)
                {
                    levelingMonster = info.monster;
                    monsterIndex = postCombatMenu.PostCombatMonsterInfos.IndexOf(info);
                    break;
                }
            }

            // Fallback to first active monster
            if (levelingMonster == null && MonsterManager.Instance.Active.Count > 0)
            {
                levelingMonster = MonsterManager.Instance.Active[0];
                monsterIndex = 0;
            }

            // Build choices array
            var choices = new JArray();
            for (int i = 0; i < skillSelectMenu.SkillTooltips.Count && i < 3; i++)
            {
                var tooltip = skillSelectMenu.SkillTooltips[i];
                var menuItem = tooltip.GetComponent<MenuListItem>();
                var skillInstance = menuItem?.Displayable as SkillInstance;

                if (skillInstance != null)
                    choices.Add(BuildSkillChoice(skillInstance, i, levelUpType));
                else
                    choices.Add(new JObject { ["index"] = i, ["error"] = "Could not read skill" });
            }

            // Count pending level ups
            int pendingLevelUps = 0;
            foreach (var info in postCombatMenu.PostCombatMonsterInfos)
            {
                if (info.monster != null)
                    pendingLevelUps += info.LevelUpUI.LevelsGainedLeft;
            }

            bool canChooseMaxHealth = levelingMonster != null &&
                levelingMonster.SkillManager.GetSkillTypeCount(levelUpType == SkillPicker.ELevelUpType.PickTrait) >= skillSelectMenu.MonsterMaxSkillCount;

            var result = new JObject
            {
                ["phase"] = "SKILL_SELECTION",
                ["levelUpType"] = levelUpType == SkillPicker.ELevelUpType.PickSpell ? "action" : "trait",
                ["monster"] = levelingMonster != null
                    ? new JObject { ["index"] = monsterIndex, ["name"] = levelingMonster.Name, ["level"] = levelingMonster.Level }
                    : null,
                ["choices"] = choices,
                ["rerollsAvailable"] = InventoryManager.Instance?.SkillRerolls ?? 0,
                ["canChooseMaxHealth"] = canChooseMaxHealth,
                ["pendingLevelUps"] = pendingLevelUps,
                ["gold"] = InventoryManager.Instance?.Gold ?? 0,
                ["artifacts"] = BuildArtifactsArray(),
                ["inventory"] = BuildInventoryObject(),
                ["party"] = BuildDetailedPartyArray()
            };

            return result.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static JObject BuildSkillChoice(SkillInstance skill, int index, SkillPicker.ELevelUpType levelUpType)
        {
            var obj = new JObject
            {
                ["index"] = index,
                ["name"] = skill.Skill?.Name ?? "Unknown",
                ["isMaverick"] = skill.Skill?.MaverickSkill ?? false
            };

            if (levelUpType == SkillPicker.ELevelUpType.PickSpell && skill.Action != null)
            {
                obj["description"] = skill.Action.GetDescription(skill) ?? "";
                obj["cost"] = BuildAetherObject(skill.GetActionCost());
                obj["elements"] = new JArray(skill.Action.Elements.ConvertAll(e => e.ToString()));
                obj["targetType"] = skill.Action.TargetType.ToString();
                obj["isTrait"] = false;
            }
            else
            {
                var trait = skill.Source as Trait;
                obj["description"] = trait?.GetDescription(skill as PassiveInstance) ?? "";
                obj["isAura"] = trait?.IsAura() ?? false;
                obj["isTrait"] = true;
            }

            return obj;
        }

        public static string GetPartyStateJson()
        {
            var party = MonsterManager.Instance?.Active;
            if (party == null) return "[]";

            var arr = new JArray();
            for (int i = 0; i < party.Count; i++)
                arr.Add(BuildPartyMonster(party[i], i));

            return arr.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static JObject BuildPartyMonster(Monster m, int index)
        {
            var skills = new JArray();
            foreach (var skill in m.SkillManager?.Actions ?? new List<SkillInstance>())
            {
                skills.Add(new JObject
                {
                    ["name"] = skill.Action?.Name ?? "",
                    ["description"] = skill.Action?.GetDescription(skill) ?? "",
                    ["cost"] = BuildAetherObject(skill.GetActionCost())
                });
            }

            return new JObject
            {
                ["index"] = index,
                ["name"] = m.Name,
                ["level"] = m.Level,
                ["hp"] = m.CurrentHealth,
                ["maxHp"] = m.Stats?.MaxHealth?.ValueInt ?? 0,
                ["currentExp"] = m.LevelManager?.CurrentExp ?? 0,
                ["expNeeded"] = m.LevelManager?.ExpNeededTotal ?? 0,
                ["worthinessLevel"] = m.Worthiness?.WorthinessLevel ?? 0,
                ["currentWorthiness"] = m.Worthiness?.CurrentWorthiness ?? 0,
                ["worthinessNeeded"] = m.Worthiness?.CurrentRequiredWorthinessTotal ?? 0,
                ["skills"] = skills,
                ["traits"] = BuildTraitsArray(m)
            };
        }

        public static string ToText()
        {
            if (!(GameStateManager.Instance?.IsCombat ?? false))
                return GetExplorationStateText();
            return GetCombatStateText();
        }

        public static string GetValidActionsJson()
        {
            if (!(GameStateManager.Instance?.IsCombat ?? false))
                return JsonHelper.Serialize(new { actions = new object[0], error = "Not in combat" });

            var cc = CombatController.Instance;
            var current = cc.CurrentMonster;

            if (current == null || !current.BelongsToPlayer)
                return JsonHelper.Serialize(new { actions = new object[0], waitingFor = "enemy_turn" });

            var actions = new JArray();
            int skillIdx = 0;
            foreach (var skill in current.SkillManager.Actions)
            {
                if (skill.Action?.CanUseAction(skill) ?? false)
                {
                    actions.Add(new JObject
                    {
                        ["skillIndex"] = skillIdx,
                        ["name"] = skill.Action?.Name ?? "",
                        ["cost"] = BuildAetherObject(skill.GetActionCost()),
                        ["targets"] = BuildValidTargets(current, skill)
                    });
                }
                skillIdx++;
            }

            return new JObject { ["actions"] = actions }.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static JArray BuildValidTargets(Monster caster, SkillInstance skill)
        {
            var targets = new JArray();
            var targetType = skill.Action?.TargetType ?? ETargetType.SingleEnemy;
            var cc = CombatController.Instance;

            switch (targetType)
            {
                case ETargetType.SingleEnemy:
                case ETargetType.AllEnemies:
                    for (int i = 0; i < cc.Enemies.Count; i++)
                    {
                        var e = cc.Enemies[i];
                        if (!e.State.IsDead)
                            targets.Add(new JObject { ["index"] = i, ["name"] = e.Name });
                    }
                    break;

                case ETargetType.SingleAlly:
                case ETargetType.AllAllies:
                    for (int i = 0; i < cc.PlayerMonsters.Count; i++)
                    {
                        var m = cc.PlayerMonsters[i];
                        if (!m.State.IsDead)
                            targets.Add(new JObject { ["index"] = i, ["name"] = m.Name });
                    }
                    break;

                case ETargetType.SelfOrOwner:
                    targets.Add(new JObject { ["index"] = -1, ["name"] = "self" });
                    break;
            }

            return targets;
        }

        private static string GetCombatStateJson()
        {
            var cc = CombatController.Instance;
            var current = cc.CurrentMonster;
            var currentIdx = current != null ? (current.BelongsToPlayer ? cc.PlayerMonsters.IndexOf(current) : -1) : -1;

            var allies = new JArray();
            for (int i = 0; i < cc.PlayerMonsters.Count; i++)
                allies.Add(BuildCombatMonster(cc.PlayerMonsters[i], i, true));

            var enemies = new JArray();
            for (int i = 0; i < cc.Enemies.Count; i++)
                enemies.Add(BuildCombatMonster(cc.Enemies[i], i, false));

            var stateManager = CombatStateManager.Instance;
            string combatResult = null;
            if (stateManager?.State?.CurrentState?.ID == CombatStateManager.EState.CombatFinished)
                combatResult = stateManager.WonEncounter ? "VICTORY" : "DEFEAT";

            var result = new JObject
            {
                ["phase"] = "COMBAT",
                ["round"] = cc.Timeline?.CurrentRound ?? 0,
                ["readyForInput"] = ActionHandler.IsReadyForInput(),
                ["inputStatus"] = ActionHandler.GetInputReadyStatus(),
                ["currentActorIndex"] = currentIdx,
                ["isPlayerTurn"] = current?.BelongsToPlayer ?? false,
                ["playerAether"] = BuildAetherObject(cc.PlayerAether?.Aether),
                ["enemyAether"] = BuildAetherObject(cc.EnemyAether?.Aether),
                ["allies"] = allies,
                ["enemies"] = enemies,
                ["consumables"] = BuildConsumablesArray(),
                ["gold"] = InventoryManager.Instance?.Gold ?? 0,
                ["artifacts"] = BuildArtifactsArray(),
                ["inventory"] = BuildInventoryObject(),
                ["combatResult"] = combatResult
            };

            return result.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static JObject BuildCombatMonster(Monster m, int index, bool isPlayer)
        {
            var obj = new JObject
            {
                ["index"] = index,
                ["name"] = m.Name,
                ["hp"] = m.CurrentHealth,
                ["maxHp"] = m.Stats?.MaxHealth?.ValueInt ?? 0,
                ["shield"] = m.Shield,
                ["corruption"] = m.Stats?.CurrentCorruption ?? 0,
                ["isDead"] = m.State?.IsDead ?? false,
                ["staggered"] = m.Turn?.WasStaggered ?? false,
                ["buffs"] = BuildBuffsArray(m, EBuffType.Buff),
                ["debuffs"] = BuildBuffsArray(m, EBuffType.Debuff),
                ["traits"] = BuildTraitsArray(m)
            };

            if (isPlayer)
            {
                var skills = new JArray();
                int skillIdx = 0;
                foreach (var skill in m.SkillManager?.Actions ?? new List<SkillInstance>())
                {
                    skills.Add(new JObject
                    {
                        ["index"] = skillIdx,
                        ["name"] = skill.Action?.Name ?? "",
                        ["description"] = skill.Action?.GetDescription(skill) ?? "",
                        ["cost"] = BuildAetherObject(skill.GetActionCost()),
                        ["canUse"] = skill.Action?.CanUseAction(skill) ?? false
                    });
                    skillIdx++;
                }
                obj["skills"] = skills;
            }
            else
            {
                // Poise
                var poise = new JArray();
                foreach (var p in m.SkillManager?.Stagger ?? new List<StaggerDefine>())
                {
                    poise.Add(new JObject
                    {
                        ["element"] = p.Element.ToString(),
                        ["current"] = p.CurrentPoise,
                        ["max"] = p.MaxHits
                    });
                }
                obj["poise"] = poise;

                // Intended action
                if (m.AI?.PickedActionList != null && m.AI.PickedActionList.Count > 0)
                {
                    var action = m.AI.PickedActionList[0];
                    var targetName = (action.Target as Monster)?.Name ?? "unknown";
                    obj["intendedAction"] = new JObject
                    {
                        ["skill"] = action.Action?.Action?.Name ?? "",
                        ["target"] = targetName
                    };
                }
                else
                {
                    obj["intendedAction"] = null;
                }
            }

            return obj;
        }

        private static JArray BuildBuffsArray(Monster m, EBuffType buffType)
        {
            var arr = new JArray();
            if (m.BuffManager?.Buffs == null) return arr;

            foreach (var buff in m.BuffManager.Buffs)
            {
                if (buff.Buff?.BuffType == buffType)
                    arr.Add(new JObject { ["name"] = buff.Buff?.Name ?? "", ["stacks"] = buff.Stacks });
            }
            return arr;
        }

        private static JArray BuildTraitsArray(Monster m)
        {
            var arr = new JArray();
            if (m.SkillManager?.Traits == null) return arr;

            var signatureTraitId = m.SkillManager.SignatureTraitInstance?.Trait?.ID ?? -1;
            foreach (var trait in m.SkillManager.Traits)
            {
                if (trait.Trait == null) continue;
                arr.Add(new JObject
                {
                    ["name"] = trait.Trait.Name ?? "",
                    ["description"] = trait.Trait.GetDescription(trait) ?? "",
                    ["isSignature"] = trait.Trait.ID == signatureTraitId,
                    ["isAura"] = trait.Trait.IsAura()
                });
            }
            return arr;
        }

        private static JObject BuildAetherObject(Aether aether)
        {
            if (aether == null) return new JObject();
            return new JObject
            {
                ["fire"] = aether.Fire,
                ["water"] = aether.Water,
                ["earth"] = aether.Earth,
                ["wind"] = aether.Wind,
                ["neutral"] = aether.Neutral,
                ["wild"] = aether.Wild
            };
        }

        private static string GetCombatStateText()
        {
            var cc = CombatController.Instance;
            var sb = new StringBuilder();
            var current = cc.CurrentMonster;

            sb.AppendLine($"=== COMBAT Round {cc.Timeline?.CurrentRound ?? 0} | Turn: {current?.Name ?? "???"} ===");

            sb.AppendLine("PLAYER TEAM:");
            for (int i = 0; i < cc.PlayerMonsters.Count; i++)
            {
                var m = cc.PlayerMonsters[i];
                var status = m.State?.IsDead == true ? "DEAD" : (m.Turn?.WasStaggered == true ? "STAGGERED" : "");
                var shield = m.Shield > 0 ? $" {m.Shield} shield" : "";
                var buffs = GetBuffsText(m);
                sb.AppendLine($"  [{i}] {m.Name,-12} {m.CurrentHealth}/{m.Stats?.MaxHealth?.ValueInt ?? 0} HP{shield} {status} {buffs}");
            }

            sb.AppendLine("ENEMIES:");
            for (int i = 0; i < cc.Enemies.Count; i++)
            {
                var m = cc.Enemies[i];
                var status = m.State?.IsDead == true ? "DEAD" : (m.Turn?.WasStaggered == true ? "STAGGERED" : "");
                var poise = GetPoiseText(m);
                var intent = GetIntentText(m);
                sb.AppendLine($"  [{i}] {m.Name,-12} {m.CurrentHealth}/{m.Stats?.MaxHealth?.ValueInt ?? 0} HP {poise} {status} {intent}");
            }

            sb.AppendLine($"AETHER: You{AetherToText(cc.PlayerAether?.Aether)} | Enemy{AetherToText(cc.EnemyAether?.Aether)}");

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
                    buffs.Add($"{buff.Buff?.Name} x{buff.Stacks}");
            }
            return buffs.Count > 0 ? $"[{string.Join(", ", buffs)}]" : "";
        }

        private static string GetPoiseText(Monster m)
        {
            if (m.SkillManager?.Stagger == null) return "";
            var parts = new List<string>();
            foreach (var p in m.SkillManager.Stagger)
                parts.Add($"{p.Element.ToString()[0]}:{p.CurrentPoise}/{p.MaxHits}");
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
            var playerPos = PlayerMovementController.Instance?.transform?.position ?? UnityEngine.Vector3.zero;
            var area = ExplorationController.Instance?.CurrentArea.ToString() ?? "Unknown";
            var zone = ExplorationController.Instance?.CurrentZone ?? 0;

            var result = new JObject
            {
                ["phase"] = "EXPLORATION",
                ["player"] = new JObject { ["x"] = playerPos.x, ["y"] = playerPos.y, ["z"] = playerPos.z },
                ["area"] = area,
                ["zone"] = zone,
                ["gold"] = InventoryManager.Instance?.Gold ?? 0,
                ["party"] = BuildDetailedPartyArray(),
                ["artifacts"] = BuildArtifactsArray(),
                ["inventory"] = BuildInventoryObject(),
                ["monsterGroups"] = BuildMonsterGroupsArray(),
                ["interactables"] = BuildInteractablesArray()
            };

            return result.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static JArray BuildMonsterGroupsArray()
        {
            var arr = new JArray();
            var groups = ExplorationController.Instance?.EncounterGroups;
            if (groups == null) return arr;

            for (int i = 0; i < groups.Count; i++)
            {
                var group = groups[i];
                if (group == null) continue;
                var pos = group.transform.position;
                arr.Add(new JObject
                {
                    ["index"] = i,
                    ["x"] = pos.x,
                    ["y"] = pos.y,
                    ["z"] = pos.z,
                    ["defeated"] = group.EncounterDefeated,
                    ["canVoidBlitz"] = !group.EncounterDefeated && group.CanBeAetherBlitzed(),
                    ["encounterType"] = group.EncounterData?.EncounterType.ToString() ?? "Unknown",
                    ["monsterCount"] = group.OverworldMonsters?.Count ?? 0
                });
            }
            return arr;
        }

        private static JArray BuildInteractablesArray()
        {
            var arr = new JArray();
            var map = LevelGenerator.Instance?.Map;
            if (map == null) return arr;

            foreach (var spring in map.AetherSpringInteractables)
            {
                if (spring == null) continue;
                var pos = spring.transform.position;
                arr.Add(new JObject
                {
                    ["type"] = "AETHER_SPRING",
                    ["x"] = pos.x, ["y"] = pos.y, ["z"] = pos.z,
                    ["used"] = spring.WasUsedUp
                });
            }

            foreach (var mg in map.MonsterGroupInteractables)
            {
                if (mg == null) continue;
                var pos = mg.transform.position;
                arr.Add(new JObject
                {
                    ["type"] = "MONSTER_GROUP",
                    ["x"] = pos.x, ["y"] = pos.y, ["z"] = pos.z,
                    ["defeated"] = mg.WasUsedUp
                });
            }

            foreach (var chest in map.ChestInteractables)
            {
                if (chest == null) continue;
                var pos = chest.transform.position;
                arr.Add(new JObject
                {
                    ["type"] = "CHEST",
                    ["x"] = pos.x, ["y"] = pos.y, ["z"] = pos.z,
                    ["opened"] = chest.WasUsedUp
                });
            }

            if (map.MerchantInteractable != null)
            {
                var pos = map.MerchantInteractable.transform.position;
                arr.Add(new JObject
                {
                    ["type"] = "MERCHANT",
                    ["x"] = pos.x, ["y"] = pos.y, ["z"] = pos.z
                });
            }

            if (map.MonsterShrine != null)
            {
                var pos = map.MonsterShrine.transform.position;
                arr.Add(new JObject
                {
                    ["type"] = "MONSTER_SHRINE",
                    ["x"] = pos.x, ["y"] = pos.y, ["z"] = pos.z,
                    ["used"] = map.MonsterShrine.WasUsedUp
                });
            }

            foreach (var npc in map.DialogueInteractables)
            {
                if (npc == null) continue;
                var pos = npc.transform.position;
                arr.Add(new JObject
                {
                    ["type"] = "NPC",
                    ["x"] = pos.x, ["y"] = pos.y, ["z"] = pos.z,
                    ["talked"] = npc.WasUsedUp
                });
            }

            foreach (var evt in map.SmallEventInteractables)
            {
                if (evt == null) continue;
                var pos = evt.transform.position;
                arr.Add(new JObject
                {
                    ["type"] = "EVENT",
                    ["x"] = pos.x, ["y"] = pos.y, ["z"] = pos.z,
                    ["completed"] = evt.WasUsedUp
                });
            }

            foreach (var secret in map.SecretRoomInteractables)
            {
                if (secret == null) continue;
                var pos = secret.transform.position;
                arr.Add(new JObject
                {
                    ["type"] = "SECRET_ROOM",
                    ["x"] = pos.x, ["y"] = pos.y, ["z"] = pos.z,
                    ["found"] = secret.WasUsedUp
                });
            }

            return arr;
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

        private static JArray BuildConsumablesArray()
        {
            var arr = new JArray();
            var consumables = PlayerController.Instance?.Inventory?.GetAllConsumables();
            if (consumables == null) return arr;

            var currentMonster = CombatController.Instance?.CurrentMonster;

            for (int i = 0; i < consumables.Count; i++)
            {
                var c = consumables[i];
                if (currentMonster != null) c.Owner = currentMonster;

                arr.Add(new JObject
                {
                    ["index"] = i,
                    ["name"] = c.Consumable?.Name ?? c.Skill?.Name ?? "Unknown",
                    ["description"] = c.Action?.GetDescription(c) ?? "",
                    ["currentCharges"] = c.Charges,
                    ["maxCharges"] = c.GetMaxCharges(),
                    ["canUse"] = c.Action?.CanUseAction(c) ?? false,
                    ["targetType"] = (c.Action?.TargetType ?? ETargetType.SelfOrOwner).ToString()
                });
            }

            return arr;
        }

        private static JArray BuildDetailedPartyArray()
        {
            var arr = new JArray();
            var party = MonsterManager.Instance?.Active;
            if (party == null) return arr;

            for (int i = 0; i < party.Count; i++)
                arr.Add(BuildDetailedPartyMonster(party[i], i));

            return arr;
        }

        private static JObject BuildDetailedPartyMonster(Monster m, int index)
        {
            var actions = new JArray();
            int actionIdx = 0;
            foreach (var skill in m.SkillManager?.Actions ?? new List<SkillInstance>())
            {
                var elements = new JArray();
                if (skill.Action?.Elements != null)
                {
                    foreach (var e in skill.Action.Elements)
                        elements.Add(e.ToString());
                }

                actions.Add(new JObject
                {
                    ["index"] = actionIdx,
                    ["name"] = skill.Action?.Name ?? "",
                    ["description"] = skill.Action?.GetDescription(skill) ?? "",
                    ["cost"] = BuildAetherObject(skill.GetActionCost()),
                    ["targetType"] = skill.Action?.TargetType.ToString() ?? "Unknown",
                    ["elements"] = elements
                });
                actionIdx++;
            }

            return new JObject
            {
                ["index"] = index,
                ["name"] = m.Name,
                ["level"] = m.Level,
                ["hp"] = m.CurrentHealth,
                ["maxHp"] = m.Stats?.MaxHealth?.ValueInt ?? 0,
                ["shield"] = m.Shield,
                ["currentExp"] = m.LevelManager?.CurrentExp ?? 0,
                ["expNeeded"] = m.LevelManager?.ExpNeededTotal ?? 0,
                ["worthinessLevel"] = m.Worthiness?.WorthinessLevel ?? 0,
                ["currentWorthiness"] = m.Worthiness?.CurrentWorthiness ?? 0,
                ["worthinessNeeded"] = m.Worthiness?.CurrentRequiredWorthinessTotal ?? 0,
                ["actions"] = actions,
                ["traits"] = BuildTraitsArray(m)
            };
        }

        private static JArray BuildArtifactsArray()
        {
            var arr = new JArray();
            var consumables = InventoryManager.Instance?.GetAllConsumables();
            if (consumables == null) return arr;

            for (int i = 0; i < consumables.Count; i++)
            {
                var c = consumables[i];
                if (c.Charges <= 0) continue;

                arr.Add(new JObject
                {
                    ["index"] = i,
                    ["name"] = c.Consumable?.Name ?? c.Skill?.Name ?? "Unknown",
                    ["description"] = c.Action?.GetDescription(c) ?? "",
                    ["currentCharges"] = c.Charges,
                    ["maxCharges"] = c.GetMaxCharges(),
                    ["targetType"] = (c.Action?.TargetType ?? ETargetType.SelfOrOwner).ToString()
                });
            }

            return arr;
        }

        private static JObject BuildInventoryObject()
        {
            var inventory = InventoryManager.Instance;

            var availableSummons = new JArray();
            var monsters = inventory?.GetAvailableMonsterSouls(excludeActiveMonsters: true);
            if (monsters != null)
            {
                for (int i = 0; i < monsters.Count; i++)
                {
                    var monster = monsters[i];
                    availableSummons.Add(new JObject
                    {
                        ["index"] = i,
                        ["name"] = monster?.Name ?? "Unknown",
                        ["id"] = monster?.ID ?? 0
                    });
                }
            }

            var artifacts = new JArray();
            var consumables = inventory?.GetAllConsumables();
            if (consumables != null)
            {
                for (int i = 0; i < consumables.Count; i++)
                {
                    var c = consumables[i];
                    if (c.Charges <= 0) continue;

                    artifacts.Add(new JObject
                    {
                        ["index"] = i,
                        ["name"] = c.Consumable?.Name ?? c.Skill?.Name ?? "Unknown",
                        ["currentCharges"] = c.Charges,
                        ["maxCharges"] = c.GetMaxCharges()
                    });
                }
            }

            return new JObject
            {
                ["monsterSoulCount"] = inventory?.MonsterSouls ?? 0,
                ["availableSummons"] = availableSummons,
                ["artifacts"] = artifacts
            };
        }
    }
}
