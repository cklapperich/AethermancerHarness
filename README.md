# AethermancerHarness

A BepInEx plugin that exposes an HTTP API for AI agent training in the Aethermancer game.

## Important: API Conventions

**All API responses use PascalCase field names** (e.g., `Phase`, `Round`, `Allies`, `MaxHp`).

The only exception is `/health` which uses camelCase for legacy compatibility.

**Empty POST bodies are supported** - endpoints like `/exploration/interact` don't require a request body.

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
- [x] auto loot all 
- [x] aether springs

**Not Yet Implemented:**
- [ ] Speed optimization (`Time.timeScale` adjustments)
- [ ] Headless mode (Steam integration issues - requires visible window)
- [ ] Automated testing
- [ ] Turn-by-turn combat logging, serialized to text.

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
| POST | `/choice` | Universal choice handler (dialogue, equipment, merchant, aether spring) |
| POST | `/merchant/interact` | Open merchant shop (auto-teleports) |
| POST | `/aether-spring/interact` | Open aether spring boon selection |

See [api_doc.md](api_doc.md) for detailed API documentation.

### Unified Choice Pattern

The `/choice` endpoint handles all selection-based interactions. The endpoint auto-detects the current game phase and routes accordingly:

| Phase | Choice Types | Example |
|-------|--------------|---------|
| `Dialogue` | Dialogue options, artifact/equipment picks | `{"choiceIndex": 0}` |
| `EquipmentSelection` | Assign to monster or scrap | `{"choiceIndex": 2}` for 3rd monster, last index for scrap |
| `Merchant` | Buy item or leave shop | Item indices to buy, last index to leave |
| `MonsterSelection` | Pick monster at shrine | `{"choiceIndex": 0, "shift": "normal"}` |
| `DifficultySelection` | Select run difficulty | `{"choiceIndex": 0}` for Normal |
| `AetherSpring` | Select boon | `{"choiceIndex": 0}` for left, `1` for right |

All phases return a `Choices` array in their `/state` response. The last choice is often a special action (scrap, leave shop, etc.).

