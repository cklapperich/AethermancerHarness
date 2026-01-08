# Aethermancer Harness - Claude Guide

This harness allows you to control the Aethermancer game via HTTP API for AI agent training.

## Starting the Game

The game must be running for the API to work. Launch via Steam:

```bash
cd ~/gitrepos/AethermancerHarness
./launch.sh
```

Or manually launch Aethermancer through Steam. The HTTP server starts automatically on port 8080.

## Checking if Ready

```bash
curl http://localhost:8080/health
```

Response:
```json
{"status": "ok", "gameReady": true, "inCombat": true, "readyForInput": true, "inputStatus": "Ready"}
```

- `gameReady`: Game systems initialized
- `inCombat`: Currently in a battle
- `readyForInput`: True when the game is ready to accept player actions
- `inputStatus`: Why input is/isn't ready ("Ready", "EnemyTurn:Goblin", "TriggersPending:3", etc.)

## API Endpoints

### GET /state
Returns current game state (combat or exploration).

```bash
curl http://localhost:8080/state
```

For compact text format (better for LLM context):
```bash
curl "http://localhost:8080/state?format=text"
```

### GET /actions
Returns valid actions for the current actor (combat only).

```bash
curl http://localhost:8080/actions
```

### POST /combat/action
Execute a combat action. **Blocks until game is ready for next input**, then returns updated state.

```bash
curl -X POST http://localhost:8080/combat/action \
  -d '{"actorIndex": 0, "skillIndex": 1, "targetIndex": 0}'
```

Parameters:
- `actorIndex`: Which player monster (0-2)
- `skillIndex`: Which skill to use (from /actions or /state)
- `targetIndex`: Target index (enemy 0-N or ally 0-2 depending on skill)

Response includes full game state after action completes:
```json
{
  "success": true,
  "action": "Char",
  "actor": "Cherufe",
  "target": "Warden",
  "waitedForReady": true,
  "state": { /* full combat state - same as GET /state */ }
}
```

The endpoint waits for:
- Action animation to complete
- Any triggered effects to resolve
- Enemy turns to complete (if applicable)
- Game to be ready for next player input

### POST /combat/preview
Preview action effects WITHOUT executing. Same parameters as `/combat/action`.

```bash
curl -X POST http://localhost:8080/combat/preview \
  -d '{"actorIndex": 0, "skillIndex": 1, "targetIndex": 0}'
```

Response:
```json
{
  "success": true,
  "preview": {
    "enemies": [
      {
        "index": 0,
        "name": "Warden",
        "damage": 45,
        "hits": 3,
        "currentHp": 356,
        "previewHp": 311,
        "debuffsApplied": ["Burn x2"],
        "willKill": false,
        "willBreak": true,
        "willPurgeCancel": false,
        "breaksCount": 1,
        "purgeCancelsCount": 0,
        "poise": [
          {"element": "Fire", "before": 15, "after": 12, "max": 17}
        ]
      }
    ],
    "allies": [...],
    "interruptSummary": {
      "kills": 0,
      "breaks": 1,
      "purgeCancels": 0,
      "totalInterrupts": 1
    }
  }
}
```

**Interrupt types** (ways to cancel enemy actions):
- `kill`: Enemy dies, all their actions cancelled
- `break`: Poise broken, enemy staggered, action(s) interrupted
- `purgeCancel`: Consuming aether leaves enemy without enough to use their action

**Per-enemy fields:**
- `willKill`/`willBreak`/`willPurgeCancel`: Boolean flags
- `breaksCount`/`purgeCancelsCount`: Number of actions interrupted by each method
- `poise`: Array showing poise before/after for each element (helps track poise damage)

**Summary across all enemies:**
- `interruptSummary.totalInterrupts`: Total action cancellations from this one action

### GET /combat/enemy-actions
Get all enemy skills, their calculated damage, and current intentions.

```bash
curl http://localhost:8080/combat/enemy-actions
```

Response:
```json
{
  "success": true,
  "enemies": [
    {
      "index": 0,
      "name": "Warden",
      "hp": 356,
      "maxHp": 400,
      "intentions": [
        {
          "skillName": "Crushing Blow",
          "targetName": "Cherufe",
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
          "name": "Basic Attack",
          "description": "1 x 10 damage against a single enemy.",
          "targetType": "SingleEnemy",
          "elements": ["Neutral"],
          "damagePerHit": 10,
          "hitCount": 1,
          "totalDamage": 10,
          "canUse": true
        }
      ]
    }
  ]
}
```

