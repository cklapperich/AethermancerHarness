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
        /// Converts PascalCase to SCREAMING_SNAKE_CASE.
        /// </summary>
        private static string ToScreamingSnakeCase(string pascalCase)
        {
            if (string.IsNullOrEmpty(pascalCase)) return pascalCase;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < pascalCase.Length; i++)
            {
                if (i > 0 && char.IsUpper(pascalCase[i]))
                    sb.Append('_');
                sb.Append(char.ToUpper(pascalCase[i]));
            }
            return sb.ToString();
        }

        public static string ToJsonString(this GamePhase phase) => ToScreamingSnakeCase(phase.ToString());
        public static string ToJsonString(this InteractableType type) => ToScreamingSnakeCase(type.ToString());
        public static string ToJsonString(this InputReadyStatus status) => ToScreamingSnakeCase(status.ToString());
        public static string ToJsonString(this CombatResult result) => ToScreamingSnakeCase(result.ToString());
        public static string ToJsonString(this ChoiceType type) => ToScreamingSnakeCase(type.ToString());
        public static string ToJsonString(this ShrineType type) => ToScreamingSnakeCase(type.ToString());

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
