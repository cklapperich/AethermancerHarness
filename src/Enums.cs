namespace AethermancerHarness
{
    /// <summary>
    /// Game phases/screens in the harness.
    /// </summary>
    public enum GamePhase
    {
        DifficultySelection,
        MonsterSelection,
        EquipmentSelection,
        SkillSelection,
        Combat,
        Exploration,
        Dialogue,
        Merchant,
        EndOfRun,
        Timeout
    }

    /// <summary>
    /// Types of interactables on the exploration map.
    /// </summary>
    public enum InteractableType
    {
        AetherSpring,
        MonsterGroup,
        Chest,
        Merchant,
        MonsterShrine,
        Npc,
        Event,
        SecretRoom,
        StartRun
    }

    /// <summary>
    /// Combat input readiness status.
    /// </summary>
    public enum InputReadyStatus
    {
        NotInCombat,
        NoCombatController,
        NoCurrentMonster,
        EnemyTurn,
        TriggersPending,
        ActionExecuting,
        Ready,
        CombatEnded
    }

    /// <summary>
    /// Combat result outcomes.
    /// </summary>
    public enum CombatResult
    {
        Victory,
        Defeat,
        Unknown
    }

    /// <summary>
    /// Types of choices in dialogues and menus.
    /// </summary>
    public enum ChoiceType
    {
        Random,
        Monster,
        Scrap,
        Close,
        Artifact,
        Consumable,
        Skill,
        Trait,
        Perk,
        Equipment,
        Unknown
    }

    /// <summary>
    /// Shrine selection types (maps to EShrineState).
    /// </summary>
    public enum ShrineType
    {
        Starter,
        RunStart,
        Normal
    }

    /// <summary>
    /// Extension methods for converting enums to their JSON string representations.
    /// </summary>
    public static class EnumExtensions
    {
        /// <summary>
        /// Converts GamePhase to SCREAMING_SNAKE_CASE string for JSON output.
        /// </summary>
        public static string ToJsonString(this GamePhase phase)
        {
            switch (phase)
            {
                case GamePhase.DifficultySelection: return "DIFFICULTY_SELECTION";
                case GamePhase.MonsterSelection: return "MONSTER_SELECTION";
                case GamePhase.EquipmentSelection: return "EQUIPMENT_SELECTION";
                case GamePhase.SkillSelection: return "SKILL_SELECTION";
                case GamePhase.Combat: return "COMBAT";
                case GamePhase.Exploration: return "EXPLORATION";
                case GamePhase.Dialogue: return "DIALOGUE";
                case GamePhase.Merchant: return "MERCHANT";
                case GamePhase.EndOfRun: return "END_OF_RUN";
                case GamePhase.Timeout: return "TIMEOUT";
                default: return phase.ToString().ToUpper();
            }
        }

        /// <summary>
        /// Converts InteractableType to SCREAMING_SNAKE_CASE string for JSON output.
        /// </summary>
        public static string ToJsonString(this InteractableType type)
        {
            switch (type)
            {
                case InteractableType.AetherSpring: return "AETHER_SPRING";
                case InteractableType.MonsterGroup: return "MONSTER_GROUP";
                case InteractableType.Chest: return "CHEST";
                case InteractableType.Merchant: return "MERCHANT";
                case InteractableType.MonsterShrine: return "MONSTER_SHRINE";
                case InteractableType.Npc: return "NPC";
                case InteractableType.Event: return "EVENT";
                case InteractableType.SecretRoom: return "SECRET_ROOM";
                case InteractableType.StartRun: return "START_RUN";
                default: return type.ToString().ToUpper();
            }
        }

        /// <summary>
        /// Converts InputReadyStatus to PascalCase string for JSON output.
        /// </summary>
        public static string ToJsonString(this InputReadyStatus status)
        {
            return status.ToString();
        }

        /// <summary>
        /// Converts CombatResult to SCREAMING_CASE string for JSON output.
        /// </summary>
        public static string ToJsonString(this CombatResult result)
        {
            switch (result)
            {
                case CombatResult.Victory: return "VICTORY";
                case CombatResult.Defeat: return "DEFEAT";
                case CombatResult.Unknown: return "UNKNOWN";
                default: return result.ToString().ToUpper();
            }
        }

        /// <summary>
        /// Converts ChoiceType to lowercase string for JSON output.
        /// </summary>
        public static string ToJsonString(this ChoiceType type)
        {
            return type.ToString().ToLower();
        }

        /// <summary>
        /// Converts ShrineType to camelCase string for JSON output.
        /// </summary>
        public static string ToJsonString(this ShrineType type)
        {
            switch (type)
            {
                case ShrineType.Starter: return "starter";
                case ShrineType.RunStart: return "runStart";
                case ShrineType.Normal: return "normal";
                default: return type.ToString().ToLower();
            }
        }

        /// <summary>
        /// Converts EShrineState to ShrineType.
        /// </summary>
        public static ShrineType ToShrineType(this EShrineState state)
        {
            switch (state)
            {
                case EShrineState.StarterSelection: return ShrineType.Starter;
                case EShrineState.RunStartSelection: return ShrineType.RunStart;
                case EShrineState.NormalShrineSelection:
                default: return ShrineType.Normal;
            }
        }
    }
}