Key fields:
- `intentions`: What the enemy plans to do this turn (target already chosen)
- `skills`: All skills the enemy has available
- `description`: Human-readable skill effect text (same as in-game tooltip)
- `damagePerHit`/`totalDamage`: Calculated damage including enemy's stat modifiers
- `isPurged`: Action cancelled due to aether consumption
- `isStaggered`: Action interrupted by breaking poise

### POST /exploration/teleport
Teleport player to any coordinates (exploration only).

```bash
curl -X POST http://localhost:8080/exploration/teleport \
  -d '{"x": 123.5, "y": 456.0, "z": 0}'
```

- `z`: 0 for ground level, >0 for elevated terrain

### POST /exploration/interact
Trigger interaction with nearby interactable.

```bash
curl -X POST http://localhost:8080/exploration/interact
```

## Combat Flow

1. Execute action: `POST /combat/action` with actor, skill, and target indices
2. The endpoint **blocks** until game is ready, then returns the new state
3. Read the returned `state` to see the updated combat situation
4. Repeat until `state.combatResult` is "VICTORY" or "DEFEAT"

**Simplified**: No need to poll `/state` or `/health` between actions - the action endpoint handles waiting automatically.

**Optional**: Use `POST /combat/preview` to compare different skills/targets before committing.

**Tip**: Any player monster can act during the player phase - you're not limited to a specific turn order.

### Example Combat State (Text)

```
=== COMBAT Round 1 | Turn: Cherufe ===
PLAYER TEAM:
  [0] Cherufe      40/50 HP
  [1] Wolpertinger 44/45 HP  [Dodge x1]
  [2] Dark Elder   37/45 HP  [Age x1]
ENEMIES:
  [0] Warden       356/356 HP Poise[A:15/17]  -> Basic Attack @ Cherufe
AETHER: You[F:2 W:0 E:3 Wi:0 N:0] | Enemy[F:2 W:0 E:1 Wi:0 N:0]
SKILLS:
  [0] Basic Attack     (free) [OK]
  [1] Char             (F:1) [OK]
  [2] Explosion        (F:1 W:1) [X]
  [3] Hellfire Bolt    (F:2 E:1) [OK]
```

## Exploration Flow

1. Get state: `GET /state` shows player position and all interactables
2. Find target: Look at interactable coordinates (springs, monsters, chests, etc.)
3. Teleport: `POST /exploration/teleport` to move near the target
4. Interact: `POST /exploration/interact` to trigger the interaction

### Example Exploration State

```json
{
  "phase": "EXPLORATION",
  "player": {"x": 100.0, "y": 200.0, "z": 0},
  "area": "PilgrimagePath",
  "zone": 0,
  "interactables": [
    {"type": "MONSTER_GROUP", "x": 150.0, "y": 250.0, "z": 0, "defeated": false},
    {"type": "AETHER_SPRING", "x": 300.0, "y": 180.0, "z": 0, "used": false},
    {"type": "CHEST", "x": 400.0, "y": 220.0, "z": 0, "opened": false}
  ]
}
```

## Key Concepts

### Aether System
- Elements: Fire (F), Water (W), Earth (E), Wind (Wi), Neutral (N), Wild (*)
- Skills cost aether to use
- Wild (*) can substitute for any element

### Poise System
- Enemies have poise bars (e.g., `Poise[A:15/17]` = 15/17 Any-element hits)
- Breaking poise staggers the enemy (skips their turn)
- Multi-hit skills are effective for breaking poise

### Target Types
- `SingleEnemy`: Target one enemy
- `AllEnemies`: Hits all enemies
- `SingleAlly`: Target one ally (buffs/heals)
- `AllAllies`: Affects all allies
- `SelfOrOwner`: Self-target only

### Traits
Monsters have traits (passive abilities) exposed via the `/state` endpoint:
- Each monster has a `traits` array with `name`, `description`, `isSignature`, `isAura`
- `isSignature`: True for the monster's unique signature trait
- `isAura`: True for aura traits that affect the whole team
- Traits are available for both player monsters and enemies

