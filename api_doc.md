# AethermancerHarness API Documentation

HTTP API for automating and controlling the Aethermancer game.

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

Get available actions in the current game state.

**Response:**
```json
{
  "actions": [
    {
      "skillIndex": 0,
      "name": "Strike",
      "cost": { "fire": 1, "water": 0, "earth": 0, "wind": 0, "wild": 0 },
      "targets": [{ "index": 0, "name": "Goblin" }],
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

Execute a combat action (skill or consumable).

**Request Body:**
```json
{
  "actorIndex": 0,
  "skillIndex": 0,
  "targetIndex": 0
}
```

**Alternative field names:**
- `actorName` instead of `actorIndex`
- `skillName` instead of `skillIndex`
- `consumableIndex` for consumable items (cannot combine with skill fields)

**Response:**
```json
{
  "success": true,
  "action": "Strike",
  "actor": "Phoenix",
  "target": "Goblin",
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

Preview action effects without executing. Same request format as `/combat/action`.

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

Handle choices across all contexts (dialogue, difficulty, monster selection, equipment, merchant, aether spring, skill selection).

**Request Body:**
```json
{
  "choiceIndex": 0,
  "shift": "normal"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `choiceIndex` | int | Index of choice to select |
| `shift` | string | `normal` or `shifted` (monster selection only) |

The endpoint auto-routes based on current game phase.

**Skill Selection Context:**
- `choiceIndex` 0-2: Select skill at index
- `choiceIndex` 3: Select max health bonus
- `choiceIndex` -1: Reroll skills (if rerolls available)

**Monster Selection Context:**
- `choiceIndex` 0-N: Select monster at index
- `choiceIndex` -1: Reroll monsters (if shrine rerolls available and in normal shrine)

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
  "choiceIndex": 0
}
```

---

## Merchant Endpoints

### Open Merchant Shop

Use `/exploration/interact` to automatically find and open the merchant shop.

### Purchase Item

Use `/choice` endpoint when merchant is open:
- `choiceIndex < stockedItems.Count`: Purchase item at index
- `choiceIndex == stockedItems.Count`: Close shop

Equipment purchases transition to equipment selection phase.

---

## Aether Spring Endpoints

### Open Aether Spring

Use `/exploration/interact` to automatically find and open the aether spring boon selection.

### Select Boon

Use `/choice` endpoint when aether spring is open:
- `choiceIndex`: 0 for left boon, 1 for right boon

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

Common error scenarios:
- Invalid indices (skill, target, actor, choice)
- Wrong game phase for the requested action
- Game not ready for input
- Missing required fields
- Resource unavailable (no rerolls, insufficient scrap, etc.)
