using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

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
        public static string StripMarkup(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var result = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]*>", "");
            return result.Replace("\n", " ").Replace("[", "").Replace("]", "").Trim();
        }

        /// <summary>
        /// Gets a display name for an object, adding a numeric suffix if there are duplicates.
        /// Uses object identity (RuntimeHelpers.GetHashCode) to ensure stable ordering.
        /// </summary>
        public static string GetDisplayName<T>(T obj, IReadOnlyList<T> context, Func<T, string> getBaseName) where T : class
        {
            if (obj == null) return "Unknown";

            var baseName = getBaseName(obj);
            var sameNameObjects = context
                .Where(o => o != null && getBaseName(o) == baseName)
                .OrderBy(o => RuntimeHelpers.GetHashCode(o))
                .ToList();

            if (sameNameObjects.Count == 1)
                return baseName;

            int position = sameNameObjects.IndexOf(obj) + 1;
            return $"{baseName} {position}";
        }

        /// <summary>
        /// Finds an object by its display name within a context list.
        /// </summary>
        public static T FindByDisplayName<T>(string displayName, IReadOnlyList<T> context, Func<T, string> getBaseName) where T : class
        {
            if (string.IsNullOrEmpty(displayName)) return null;

            foreach (var obj in context)
            {
                if (obj == null) continue;
                var objDisplayName = GetDisplayName(obj, context, getBaseName);
                if (objDisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase))
                    return obj;
            }
            return null;
        }

        /// <summary>
        /// Gets all display names for objects in a context list.
        /// </summary>
        public static List<string> GetAllDisplayNames<T>(IReadOnlyList<T> context, Func<T, string> getBaseName) where T : class
        {
            var result = new List<string>();
            foreach (var obj in context)
            {
                if (obj == null) continue;
                result.Add(GetDisplayName(obj, context, getBaseName));
            }
            return result;
        }

        public static string ToJson()
        {
            if (IsInEquipmentSelection())
                return GetEquipmentSelectionStateJson();

            if (IsInDialogue())
                return GetDialogueStateJson();

            if (IsInMerchantMenu())
                return GetMerchantStateJson();

            if (IsInSkillSelection())
                return GetSkillSelectionStateJson();

            if (IsInMonsterSelection())
                return GetMonsterSelectionStateJson();

            if (IsInDifficultySelection())
                return GetDifficultySelectionStateJson();

            if (IsInEndOfRunMenu())
                return GetEndOfRunStateJson();

            if (IsInAetherSpringMenu())
                return GetAetherSpringStateJson();

            if (!(GameStateManager.Instance?.IsCombat ?? false))
                return GetExplorationStateJson();

            return GetCombatStateJson();
        }

        public static bool IsInSkillSelection() =>
            UIController.Instance?.PostCombatMenu?.SkillSelectMenu?.IsOpen ?? false;

        public static bool IsInPostCombatMenu() =>
            UIController.Instance?.PostCombatMenu?.IsOpen ?? false;

        public static bool IsInDialogue() =>
            UIController.Instance?.DialogueDisplay?.IsOpen ?? false;

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

        public static bool IsInDifficultySelection() =>
            UIController.Instance?.DifficultySelectMenu?.IsOpen ?? false;

        public static bool IsInEndOfRunMenu() =>
            ActionHandler.GetEndOfRunMenu()?.IsOpen ?? false;

        public static bool IsInMerchantMenu() =>
            ActionHandler.GetMerchantMenu()?.IsOpen ?? false;

        public static bool IsInAetherSpringMenu() =>
            ActionHandler.GetAetherSpringMenu()?.IsOpen ?? false;

        public static string GetDifficultySelectionStateJson()
        {
            var menu = UIController.Instance?.DifficultySelectMenu;
            if (menu == null || !menu.IsOpen)
            {
                return JsonConfig.Serialize(new DifficultySelectionState
                {
                    Phase = GamePhase.DifficultySelection,
                    Error = "Difficulty selection menu not available"
                });
            }

            var maxUnlocked = ProgressManager.Instance?.UnlockedDifficulty ?? 1;
            var currentDifficulty = menu.CurrentDifficulty;

            var state = new DifficultySelectionState
            {
                Phase = GamePhase.DifficultySelection,
                CurrentDifficulty = currentDifficulty.ToString(),
                MaxUnlockedDifficulty = maxUnlocked,
                Choices = new List<DifficultyChoice>
                {
                    new DifficultyChoice { Name = "Normal", Difficulty = "Normal", Unlocked = maxUnlocked >= 1, Selected = currentDifficulty == EDifficulty.Normal },
                    new DifficultyChoice { Name = "Heroic", Difficulty = "Heroic", Unlocked = maxUnlocked >= 2, Selected = currentDifficulty == EDifficulty.Heroic },
                    new DifficultyChoice { Name = "Mythic", Difficulty = "Mythic", Unlocked = maxUnlocked >= 3, Selected = currentDifficulty == EDifficulty.Mythic }
                },
                Gold = InventoryManager.Instance?.Gold ?? 0,
                Party = BuildDetailedPartyList()
            };

            return JsonConfig.Serialize(state);
        }

        public static string GetEndOfRunStateJson()
        {
            var menu = ActionHandler.GetEndOfRunMenu();
            if (menu == null || !menu.IsOpen)
            {
                return JsonConfig.Serialize(new EndOfRunState
                {
                    Phase = GamePhase.EndOfRun,
                    Error = "End of run menu not available"
                });
            }

            bool isVictory = menu.VictoryBanner?.activeSelf ?? false;
            bool isDefeat = menu.DefeatBanner?.activeSelf ?? false;

            var result = CombatResult.Unknown;
            if (isVictory) result = CombatResult.Victory;
            else if (isDefeat) result = CombatResult.Defeat;

            return JsonConfig.Serialize(new EndOfRunState
            {
                Phase = GamePhase.EndOfRun,
                Result = result,
                CanContinue = true,
                Gold = InventoryManager.Instance?.Gold ?? 0
            });
        }

        public static string GetMonsterSelectionStateJson()
        {
            var menu = UIController.Instance?.MonsterShrineMenu;
            if (menu == null || !menu.IsOpen)
            {
                return JsonConfig.Serialize(new MonsterSelectionState
                {
                    Phase = GamePhase.MonsterSelection,
                    Error = "Monster selection menu not available"
                });
            }

            var selection = menu.MonsterSelection;
            var displayedMonsters = menu.DisplayedMonsters;

            if (displayedMonsters == null || displayedMonsters.Count == 0)
            {
                return JsonConfig.Serialize(new MonsterSelectionState
                {
                    Phase = GamePhase.MonsterSelection,
                    Error = "No monsters available for selection"
                });
            }

            // Build choices using identity-based naming
            Func<Monster, string> getMonsterName = m => m.Name ?? "Unknown";
            var choices = new List<MonsterChoice>();
            int outputIndex = 0;
            int monsterIndex = 0;

            while (monsterIndex < displayedMonsters.Count || (selection.HasRandomMonster && outputIndex == selection.RandomMonsterPosition))
            {
                if (selection.HasRandomMonster && outputIndex == selection.RandomMonsterPosition)
                {
                    choices.Add(new MonsterChoice
                    {
                        Type = ChoiceType.Random,
                        Name = "Random Monster"
                    });
                    outputIndex++;
                }
                else if (monsterIndex < displayedMonsters.Count)
                {
                    var monster = displayedMonsters[monsterIndex];
                    bool hasShiftedVariant = InventoryManager.Instance?.HasShiftedMementosOfMonster(monster) ?? false;

                    choices.Add(new MonsterChoice
                    {
                        Type = ChoiceType.Monster,
                        Name = GetDisplayName(monster, displayedMonsters, getMonsterName),
                        HasShiftedVariant = hasShiftedVariant,
                        Details = BuildMonsterDetails(monster, outputIndex)
                    });
                    outputIndex++;
                    monsterIndex++;
                }
                else
                {
                    break;
                }
            }

            return JsonConfig.Serialize(new MonsterSelectionState
            {
                Phase = GamePhase.MonsterSelection,
                ShrineType = menu.ShrineSelectionState.ToShrineType(),
                SelectedIndex = selection.GetSelectedMonsterIndex(),
                CurrentShift = selection.CurrentShift.ToString(),
                Choices = choices,
                Gold = InventoryManager.Instance?.Gold ?? 0,
                Party = BuildDetailedPartyList(),
                ShrineRerollsAvailable = InventoryManager.Instance?.ShrineRerolls ?? 0,
                CanReroll = (InventoryManager.Instance?.ShrineRerolls ?? 0) > 0
                            && menu.ShrineSelectionState == EShrineState.NormalShrineSelection
            });
        }

        public static string GetEquipmentSelectionStateJson()
        {
            var menu = UIController.Instance?.MonsterSelectMenu;
            if (menu == null || !menu.IsOpen)
            {
                return JsonConfig.Serialize(new EquipmentSelectionState
                {
                    Phase = GamePhase.EquipmentSelection,
                    Error = "Equipment selection menu not available"
                });
            }

            var heldEquipment = menu.NewEquipmentInstance;
            if (heldEquipment == null)
            {
                return JsonConfig.Serialize(new EquipmentSelectionState
                {
                    Phase = GamePhase.EquipmentSelection,
                    Error = "No equipment being held"
                });
            }

            var scrapValue = heldEquipment.Equipment?.GetScrapGoldGain() ?? 0;
            var choices = new List<EquipmentChoice>();
            var party = MonsterManager.Instance?.Active;

            if (party != null)
            {
                for (int i = 0; i < party.Count; i++)
                {
                    var monster = party[i];
                    var currentEquip = monster.Equipment?.Equipment;

                    choices.Add(new EquipmentChoice
                    {
                        Type = ChoiceType.Monster,
                        Name = monster.Name,
                        Level = monster.Level,
                        CurrentEquipment = currentEquip != null ? BuildEquipmentInfo(currentEquip) : null,
                        WillTrade = currentEquip != null
                    });
                }
            }

            choices.Add(new EquipmentChoice
            {
                Type = ChoiceType.Scrap,
                GoldValue = scrapValue
            });

            return JsonConfig.Serialize(new EquipmentSelectionState
            {
                Phase = GamePhase.EquipmentSelection,
                HeldEquipment = BuildEquipmentInfo(heldEquipment),
                ScrapValue = scrapValue,
                Choices = choices,
                Gold = InventoryManager.Instance?.Gold ?? 0,
                Party = BuildDetailedPartyList()
            });
        }

        public static string GetMerchantStateJson()
        {
            var merchant = MerchantInteractable.currentMerchant;
            if (merchant == null)
            {
                return JsonConfig.Serialize(new MerchantState
                {
                    Phase = GamePhase.Merchant,
                    Error = "No active merchant"
                });
            }

            var stockedItems = merchant.StockedItems;

            // Build choices using identity-based naming
            Func<ShopItem, string> getItemName = item => item.GetName();
            var choices = new List<MerchantItem>();
            for (int i = 0; i < stockedItems.Count; i++)
            {
                var item = stockedItems[i];
                choices.Add(new MerchantItem
                {
                    Name = GetDisplayName(item, stockedItems, getItemName),
                    Type = item.ItemType.ToString().ToLower(),
                    Rarity = item.ItemRarity.ToString(),
                    Price = item.Price,
                    OriginalPrice = item.OriginalPrice,
                    IsDiscounted = item.IsDiscounted,
                    Quantity = item.AmountInStock,
                    CanAfford = merchant.CanBuyItem(item, 1),
                    Description = StripMarkup(item.GetDescription())
                });
            }

            choices.Add(new MerchantItem
            {
                Type = ChoiceType.Close.ToString().ToLower(),
                Name = "Leave Shop"
            });

            return JsonConfig.Serialize(new MerchantState
            {
                Phase = GamePhase.Merchant,
                Gold = InventoryManager.Instance?.Gold ?? 0,
                Choices = choices,
                Party = BuildDetailedPartyList(),
                Artifacts = BuildArtifactsList()
            });
        }

        public static string GetDialogueStateJson()
        {
            var dialogueInteractable = ActionHandler.GetCurrentDialogue();
            var dialogueData = ActionHandler.GetCurrentDialogueData();

            if (dialogueInteractable == null || dialogueData == null)
            {
                return JsonConfig.Serialize(new DialogueState
                {
                    Phase = GamePhase.Dialogue,
                    Error = "Dialogue data not available"
                });
            }

            var npcName = dialogueInteractable.DialogueCharacter?.CharacterName ?? "Unknown";
            var choices = new List<DialogueChoice>();
            var currentOptions = DialogueEventManager.CurrentInteraction?.CurrentOptions;

            for (int i = 0; i < dialogueData.DialogueOptions.Length; i++)
            {
                var choice = new DialogueChoice
                {
                    Index = i,
                    Text = StripMarkup(dialogueData.DialogueOptions[i])
                };

                if (currentOptions != null && i < currentOptions.Count)
                {
                    var option = currentOptions[i];
                    choice.Type = GetChoiceType(option);

                    if (option is EquipmentInstance equipmentInstance)
                        choice.Equipment = BuildEquipmentInfo(equipmentInstance);
                }

                choices.Add(choice);
            }

            var canGoBack = dialogueInteractable.IsGoBackPossible(out _);

            return JsonConfig.Serialize(new DialogueState
            {
                Phase = GamePhase.Dialogue,
                Npc = npcName,
                DialogueText = StripMarkup(dialogueData.DialogueText),
                IsChoiceEvent = dialogueData.IsChoiceEvent,
                Choices = choices,
                CanGoBack = canGoBack,
                Gold = InventoryManager.Instance?.Gold ?? 0,
                Artifacts = BuildArtifactsList(),
                Party = BuildDetailedPartyList()
            });
        }

        public static string GetAetherSpringStateJson()
        {
            var spring = ActionHandler.GetCurrentAetherSpring();
            if (spring == null)
            {
                return JsonConfig.Serialize(new AetherSpringState
                {
                    Phase = GamePhase.AetherSpring,
                    Error = "Aether spring not available"
                });
            }

            var choices = new List<BoonChoice>();

            // Get both available boons (aether springs always offer 2 choices)
            for (int i = 0; i < 2; i++)
            {
                try
                {
                    var boonGO = spring.GetAvailableBoon(i);
                    if (boonGO == null) continue;

                    var boon = boonGO.GetComponent<Boon>();
                    if (boon == null) continue;

                    // Create BoonInstance for proper description formatting
                    var aethermancer = MonsterManager.Instance?.AethermancerMonster;
                    var boonInstance = new BoonInstance(boon, aethermancer);

                    // Get the formatted description and strip markup
                    var description = boon.GetDescription(boonInstance);
                    description = StripMarkup(description);

                    choices.Add(new BoonChoice
                    {
                        Name = StripMarkup(boon.GetName() ?? boon.Name ?? "Unknown Boon"),
                        Element = boon.Element.ToString(),
                        Tier = boon.Tier,
                        Description = description,
                        Effect = boon.Effect.ToString()
                    });
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogWarning($"GetAetherSpringStateJson: Failed to get boon {i}: {ex.Message}");
                }
            }

            // Get currently active boons
            var activeBoons = new List<ActiveBoonInfo>();
            var explorationController = ExplorationController.Instance;
            if (explorationController?.ActiveBoons != null)
            {
                foreach (var boon in explorationController.ActiveBoons)
                {
                    if (boon == null || boon.IsAltarTableBoon) continue;

                    var aethermancer = MonsterManager.Instance?.AethermancerMonster;
                    var boonInstance = new BoonInstance(boon, aethermancer);

                    activeBoons.Add(new ActiveBoonInfo
                    {
                        Name = StripMarkup(boon.GetName() ?? boon.Name ?? "Unknown"),
                        Element = boon.Element.ToString(),
                        Tier = boon.Tier,
                        Description = StripMarkup(boon.GetDescription(boonInstance))
                    });
                }
            }

            return JsonConfig.Serialize(new AetherSpringState
            {
                Phase = GamePhase.AetherSpring,
                Choices = choices,
                ActiveBoons = activeBoons,
                Gold = InventoryManager.Instance?.Gold ?? 0,
                Party = BuildDetailedPartyList()
            });
        }

        private static string GetChoiceType(object option)
        {
            if (option == null) return ChoiceType.Unknown.ToString();

            if (option is ConsumableInstance ci)
            {
                if (ci.Consumable?.ConsumableType == EConsumableType.Artifact)
                    return ChoiceType.Artifact.ToString();
                return ChoiceType.Consumable.ToString();
            }
            if (option is Monster) return ChoiceType.Monster.ToString();
            if (option is SkillInstance) return ChoiceType.Skill.ToString();
            if (option is PassiveInstance) return ChoiceType.Trait.ToString();
            if (option is PerkInstance) return ChoiceType.Perk.ToString();
            if (option is EquipmentInstance) return ChoiceType.Equipment.ToString();
            if (option is EquipmentManager) return ChoiceType.Equipment.ToString();

            return option.GetType().Name.ToLower();
        }

        private static string GetActionCategory(SkillInstance skill)
        {
            if (skill?.Action == null) return "unknown";

            var action = skill.Action;
            bool isDamaging = action.IsActionSubType(EActionSubType.DamagingAction);

            if (isDamaging)
                return "attack";

            bool isFreeAction = skill.IsFreeAction();
            return isFreeAction ? "support" : "dedicated_support";
        }

        private static List<string> GetActionSubTypes(SkillInstance skill)
        {
            var subTypes = new List<string>();
            if (skill?.Action == null) return subTypes;

            var action = skill.Action;

            if (action.IsActionSubType(EActionSubType.HealingAction)) subTypes.Add("healing");
            if (action.IsActionSubType(EActionSubType.ShieldingAction)) subTypes.Add("shielding");
            if (action.IsActionSubType(EActionSubType.BuffAction)) subTypes.Add("buff");
            if (action.IsActionSubType(EActionSubType.DebuffAction)) subTypes.Add("debuff");
            if (action.IsActionSubType(EActionSubType.SummonAction)) subTypes.Add("summon");
            if (action.IsActionSubType(EActionSubType.DamagingAction)) subTypes.Add("damaging");

            return subTypes;
        }

        private static EquipmentInfo BuildEquipmentInfo(EquipmentInstance equipment)
        {
            if (equipment == null) return null;

            var eq = equipment.Equipment;
            if (eq == null) return null;

            var affixes = new List<AffixInfo>();
            if (equipment.Affixes != null)
            {
                foreach (var affix in equipment.Affixes)
                {
                    if (affix?.Perk == null) continue;
                    affixes.Add(new AffixInfo
                    {
                        Name = StripMarkup(affix.Perk.GetName() ?? ""),
                        ShortDescription = StripMarkup(affix.GetShortDescription() ?? ""),
                        Description = StripMarkup(affix.Perk.GetDescription(affix, colorize: false) ?? ""),
                        IsRare = affix.IsRare,
                        PerkType = affix.Perk.PerkType.ToString()
                    });
                }
            }

            return new EquipmentInfo
            {
                Name = StripMarkup(eq.GetName() ?? eq.Name ?? "Unknown"),
                EquipmentType = eq.EquipmentType.ToString(),
                Rarity = eq.EquipmentRarity.ToString(),
                IsAura = eq.Aura,
                BaseDescription = StripMarkup(eq.GetDescription(equipment) ?? ""),
                Affixes = affixes
            };
        }

        private static EquipmentInfo BuildEquipmentInfo(Equipment eq)
        {
            if (eq == null) return null;

            return new EquipmentInfo
            {
                Name = StripMarkup(eq.GetName() ?? eq.Name ?? "Unknown"),
                EquipmentType = eq.EquipmentType.ToString(),
                Rarity = eq.EquipmentRarity.ToString(),
                IsAura = eq.Aura,
                BaseDescription = StripMarkup(eq.GetDescription(null) ?? ""),
                Affixes = new List<AffixInfo>()
            };
        }

        public static string GetSkillSelectionStateJson()
        {
            var skillSelectMenu = UIController.Instance.PostCombatMenu.SkillSelectMenu;
            var postCombatMenu = UIController.Instance.PostCombatMenu;
            var levelUpType = skillSelectMenu.LevelUpType;

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

            if (levelingMonster == null && MonsterManager.Instance.Active.Count > 0)
            {
                levelingMonster = MonsterManager.Instance.Active[0];
                monsterIndex = 0;
            }

            var choices = new List<SkillChoice>();
            for (int i = 0; i < skillSelectMenu.SkillTooltips.Count && i < 3; i++)
            {
                var tooltip = skillSelectMenu.SkillTooltips[i];
                var menuItem = tooltip.GetComponent<MenuListItem>();
                var skillInstance = menuItem?.Displayable as SkillInstance;

                if (skillInstance != null)
                    choices.Add(BuildSkillChoice(skillInstance, i, levelUpType));
                else
                    choices.Add(new SkillChoice { Error = "Could not read skill" });
            }

            int pendingLevelUps = 0;
            foreach (var info in postCombatMenu.PostCombatMonsterInfos)
            {
                if (info.monster != null)
                    pendingLevelUps += info.LevelUpUI.LevelsGainedLeft;
            }

            bool canChooseMaxHealth = levelingMonster != null &&
                levelingMonster.SkillManager.GetSkillTypeCount(levelUpType == SkillPicker.ELevelUpType.PickTrait) >= skillSelectMenu.MonsterMaxSkillCount;

            return JsonConfig.Serialize(new SkillSelectionState
            {
                Phase = GamePhase.SkillSelection,
                LevelUpType = levelUpType == SkillPicker.ELevelUpType.PickSpell ? "action" : "trait",
                Monster = levelingMonster != null ? new LevelingMonster { Name = levelingMonster.Name, Level = levelingMonster.Level } : null,
                Choices = choices,
                RerollsAvailable = InventoryManager.Instance?.SkillRerolls ?? 0,
                CanChooseMaxHealth = canChooseMaxHealth,
                PendingLevelUps = pendingLevelUps,
                Gold = InventoryManager.Instance?.Gold ?? 0,
                Artifacts = BuildArtifactsList(),
                MonsterSoulCount = InventoryManager.Instance?.MonsterSouls ?? 0,
                Party = BuildDetailedPartyList()
            });
        }

        private static SkillChoice BuildSkillChoice(SkillInstance skill, int index, SkillPicker.ELevelUpType levelUpType)
        {
            var choice = new SkillChoice
            {
                Name = skill.Skill?.Name ?? "Unknown",
                IsMaverick = skill.Skill?.MaverickSkill ?? false
            };

            if (levelUpType == SkillPicker.ELevelUpType.PickSpell && skill.Action != null)
            {
                choice.Description = StripMarkup(skill.Action.GetDescription(skill) ?? "");
                choice.Cost = BuildAetherValues(skill.GetActionCost());
                choice.Elements = skill.Action.Elements.ConvertAll(e => e.ToString());
                choice.TargetType = skill.Action.TargetType.ToString();
                choice.IsTrait = false;
                choice.Category = GetActionCategory(skill);
                choice.SubTypes = GetActionSubTypes(skill);
            }
            else
            {
                var trait = skill.Source as Trait;
                choice.Description = StripMarkup(trait?.GetDescription(skill as PassiveInstance) ?? "");
                choice.IsAura = trait?.IsAura() ?? false;
                choice.IsTrait = true;
            }

            return choice;
        }

        public static string GetValidActionsJson()
        {
            if (!(GameStateManager.Instance?.IsCombat ?? false))
                return JsonConfig.Serialize(new ValidActionsResponse { Actions = new List<ValidAction>(), Error = "Not in combat" });

            var cc = CombatController.Instance;
            var current = cc.CurrentMonster;

            if (current == null || !current.BelongsToPlayer)
                return JsonConfig.Serialize(new ValidActionsResponse { Actions = new List<ValidAction>(), WaitingFor = "enemy_turn" });

            var actions = new List<ValidAction>();
            int skillIdx = 0;
            foreach (var skill in current.SkillManager.Actions)
            {
                if (skill.Action?.CanUseAction(skill) ?? false)
                {
                    actions.Add(new ValidAction
                    {
                        Name = StripMarkup(skill.Action?.Name ?? ""),
                        Cost = BuildAetherValues(skill.GetActionCost()),
                        Targets = BuildValidTargets(current, skill),
                        Category = GetActionCategory(skill),
                        SubTypes = GetActionSubTypes(skill)
                    });
                }
                skillIdx++;
            }

            return JsonConfig.Serialize(new ValidActionsResponse { Actions = actions });
        }

        private static List<TargetInfo> BuildValidTargets(Monster caster, SkillInstance skill)
        {
            var targets = new List<TargetInfo>();
            var targetType = skill.Action?.TargetType ?? ETargetType.SingleEnemy;
            var cc = CombatController.Instance;
            Func<Monster, string> getMonsterName = m => StripMarkup(m.Name);

            switch (targetType)
            {
                case ETargetType.SingleEnemy:
                case ETargetType.AllEnemies:
                    // Filter alive enemies and build display names using identity-based naming
                    var aliveEnemies = cc.Enemies.Where(e => !e.State.IsDead).ToList();
                    foreach (var e in aliveEnemies)
                        targets.Add(new TargetInfo { Name = GetDisplayName(e, aliveEnemies, getMonsterName) });
                    break;

                case ETargetType.SingleAlly:
                case ETargetType.AllAllies:
                    // Filter alive allies and build display names using identity-based naming
                    var aliveAllies = cc.PlayerMonsters.Where(m => !m.State.IsDead).ToList();
                    foreach (var m in aliveAllies)
                        targets.Add(new TargetInfo { Name = GetDisplayName(m, aliveAllies, getMonsterName) });
                    break;

                case ETargetType.SelfOrOwner:
                    targets.Add(new TargetInfo { Name = "self" });
                    break;
            }

            return targets;
        }

        private static string GetCombatStateJson()
        {
            var cc = CombatController.Instance;
            var current = cc.CurrentMonster;
            var currentIdx = current != null ? (current.BelongsToPlayer ? cc.PlayerMonsters.IndexOf(current) : -1) : -1;
            Func<Monster, string> getMonsterName = m => StripMarkup(m.Name);

            // Build ally/enemy lists using identity-based naming
            var allies = new List<CombatMonster>();
            for (int i = 0; i < cc.PlayerMonsters.Count; i++)
                allies.Add(BuildCombatMonster(cc.PlayerMonsters[i], i, true, GetDisplayName(cc.PlayerMonsters[i], cc.PlayerMonsters, getMonsterName)));

            var enemies = new List<CombatMonster>();
            for (int i = 0; i < cc.Enemies.Count; i++)
                enemies.Add(BuildCombatMonster(cc.Enemies[i], i, false, GetDisplayName(cc.Enemies[i], cc.Enemies, getMonsterName)));

            return JsonConfig.Serialize(new CombatState
            {
                Phase = GamePhase.Combat,
                Round = cc.Timeline?.CurrentRound ?? 0,
                CurrentActorIndex = currentIdx,
                PlayerAether = BuildAetherValues(cc.PlayerAether?.Aether),
                EnemyAether = BuildAetherValues(cc.EnemyAether?.Aether),
                Allies = allies,
                Enemies = enemies,
                Consumables = BuildConsumablesList(),
                Gold = InventoryManager.Instance?.Gold ?? 0,
                Artifacts = BuildArtifactsList(),
                MonsterSoulCount = InventoryManager.Instance?.MonsterSouls ?? 0
            });
        }

        public static string BuildCondensedCombatStateJson(CombatStateSnapshot before)
        {
            var cc = CombatController.Instance;
            var current = cc.CurrentMonster;
            var currentIdx = current != null ? (current.BelongsToPlayer ? cc.PlayerMonsters.IndexOf(current) : -1) : -1;

            var state = new CondensedCombatState
            {
                Phase = GamePhase.Combat,
                PlayerAether = BuildAetherValues(cc.PlayerAether?.Aether),
                CurrentActorIndex = currentIdx
            };

            if (before != null && AetherChanged(before.EnemyAether, cc.EnemyAether?.Aether))
                state.EnemyAether = BuildAetherValues(cc.EnemyAether?.Aether);

            state.HpChanges = new HpChanges
            {
                Allies = BuildHpChangesList(before?.AllyHp, cc.PlayerMonsters, true),
                Enemies = BuildHpChangesList(before?.EnemyHp, cc.Enemies, false)
            };

            state.BuffChanges = new BuffChanges
            {
                Allies = BuildBuffChangesList(before?.AllyBuffs, cc.PlayerMonsters),
                Enemies = BuildBuffChangesList(before?.EnemyBuffs, cc.Enemies)
            };

            state.MonstersCanAct = BuildMonstersCanActList(cc);

            return JsonConfig.Serialize(state);
        }

        private static bool AetherChanged(Dictionary<EElement, int> before, Aether after)
        {
            if (before == null || after == null) return true;
            return before[EElement.Fire] != after.Fire ||
                   before[EElement.Water] != after.Water ||
                   before[EElement.Earth] != after.Earth ||
                   before[EElement.Wind] != after.Wind ||
                   before[EElement.Wild] != after.Wild;
        }

        private static List<HpChange> BuildHpChangesList(Dictionary<int, int> before, List<Monster> monsters, bool isAlly)
        {
            var list = new List<HpChange>();
            if (monsters == null) return list;

            for (int i = 0; i < monsters.Count; i++)
            {
                var m = monsters[i];
                int currentHp = m.CurrentHealth;
                int beforeHp = before != null && before.ContainsKey(i) ? before[i] : currentHp;

                if (currentHp != beforeHp)
                {
                    var change = new HpChange
                    {
                        Name = StripMarkup(m.Name),
                        Hp = currentHp
                    };
                    if (!isAlly)
                        change.IsDead = m.State?.IsDead ?? false;
                    list.Add(change);
                }
            }
            return list;
        }

        private static List<MonsterBuffChanges> BuildBuffChangesList(Dictionary<int, Dictionary<string, int>> before, List<Monster> monsters)
        {
            var list = new List<MonsterBuffChanges>();
            if (monsters == null) return list;

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

                var added = new List<BuffChange>();
                var removed = new List<BuffRemoved>();

                foreach (var kvp in currentBuffs)
                {
                    if (!beforeBuffs.ContainsKey(kvp.Key))
                    {
                        added.Add(new BuffChange { Name = kvp.Key, Stacks = kvp.Value.stacks, Type = kvp.Value.type });
                    }
                    else if (beforeBuffs[kvp.Key] != kvp.Value.stacks)
                    {
                        added.Add(new BuffChange { Name = kvp.Key, Stacks = kvp.Value.stacks, Type = kvp.Value.type, PreviousStacks = beforeBuffs[kvp.Key] });
                    }
                }

                foreach (var kvp in beforeBuffs)
                {
                    if (!currentBuffs.ContainsKey(kvp.Key))
                        removed.Add(new BuffRemoved { Name = kvp.Key });
                }

                if (added.Count > 0 || removed.Count > 0)
                {
                    list.Add(new MonsterBuffChanges
                    {
                        Name = StripMarkup(m.Name),
                        Added = added,
                        Removed = removed
                    });
                }
            }
            return list;
        }

        private static List<MonsterCanAct> BuildMonstersCanActList(CombatController cc)
        {
            var list = new List<MonsterCanAct>();
            for (int i = 0; i < cc.PlayerMonsters.Count; i++)
            {
                var m = cc.PlayerMonsters[i];
                bool canAct = !m.State.IsDead && !(m.Turn?.WasStaggered ?? false);
                list.Add(new MonsterCanAct { Name = StripMarkup(m.Name), CanAct = canAct });
            }
            return list;
        }

        private static CombatMonster BuildCombatMonster(Monster m, int index, bool isPlayer, string displayName = null)
        {
            var monster = new CombatMonster
            {
                Name = displayName ?? StripMarkup(m.Name),
                Hp = m.CurrentHealth,
                MaxHp = m.Stats?.MaxHealth?.ValueInt ?? 0,
                Shield = m.Shield,
                Corruption = m.Stats?.CurrentCorruption ?? 0,
                IsDead = m.State?.IsDead ?? false,
                Buffs = BuildBuffsList(m, EBuffType.Buff),
                Debuffs = BuildBuffsList(m, EBuffType.Debuff),
                Traits = BuildTraitsList(m)
            };

            if (isPlayer)
            {
                var skills = new List<SkillInfo>();
                int skillIdx = 0;
                foreach (var skill in m.SkillManager?.Actions ?? new List<SkillInstance>())
                {
                    skills.Add(new SkillInfo
                    {
                        Name = StripMarkup(skill.Action?.Name ?? ""),
                        Description = StripMarkup(skill.Action?.GetDescription(skill) ?? ""),
                        Cost = BuildAetherValues(skill.GetActionCost()),
                        CanUse = skill.Action?.CanUseAction(skill) ?? false,
                        Category = GetActionCategory(skill),
                        SubTypes = GetActionSubTypes(skill)
                    });
                    skillIdx++;
                }
                monster.Skills = skills;
            }
            else
            {
                monster.Staggered = m.Turn?.WasStaggered ?? false;

                var poise = new List<PoiseInfo>();
                foreach (var p in m.SkillManager?.Stagger ?? new List<StaggerDefine>())
                {
                    poise.Add(new PoiseInfo
                    {
                        Element = p.Element.ToString(),
                        Current = p.CurrentPoise,
                        Max = p.MaxHits
                    });
                }
                monster.Poise = poise;

                if (m.AI?.PickedActionList != null && m.AI.PickedActionList.Count > 0)
                {
                    var action = m.AI.PickedActionList[0];
                    var targetName = StripMarkup((action.Target as Monster)?.Name ?? "unknown");
                    monster.IntendedAction = new IntendedAction
                    {
                        Skill = StripMarkup(action.Action?.Action?.Name ?? ""),
                        Target = targetName
                    };
                }
            }

            return monster;
        }

        private static List<BuffInfo> BuildBuffsList(Monster m, EBuffType buffType)
        {
            var list = new List<BuffInfo>();
            if (m.BuffManager?.Buffs == null) return list;

            foreach (var buff in m.BuffManager.Buffs)
            {
                if (buff.Buff?.BuffType == buffType)
                    list.Add(new BuffInfo { Name = StripMarkup(buff.Buff?.Name ?? ""), Stacks = buff.Stacks });
            }
            return list;
        }

        private static List<TraitInfo> BuildTraitsList(Monster m)
        {
            var list = new List<TraitInfo>();
            if (m.SkillManager?.Traits == null) return list;

            var signatureTraitId = m.SkillManager.SignatureTraitInstance?.Trait?.ID ?? -1;
            foreach (var trait in m.SkillManager.Traits)
            {
                if (trait.Trait == null) continue;
                list.Add(new TraitInfo
                {
                    Name = StripMarkup(trait.Trait.Name ?? ""),
                    Description = StripMarkup(trait.Trait.GetDescription(trait) ?? ""),
                    IsSignature = trait.Trait.ID == signatureTraitId,
                    IsAura = trait.Trait.IsAura()
                });
            }
            return list;
        }

        private static AetherValues BuildAetherValues(Aether aether)
        {
            if (aether == null) return new AetherValues();
            return new AetherValues
            {
                Fire = aether.Fire,
                Water = aether.Water,
                Earth = aether.Earth,
                Wind = aether.Wind,
                Wild = aether.Wild
            };
        }

        private static string GetExplorationStateJson()
        {
            var playerPos = PlayerMovementController.Instance?.transform?.position ?? UnityEngine.Vector3.zero;
            var currentArea = ExplorationController.Instance?.CurrentArea ?? EArea.PilgrimsRest;
            var zone = ExplorationController.Instance?.CurrentZone ?? 0;

            bool canStartRun = currentArea == EArea.PilgrimsRest && FindStartRunInteractable() != null;

            return JsonConfig.Serialize(new ExplorationState
            {
                Phase = GamePhase.Exploration,
                Player = new PlayerPosition { X = playerPos.x, Y = playerPos.y, Z = playerPos.z },
                Area = currentArea.ToString(),
                Zone = zone,
                CanStartRun = canStartRun,
                Gold = InventoryManager.Instance?.Gold ?? 0,
                Party = BuildDetailedPartyList(),
                Artifacts = BuildArtifactsList(),
                MonsterSoulCount = InventoryManager.Instance?.MonsterSouls ?? 0,
                MonsterGroups = BuildMonsterGroupsList(),
                Interactables = BuildInteractablesList()
            });
        }

        public static NextAreaInteractable FindStartRunInteractable()
        {
            var interactables = UnityEngine.Object.FindObjectsByType<NextAreaInteractable>(UnityEngine.FindObjectsSortMode.None);
            foreach (var interactable in interactables)
            {
                if (interactable.HeadlinerText == NextAreaInteractable.Headliner.StartRun)
                    return interactable;
            }
            return null;
        }

        private static List<MonsterGroupInfo> BuildMonsterGroupsList()
        {
            var list = new List<MonsterGroupInfo>();
            var groups = ExplorationController.Instance?.EncounterGroups;
            if (groups == null) return list;

            // Filter null groups and build display names using identity-based naming
            var validGroups = groups.Where(g => g != null).ToList();
            Func<MonsterGroup, string> getGroupName = g =>
            {
                var pos = g.transform.position;
                return $"Monster Group at ({pos.x:F0}, {pos.y:F0})";
            };

            // Build monster groups list with identity-based naming
            foreach (var group in validGroups)
            {
                var pos = group.transform.position;
                list.Add(new MonsterGroupInfo
                {
                    Name = GetDisplayName(group, validGroups, getGroupName),
                    X = pos.x,
                    Y = pos.y,
                    Z = pos.z,
                    Defeated = group.EncounterDefeated,
                    CanVoidBlitz = !group.EncounterDefeated && group.CanBeAetherBlitzed(),
                    EncounterType = group.EncounterData?.EncounterType.ToString() ?? "Unknown",
                    MonsterCount = group.OverworldMonsters?.Count ?? 0
                });
            }
            return list;
        }

        private static List<InteractableInfo> BuildInteractablesList()
        {
            var list = new List<InteractableInfo>();

            // Find ALL DialogueInteractable objects (NPCs, events, collectables)
            var allInteractables = UnityEngine.Object.FindObjectsByType<DialogueInteractable>(
                UnityEngine.FindObjectsSortMode.None);

            foreach (DialogueInteractable interactable in allInteractables)
            {
                if (interactable == null) continue;

                var pos = interactable.transform.position;
                var gameObjectName = interactable.gameObject.name;

                // Check if it's a collectable event or a regular NPC
                InteractableType type;
                if (gameObjectName.Contains("Collectable") || gameObjectName.Contains("SmallEvent"))
                    type = InteractableType.Event;
                else
                    type = InteractableType.Npc;

                // Determine name - check for care chest pattern first
                string name;
                if (gameObjectName.Contains("SmallEvent_NPC_Collectable"))
                    name = "Care Chest";
                else
                    name = interactable.DialogueCharacter?.CharacterName ?? gameObjectName;

                var info = new InteractableInfo
                {
                    Type = type,
                    X = pos.x, Y = pos.y, Z = pos.z,
                    Used = interactable.WasUsedUp,
                    Talked = interactable.WasUsedUp,
                    Name = name,
                    HasEvent = interactable.DialogueCharacter?.EventOptions?.Count > 0
                };

                if (type == InteractableType.Event)
                    info.Completed = interactable.WasUsedUp;

                list.Add(info);
            }

            // Also find ChestInteractible objects
            var allChests = UnityEngine.Object.FindObjectsByType<ChestInteractible>(
                UnityEngine.FindObjectsSortMode.None);
            foreach (var chest in allChests)
            {
                if (chest == null) continue;
                var pos = chest.transform.position;
                list.Add(new InteractableInfo
                {
                    Type = InteractableType.Chest,
                    X = pos.x, Y = pos.y, Z = pos.z,
                    Opened = chest.WasUsedUp
                });
            }

            // Use map lists for types not directly accessible
            var map = LevelGenerator.Instance?.Map;
            if (map != null)
            {
                // AetherSprings from map
                foreach (var spring in map.AetherSpringInteractables)
                {
                    if (spring == null) continue;
                    var pos = spring.transform.position;
                    list.Add(new InteractableInfo
                    {
                        Type = InteractableType.AetherSpring,
                        X = pos.x, Y = pos.y, Z = pos.z,
                        Used = spring.WasUsedUp
                    });
                }

                // MonsterShrine from map
                if (map.MonsterShrine != null)
                {
                    var pos = map.MonsterShrine.transform.position;
                    list.Add(new InteractableInfo
                    {
                        Type = InteractableType.MonsterShrine,
                        X = pos.x, Y = pos.y, Z = pos.z,
                        Used = map.MonsterShrine.WasUsedUp
                    });
                }

                // Merchant from map
                if (map.MerchantInteractable != null)
                {
                    var pos = map.MerchantInteractable.transform.position;
                    list.Add(new InteractableInfo
                    {
                        Type = InteractableType.Merchant,
                        X = pos.x, Y = pos.y, Z = pos.z
                    });
                }

                // SecretRooms from map
                foreach (var secret in map.SecretRoomInteractables)
                {
                    if (secret == null) continue;
                    var pos = secret.transform.position;
                    list.Add(new InteractableInfo
                    {
                        Type = InteractableType.SecretRoom,
                        X = pos.x, Y = pos.y, Z = pos.z,
                        Found = secret.WasUsedUp
                    });
                }
            }

            // Find StartRun interactable in Pilgrim's Rest
            var currentArea = ExplorationController.Instance?.CurrentArea ?? EArea.PilgrimsRest;
            if (currentArea == EArea.PilgrimsRest)
            {
                var startRun = FindStartRunInteractable();
                if (startRun != null)
                {
                    var pos = startRun.transform.position;
                    list.Add(new InteractableInfo
                    {
                        Type = InteractableType.StartRun,
                        X = pos.x, Y = pos.y, Z = pos.z
                    });
                }
            }

            // Also find LootDroppers that might not be BaseInteractable
            var propGen = LevelGenerator.Instance?.PropGenerator;
            if (propGen != null)
            {
                var breakables = propGen.GeneratedBreakableObjects;
                if (breakables != null)
                {
                    var seenPositions = new HashSet<string>();
                    foreach (var item in list)
                        seenPositions.Add($"{item.X:F0},{item.Y:F0}");

                    foreach (var breakable in breakables)
                    {
                        if (breakable == null) continue;
                        if (breakable.gameObject.name.Contains("LootDropper"))
                        {
                            var pos = breakable.transform.position;
                            var posKey = $"{pos.x:F0},{pos.y:F0}";
                            if (!seenPositions.Contains(posKey))
                            {
                                seenPositions.Add(posKey);
                                list.Add(new InteractableInfo
                                {
                                    Type = InteractableType.LootDropper,
                                    X = pos.x, Y = pos.y, Z = pos.z,
                                    Used = breakable.WasUsedUp
                                });
                            }
                        }
                    }
                }
            }

            // Portals (ExitInteractables)
            var exitInteractables = propGen.ExitInteractables;
            if (exitInteractables != null)
            {
                // Use reflection to access private nextMapBubble field
                var nextMapBubbleField = typeof(ExitInteractable).GetField(
                    "nextMapBubble",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                for (int i = 0; i < exitInteractables.Count; i++)
                {
                    var go = exitInteractables[i];
                    if (go == null) continue;

                    var exitInteractable = go.GetComponent<ExitInteractable>();
                    if (exitInteractable == null) continue;

                    var pos = go.transform.position;
                    string portalType = "None";

                    if (nextMapBubbleField != null)
                    {
                        var mapBubble = nextMapBubbleField.GetValue(exitInteractable) as MapBubble;
                        if (mapBubble != null)
                            portalType = mapBubble.Customization.ToString();
                    }

                    list.Add(new InteractableInfo
                    {
                        Type = InteractableType.Portal,
                        X = pos.x,
                        Y = pos.y,
                        Z = pos.z,
                        Name = $"Portal ({portalType})",
                        PortalType = portalType,
                        Used = exitInteractable.WasUsedUp
                    });
                }
            }

            return list;
        }

        private static List<ConsumableInfo> BuildConsumablesList()
        {
            var list = new List<ConsumableInfo>();
            var consumables = PlayerController.Instance?.Inventory?.GetAllConsumables();
            if (consumables == null) return list;

            var currentMonster = CombatController.Instance?.CurrentMonster;
            Func<ConsumableInstance, string> getConsumableName = c =>
                StripMarkup(c.Consumable?.Name ?? c.Skill?.Name ?? "Unknown");

            // Build consumables list with identity-based naming
            for (int i = 0; i < consumables.Count; i++)
            {
                var c = consumables[i];
                if (currentMonster != null) c.Owner = currentMonster;

                list.Add(new ConsumableInfo
                {
                    Name = GetDisplayName(c, consumables, getConsumableName),
                    Description = StripMarkup(c.Action?.GetDescription(c) ?? ""),
                    CurrentCharges = c.Charges,
                    MaxCharges = c.GetMaxCharges(),
                    CanUse = c.Action?.CanUseAction(c) ?? false,
                    TargetType = (c.Action?.TargetType ?? ETargetType.SelfOrOwner).ToString()
                });
            }

            return list;
        }

        private static List<MonsterDetails> BuildDetailedPartyList()
        {
            var list = new List<MonsterDetails>();
            var party = MonsterManager.Instance?.Active;
            if (party == null) return list;

            for (int i = 0; i < party.Count; i++)
                list.Add(BuildMonsterDetails(party[i], i));

            return list;
        }

        private static List<ActionInfo> BuildActionsList(Monster m)
        {
            var actions = new List<ActionInfo>();
            int actionIdx = 0;
            foreach (var skill in m.SkillManager?.Actions ?? new List<SkillInstance>())
            {
                var elements = new List<string>();
                if (skill.Action?.Elements != null)
                {
                    foreach (var e in skill.Action.Elements)
                        elements.Add(e.ToString());
                }

                actions.Add(new ActionInfo
                {
                    Name = StripMarkup(skill.Action?.Name ?? ""),
                    Description = StripMarkup(skill.Action?.GetDescription(skill) ?? ""),
                    Cost = BuildAetherValues(skill.GetActionCost()),
                    TargetType = skill.Action?.TargetType.ToString() ?? "Unknown",
                    Elements = elements,
                    Category = GetActionCategory(skill),
                    SubTypes = GetActionSubTypes(skill)
                });
                actionIdx++;
            }
            return actions;
        }

        private static MonsterDetails BuildMonsterDetails(Monster m, int index)
        {
            return new MonsterDetails
            {
                Name = StripMarkup(m.Name),
                Level = m.Level,
                Hp = m.CurrentHealth,
                MaxHp = m.Stats?.MaxHealth?.ValueInt ?? 0,
                Shield = m.Shield,
                CurrentExp = m.LevelManager?.CurrentExp ?? 0,
                ExpNeeded = m.LevelManager?.ExpNeededTotal ?? 0,
                WorthinessLevel = m.Worthiness?.WorthinessLevel ?? 0,
                CurrentWorthiness = m.Worthiness?.CurrentWorthiness ?? 0,
                WorthinessNeeded = m.Worthiness?.CurrentRequiredWorthinessTotal ?? 0,
                Actions = BuildActionsList(m),
                Traits = BuildTraitsList(m)
            };
        }

        private static List<ArtifactInfo> BuildArtifactsList()
        {
            var list = new List<ArtifactInfo>();
            var consumables = InventoryManager.Instance?.GetAllConsumables();
            if (consumables == null) return list;

            // Filter to only items with charges
            var activeArtifacts = consumables.Where(c => c.Charges > 0).ToList();
            Func<ConsumableInstance, string> getArtifactName = c =>
                StripMarkup(c.Consumable?.Name ?? c.Skill?.Name ?? "Unknown");

            // Build artifacts list with identity-based naming
            foreach (var c in activeArtifacts)
            {
                list.Add(new ArtifactInfo
                {
                    Name = GetDisplayName(c, activeArtifacts, getArtifactName),
                    Description = StripMarkup(c.Action?.GetDescription(c) ?? ""),
                    CurrentCharges = c.Charges,
                    MaxCharges = c.GetMaxCharges(),
                    TargetType = (c.Action?.TargetType ?? ETargetType.SelfOrOwner).ToString()
                });
            }

            return list;
        }
    }
}
