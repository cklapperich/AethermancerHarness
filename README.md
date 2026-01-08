# AethermancerHarness - a ClaudePlaysPokemon alternative!

## What is Aethermancer?

Aethermancer is a monster collecting and fighting game by Moi Rai games. Its kinda like Pokemon but a game only lasts a couple hours. 

## What does this do? 

This fully automates the game and exposes an HTTP API to read game state and interact with the game via HTTP API requests. This is complete enough to finish the game, including starting a run, choosing monsters, equipment, NPCs, merchants. It works via a bepinex mod that hooks into game code.


## Advantages to ClaudePlaysPokemon:

- No vision or pathfinding needed! Teleport to any map location via API.

- headless mode

- Execute and iterate **much** faster and with more reliability, at the trade-off of not testing AI vision or pathfinding capabilities.

- Compared to ClaudePlaysPokemon, which requires each button press to be a separate 'step' from the agent, this is far more automated and focused on the cutting through to the key decision points. Anything that doesnt require a decision is auto-advanced for the Agent. This also doesnt strain context management AS heavily as 1000 A-button presses do.

Not yet implemented: Aether springs, cleansing springs, intractable boxes/plants for gold, and various other non-NPC interactables. Coming soon.


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
| POST | `/exploration/interact` | Interact with nearby objects (shrines, merchants, aether springs, etc.) |
| POST | `/exploration/loot-all` | Break all destructibles + collect loot |
| POST | `/npc/interact` | Start dialogue with NPC (auto-progresses to choices) |
| POST | `/choice` | Universal choice handler (dialogue, equipment, merchant, aether spring, skill selection, monster reroll) |

See [api_doc.md](api_doc.md) for detailed API documentation.

## Notes

- Game requires Steam to be running (uses Steamworks)
- Headless mode (`-batchmode -nographics`) doesn't work due to Steam integration
- Plugin reloads require game restart
- Void Blitz bypasses distance checks but requires exploration mode