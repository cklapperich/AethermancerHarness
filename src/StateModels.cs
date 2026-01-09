using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace AethermancerHarness
{
    public static class JsonConfig
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Converters = { new StringEnumConverter() },
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None
        };

        public static string Serialize(object obj) => JsonConvert.SerializeObject(obj, Settings);

        public static JObject Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new JObject();
            return JObject.Parse(json);
        }

        public static T Value<T>(JObject obj, string key, T defaultValue = default)
        {
            var token = obj[key];
            if (token == null) return defaultValue;
            return token.Value<T>();
        }

        public static string Error(string message) => Serialize(new { success = false, error = message });

        public static string Error(string message, object details)
        {
            return new JObject
            {
                ["success"] = false,
                ["error"] = message,
                ["details"] = JObject.FromObject(details, JsonSerializer.Create(Settings))
            }.ToString(Formatting.None);
        }
    }

    public class AetherValues
    {
        public int Fire { get; set; }
        public int Water { get; set; }
        public int Earth { get; set; }
        public int Wind { get; set; }
        public int Wild { get; set; }
    }

    public class BuffInfo
    {
        public string Name { get; set; }
        public int Stacks { get; set; }
    }

    public class BuffChange
    {
        public string Name { get; set; }
        public int Stacks { get; set; }
        public string Type { get; set; }
        public int? PreviousStacks { get; set; }
    }

    public class BuffRemoved
    {
        public string Name { get; set; }
    }

    public class TraitInfo
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public bool IsSignature { get; set; }
        public bool IsAura { get; set; }
    }

    public class SkillInfo
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public AetherValues Cost { get; set; }
        public bool CanUse { get; set; }
        public string Category { get; set; }
        public List<string> SubTypes { get; set; }
    }

    public class ActionInfo
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public AetherValues Cost { get; set; }
        public string TargetType { get; set; }
        public List<string> Elements { get; set; }
        public string Category { get; set; }
        public List<string> SubTypes { get; set; }
    }

    public class TargetInfo
    {
        public string Name { get; set; }
    }

    public class ValidAction
    {
        public string Name { get; set; }
        public AetherValues Cost { get; set; }
        public List<TargetInfo> Targets { get; set; }
        public string Category { get; set; }
        public List<string> SubTypes { get; set; }
    }

    public class PoiseInfo
    {
        public string Element { get; set; }
        public int Current { get; set; }
        public int Max { get; set; }
    }

    public class IntendedAction
    {
        public string Skill { get; set; }
        public string Target { get; set; }
    }

    public class AffixInfo
    {
        public string Name { get; set; }
        public string ShortDescription { get; set; }
        public string Description { get; set; }
        public bool IsRare { get; set; }
        public string PerkType { get; set; }
    }

    public class EquipmentInfo
    {
        public string Name { get; set; }
        public string EquipmentType { get; set; }
        public string Rarity { get; set; }
        public bool IsAura { get; set; }
        public string BaseDescription { get; set; }
        public List<AffixInfo> Affixes { get; set; }
    }

    public class MonsterDetails
    {
        public string Name { get; set; }
        public int Level { get; set; }
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public int Shield { get; set; }
        public int CurrentExp { get; set; }
        public int ExpNeeded { get; set; }
        public int WorthinessLevel { get; set; }
        public int CurrentWorthiness { get; set; }
        public int WorthinessNeeded { get; set; }
        public List<ActionInfo> Actions { get; set; }
        public List<TraitInfo> Traits { get; set; }
    }

    public class CombatMonster
    {
        public string Name { get; set; }
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public int Shield { get; set; }
        public int Corruption { get; set; }
        public bool IsDead { get; set; }
        public List<BuffInfo> Buffs { get; set; }
        public List<BuffInfo> Debuffs { get; set; }
        public List<TraitInfo> Traits { get; set; }
        public List<SkillInfo> Skills { get; set; }
        public bool? Staggered { get; set; }
        public List<PoiseInfo> Poise { get; set; }
        public IntendedAction IntendedAction { get; set; }
    }

    public class ConsumableInfo
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public int CurrentCharges { get; set; }
        public int MaxCharges { get; set; }
        public bool CanUse { get; set; }
        public string TargetType { get; set; }
    }

    public class ArtifactInfo
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public int CurrentCharges { get; set; }
        public int MaxCharges { get; set; }
        public string TargetType { get; set; }
    }

    public class HpChange
    {
        public string Name { get; set; }
        public int Hp { get; set; }
        public bool? IsDead { get; set; }
    }

    public class MonsterBuffChanges
    {
        public string Name { get; set; }
        public List<BuffChange> Added { get; set; }
        public List<BuffRemoved> Removed { get; set; }
    }

    public class HpChanges
    {
        public List<HpChange> Allies { get; set; }
        public List<HpChange> Enemies { get; set; }
    }

    public class BuffChanges
    {
        public List<MonsterBuffChanges> Allies { get; set; }
        public List<MonsterBuffChanges> Enemies { get; set; }
    }

    public class MonsterCanAct
    {
        public string Name { get; set; }
        public bool CanAct { get; set; }
    }

    public class InteractableInfo
    {
        public InteractableType Type { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public string Name { get; set; }
        public bool? Used { get; set; }
        public bool? Defeated { get; set; }
        public bool? Opened { get; set; }
        public bool? Talked { get; set; }
        public bool? HasEvent { get; set; }
        public bool? Completed { get; set; }
        public bool? Found { get; set; }
        public string PortalType { get; set; }
    }

    public class MonsterGroupInfo
    {
        public string Name { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public bool Defeated { get; set; }
        public bool CanVoidBlitz { get; set; }
        public string EncounterType { get; set; }
        public int MonsterCount { get; set; }
    }

    public class PlayerPosition
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }

    public class DifficultyChoice
    {
        public string Name { get; set; }
        public string Difficulty { get; set; }
        public bool Unlocked { get; set; }
        public bool Selected { get; set; }
    }

    public class MonsterChoice
    {
        public ChoiceType Type { get; set; }
        public string Name { get; set; }
        public bool? HasShiftedVariant { get; set; }
        public MonsterDetails Details { get; set; }
    }

    public class EquipmentChoice
    {
        public ChoiceType Type { get; set; }
        public string Name { get; set; }
        public int? Level { get; set; }
        public EquipmentInfo CurrentEquipment { get; set; }
        public bool? WillTrade { get; set; }
        public int? GoldValue { get; set; }
    }

    public class SkillChoice
    {
        public string Name { get; set; }
        public bool IsMaverick { get; set; }
        public string Description { get; set; }
        public AetherValues Cost { get; set; }
        public List<string> Elements { get; set; }
        public string TargetType { get; set; }
        public bool IsTrait { get; set; }
        public string Category { get; set; }
        public List<string> SubTypes { get; set; }
        public bool? IsAura { get; set; }
        public string Error { get; set; }
    }

    public class DialogueChoice
    {
        public string Text { get; set; }
        public string Type { get; set; }
        public EquipmentInfo Equipment { get; set; }
    }

    public class MerchantItem
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Rarity { get; set; }
        public int Price { get; set; }
        public int OriginalPrice { get; set; }
        public bool IsDiscounted { get; set; }
        public int Quantity { get; set; }
        public bool CanAfford { get; set; }
        public string Description { get; set; }
    }

    public class LevelingMonster
    {
        public string Name { get; set; }
        public int Level { get; set; }
    }

    // State classes
    public class BaseState
    {
        public GamePhase Phase { get; set; }
        public string Error { get; set; }
    }

    public class DifficultySelectionState : BaseState
    {
        public string CurrentDifficulty { get; set; }
        public int MaxUnlockedDifficulty { get; set; }
        public List<DifficultyChoice> Choices { get; set; }
        public int Gold { get; set; }
        public List<MonsterDetails> Party { get; set; }
    }

    public class MonsterSelectionState : BaseState
    {
        public ShrineType ShrineType { get; set; }
        public int SelectedIndex { get; set; }
        public string CurrentShift { get; set; }
        public List<MonsterChoice> Choices { get; set; }
        public int Gold { get; set; }
        public List<MonsterDetails> Party { get; set; }
        public int ShrineRerollsAvailable { get; set; }
        public bool CanReroll { get; set; }
    }

    public class EquipmentSelectionState : BaseState
    {
        public EquipmentInfo HeldEquipment { get; set; }
        public int ScrapValue { get; set; }
        public List<EquipmentChoice> Choices { get; set; }
        public int Gold { get; set; }
        public List<MonsterDetails> Party { get; set; }
    }

    public class SkillSelectionState : BaseState
    {
        public string LevelUpType { get; set; }
        public LevelingMonster Monster { get; set; }
        public List<SkillChoice> Choices { get; set; }
        public int RerollsAvailable { get; set; }
        public bool CanChooseMaxHealth { get; set; }
        public int PendingLevelUps { get; set; }
        public int Gold { get; set; }
        public List<ArtifactInfo> Artifacts { get; set; }
        public int MonsterSoulCount { get; set; }
        public List<MonsterDetails> Party { get; set; }
    }

    public class CombatState : BaseState
    {
        public int Round { get; set; }
        public bool ReadyForInput { get; set; }
        public string InputStatus { get; set; }
        public int CurrentActorIndex { get; set; }
        public AetherValues PlayerAether { get; set; }
        public AetherValues EnemyAether { get; set; }
        public List<CombatMonster> Allies { get; set; }
        public List<CombatMonster> Enemies { get; set; }
        public List<ConsumableInfo> Consumables { get; set; }
        public int Gold { get; set; }
        public List<ArtifactInfo> Artifacts { get; set; }
        public int MonsterSoulCount { get; set; }
    }

    public class CondensedCombatState : BaseState
    {
        public AetherValues PlayerAether { get; set; }
        public AetherValues EnemyAether { get; set; }
        public int CurrentActorIndex { get; set; }
        public bool ReadyForInput { get; set; }
        public HpChanges HpChanges { get; set; }
        public BuffChanges BuffChanges { get; set; }
        public List<MonsterCanAct> MonstersCanAct { get; set; }
    }

    public class ExplorationState : BaseState
    {
        public PlayerPosition Player { get; set; }
        public string Area { get; set; }
        public int Zone { get; set; }
        public bool CanStartRun { get; set; }
        public int Gold { get; set; }
        public List<MonsterDetails> Party { get; set; }
        public List<ArtifactInfo> Artifacts { get; set; }
        public int MonsterSoulCount { get; set; }
        public List<MonsterGroupInfo> MonsterGroups { get; set; }
        public List<InteractableInfo> Interactables { get; set; }
    }

    public class DialogueState : BaseState
    {
        public string Npc { get; set; }
        public string DialogueText { get; set; }
        public bool IsChoiceEvent { get; set; }
        public List<DialogueChoice> Choices { get; set; }
        public bool CanGoBack { get; set; }
        public int Gold { get; set; }
        public List<ArtifactInfo> Artifacts { get; set; }
        public List<MonsterDetails> Party { get; set; }
    }

    public class MerchantState : BaseState
    {
        public int Gold { get; set; }
        public List<MerchantItem> Choices { get; set; }
        public List<MonsterDetails> Party { get; set; }
        public List<ArtifactInfo> Artifacts { get; set; }
    }

    public class BoonChoice
    {
        public string Name { get; set; }
        public string Element { get; set; }
        public int Tier { get; set; }
        public string Description { get; set; }
        public string Effect { get; set; }
    }

    public class ActiveBoonInfo
    {
        public string Name { get; set; }
        public string Element { get; set; }
        public int Tier { get; set; }
        public string Description { get; set; }
    }

    public class AetherSpringState : BaseState
    {
        public List<BoonChoice> Choices { get; set; }
        public List<ActiveBoonInfo> ActiveBoons { get; set; }
        public int Gold { get; set; }
        public List<MonsterDetails> Party { get; set; }
    }

    public class EndOfRunState : BaseState
    {
        public CombatResult Result { get; set; }
        public bool CanContinue { get; set; }
        public int Gold { get; set; }
    }

    public class ValidActionsResponse
    {
        public List<ValidAction> Actions { get; set; }
        public string Error { get; set; }
        public string WaitingFor { get; set; }
    }
}
