using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace AethermancerHarness
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        private static Harmony _harmony;

        private bool _hasLoggedCombatAccess = false;
        private bool _hasLoggedGameReady = false;
        private HarnessServer _server;

        private void Awake()
        {
            Log = Logger;
            Logger.LogInfo($"AethermancerHarness v{PluginInfo.PLUGIN_VERSION} loaded!");

            // Initialize Harmony patches
            Logger.LogInfo("Initializing Harmony patches...");
            _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            _harmony.PatchAll(typeof(VoidBlitzPatches).Assembly);
            Logger.LogInfo("Harmony patches applied.");

            Logger.LogInfo("Starting HTTP server on port 8080...");

            _server = new HarnessServer(Logger, 8080);
            _server.Start();

            Logger.LogInfo("Waiting for game systems to initialize...");
        }

        private void OnDestroy()
        {
            _server?.Stop();
            _harmony?.UnpatchSelf();
        }

        private void Update()
        {
            // Check if game systems are ready
            if (!_hasLoggedGameReady && GameController.Instance != null)
            {
                _hasLoggedGameReady = true;
                Logger.LogInfo("GameController.Instance is available!");

                // Try to log game state info
                try
                {
                    var state = GameStateManager.Instance?.CurrentState;
                    Logger.LogInfo($"Current GameState: {state}");
                }
                catch (System.Exception e)
                {
                    Logger.LogWarning($"Could not read GameState: {e.Message}");
                }
            }

            // Check if CombatController is available
            if (!_hasLoggedCombatAccess && CombatController.Instance != null)
            {
                _hasLoggedCombatAccess = true;
                Logger.LogInfo("CombatController.Instance is available!");

                try
                {
                    var playerMonsters = CombatController.Instance.PlayerMonsters;
                    var enemies = CombatController.Instance.Enemies;
                    Logger.LogInfo($"PlayerMonsters count: {playerMonsters?.Count ?? 0}");
                    Logger.LogInfo($"Enemies count: {enemies?.Count ?? 0}");
                }
                catch (System.Exception e)
                {
                    Logger.LogWarning($"Could not read combat state: {e.Message}");
                }
            }

            // Debug key: F11 to dump current state
            if (Input.GetKeyDown(KeyCode.F11))
            {
                DumpCurrentState();
            }
        }

        private void DumpCurrentState()
        {
            Logger.LogInfo("=== STATE DUMP (F11) ===");

            try
            {
                // Game state
                var gameState = GameStateManager.Instance?.CurrentState;
                Logger.LogInfo($"GameState: {gameState}");

                // Check if in combat
                var isCombat = GameStateManager.Instance?.IsCombat ?? false;
                Logger.LogInfo($"IsCombat: {isCombat}");

                if (isCombat && CombatController.Instance != null)
                {
                    DumpCombatState();
                }
                else
                {
                    Logger.LogInfo("Not in combat - no combat state to dump");
                }
            }
            catch (System.Exception e)
            {
                Logger.LogError($"Error dumping state: {e}");
            }

            Logger.LogInfo("=== END STATE DUMP ===");
        }

        private void DumpCombatState()
        {
            var cc = CombatController.Instance;

            // Round info
            var round = cc.Timeline?.CurrentRound ?? 0;
            Logger.LogInfo($"Combat Round: {round}");

            // Current monster
            var current = cc.CurrentMonster;
            Logger.LogInfo($"Current Monster: {current?.Name ?? "none"} (Player: {current?.BelongsToPlayer})");

            // Player monsters
            Logger.LogInfo("--- Player Monsters ---");
            foreach (var m in cc.PlayerMonsters)
            {
                var hp = m.CurrentHealth;
                var maxHp = m.Stats?.MaxHealth?.ValueInt ?? 0;
                var staggered = m.Turn?.WasStaggered ?? false;
                Logger.LogInfo($"  {m.Name}: {hp}/{maxHp} HP, Staggered: {staggered}");

                // List skills
                if (m.SkillManager?.Actions != null)
                {
                    foreach (var skill in m.SkillManager.Actions)
                    {
                        var canUse = skill.Action?.CanUseAction(skill) ?? false;
                        Logger.LogInfo($"    - {skill.Action?.Name}: CanUse={canUse}");
                    }
                }
            }

            // Enemies
            Logger.LogInfo("--- Enemies ---");
            foreach (var m in cc.Enemies)
            {
                var hp = m.CurrentHealth;
                var maxHp = m.Stats?.MaxHealth?.ValueInt ?? 0;
                var staggered = m.Turn?.WasStaggered ?? false;
                Logger.LogInfo($"  {m.Name}: {hp}/{maxHp} HP, Staggered: {staggered}");

                // Poise
                if (m.SkillManager?.Stagger != null)
                {
                    foreach (var poise in m.SkillManager.Stagger)
                    {
                        Logger.LogInfo($"    Poise[{poise.Element}]: {poise.CurrentPoise}/{poise.MaxHits}");
                    }
                }

                // Intended action
                if (m.AI?.PickedActionList != null && m.AI.PickedActionList.Count > 0)
                {
                    var action = m.AI.PickedActionList[0];
                    var targetName = (action.Target as Monster)?.Name ?? "unknown";
                    Logger.LogInfo($"    Intends: {action.Action?.Action?.Name} -> {targetName}");
                }
            }

            // Aether
            Logger.LogInfo("--- Aether ---");
            var playerAether = cc.PlayerAether?.Aether;
            var enemyAether = cc.EnemyAether?.Aether;
            if (playerAether != null)
            {
                Logger.LogInfo($"Player: F={playerAether.Fire} W={playerAether.Water} E={playerAether.Earth} Wi={playerAether.Wind} N={playerAether.Neutral} Any={playerAether.Wild}");
            }
            if (enemyAether != null)
            {
                Logger.LogInfo($"Enemy:  F={enemyAether.Fire} W={enemyAether.Water} E={enemyAether.Earth} Wi={enemyAether.Wind} N={enemyAether.Neutral} Any={enemyAether.Wild}");
            }
        }
    }

    public static class PluginInfo
    {
        public const string PLUGIN_GUID = "com.klappec.aethermancerharness";
        public const string PLUGIN_NAME = "AethermancerHarness";
        public const string PLUGIN_VERSION = "1.0.0";
    }
}