**Important for Monster Selection:** Always parse the `Choices` array to find the monster by `Name`, don't assume indices are stable between sessions.

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
  "Index": 0,
  "Name": "Fireball",
  "Description": "Deal 50 Fire damage to an enemy.",
  "Cost": {"Fire": 2, "Water": 0, "Earth": 0, "Wind": 0, "Wild": 0},
  "CanUse": true,
  "Category": "Attack",
  "SubTypes": ["Damaging", "Debuff"]
}
```

### Combat Start Examples

**Get available monster groups** (from `/state` response in exploration):
```json
{
  "Phase": "Exploration",
  "MonsterGroups": [
    {"Index": 0, "X": 123.5, "Y": 456.0, "Z": 0, "Defeated": false, "CanVoidBlitz": true, "EncounterType": "Normal", "MonsterCount": 2},
    {"Index": 1, "X": 200.0, "Y": 300.0, "Z": 0, "Defeated": false, "CanVoidBlitz": true, "EncounterType": "Champion", "MonsterCount": 1}
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
  "Phase": "Exploration",
  "Interactables": [
    {"Type": "Npc", "Index": 0, "Name": "Alioth", "X": 250.0, "Y": 200.0, "Z": 0, "Talked": false, "HasEvent": true},
    {"Type": "Npc", "Index": 1, "Name": "Lily", "X": 300.0, "Y": 150.0, "Z": 0, "Talked": true, "HasEvent": false}
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
  "Phase": "Dialogue",
  "Npc": "Alioth",
  "DialogueText": "Which artifact interests you?",
  "IsChoiceEvent": true,
  "Choices": [
    {"Index": 0, "Text": "Purging Harp", "Type": "Artifact"},
    {"Index": 1, "Text": "Shielding Dust", "Type": "Artifact"}
  ],
  "CanGoBack": false
}
```

**Response with equipment choices** (e.g., Tiberion the Knight):
```json
{
  "Phase": "Dialogue",
  "Npc": "Tiberion",
  "DialogueText": "Choose your equipment:",
  "IsChoiceEvent": true,
  "Choices": [
    {
      "Index": 0,
      "Text": "Iron Sword",
      "Type": "Equipment",
      "Equipment": {
        "Name": "Iron Sword - Rare",
        "EquipmentType": "Weapon",
        "Rarity": "Rare",
        "IsAura": false,
        "BaseDescription": "Deal 10% more damage.",
        "Affixes": [
          {
            "Name": "Crit Chance",
            "ShortDescription": "Crit Chance +15%",
            "Description": "Increases critical hit chance by 15%.",
            "IsRare": false,
            "PerkType": "Common"
          },
          {
            "Name": "Life Steal",
            "ShortDescription": "Life Steal +8%",
            "Description": "Heal for 8% of damage dealt.",
            "IsRare": true,
            "PerkType": "Rare"
          }
        ]
      }
    },
    {
      "Index": 1,
      "Text": "Steel Ring",
      "Type": "Equipment",
      "Equipment": {
        "Name": "Steel Ring - Common",
        "EquipmentType": "Accessory",
        "Rarity": "Common",
        "IsAura": false,
        "BaseDescription": "Gain 5 shield at start of combat.",
        "Affixes": []
      }
    }
  ],
  "CanGoBack": false
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
  "Phase": "EquipmentSelection",
  "HeldEquipment": {
    "Name": "Iron Sword - Rare",
    "EquipmentType": "Weapon",
    "Rarity": "Rare",
    "BaseDescription": "Deal 10% more damage.",
    "Affixes": [...]
  },
  "ScrapValue": 8,
  "Choices": [
    {"Index": 0, "Type": "Monster", "Name": "Fenrir", "CurrentEquipment": null, "WillTrade": false},
    {"Index": 1, "Type": "Monster", "Name": "Drake", "CurrentEquipment": {"Name": "Old Dagger", ...}, "WillTrade": true},
    {"Index": 2, "Type": "Monster", "Name": "Phoenix", "CurrentEquipment": null, "WillTrade": false},
    {"Index": 3, "Type": "Scrap", "GoldValue": 8}
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
When assigning to a monster that has equipment, the response shows `Phase: EquipmentSelection` again with the traded equipment as `HeldEquipment`.

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

**Response** (Phase: Merchant):
```json
{
  "Phase": "Merchant",
  "Gold": 150,
  "Choices": [
    {"Index": 0, "Name": "Iron Ring", "Type": "Equipment", "Rarity": "Common", "Price": 45, "IsDiscounted": false, "CanAfford": true},
    {"Index": 1, "Name": "Monster Soul", "Type": "MonsterSoul", "Price": 20, "IsDiscounted": true, "CanAfford": true},
    {"Index": 2, "Name": "Health Potion", "Type": "Consumable", "Price": 15, "CanAfford": true},
    {"Index": 3, "Type": "Close", "Name": "Leave Shop"}
  ]
}
```

**Buy an item** (use `/choice` with item index):
```bash
curl -X POST http://localhost:8080/choice \
  -H "Content-Type: application/json" \
  -d '{"choiceIndex": 1}'
```

Note: When buying equipment, the game enters `EquipmentSelection` phase. Use `/choice` to assign to a monster or scrap.

**Close shop** (use `/choice` with "Leave Shop" index):
```bash
curl -X POST http://localhost:8080/choice \
  -H "Content-Type: application/json" \
  -d '{"choiceIndex": 3}'
```

## Project Structure

```
AethermancerHarness/
├── src/
│   ├── Plugin.cs            # BepInEx entry point, Harmony init, F11 debug
│   ├── HarnessServer.cs     # HTTP server and routing
│   ├── StateSerializer.cs   # Game state → JSON conversion
│   ├── StateModels.cs       # Response data models
│   ├── Enums.cs             # Game phase and type enums
│   ├── ActionHandler.cs     # Core action execution
│   ├── CombatActions.cs     # Combat-specific actions
│   ├── ExplorationActions.cs # Exploration actions (teleport, interact, loot)
│   ├── MenuActions.cs       # Menu/dialogue/merchant actions
│   └── VoidBlitzBypass.cs   # Harmony patches for void blitz distance bypass
├── build.sh                 # Build and deploy script
├── launch.sh                # Launch game via Steam
├── api_doc.md               # Detailed API documentation
├── CLAUDE_GUIDE.md          # AI agent usage guide
└── README.md                # This file
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
