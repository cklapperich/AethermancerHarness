# AethermancerHarness API Documentation

HTTP API for automating and controlling the Aethermancer game.

## API Design

This API uses names instead of numeric indices. Use the names you see in state responses for all combat actions and choices.

- Case-insensitive matching
- Names are unique (numbered when needed: `"Goblin 1"`, `"Goblin 2"`)

## Server Configuration

| Setting | Value |
|---------|-------|
| Port | 8080 (configurable) |
| Binding | localhost only |
| Response Format | JSON |
| CORS | Enabled (`Access-Control-Allow-Origin: *`) |

---

## Endpoints

### Health Check

**`GET /health`**

Check game and server readiness status.

**Response:**
```json
{
  "status": "ok",
  "gameReady": true,
  "inCombat": false,
  "readyForInput": true,
  "inputStatus": "Ready"
}
```

**inputStatus values:** `NotInCombat`, `NoCombatController`, `NoCurrentMonster`, `EnemyTurn`, `TriggersPending`, `ActionExecuting`, `Ready`, `CombatEnded`

---

### Game State

**`GET /state`**

Get complete game state. Response structure varies by current game phase.

**Query Parameters:**
| Parameter | Type | Description |
|-----------|------|-------------|
| `format` | string | Only `json` supported (default) |

