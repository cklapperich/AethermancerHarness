# AethermancerHarness

A BepInEx plugin that exposes an HTTP API for AI agent training in the Aethermancer game.

## Status: Working

**Implemented:**
- [x] Combat state reading (JSON)
- [x] Combat action execution (skills + consumables)
- [x] Combat preview (simulate actions without executing)
- [x] Exploration state reading (player position, interactables, monster groups)
- [x] Teleportation to arbitrary coordinates
- [x] Interaction triggering
- [x] Programmatic combat start with any monster group
- [x] Void Blitz automation (full animation, bypasses distance checks)
- [x] Post-combat skill selection
- [x] Equipment selection (assign to monster or scrap for gold)

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
| GET | `/state` | Get current game state (exploration, combat, dialogue, merchant, or skill selection) |
| GET | `/actions` | Get valid combat actions |
| POST | `/combat/action` | Execute combat action (skill or consumable) |
| POST | `/combat/preview` | Preview action effects without executing |
| GET | `/combat/enemy-actions` | Get enemy skills, damage, and intentions |
| POST | `/combat/start` | Start combat with any monster group |
| POST | `/exploration/teleport` | Teleport player to coordinates |
| POST | `/exploration/interact` | Trigger interaction with nearby object |
| POST | `/exploration/loot-all` | Break all destructibles + collect loot |
| POST | `/skill-select` | Select skill during level-up |
| POST | `/npc/interact` | Start dialogue with NPC (auto-progresses to choices) |
| POST | `/choice` | Universal choice handler (dialogue, equipment, merchant) |
| POST | `/merchant/interact` | Open merchant shop (auto-teleports) |

See [CLAUDE_GUIDE.md](CLAUDE_GUIDE.md) for detailed API documentation.

### Unified Choice Pattern

The `/choice` endpoint handles all selection-based interactions. The endpoint auto-detects the current game phase and routes accordingly:

| Phase | Choice Types | Example |
|-------|--------------|---------|
| `DIALOGUE` | Dialogue options, artifact/equipment picks | `{"choiceIndex": 0}` |
| `EQUIPMENT_SELECTION` | Assign to monster or scrap | `{"choiceIndex": 2}` for 3rd monster, last index for scrap |
| `MERCHANT` | Buy item or leave shop | Item indices to buy, last index to leave |
| `MONSTER_SELECTION` | Pick monster at shrine | `{"choiceIndex": 0}` |
| `DIFFICULTY_SELECTION` | Select run difficulty | `{"choiceIndex": 1}` |

All phases return a `choices` array in their `/state` response. The last choice is often a special action (scrap, leave shop, etc.).

### Action Categories

All actions/skills include a `category` and `subTypes` array to help classify them:

