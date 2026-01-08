using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace AethermancerHarness
{
    public class CombatStateSnapshot
    {
        public int Round { get; set; }
        public Dictionary<EElement, int> PlayerAether { get; set; }
        public Dictionary<EElement, int> EnemyAether { get; set; }
        public Dictionary<int, int> AllyHp { get; set; }
        public Dictionary<int, int> EnemyHp { get; set; }
        public Dictionary<int, Dictionary<string, int>> AllyBuffs { get; set; }
        public Dictionary<int, Dictionary<string, int>> EnemyBuffs { get; set; }

        public static CombatStateSnapshot Capture()
        {
            var cc = CombatController.Instance;
            if (cc == null) return null;

            var snapshot = new CombatStateSnapshot
            {
                Round = cc.Timeline?.CurrentRound ?? 0,
                PlayerAether = CaptureAether(cc.PlayerAether?.Aether),
                EnemyAether = CaptureAether(cc.EnemyAether?.Aether),
                AllyHp = new Dictionary<int, int>(),
                EnemyHp = new Dictionary<int, int>(),
                AllyBuffs = new Dictionary<int, Dictionary<string, int>>(),
                EnemyBuffs = new Dictionary<int, Dictionary<string, int>>()
            };

            for (int i = 0; i < cc.PlayerMonsters.Count; i++)
            {
                snapshot.AllyHp[i] = cc.PlayerMonsters[i].CurrentHealth;
                snapshot.AllyBuffs[i] = CaptureBuffs(cc.PlayerMonsters[i]);
            }

            for (int i = 0; i < cc.Enemies.Count; i++)
            {
                snapshot.EnemyHp[i] = cc.Enemies[i].CurrentHealth;
                snapshot.EnemyBuffs[i] = CaptureBuffs(cc.Enemies[i]);
            }

            return snapshot;
        }

        private static Dictionary<EElement, int> CaptureAether(Aether a)
        {
            if (a == null) return new Dictionary<EElement, int>();
            return new Dictionary<EElement, int>
            {
                { EElement.Fire, a.Fire },
                { EElement.Water, a.Water },
                { EElement.Earth, a.Earth },
                { EElement.Wind, a.Wind },
                { EElement.Neutral, a.Neutral },
                { EElement.Wild, a.Wild }
            };
        }

        private static Dictionary<string, int> CaptureBuffs(Monster m)
        {
            var result = new Dictionary<string, int>();
            if (m.BuffManager?.Buffs == null) return result;

            foreach (var buff in m.BuffManager.Buffs)
            {
                var name = buff.Buff?.Name;
                if (!string.IsNullOrEmpty(name))
                    result[name] = buff.Stacks;
            }
            return result;
        }
    }

    public static class StateSerializer
    {
        private static string StripMarkup(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var result = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]*>", "");
            return result.Replace("\n", " ").Replace("[", "").Replace("]", "").Trim();
        }

        public static string ToJson()
        {
            // Check for equipment selection state first (after picking equipment from dialogue/loot)
            if (IsInEquipmentSelection())
                return GetEquipmentSelectionStateJson();

            // Check for dialogue state
            if (IsInDialogue())
                return GetDialogueStateJson();

            // Check for merchant menu
            if (IsInMerchantMenu())
                return GetMerchantStateJson();

            // Check for skill selection state (post-combat level up)
            if (IsInSkillSelection())
                return GetSkillSelectionStateJson();

            // Check for monster selection state (shrine/starter selection)
            if (IsInMonsterSelection())
                return GetMonsterSelectionStateJson();

            // Check for difficulty selection state (before run start)
            if (IsInDifficultySelection())
                return GetDifficultySelectionStateJson();

            // Check for end of run menu (victory/defeat screen)
            if (IsInEndOfRunMenu())
                return GetEndOfRunStateJson();

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

        public static bool IsInDialogue()
        {
            return UIController.Instance?.DialogueDisplay?.IsOpen ?? false;
        }

        public static bool IsInEquipmentSelection()
        {
            var menu = UIController.Instance?.MonsterSelectMenu;
            if (menu == null || !menu.IsOpen) return false;
            return menu.SelectType == MonsterSelectMenu.ESelectType.EquipmentSelect;
        }

        public static bool IsInMonsterSelection()
        {
            var menu = UIController.Instance?.MonsterShrineMenu;
            return menu != null && menu.IsOpen;
        }

        public static bool IsInDifficultySelection()
        {
            return UIController.Instance?.DifficultySelectMenu?.IsOpen ?? false;
        }

        // Cached reflection for EndOfRunMenu access
        private static System.Reflection.FieldInfo _endOfRunMenuField;

        private static EndOfRunMenu GetEndOfRunMenu()
        {
            var ui = UIController.Instance;
            if (ui == null) return null;

            if (_endOfRunMenuField == null)
            {
                _endOfRunMenuField = typeof(UIController).GetField("EndOfRunMenu",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            }
            return _endOfRunMenuField?.GetValue(ui) as EndOfRunMenu;
        }

        public static bool IsInEndOfRunMenu()
        {
            return GetEndOfRunMenu()?.IsOpen ?? false;
        }

        public static string GetDifficultySelectionStateJson()
        {
            var menu = UIController.Instance?.DifficultySelectMenu;
            if (menu == null || !menu.IsOpen)
            {
                return new JObject
                {
                    ["phase"] = "DIFFICULTY_SELECTION",
                    ["error"] = "Difficulty selection menu not available"
                }.ToString(Newtonsoft.Json.Formatting.None);
            }

            var maxUnlocked = ProgressManager.Instance?.UnlockedDifficulty ?? 1;
            var currentDifficulty = menu.CurrentDifficulty;

            var choices = new JArray();
            // EDifficulty: Undefined=0, Normal=1, Heroic=2, Mythic=3
            choices.Add(new JObject
            {
                ["index"] = 0,
                ["name"] = "Normal",
                ["difficulty"] = "Normal",
                ["unlocked"] = maxUnlocked >= 1,
                ["selected"] = currentDifficulty == EDifficulty.Normal
            });
            choices.Add(new JObject
            {
                ["index"] = 1,
                ["name"] = "Heroic",
                ["difficulty"] = "Heroic",
                ["unlocked"] = maxUnlocked >= 2,
                ["selected"] = currentDifficulty == EDifficulty.Heroic
            });
            choices.Add(new JObject
            {
                ["index"] = 2,
                ["name"] = "Mythic",
                ["difficulty"] = "Mythic",
                ["unlocked"] = maxUnlocked >= 3,
                ["selected"] = currentDifficulty == EDifficulty.Mythic
            });

            var result = new JObject
            {
                ["phase"] = "DIFFICULTY_SELECTION",
                ["currentDifficulty"] = currentDifficulty.ToString(),
                ["maxUnlockedDifficulty"] = maxUnlocked,
                ["choices"] = choices,
                ["gold"] = InventoryManager.Instance?.Gold ?? 0,
                ["party"] = BuildDetailedPartyArray()
            };

            return result.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static string GetEndOfRunStateJson()
        {
            var menu = GetEndOfRunMenu();
            if (menu == null || !menu.IsOpen)
            {
                return new JObject
                {
                    ["phase"] = "END_OF_RUN",
                    ["error"] = "End of run menu not available"
                }.ToString(Newtonsoft.Json.Formatting.None);
            }

            // Determine victory/defeat from the active banner
            bool isVictory = menu.VictoryBanner?.activeSelf ?? false;
            bool isDefeat = menu.DefeatBanner?.activeSelf ?? false;

            string resultText = "UNKNOWN";
            if (isVictory) resultText = "VICTORY";
            else if (isDefeat) resultText = "DEFEAT";

            var result = new JObject
            {
                ["phase"] = "END_OF_RUN",
                ["result"] = resultText,
                ["canContinue"] = true,
                ["gold"] = InventoryManager.Instance?.Gold ?? 0
            };

            return result.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static string GetMonsterSelectionStateJson()
        {
            var menu = UIController.Instance?.MonsterShrineMenu;
            if (menu == null || !menu.IsOpen)
            {
                return new JObject
                {
                    ["phase"] = "MONSTER_SELECTION",
                    ["error"] = "Monster selection menu not available"
                }.ToString(Newtonsoft.Json.Formatting.None);
            }

            var selection = menu.MonsterSelection;
            var displayedMonsters = menu.DisplayedMonsters;

            if (displayedMonsters == null || displayedMonsters.Count == 0)
            {
                return new JObject
                {
                    ["phase"] = "MONSTER_SELECTION",
                    ["error"] = "No monsters available for selection"
                }.ToString(Newtonsoft.Json.Formatting.None);
            }

            // Map shrine state to string
            string shrineType;
            switch (menu.ShrineSelectionState)
            {
                case EShrineState.StarterSelection:
                    shrineType = "starter";
                    break;
                case EShrineState.RunStartSelection:
                    shrineType = "runStart";
                    break;
                case EShrineState.NormalShrineSelection:
                default:
                    shrineType = "normal";
                    break;
            }

            // Build choices array - manually insert random entry at correct position
            // The game's internal list has random inserted, but menu.DisplayedMonsters doesn't
            var choices = new JArray();
            int outputIndex = 0;
            int monsterIndex = 0;

            // Insert random entry at RandomMonsterPosition if applicable
            while (monsterIndex < displayedMonsters.Count || (selection.HasRandomMonster && outputIndex == selection.RandomMonsterPosition))
            {
                if (selection.HasRandomMonster && outputIndex == selection.RandomMonsterPosition)
                {
                    // Insert random entry
                    choices.Add(new JObject
                    {
                        ["index"] = outputIndex,
                        ["type"] = "random",
                        ["name"] = "Random Monster"
                    });
                    outputIndex++;
                }
                else if (monsterIndex < displayedMonsters.Count)
                {
                    var monster = displayedMonsters[monsterIndex];
                    var choiceObj = new JObject
                    {
                        ["index"] = outputIndex,
                        ["type"] = "monster",
                        ["name"] = monster.Name ?? "Unknown",
                        ["monsterKind"] = monster.MonsterKind.ToString()
                    };

                    // Check if shifted variant is available
                    bool hasShiftedVariant = InventoryManager.Instance?.HasShiftedMementosOfMonster(monster) ?? false;
                    choiceObj["hasShiftedVariant"] = hasShiftedVariant;

                    // Add full monster details
                    choiceObj["details"] = BuildShrineMonsterDetails(monster);

                    choices.Add(choiceObj);
                    outputIndex++;
                    monsterIndex++;
                }
                else
                {
                    break;
                }
            }

            var result = new JObject
            {
                ["phase"] = "MONSTER_SELECTION",
                ["shrineType"] = shrineType,
                ["selectedIndex"] = selection.GetSelectedMonsterIndex(),
                ["currentShift"] = selection.CurrentShift.ToString(),
                ["choices"] = choices,
                ["gold"] = InventoryManager.Instance?.Gold ?? 0,
                ["party"] = BuildDetailedPartyArray()
            };

            return result.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static string GetEquipmentSelectionStateJson()
        {
            var menu = UIController.Instance?.MonsterSelectMenu;
            if (menu == null || !menu.IsOpen)
            {
                return new JObject
                {
                    ["phase"] = "EQUIPMENT_SELECTION",
                    ["error"] = "Equipment selection menu not available"
                }.ToString(Newtonsoft.Json.Formatting.None);
            }

            var heldEquipment = menu.NewEquipmentInstance;
            if (heldEquipment == null)
            {
                return new JObject
                {
                    ["phase"] = "EQUIPMENT_SELECTION",
                    ["error"] = "No equipment being held"
                }.ToString(Newtonsoft.Json.Formatting.None);
            }

            var scrapValue = heldEquipment.Equipment?.GetScrapGoldGain() ?? 0;

            // Build choices array: monsters + scrap option
            var choices = new JArray();
            var party = MonsterManager.Instance?.Active;
            if (party != null)
            {
                for (int i = 0; i < party.Count; i++)
                {
                    var monster = party[i];
                    var currentEquip = monster.Equipment?.Equipment;

                    var choiceObj = new JObject
                    {
                        ["index"] = i,
                        ["type"] = "monster",
                        ["name"] = monster.Name,
                        ["level"] = monster.Level
                    };

                    if (currentEquip != null)
                    {
                        choiceObj["currentEquipment"] = BuildEquipmentDetails(currentEquip);
                        choiceObj["willTrade"] = true;
                    }
                    else
                    {
                        choiceObj["currentEquipment"] = null;
                        choiceObj["willTrade"] = false;
                    }

                    choices.Add(choiceObj);
                }
            }

            // Add scrap option as last choice
            choices.Add(new JObject
            {
                ["index"] = party?.Count ?? 0,
                ["type"] = "scrap",
                ["goldValue"] = scrapValue
            });

            var result = new JObject
            {
                ["phase"] = "EQUIPMENT_SELECTION",
                ["heldEquipment"] = BuildEquipmentDetails(heldEquipment),
                ["scrapValue"] = scrapValue,
                ["choices"] = choices,
                ["gold"] = InventoryManager.Instance?.Gold ?? 0,
                ["party"] = BuildDetailedPartyArray()
            };

            return result.ToString(Newtonsoft.Json.Formatting.None);
        }

        // Cached reflection for merchant menu access
        private static System.Reflection.FieldInfo _merchantMenuField;

        private static MerchantMenu GetMerchantMenu()
        {
            var ui = UIController.Instance;
            if (ui == null) return null;

            if (_merchantMenuField == null)
            {
                _merchantMenuField = typeof(UIController).GetField("MerchantMenu",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            }
            return _merchantMenuField?.GetValue(ui) as MerchantMenu;
        }

        public static bool IsInMerchantMenu()
        {
            return GetMerchantMenu()?.IsOpen ?? false;
        }

        public static string GetMerchantStateJson()
        {
            var merchant = MerchantInteractable.currentMerchant;
            if (merchant == null)
            {
                return new JObject
                {
                    ["phase"] = "MERCHANT",
                    ["error"] = "No active merchant"
                }.ToString(Newtonsoft.Json.Formatting.None);
            }

            var items = new JArray();
            var stockedItems = merchant.StockedItems;

            for (int i = 0; i < stockedItems.Count; i++)
            {
                var item = stockedItems[i];
                var itemObj = new JObject
                {
                    ["index"] = i,
                    ["name"] = item.GetName(),
                    ["type"] = item.ItemType.ToString().ToLower(),
                    ["rarity"] = item.ItemRarity.ToString(),
                    ["price"] = item.Price,
                    ["originalPrice"] = item.OriginalPrice,
                    ["isDiscounted"] = item.IsDiscounted,
                    ["quantity"] = item.AmountInStock,
                    ["canAfford"] = merchant.CanBuyItem(item, 1),
                    ["description"] = StripMarkup(item.GetDescription())
                };
                items.Add(itemObj);
            }

            var result = new JObject
            {
                ["phase"] = "MERCHANT",
                ["gold"] = InventoryManager.Instance?.Gold ?? 0,
                ["items"] = items,
                ["party"] = BuildDetailedPartyArray(),
                ["artifacts"] = BuildArtifactsArray()
            };

            return result.ToString(Newtonsoft.Json.Formatting.None);
        }

        // Cached reflection fields for dialogue access
        private static System.Reflection.FieldInfo _currentDialogueField;
        private static System.Reflection.FieldInfo _currentDialogueDataField;

        private static DialogueInteractable GetCurrentDialogue()
        {
            var display = UIController.Instance?.DialogueDisplay;
            if (display == null) return null;

            if (_currentDialogueField == null)
            {
                _currentDialogueField = typeof(DialogueDisplay).GetField("currentDialogue",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            }
            return _currentDialogueField?.GetValue(display) as DialogueInteractable;
        }

        private static DialogueDisplayData GetCurrentDialogueData()
        {
            var display = UIController.Instance?.DialogueDisplay;
            if (display == null) return null;

            if (_currentDialogueDataField == null)
            {
                _currentDialogueDataField = typeof(DialogueDisplay).GetField("currentDialogueData",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            }
            return _currentDialogueDataField?.GetValue(display) as DialogueDisplayData;
        }

        public static string GetDialogueStateJson()
        {
            var dialogueInteractable = GetCurrentDialogue();
            var dialogueData = GetCurrentDialogueData();

            if (dialogueInteractable == null || dialogueData == null)
            {
                return new JObject
                {
                    ["phase"] = "DIALOGUE",
                    ["error"] = "Dialogue data not available"
                }.ToString(Newtonsoft.Json.Formatting.None);
            }

            // Get NPC name
            var npcName = dialogueInteractable.DialogueCharacter?.CharacterName ?? "Unknown";

            // Build choices array
            var choices = new JArray();
            var currentOptions = DialogueEventManager.CurrentInteraction?.CurrentOptions;

            for (int i = 0; i < dialogueData.DialogueOptions.Length; i++)
            {
                var choiceObj = new JObject
                {
                    ["index"] = i,
                    ["text"] = dialogueData.DialogueOptions[i]
                };

                // Try to determine choice type from CurrentOptions and add details
                if (currentOptions != null && i < currentOptions.Count)
                {
                    var option = currentOptions[i];
                    choiceObj["type"] = GetChoiceType(option);

                    // Add equipment details if this is an equipment choice
                    if (option is EquipmentInstance equipmentInstance)
                    {
                        choiceObj["equipment"] = BuildEquipmentDetails(equipmentInstance);
                    }
                }

                choices.Add(choiceObj);
            }

            // Check if go back is possible
            var canGoBack = dialogueInteractable.IsGoBackPossible(out _);

            var result = new JObject
            {
                ["phase"] = "DIALOGUE",
                ["npc"] = npcName,
                ["dialogueText"] = dialogueData.DialogueText,
                ["isChoiceEvent"] = dialogueData.IsChoiceEvent,
                ["choices"] = choices,
                ["canGoBack"] = canGoBack,
                ["gold"] = InventoryManager.Instance?.Gold ?? 0,
                ["artifacts"] = BuildArtifactsArray(),
                ["party"] = BuildDetailedPartyArray()
            };

            return result.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string GetChoiceType(object option)
        {
            if (option == null) return "unknown";

            var typeName = option.GetType().Name;

            if (option is ConsumableInstance ci)
            {
                if (ci.Consumable?.ConsumableType == EConsumableType.Artifact)
                    return "artifact";
                return "consumable";
            }
            if (option is Monster) return "monster";
            if (option is SkillInstance) return "skill";
            if (option is PassiveInstance) return "trait";
            if (option is PerkInstance) return "perk";
            if (option is EquipmentInstance) return "equipment";
            if (option is EquipmentManager) return "equipment";

            return typeName.ToLower();
        }

        /// <summary>
        /// Determines the action category based on game state.
        /// Attack: deals damage to enemies
        /// Support: non-damaging free action
        /// Dedicated Support: non-damaging non-free action
        /// </summary>
        private static string GetActionCategory(SkillInstance skill)
        {
            if (skill?.Action == null) return "unknown";

            var action = skill.Action;

            // Check if it's a damaging action
            bool isDamaging = action.IsActionSubType(EActionSubType.DamagingAction);

            if (isDamaging)
                return "attack";

            // Non-damaging action - check if free or dedicated
            bool isFreeAction = skill.IsFreeAction();

            return isFreeAction ? "support" : "dedicated_support";
        }

        /// <summary>
        /// Gets detailed sub-types for an action (healing, shielding, buff, debuff, summon).
        /// </summary>
        private static JArray GetActionSubTypes(SkillInstance skill)
        {
            var subTypes = new JArray();
            if (skill?.Action == null) return subTypes;

            var action = skill.Action;

            if (action.IsActionSubType(EActionSubType.HealingAction))
                subTypes.Add("healing");
            if (action.IsActionSubType(EActionSubType.ShieldingAction))
                subTypes.Add("shielding");
            if (action.IsActionSubType(EActionSubType.BuffAction))
                subTypes.Add("buff");
            if (action.IsActionSubType(EActionSubType.DebuffAction))
                subTypes.Add("debuff");
            if (action.IsActionSubType(EActionSubType.SummonAction))
                subTypes.Add("summon");
            if (action.IsActionSubType(EActionSubType.DamagingAction))
                subTypes.Add("damaging");

            return subTypes;
        }

        private static JObject BuildEquipmentDetails(EquipmentInstance equipment)
        {
            if (equipment == null) return null;

            var eq = equipment.Equipment;
            if (eq == null) return null;

            // Get the formatted name and strip HTML tags for clean API output
            var cleanName = StripMarkup(eq.GetName() ?? eq.Name ?? "Unknown");
            var cleanDescription = StripMarkup(eq.GetDescription(equipment) ?? "");

            var details = new JObject
            {
                ["name"] = cleanName,
                ["equipmentType"] = eq.EquipmentType.ToString(),
                ["rarity"] = eq.EquipmentRarity.ToString(),
                ["isAura"] = eq.Aura,
                ["baseDescription"] = cleanDescription
            };

            // Build affixes array
            var affixes = new JArray();
            if (equipment.Affixes != null)
            {
                foreach (var affix in equipment.Affixes)
                {
                    if (affix?.Perk == null) continue;

                    var affixObj = new JObject
                    {
                        ["name"] = StripMarkup(affix.Perk.GetName() ?? ""),
                        ["shortDescription"] = StripMarkup(affix.GetShortDescription() ?? ""),
                        ["description"] = StripMarkup(affix.Perk.GetDescription(affix, colorize: false) ?? ""),
                        ["isRare"] = affix.IsRare,
                        ["perkType"] = affix.Perk.PerkType.ToString()
                    };
                    affixes.Add(affixObj);
                }
            }
            details["affixes"] = affixes;

            return details;
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
                obj["description"] = StripMarkup(skill.Action.GetDescription(skill) ?? "");
                obj["cost"] = BuildAetherObject(skill.GetActionCost());
                obj["elements"] = new JArray(skill.Action.Elements.ConvertAll(e => e.ToString()));
                obj["targetType"] = skill.Action.TargetType.ToString();
                obj["isTrait"] = false;
                obj["category"] = GetActionCategory(skill);
                obj["subTypes"] = GetActionSubTypes(skill);
            }
            else
            {
                var trait = skill.Source as Trait;
                obj["description"] = StripMarkup(trait?.GetDescription(skill as PassiveInstance) ?? "");
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
                    ["name"] = StripMarkup(skill.Action?.Name ?? ""),
                    ["description"] = StripMarkup(skill.Action?.GetDescription(skill) ?? ""),
                    ["cost"] = BuildAetherObject(skill.GetActionCost()),
                    ["category"] = GetActionCategory(skill),
                    ["subTypes"] = GetActionSubTypes(skill)
                });
            }

            return new JObject
            {
                ["index"] = index,
                ["name"] = StripMarkup(m.Name),
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
            // Text format not supported - use JSON
            return "Text format not supported. Use JSON.";
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
                        ["name"] = StripMarkup(skill.Action?.Name ?? ""),
                        ["cost"] = BuildAetherObject(skill.GetActionCost()),
                        ["targets"] = BuildValidTargets(current, skill),
                        ["category"] = GetActionCategory(skill),
                        ["subTypes"] = GetActionSubTypes(skill)
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
                            targets.Add(new JObject { ["index"] = i, ["name"] = StripMarkup(e.Name) });
                    }
                    break;

                case ETargetType.SingleAlly:
                case ETargetType.AllAllies:
                    for (int i = 0; i < cc.PlayerMonsters.Count; i++)
                    {
                        var m = cc.PlayerMonsters[i];
                        if (!m.State.IsDead)
                            targets.Add(new JObject { ["index"] = i, ["name"] = StripMarkup(m.Name) });
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

        public static string BuildCondensedCombatStateJson(CombatStateSnapshot before)
        {
            var cc = CombatController.Instance;
            var current = cc.CurrentMonster;
            var currentIdx = current != null ? (current.BelongsToPlayer ? cc.PlayerMonsters.IndexOf(current) : -1) : -1;

            var stateManager = CombatStateManager.Instance;
            string combatResult = null;
            if (stateManager?.State?.CurrentState?.ID == CombatStateManager.EState.CombatFinished)
                combatResult = stateManager.WonEncounter ? "VICTORY" : "DEFEAT";

            var result = new JObject
            {
                ["phase"] = "COMBAT",
                ["playerAether"] = BuildAetherObject(cc.PlayerAether?.Aether),
                ["currentActorIndex"] = currentIdx,
                ["isPlayerTurn"] = current?.BelongsToPlayer ?? false,
                ["readyForInput"] = ActionHandler.IsReadyForInput(),
                ["combatResult"] = combatResult
            };

            if (before != null && AetherChanged(before.EnemyAether, cc.EnemyAether?.Aether))
                result["enemyAether"] = BuildAetherObject(cc.EnemyAether?.Aether);

            var hpChanges = new JObject
            {
                ["allies"] = BuildHpChangesArray(before?.AllyHp, cc.PlayerMonsters, true),
                ["enemies"] = BuildHpChangesArray(before?.EnemyHp, cc.Enemies, false)
            };
            result["hpChanges"] = hpChanges;

            var buffChanges = new JObject
            {
                ["allies"] = BuildBuffChangesArray(before?.AllyBuffs, cc.PlayerMonsters),
                ["enemies"] = BuildBuffChangesArray(before?.EnemyBuffs, cc.Enemies)
            };
            result["buffChanges"] = buffChanges;

            result["monstersCanAct"] = BuildMonstersCanActArray(cc);

            return result.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static bool AetherChanged(Dictionary<EElement, int> before, Aether after)
        {
            if (before == null || after == null) return true;
            return before[EElement.Fire] != after.Fire ||
                   before[EElement.Water] != after.Water ||
                   before[EElement.Earth] != after.Earth ||
                   before[EElement.Wind] != after.Wind ||
                   before[EElement.Neutral] != after.Neutral ||
                   before[EElement.Wild] != after.Wild;
        }

        private static JArray BuildHpChangesArray(Dictionary<int, int> before, List<Monster> monsters, bool isAlly)
        {
            var arr = new JArray();
            if (monsters == null) return arr;

            for (int i = 0; i < monsters.Count; i++)
            {
                var m = monsters[i];
                int currentHp = m.CurrentHealth;
                int beforeHp = before != null && before.ContainsKey(i) ? before[i] : currentHp;

                if (currentHp != beforeHp)
                {
                    var obj = new JObject
                    {
                        ["index"] = i,
                        ["name"] = StripMarkup(m.Name),
                        ["hp"] = currentHp
                    };
                    if (!isAlly)
                        obj["isDead"] = m.State?.IsDead ?? false;
                    arr.Add(obj);
                }
            }
            return arr;
        }

        private static JArray BuildBuffChangesArray(Dictionary<int, Dictionary<string, int>> before, List<Monster> monsters)
        {
            var arr = new JArray();
            if (monsters == null) return arr;

            for (int i = 0; i < monsters.Count; i++)
            {
                var m = monsters[i];
                var beforeBuffs = before != null && before.ContainsKey(i) ? before[i] : new Dictionary<string, int>();
                var currentBuffs = new Dictionary<string, (int stacks, string type)>();

                if (m.BuffManager?.Buffs != null)
                {
                    foreach (var buff in m.BuffManager.Buffs)
                    {
                        var name = buff.Buff?.Name;
                        if (!string.IsNullOrEmpty(name))
                        {
                            var type = buff.Buff.BuffType == EBuffType.Buff ? "buff" : "debuff";
                            currentBuffs[name] = (buff.Stacks, type);
                        }
                    }
                }

                var added = new JArray();
                var removed = new JArray();

                foreach (var kvp in currentBuffs)
                {
                    if (!beforeBuffs.ContainsKey(kvp.Key))
                    {
                        added.Add(new JObject
                        {
                            ["name"] = kvp.Key,
                            ["stacks"] = kvp.Value.stacks,
                            ["type"] = kvp.Value.type
                        });
                    }
                    else if (beforeBuffs[kvp.Key] != kvp.Value.stacks)
                    {
                        added.Add(new JObject
                        {
                            ["name"] = kvp.Key,
                            ["stacks"] = kvp.Value.stacks,
                            ["type"] = kvp.Value.type,
                            ["previousStacks"] = beforeBuffs[kvp.Key]
                        });
                    }
                }

                foreach (var kvp in beforeBuffs)
                {
                    if (!currentBuffs.ContainsKey(kvp.Key))
                    {
                        removed.Add(new JObject
                        {
                            ["name"] = kvp.Key
                        });
                    }
                }

                if (added.Count > 0 || removed.Count > 0)
                {
                    arr.Add(new JObject
                    {
                        ["index"] = i,
                        ["name"] = StripMarkup(m.Name),
                        ["added"] = added,
                        ["removed"] = removed
                    });
                }
            }
            return arr;
        }

        private static JArray BuildMonstersCanActArray(CombatController cc)
        {
            var arr = new JArray();
            for (int i = 0; i < cc.PlayerMonsters.Count; i++)
            {
                var m = cc.PlayerMonsters[i];
                bool canAct = !m.State.IsDead && !(m.Turn?.WasStaggered ?? false);
                arr.Add(new JObject
                {
                    ["index"] = i,
                    ["name"] = StripMarkup(m.Name),
                    ["canAct"] = canAct
                });
            }
            return arr;
        }

        private static JObject BuildCombatMonster(Monster m, int index, bool isPlayer)
        {
            var obj = new JObject
            {
                ["index"] = index,
                ["name"] = StripMarkup(m.Name),
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
                        ["name"] = StripMarkup(skill.Action?.Name ?? ""),
                        ["description"] = StripMarkup(skill.Action?.GetDescription(skill) ?? ""),
                        ["cost"] = BuildAetherObject(skill.GetActionCost()),
                        ["canUse"] = skill.Action?.CanUseAction(skill) ?? false,
                        ["category"] = GetActionCategory(skill),
                        ["subTypes"] = GetActionSubTypes(skill)
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
                    var targetName = StripMarkup((action.Target as Monster)?.Name ?? "unknown");
                    obj["intendedAction"] = new JObject
                    {
                        ["skill"] = StripMarkup(action.Action?.Action?.Name ?? ""),
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
                    arr.Add(new JObject { ["name"] = StripMarkup(buff.Buff?.Name ?? ""), ["stacks"] = buff.Stacks });
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
                    ["name"] = StripMarkup(trait.Trait.Name ?? ""),
                    ["description"] = StripMarkup(trait.Trait.GetDescription(trait) ?? ""),
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

        private static string GetExplorationStateJson()
        {
            var playerPos = PlayerMovementController.Instance?.transform?.position ?? UnityEngine.Vector3.zero;
            var currentArea = ExplorationController.Instance?.CurrentArea ?? EArea.PilgrimsRest;
            var area = currentArea.ToString();
            var zone = ExplorationController.Instance?.CurrentZone ?? 0;

            // Check if we're in Pilgrim's Rest and can start a run
            bool canStartRun = currentArea == EArea.PilgrimsRest && FindStartRunInteractable() != null;

            var result = new JObject
            {
                ["phase"] = "EXPLORATION",
                ["player"] = new JObject { ["x"] = (double)playerPos.x, ["y"] = (double)playerPos.y, ["z"] = (double)playerPos.z },
                ["area"] = area,
                ["zone"] = zone,
                ["canStartRun"] = canStartRun,
                ["gold"] = InventoryManager.Instance?.Gold ?? 0,
                ["party"] = BuildDetailedPartyArray(),
                ["artifacts"] = BuildArtifactsArray(),
                ["inventory"] = BuildInventoryObject(),
                ["monsterGroups"] = BuildMonsterGroupsArray(),
                ["interactables"] = BuildInteractablesArray()
            };

            return result.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static NextAreaInteractable FindStartRunInteractable()
        {
            // Find NextAreaInteractable with StartRun headliner
            var interactables = UnityEngine.Object.FindObjectsByType<NextAreaInteractable>(UnityEngine.FindObjectsSortMode.None);
            foreach (var interactable in interactables)
            {
                if (interactable.HeadlinerText == NextAreaInteractable.Headliner.StartRun)
                    return interactable;
            }
            return null;
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
                    ["x"] = (double)pos.x,
                    ["y"] = (double)pos.y,
                    ["z"] = (double)pos.z,
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
                    ["x"] = (double)pos.x, ["y"] = (double)pos.y, ["z"] = (double)pos.z,
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
                    ["x"] = (double)pos.x, ["y"] = (double)pos.y, ["z"] = (double)pos.z,
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
                    ["x"] = (double)pos.x, ["y"] = (double)pos.y, ["z"] = (double)pos.z,
                    ["opened"] = chest.WasUsedUp
                });
            }

            if (map.MerchantInteractable != null)
            {
                var pos = map.MerchantInteractable.transform.position;
                arr.Add(new JObject
                {
                    ["type"] = "MERCHANT",
                    ["x"] = (double)pos.x, ["y"] = (double)pos.y, ["z"] = (double)pos.z
                });
            }

            if (map.MonsterShrine != null)
            {
                var pos = map.MonsterShrine.transform.position;
                arr.Add(new JObject
                {
                    ["type"] = "MONSTER_SHRINE",
                    ["x"] = (double)pos.x, ["y"] = (double)pos.y, ["z"] = (double)pos.z,
                    ["used"] = map.MonsterShrine.WasUsedUp
                });
            }

            int npcIndex = 0;
            foreach (var interactable in map.DialogueInteractables)
            {
                if (interactable == null) continue;
                var npc = interactable as DialogueInteractable;
                if (npc == null) continue;
                var pos = npc.transform.position;
                var npcName = npc.DialogueCharacter?.CharacterName ?? "Unknown";
                var hasEvent = npc.DialogueCharacter?.EventOptions?.Count > 0;
                arr.Add(new JObject
                {
                    ["type"] = "NPC",
                    ["index"] = npcIndex++,
                    ["name"] = npcName,
                    ["x"] = (double)pos.x, ["y"] = (double)pos.y, ["z"] = (double)pos.z,
                    ["talked"] = npc.WasUsedUp,
                    ["hasEvent"] = hasEvent
                });
            }

            foreach (var evt in map.SmallEventInteractables)
            {
                if (evt == null) continue;
                var pos = evt.transform.position;
                arr.Add(new JObject
                {
                    ["type"] = "EVENT",
                    ["x"] = (double)pos.x, ["y"] = (double)pos.y, ["z"] = (double)pos.z,
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
                    ["x"] = (double)pos.x, ["y"] = (double)pos.y, ["z"] = (double)pos.z,
                    ["found"] = secret.WasUsedUp
                });
            }

            // Add start run interactable if in Pilgrim's Rest
            var currentArea = ExplorationController.Instance?.CurrentArea ?? EArea.PilgrimsRest;
            if (currentArea == EArea.PilgrimsRest)
            {
                var startRun = FindStartRunInteractable();
                if (startRun != null)
                {
                    var pos = startRun.transform.position;
                    arr.Add(new JObject
                    {
                        ["type"] = "START_RUN",
                        ["x"] = (double)pos.x, ["y"] = (double)pos.y, ["z"] = (double)pos.z
                    });
                }
            }

            return arr;
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
                    ["name"] = StripMarkup(c.Consumable?.Name ?? c.Skill?.Name ?? "Unknown"),
                    ["description"] = StripMarkup(c.Action?.GetDescription(c) ?? ""),
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
                    ["name"] = StripMarkup(skill.Action?.Name ?? ""),
                    ["description"] = StripMarkup(skill.Action?.GetDescription(skill) ?? ""),
                    ["cost"] = BuildAetherObject(skill.GetActionCost()),
                    ["targetType"] = skill.Action?.TargetType.ToString() ?? "Unknown",
                    ["elements"] = elements,
                    ["category"] = GetActionCategory(skill),
                    ["subTypes"] = GetActionSubTypes(skill)
                });
                actionIdx++;
            }

            return new JObject
            {
                ["index"] = index,
                ["name"] = StripMarkup(m.Name),
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

        private static JObject BuildShrineMonsterDetails(Monster m)
        {
            // Build actions array with full details
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
                    ["name"] = StripMarkup(skill.Action?.Name ?? ""),
                    ["description"] = StripMarkup(skill.Action?.GetDescription(skill) ?? ""),
                    ["cost"] = BuildAetherObject(skill.GetActionCost()),
                    ["targetType"] = skill.Action?.TargetType.ToString() ?? "Unknown",
                    ["elements"] = elements,
                    ["category"] = GetActionCategory(skill),
                    ["subTypes"] = GetActionSubTypes(skill)
                });
                actionIdx++;
            }

            // Get signature trait info
            var signatureTrait = m.SkillManager?.SignatureTraitInstance;
            JObject signatureTraitObj = null;
            if (signatureTrait?.Trait != null)
            {
                signatureTraitObj = new JObject
                {
                    ["name"] = StripMarkup(signatureTrait.Trait.Name ?? ""),
                    ["description"] = StripMarkup(signatureTrait.Trait.GetDescription(signatureTrait) ?? ""),
                    ["isAura"] = signatureTrait.Trait.IsAura()
                };
            }

            return new JObject
            {
                ["name"] = StripMarkup(m.Name),
                ["monsterKind"] = m.MonsterKind.ToString(),
                ["level"] = m.Level,
                ["maxHp"] = m.Stats?.MaxHealth?.ValueInt ?? 0,
                ["actions"] = actions,
                ["traits"] = BuildTraitsArray(m),
                ["signatureTrait"] = signatureTraitObj
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
                    ["name"] = StripMarkup(c.Consumable?.Name ?? c.Skill?.Name ?? "Unknown"),
                    ["description"] = StripMarkup(c.Action?.GetDescription(c) ?? ""),
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
                        ["name"] = StripMarkup(c.Consumable?.Name ?? c.Skill?.Name ?? "Unknown"),
                        ["currentCharges"] = c.Charges,
                        ["maxCharges"] = c.GetMaxCharges()
                    });
                }
            }

            return new JObject
            {
                ["monsterSoulCount"] = inventory?.MonsterSouls ?? 0,
                ["artifacts"] = artifacts
            };
        }
    }
}