**Response:** Phase-specific state object (see [State Models](#state-models))

---

### Valid Actions

**`GET /actions`**

Get available actions and targets for use in `/combat/action` requests.

**Response:**
```json
{
  "actions": [
    {
      "skillIndex": 0,
      "name": "Strike",
      "cost": { "fire": 1, "water": 0, "earth": 0, "wind": 0, "wild": 0 },
      "targets": [{ "index": 0, "name": "Goblin 1" }, { "index": 1, "name": "Goblin 2" }],
      "category": "Attack",
      "subTypes": ["Physical"]
    }
  ],
  "error": null,
  "waitingFor": null
}
```

---

## Combat Endpoints

### Execute Action

**`POST /combat/action`**

Execute a combat action using semantic names.

**Request Body (Skills):**
```json
{
  "actorName": "Wolpertinger",
  "skillName": "Fireball",
  "targetName": "Goblin 2"
}
```

**Request Body (Consumables):**
```json
{
  "consumableIndex": 0,
  "targetIndex": 0
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `actorName` | string | Yes* | Ally name from state |
| `skillName` | string | Yes* | Skill name from state |
| `targetName` | string | Yes* | Target name from state |
| `consumableIndex` | int | No | Consumable inventory index |
| `targetIndex` | int | No | Target index for consumables |

*Required for skill actions. Use `consumableIndex` + `targetIndex` for consumables.

**Response:**
```json
{
  "success": true,
  "action": "Fireball",
  "actor": "Wolpertinger",
  "target": "Goblin 2",
  "waitedForReady": false,
  "condensed": true,
  "roundChanged": false,
  "state": { ... },
  "combatResult": null
}
```

**combatResult:** `Victory`, `Defeat`, or `null` if combat ongoing

---

### Preview Action

**`POST /combat/preview`**

Preview action effects without executing.

**Request Body:** Same format as `/combat/action`
```json
{
  "actorName": "Wolpertinger",
  "skillName": "Fireball",
  "targetName": "Goblin 2"
}
```

**Response:**
```json
{
  "success": true,
  "preview": {
    "enemies": [
      {
        "index": 0,
        "name": "Goblin",
        "damage": 45,
        "heal": 0,
        "shield": 0,
        "hits": 3,
        "hasCrit": true,
        "currentHp": 100,
        "previewHp": 55,
        "buffsApplied": [],
        "debuffsApplied": ["Burn"],
        "willKill": false,
        "willBreak": true,
        "willPurgeCancel": false,
        "breaksCount": 1,
        "purgeCancelsCount": 0,
        "poise": [
          { "element": "Fire", "before": 3, "after": 0, "max": 3 }
        ]
      }
    ],
    "allies": [],
    "interruptSummary": {
      "kills": 0,
      "breaks": 1,
      "purgeCancels": 0,
      "totalInterrupts": 1
    }
  }
}
```

---

### Enemy Actions

**`GET /combat/enemy-actions`**

Get enemy intentions and available skills.

**Response:**
```json
{
  "success": true,
  "enemies": [
    {
      "index": 0,
      "name": "Goblin",
      "hp": 100,
      "maxHp": 100,
      "intentions": [
        {
          "skillName": "Slash",
          "targetName": "Phoenix",
          "targetIndex": 0,
          "isPurged": false,
          "isStaggered": false,
          "damagePerHit": 15,
          "hitCount": 2,
          "totalDamage": 30
        }
      ],
      "skills": [
        {
          "index": 0,
          "name": "Slash",
          "description": "A quick slash attack",
          "targetType": "Single",
          "elements": ["Physical"],
          "canUse": true,
          "damagePerHit": 15,
          "hitCount": 2,
          "totalDamage": 30
        }
      ]
    }
  ]
}
```

---

### Start Combat

**`POST /combat/start`**

Initiate combat with a monster group. Optionally use void blitz to target a specific monster.

**Request Body:**
```json
{
  "monsterGroupIndex": 0,
  "monsterIndex": 0,
  "voidBlitz": true
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `monsterGroupIndex` | int | Yes | Index of monster group on map |
| `monsterIndex` | int | No | Target monster for void blitz |
| `voidBlitz` | bool | No | Enable void blitz attack (default: false) |

**Response:**
```json
{
  "success": true,
  "action": "void_blitz",
  "monsterGroupIndex": 0,
  "targetMonster": "Elite Goblin",
  "note": "Void blitz initiated"
}
```

---

## Exploration Endpoints

### Teleport

**`POST /exploration/teleport`**

Teleport player to coordinates.

**Request Body:**
```json
{
  "x": 10.5,
  "y": 0.0,
  "z": -5.2
}
```

**Response:**
```json
{
  "success": true,
  "from": { "x": 0.0, "y": 0.0, "z": 0.0 },
  "to": { "x": 10.5, "y": 0.0, "z": -5.2 }
}
```

---

### Interact

**`POST /exploration/interact`**

Interact with nearby interactables. Automatically detects and handles:
- Monster shrines
- Merchants (opens shop)
- Aether springs (opens boon selection)
- Generic interactables (chests, etc.)

**Request Body:** Empty

**Response Examples:**

Shrine interaction:
```json
{
  "success": true,
  "action": "shrine_interact",
  "type": "MonsterShrine"
}
```

Merchant interaction:
```json
{
  "success": true,
  "action": "merchant_interact",
  "type": "Merchant"
}
```

Aether spring interaction:
```json
{
  "success": true,
  "action": "aether_spring_interact",
  "type": "AetherSpring"
}
```

---

### Loot All

**`POST /exploration/loot-all`**

Break all destructibles and collect loot on the current map.

**Request Body:** Empty

**Response:**
```json
{
  "success": true,
  "action": "loot_all",
  "brokenCount": 12
}
```

---

## Selection Endpoints

### Universal Choice Handler

**`POST /choice`**

Handle choices across all contexts using semantic names (dialogue, difficulty, monster selection, equipment, merchant, aether spring, skill selection).

**Request Body:**
```json
{
  "choiceName": "Fireball",
  "shift": "normal"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `choiceName` | string | Name of choice to select (case-insensitive) |
| `shift` | string | `normal` or `shifted` (monster selection only) |

The endpoint auto-routes based on current game phase.

**Example names:**
- Skills: `"Fireball"`, `"Max Health"`
- Monsters: `"Wolpertinger"`, `"Random Monster"`
- Merchant: `"Health Potion 2"`, `"Leave Shop"`
- Dialogue: `"Tell me more"`
- Equipment: `"Blazing Sword"`, `"Scrap Equipment"`

---

## NPC Endpoints

### NPC Interact

**`POST /npc/interact`**

Start dialogue with an NPC.

**Request Body:**
```json
{
  "npcIndex": 0
}
```

**Response:** Dialogue state or transitions to next phase

---

### Dialogue Choice

**`POST /npc/dialogue-choice`**

Select a dialogue option. (Alias for `/choice` in dialogue context)

**Request Body:**
```json
{
  "choiceName": "Show me your wares"
}
```

---

## Merchant Endpoints

### Open Merchant Shop

Use `/exploration/interact` to automatically find and open the merchant shop.

### Purchase Item

Use `/choice` endpoint when merchant is open with the item name:

```json
{
  "choiceName": "Health Potion 2"
}
```

Or to close the shop:
```json
{
  "choiceName": "Leave Shop"
}
```

Equipment purchases transition to equipment selection phase.

---

## Aether Spring Endpoints

### Open Aether Spring

Use `/exploration/interact` to automatically find and open the aether spring boon selection.

### Select Boon

Use `/choice` endpoint when aether spring is open with the boon name:

```json
{
  "choiceName": "Fire Attunement"
}
```

---

## State Models

### Base State Fields

All states include:
```json
{
  "phase": "Combat",
  "currentFloor": 1,
  "currentRegion": "Forest",
  "scrap": 500,
  "monsters": [ ... ],
  "consumables": [ ... ],
  "artifacts": [ ... ]
}
```

### Combat State

```json
{
  "phase": "Combat",
  "round": 3,
  "aetherPool": { "fire": 2, "water": 1, "earth": 0, "wind": 3, "wild": 1 },
  "allies": [ CombatMonster ],
  "enemies": [ CombatMonster ],
  "turnOrder": ["Phoenix", "Goblin", "Drake"],
  "currentTurn": "Phoenix",
  "combatLog": ["Phoenix used Strike on Goblin"]
}
```

### Exploration State

```json
{
  "phase": "Exploration",
  "playerPosition": { "x": 0.0, "y": 0.0, "z": 0.0 },
  "interactables": [ InteractableInfo ],
  "monsterGroups": [ MonsterGroupInfo ]
}
```

### Dialogue State

```json
{
  "phase": "Dialogue",
  "npcName": "Merchant Bob",
  "currentText": "Welcome, traveler!",
  "choices": [
    { "index": 0, "text": "Show me your wares", "type": "Merchant" },
    { "index": 1, "text": "Goodbye", "type": "Close" }
  ]
}
```

### Merchant State

```json
{
  "phase": "Merchant",
  "merchantName": "Traveling Merchant",
  "stockedItems": [
    {
      "index": 0,
      "name": "Health Potion",
      "cost": 100,
      "type": "Consumable",
      "description": "Restores 50 HP"
    }
  ],
  "playerScrap": 500
}
```

### Aether Spring State

```json
{
  "phase": "AetherSpring",
  "leftBoon": {
    "name": "Fire Attunement",
    "description": "+1 Fire aether per turn"
  },
  "rightBoon": {
    "name": "Water Attunement",
    "description": "+1 Water aether per turn"
  }
}
```

---

## Data Types

### AetherValues

```json
{
  "fire": 0,
  "water": 0,
  "earth": 0,
  "wind": 0,
  "wild": 0
}
```

### CombatMonster

```json
{
  "index": 0,
  "name": "Phoenix",
  "hp": 100,
  "maxHp": 150,
  "shield": 25,
  "corruption": 0,
  "isDead": false,
  "staggered": false,
  "buffs": [{ "name": "Strength", "stacks": 2 }],
  "debuffs": [{ "name": "Burn", "stacks": 3 }],
  "traits": [{ "name": "Fireborn", "description": "Immune to burn" }],
  "skills": [ SkillInfo ],
  "poise": [{ "element": "Fire", "current": 3, "max": 3 }],
  "intendedAction": { "skill": "Slash", "target": "Phoenix" }
}
```

### SkillInfo

```json
{
  "index": 0,
  "name": "Fireball",
  "description": "Launch a ball of fire",
  "cost": { "fire": 2, "water": 0, "earth": 0, "wind": 0, "wild": 0 },
  "canUse": true,
  "category": "Attack",
  "subTypes": ["Fire", "Magic"]
}
```

### ConsumableInfo

```json
{
  "index": 0,
  "name": "Health Potion",
  "description": "Restores 50 HP",
  "currentCharges": 2,
  "maxCharges": 3,
  "canUse": true,
  "targetType": "SingleAlly"
}
```

### InteractableInfo

```json
{
  "type": "Chest",
  "x": 10.5,
  "y": 0.0,
  "z": -5.2,
  "index": 0,
  "name": "Treasure Chest",
  "used": false,
  "defeated": false,
  "opened": false,
  "talked": false,
  "hasEvent": false,
  "completed": false,
  "found": true
}
```

### MonsterGroupInfo

```json
{
  "index": 0,
  "x": 15.0,
  "y": 0.0,
  "z": 8.0,
  "defeated": false,
  "canVoidBlitz": true,
  "encounterType": "Normal",
  "monsterCount": 3
}
```

### EquipmentInfo

```json
{
  "name": "Blazing Sword",
  "equipmentType": "Weapon",
  "rarity": "Rare",
  "isAura": false,
  "baseDescription": "A sword wreathed in flames",
  "affixes": [
    { "name": "Burning", "description": "+10% fire damage" }
  ]
}
```

---

## Enums

### GamePhase

| Value | Description |
|-------|-------------|
| `DifficultySelection` | Selecting run difficulty |
| `MonsterSelection` | Selecting monster for party |
| `EquipmentSelection` | Assigning equipment to monster |
| `SkillSelection` | Post-combat skill/bonus selection |
| `Combat` | Active combat |
| `Exploration` | Exploring the map |
| `Dialogue` | NPC dialogue |
| `Merchant` | Shopping at merchant |
| `AetherSpring` | Selecting aether spring boon |
| `EndOfRun` | Run completion screen |
| `Timeout` | Error/timeout state |

### InteractableType

| Value | Description |
|-------|-------------|
| `AetherSpring` | Aether spring for boon selection |
| `MonsterGroup` | Enemy encounter |
| `Chest` | Loot chest |
| `Merchant` | Shop NPC |
| `MonsterShrine` | Monster recruitment shrine |
| `Npc` | Non-combat NPC |
| `Event` | Story/random event |
| `SecretRoom` | Hidden area |
| `StartRun` | Run start point |

### InputReadyStatus

| Value | Description |
|-------|-------------|
| `NotInCombat` | Not currently in combat |
| `NoCombatController` | Combat controller unavailable |
| `NoCurrentMonster` | No active monster |
| `EnemyTurn` | Enemy is acting |
| `TriggersPending` | Triggers resolving |
| `ActionExecuting` | Action in progress |
| `Ready` | Ready for player input |
| `CombatEnded` | Combat has ended |

### CombatResult

| Value | Description |
|-------|-------------|
| `Victory` | Player won |
| `Defeat` | Player lost |
| `Unknown` | Result undetermined |

### ChoiceType

| Value | Description |
|-------|-------------|
| `Random` | Random selection |
| `Monster` | Monster-related choice |
| `Scrap` | Currency reward |
| `Close` | Close/exit option |
| `Artifact` | Artifact selection |
| `Consumable` | Consumable item |
| `Skill` | Skill selection |
| `Trait` | Trait selection |
| `Perk` | Perk selection |
| `Equipment` | Equipment selection |
| `Unknown` | Unclassified |

### ShrineType

| Value | Description |
|-------|-------------|
| `Starter` | Initial starter monster selection |
| `RunStart` | Run start monster selection |
| `Normal` | Standard shrine encounter |

---

## Error Handling

All endpoints return errors in this format:

```json
{
  "success": false,
  "error": "Description of what went wrong"
}
```

Some endpoints include additional context:

```json
{
  "success": false,
  "error": "Invalid skill index",
  "details": {
    "requested": 5,
    "available": [0, 1, 2]
  }
}
```

Common errors:
- Name not found
- Wrong game phase
- Game not ready for input
- Missing required fields
- Insufficient resources
- Skill cannot be used
