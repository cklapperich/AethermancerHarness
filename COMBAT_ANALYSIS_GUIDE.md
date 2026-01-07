# Aethermancer Combat Analysis Guide

This guide documents the optimal approach for analyzing combat situations and finding the best line of play using parallel subagents.

## Phase 1: Gather All Information (Parallel)

Launch these requests simultaneously to minimize latency:

```bash
# All three in parallel
curl -s "http://localhost:8080/state?format=text"
curl -s http://localhost:8080/actions
curl -s http://localhost:8080/combat/enemy-actions
```

This gives you:
- **State**: Current HP, buffs/debuffs, aether pools, poise status
- **Actions**: Valid skills and targets for current actor
- **Enemy Actions**: Intentions (what enemies will do) and all enemy skills

## Phase 2: Preview All Options (Massively Parallel)

For each ally, preview ALL usable skills against ALL valid targets simultaneously.

### Example: 3 allies with 3-4 skills each = 9-12 parallel preview calls

```bash
# Launch ALL previews in parallel
curl -s -X POST http://localhost:8080/combat/preview -d '{"actorIndex": 0, "skillIndex": 0, "targetIndex": 0}'
curl -s -X POST http://localhost:8080/combat/preview -d '{"actorIndex": 0, "skillIndex": 1, "targetIndex": 0}'
curl -s -X POST http://localhost:8080/combat/preview -d '{"actorIndex": 0, "skillIndex": 2, "targetIndex": 0}'
curl -s -X POST http://localhost:8080/combat/preview -d '{"actorIndex": 1, "skillIndex": 0, "targetIndex": 0}'
curl -s -X POST http://localhost:8080/combat/preview -d '{"actorIndex": 1, "skillIndex": 1, "targetIndex": 0}'
# ... etc for all combinations
```

### Key Preview Fields to Extract

| Field | Meaning |
|-------|---------|
| `damage` | Total damage dealt |
| `hits` | Number of hits (for poise breaking) |
| `debuffsApplied` | Status effects applied |
| `willKill` | Kills the target |
| `willBreak` | Breaks poise (staggers enemy) |
| `willPurgeCancel` | Cancels enemy action via aether denial |
| `interruptSummary.totalInterrupts` | Total enemy actions cancelled |

## Phase 3: Evaluate Strategic Factors

Build a decision matrix considering:

### Offensive Factors
- **Raw Damage**: Total HP reduction
- **Poise Hits**: Progress toward breaking enemy (stagger = skip turn)
- **Interrupts**: Actions that cancel enemy attacks (kills, breaks, purge-cancels)
- **Debuffs**: Terror, Weakness, etc. that amplify future damage

### Defensive Factors
- **Enemy Intentions**: What attacks are coming and at whom
- **Aether Denial**: Can we purge enemy aether to cancel their skills?
- **HP Preservation**: Avoiding damage > dealing slightly more damage
- **Aether Retention**: Preventing enemy purges of our resources

### Resource Factors
- **Aether Cost**: What we spend
- **Aether Generation**: What we gain (basic attacks, rituals)
- **Net Aether**: Cost vs generation balance

## Phase 4: Chain Evaluation with Subagents

Use parallel subagents to evaluate different strategic branches:

### Subagent Strategy Template

```
Subagent 1: "Evaluate max damage chain"
- Prioritize highest damage skills
- Calculate total damage, hits, aether cost
- Note: enemy actions all go through

Subagent 2: "Evaluate interrupt chain"
- Prioritize skills that cancel enemy actions
- Calculate damage prevented + damage dealt
- Note: may sacrifice some offense for defense

Subagent 3: "Evaluate setup chain"
- Prioritize debuff application (Terror, Weakness)
- Calculate immediate damage + future damage amplification
- Note: investment for multi-turn payoff

Subagent 4: "Evaluate poise break chain"
- Prioritize multi-hit skills
- Calculate if poise break is achievable this turn
- Note: breaking poise = free turn
```

### Decision Matrix Example

| Chain | Damage | Hits | Dmg Taken | Interrupts | Setup Value |
|-------|--------|------|-----------|------------|-------------|
| Max Damage | 25 | 6 | 10 | 0 | None |
| Interrupt | 21 | 4 | 4 | 1 | None |
| Setup | 23 | 6 | 4 | 1 | Terror x2 |
| Poise Break | 18 | 12 | 10 | 0 | Stagger |

## Phase 5: Selection Criteria

### Priority Order (generally)

1. **Can we kill an enemy?** → Eliminates all their future actions
2. **Can we break poise?** → Skips their turn entirely
3. **Can we cancel dangerous attacks?** → Purge-cancel or interrupt
4. **Setup vs immediate damage** → Terror/debuffs if fight will last 3+ turns
5. **Resource efficiency** → Preserve aether for future turns

### Red Flags to Avoid

- Taking heavy damage when preventable
- Losing aether to enemy purge when avoidable
- Overkilling (wasting damage on dying enemies)
- Ignoring enemy intentions entirely

## Optimal Parallelization Pattern

### Single Message, Multiple Tool Calls

```
Message 1: [Parallel]
  - Task: Gather state (curl state)
  - Task: Gather actions (curl actions)
  - Task: Gather enemy info (curl enemy-actions)

Message 2: [Parallel] - After reviewing state
  - Task: Preview all Ally 0 skills
  - Task: Preview all Ally 1 skills
  - Task: Preview all Ally 2 skills

Message 3: [Parallel] - Strategic analysis
  - Subagent: Evaluate damage chain
  - Subagent: Evaluate defensive chain
  - Subagent: Evaluate setup chain

Message 4: Synthesize and recommend
```

### Total Latency: 4 round trips (vs 10+ sequential)

## Example Winning Analysis

### Situation
- Enemy Warden planning Echo (6 dmg + purge) and Basic Attack (4 dmg)
- Enemy has Wi:2 aether (Echo needs Wind)
- Our team has Fire Ritual (purges 2 aether)

### Winning Chain: Cancel + Terror Setup

1. **Wolpertinger: Fire Ritual** → Purges Wi:2, cancels Echo
2. **Cherufe: Char** → 8 dmg, applies 2 Terror
3. **Dark Elder: Hellfire Bolt** → 15 dmg + Terror poise bonus

### Why This Wins
- Damage dealt: 23 (vs 25 for pure offense)
- Damage taken: 4 (vs 10 for pure offense)
- Net HP advantage: +4
- Aether preserved (no purge)
- Terror stacks amplify future Hellfire Bolts
- Fire Ritual generates +2 net Fire aether

## Quick Reference: Interrupt Priority

| Method | Effect | When to Use |
|--------|--------|-------------|
| Kill | Cancels ALL enemy actions | Low HP enemies |
| Break | Staggers, cancels current action | Near poise threshold |
| Purge-Cancel | Denies aether for skill | Enemy needs specific element |

## Checklist Before Executing

- [ ] Reviewed all enemy intentions
- [ ] Checked if any enemy actions can be cancelled
- [ ] Compared at least 2-3 different chains
- [ ] Verified aether costs are affordable
- [ ] Confirmed action order matches turn sequence
- [ ] Considered multi-turn implications
