# AethermancerHarness

A BepInEx plugin that exposes an HTTP API for AI agent training in the Aethermancer game.

## Status: Working

**Implemented:**
- [x] Combat state reading (JSON + compact text)
- [x] Combat action execution (skills + consumables)
- [x] Combat preview (simulate actions without executing)
- [x] Exploration state reading (player position, interactables, monster groups)
- [x] Teleportation to arbitrary coordinates
- [x] Interaction triggering
- [x] Programmatic combat start with any monster group
- [x] Void Blitz automation (full animation, bypasses distance checks)
- [x] Post-combat skill selection

**Not Yet Implemented:**
- [ ] Speed optimization (`Time.timeScale` adjustments)
- [ ] Headless mode (Steam integration issues - requires visible window)

## Installation

BepInEx 5.4.23.4 must be installed in the game directory.

```bash
./build.sh  # Builds and copies to BepInEx/plugins
```

## Usage

```bash
./launch.sh  # Launches game via Steam, waits for server
```

Or launch Aethermancer through Steam normally. Server runs on `http://localhost:8080`.

## API Reference

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/health` | Check if game is ready |
| GET | `/state` | Get current game state (exploration or combat) |
| GET | `/state?format=text` | Get state as compact text |
| GET | `/actions` | Get valid combat actions |
| POST | `/combat/action` | Execute combat action (skill or consumable) |
| POST | `/combat/preview` | Preview action effects without executing |
| GET | `/combat/enemy-actions` | Get enemy skills, damage, and intentions |
| POST | `/combat/start` | Start combat with any monster group |
| POST | `/exploration/teleport` | Teleport player to coordinates |
| POST | `/exploration/interact` | Trigger interaction with nearby object |
| POST | `/skill-select` | Select skill during level-up |

See [CLAUDE_GUIDE.md](CLAUDE_GUIDE.md) for detailed API documentation.

### Combat Start Examples

**Get available monster groups** (from `/state` response in exploration):
```json
{
  "phase": "EXPLORATION",
  "monsterGroups": [
    {"index": 0, "x": 123.5, "y": 456.0, "z": 0, "defeated": false, "canVoidBlitz": true, "encounterType": "Normal", "monsterCount": 2},
    {"index": 1, "x": 200.0, "y": 300.0, "z": 0, "defeated": false, "canVoidBlitz": true, "encounterType": "Champion", "monsterCount": 1}
  ]
}
```

**Start combat with Void Blitz** (full teleport animation + poise break):
```bash
curl -X POST http://localhost:8080/combat/start \
  -H "Content-Type: application/json" \
  -d '{"monsterGroupIndex": 0, "voidBlitz": true}'
```

**Void Blitz specific monster in group:**
```bash
curl -X POST http://localhost:8080/combat/start \
  -H "Content-Type: application/json" \
  -d '{"monsterGroupIndex": 0, "voidBlitz": true, "monsterIndex": 1}'
```

**Start combat without Void Blitz:**
```bash
curl -X POST http://localhost:8080/combat/start \
  -H "Content-Type: application/json" \
  -d '{"monsterGroupIndex": 0}'
```

## Project Structure

```
AethermancerHarness/
├── Plugin.cs            # BepInEx entry point, Harmony init, F11 debug
├── HarnessServer.cs     # HTTP server and routing
├── StateSerializer.cs   # Game state → JSON/text conversion
├── ActionHandler.cs     # Action execution (combat, teleport, interact, void blitz)
├── VoidBlitzBypass.cs   # Harmony patches for void blitz distance bypass
├── build.sh             # Build and deploy script
├── launch.sh            # Launch game via Steam
├── CLAUDE_GUIDE.md      # AI agent usage guide
└── README.md            # This file
```

## Key Game Entry Points

```csharp
// Execute player combat action
monster.State.StartAction(skillInstance, target, target);

// Read combat state
CombatController.Instance.PlayerMonsters
CombatController.Instance.Enemies
CombatController.Instance.CurrentMonster

// Teleport player
PlayerMovementController.Instance.transform.position = new Vector3(x, y, z);

// Start combat with void blitz (bypasses distance checks via Harmony patches)
// API: POST /combat/start {"monsterGroupIndex": 0, "voidBlitz": true}
var group = ExplorationController.Instance.EncounterGroups[index];
PlayerController.Instance.TryToStartAetherBlitz(targetMonster);  // triggers animation

// Start combat without void blitz
// API: POST /combat/start {"monsterGroupIndex": 0}
group.StartCombat(aetherBlitzed: false, null, ignoreGameState: true);
```

## Void Blitz Implementation

The Void Blitz feature uses Harmony patches to bypass distance checks while preserving the full animation:

1. **`VoidBlitzBypass.IsActive`** - Static flag to enable bypass mode
2. **`CanBeAetherBlitzedPatch`** - Returns true for target group when bypass active
3. **`GetNearestMonsterInRangePatch`** - Returns target monster regardless of distance
4. **`StartVoidBlitzPatch`** - Auto-confirms targeting after entering blitz mode

This allows the API to trigger void blitz on any monster group in the level, regardless of player position, while still playing the full teleport animation and applying the poise break effect.

## Game Paths

- **Game**: `~/.steam/debian-installation/steamapps/common/Aethermancer/`
- **Plugin**: `<game>/BepInEx/plugins/AethermancerHarness.dll`
- **Decompiled source**: `~/gitrepos/Aethermancer_Decompiled/`

## Notes

- Game requires Steam to be running (uses Steamworks)
- Headless mode (`-batchmode -nographics`) doesn't work due to Steam integration
- Plugin reloads require game restart
- F11 in-game dumps state to BepInEx console
- Void Blitz bypasses distance checks but requires exploration mode