**Categories:**
- `attack` - Actions that deal damage to enemies
- `support` - Non-damaging free actions (don't end your turn)
- `dedicated_support` - Non-damaging actions that end your turn

**Sub-types:**
- `damaging` - Deals damage
- `healing` - Heals allies
- `shielding` - Grants shield
- `buff` - Applies buffs
- `debuff` - Applies debuffs
- `summon` - Summons creatures

**Example action:**
```json
{
  "index": 0,
  "name": "Fireball",
  "description": "Deal 50 Fire damage to an enemy.",
  "cost": {"fire": 2},
  "canUse": true,
  "category": "attack",
  "subTypes": ["damaging", "debuff"]
}
```

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

### NPC Dialogue Examples

**Get NPCs from `/state`** (in exploration phase):
```json
{
  "phase": "EXPLORATION",
  "interactables": [
    {"type": "NPC", "index": 0, "name": "Alioth", "x": 250.0, "y": 200.0, "z": 0, "talked": false, "hasEvent": true},
    {"type": "NPC", "index": 1, "name": "Lily", "x": 300.0, "y": 150.0, "z": 0, "talked": true, "hasEvent": false}
  ]
}
```

**Start dialogue with NPC** (auto-teleports and auto-progresses):
```bash
curl -X POST http://localhost:8080/npc/interact \
  -H "Content-Type: application/json" \
  -d '{"npcIndex": 0}'
```

**Response with choices** (blocks until meaningful choice or dialogue ends):
```json
{
  "phase": "DIALOGUE",
  "npc": "Alioth",
  "dialogueText": "Which artifact interests you?",
  "isChoiceEvent": true,
  "choices": [
    {"index": 0, "text": "Purging Harp", "type": "artifact"},
    {"index": 1, "text": "Shielding Dust", "type": "artifact"}
  ],
  "canGoBack": false
}
```

**Response with equipment choices** (e.g., Tiberion the Knight):
```json
{
  "phase": "DIALOGUE",
  "npc": "Tiberion",
  "dialogueText": "Choose your equipment:",
  "isChoiceEvent": true,
  "choices": [
    {
      "index": 0,
      "text": "Iron Sword",
      "type": "equipment",
      "equipment": {
        "name": "Iron Sword - Rare",
        "equipmentType": "Weapon",
        "rarity": "Rare",
        "isAura": false,
        "baseDescription": "Deal 10% more damage.",
        "affixes": [
          {
            "name": "Crit Chance",
            "shortDescription": "Crit Chance +15%",
            "description": "Increases critical hit chance by 15%.",
            "isRare": false,
            "perkType": "Common"
          },
          {
            "name": "Life Steal",
            "shortDescription": "Life Steal +8%",
            "description": "Heal for 8% of damage dealt.",
            "isRare": true,
            "perkType": "Rare"
          }
        ]
      }
    },
    {
      "index": 1,
      "text": "Steel Ring",
      "type": "equipment",
      "equipment": {
        "name": "Steel Ring - Common",
        "equipmentType": "Accessory",
        "rarity": "Common",
        "isAura": false,
        "baseDescription": "Gain 5 shield at start of combat.",
        "affixes": []
      }
    }
  ],
  "canGoBack": false
}
```

**Select a dialogue choice:**
```bash
curl -X POST http://localhost:8080/choice \
  -H "Content-Type: application/json" \
  -d '{"choiceIndex": 0}'
```

### Equipment Selection Examples

After selecting equipment from dialogue (e.g., Tiberion) or loot, the game enters equipment selection mode. The `/state` endpoint returns:

```json
{
  "phase": "EQUIPMENT_SELECTION",
  "heldEquipment": {
    "name": "Iron Sword - Rare",
    "equipmentType": "Weapon",
    "rarity": "Rare",
    "baseDescription": "Deal 10% more damage.",
    "affixes": [...]
  },
  "scrapValue": 8,
  "choices": [
    {"index": 0, "type": "monster", "name": "Fenrir", "currentEquipment": null, "willTrade": false},
    {"index": 1, "type": "monster", "name": "Drake", "currentEquipment": {"name": "Old Dagger", ...}, "willTrade": true},
    {"index": 2, "type": "monster", "name": "Phoenix", "currentEquipment": null, "willTrade": false},
    {"index": 3, "type": "scrap", "goldValue": 8}
  ]
}
```

**Assign equipment to a monster:**
```bash
curl -X POST http://localhost:8080/choice \
  -H "Content-Type: application/json" \
  -d '{"choiceIndex": 0}'
```

**Trade equipment** (if monster already has equipment):
When assigning to a monster that has equipment, the response shows `phase: EQUIPMENT_SELECTION` again with the traded equipment as `heldEquipment`.

**Scrap equipment for gold:**
```bash
curl -X POST http://localhost:8080/choice \
  -H "Content-Type: application/json" \
  -d '{"choiceIndex": 3}'
```

### Merchant Examples

**Open merchant shop:**
```bash
curl -X POST http://localhost:8080/merchant/interact
```

**Response** (phase: MERCHANT):
```json
{
  "phase": "MERCHANT",
  "gold": 150,
  "choices": [
    {"index": 0, "name": "Iron Ring", "type": "equipment", "rarity": "Common", "price": 45, "isDiscounted": false, "canAfford": true},
    {"index": 1, "name": "Monster Soul", "type": "monstersoul", "price": 20, "isDiscounted": true, "canAfford": true},
    {"index": 2, "name": "Health Potion", "type": "consumable", "price": 15, "canAfford": true},
    {"index": 3, "type": "close", "name": "Leave Shop"}
  ]
}
```

**Buy an item** (use `/choice` with item index):
```bash
curl -X POST http://localhost:8080/choice \
  -H "Content-Type: application/json" \
  -d '{"choiceIndex": 1}'
```

Note: When buying equipment, the game enters `EQUIPMENT_SELECTION` phase. Use `/choice` to assign to a monster or scrap.

**Close shop** (use `/choice` with "Leave Shop" index):
```bash
curl -X POST http://localhost:8080/choice \
  -H "Content-Type: application/json" \
  -d '{"choiceIndex": 3}'
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
