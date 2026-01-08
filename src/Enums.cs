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
        AetherSpring,
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
        StartRun,
        LootDropper
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

    public static class EnumExtensions
    {
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
