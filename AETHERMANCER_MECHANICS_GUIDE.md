# AETHERMANCER MECHANICS GUIDE (Run Only)

## COMBAT BASICS
- **Turn-based**: Player always goes first. Choose which monster acts in what order.
- All player monsters act, then all enemies act.
- **Artifacts**: Use during your turn, but only before all monsters have acted.

---

## CORE SYSTEMS

### Aether (Resource)
- 5 types: **Fire / Earth / Water / Wind / Wild**
- Wild substitutes for any element
- Generated at turn start + via traits/actions
- Used to pay for Actions

### Elements
- Monsters have 2 elements (or Wild)
- Determines learnable Actions
- **No type weaknesses/resistances**

### Types
- Each monster has 3 Types (e.g., Heal, Power, Sidekick)
- Defines role + available Traits

### Skills
- Learned each level-up
- Two categories: **Traits** (passive) and **Actions** (active, cost Aether)
- **Maverick Skills**: Require matching Type on self + another party member
- **Signature Traits**: Unique to each monster species
- **Aura Traits**: Only one monster in party can have a specific Aura

---

## LEVELING

### EXP Requirements
- Start at Level 1
- Level 2 costs 2 EXP, Level 3 costs 3 EXP, etc.

### Level-Up Rewards
- **+Health**
- **Odd levels** (1→2, 3→4...): Learn an **Action**
- **Even levels** (2→3, 4→5...): Learn a **Trait**

### EXP Sources
| Source | EXP Gained |
|--------|------------|
| Regular monster fight | = Biome Tier |
| Champion monster fight | = Biome Tier + 1 |
| Rare Loot Drop | 1 (chance, random monster) |
| Epic Loot Drop | 2 (chance, random monster) |
| Element Challenge | 1 (chosen monster) |
| Monster Cultivation Fruit | 2 (chosen monster) |
| Myrne/Lily/Merchant | varies |

---

## POISE & STAGGER

### Poise
- Enemies have **Poise** (elemental weakness counter)
- Matching element damage or Wild damage reduces Poise by 1 per hit
- Formula: `floor(Base * (1 + (Level-1) * 0.175)) + Mythic`
  - Mythic adds +1 (normal) or +2 (Champions/Deities)

### Stagger
When Poise = 0, enemy becomes **Staggered**:
- Cancels current/next enemy action
- Triggers player's **On Interrupt** effects
- Enemy takes **+50% damage** from all sources until end of their next turn
- Required for crafting Mementos

### Poise Reset
Enemies reset Poise after their action when:
1. Not staggered by Round 3 (once per fight)
2. Was staggered previous round (every time)

Reset triggers a **Reset Action** based on monster Type:

| Reset Type | Effect |
|------------|--------|
| Aether | +3 Random Aether |
| Burn | +1 Aether, 2 Burn to all enemies |
| Dodge | +1 Aether, 2 Dodge to self |
| Force | +1 Aether, 3 Force to self |
| Heal | +1 Aether, Heal all allies 8 |
| Poison | +1 Aether, 3 Poison to all enemies |
| Power | +1 Aether, 1 Power to all allies |
| Purge | +1 Aether, Steal 1 enemy Aether |
| Regeneration | +1 Aether, 2 Regen to all allies |
| Shield | +1 Aether, Shield all allies 5 |
| Sidekick | +1 Aether, 2 Sidekick to self |
| Summon | Summons Wind Lance minion |
| Terror | +1 Aether, 3 Terror to all enemies |
| Weakness | +1 Aether, 2 Weakness to all enemies |

### Exploration Poise Damage
- **Void Blitz**: -2 Poise on targeted enemy at fight start
- From high ground: -3 Poise instead
- **Void Parry**: Same effect when ambushed

---

## STATUS CONDITIONS

### Buffs
| Status | Effect |
|--------|--------|
| **Age** | No innate effect; triggers Aging traits |
| **Cooking** | No innate effect; used by cooking skills |
| **Dodge** | Evade every 2nd hit of incoming Attack (consumes 1/attack) |
| **Force** | Each hit = Crit (+50% dmg rounded up, +1 Poise dmg), consumes 1/hit |
| **Glory** | +10% Crit Chance, +20% Crit Damage (max 1 stack) |
| **Power** | +1 damage per hit (permanent stacks) |
| **Temp Power** | +1 damage per hit, consumed end of turn after attacking |
| **Redirect** | Next single-target attack on allies hits you instead |
| **Regeneration** | Heal 5 at turn start, consumes 1 stack |
| **Sidekick** | When ally Attacks, you do 1x4 follow-up attack, consumes 1 |

### Debuffs
| Status | Effect |
|--------|--------|
| **Bleed** | Turn start: take 2x stacks Wild dmg, lose half stacks |
| **Burn** | When using Aether for Action or Interrupted: take stacks as Fire dmg |
| **Poison** | Turn start: take stacks as Earth dmg, lose 1 stack |
| **Terror** | First hit of incoming attack: +1 dmg/stack (Wild), lose half stacks (rounded up) |
| **Weakness** | -3 damage per hit dealt, consumes 1/hit |

### Other
| Status | Effect |
|--------|--------|
| **Retaliate** | Counter when attacked or taking non-attack damage. Lasts until your next turn. |

---

## CORRUPTION

### Gaining Corruption
Taking damage in combat adds Corruption based on total damage received:

| Damage Taken | Corruption Gained |
|--------------|-------------------|
| 0-2 | 0 |
| 3-5 | 1 |
| 6-9 | 2 |
| 10-14 | 3 |
| 15-19 | 4 |
| 20+ | 5 |

Lily's potions also add Corruption (Tier 1: 3, Tier 2: 5).

### Effect
- Corruption reduces starting HP in the next fight
- **Cap**: Normal difficulty = 75% max HP, Heroic = max HP - 1

### Cleansing Corruption
| Method | Amount Cleansed |
|--------|-----------------|
| Skills with cleanse (once/combat) | varies |
| Equipment/Perk Cleanse modifier | end of combat |
| Salve Equipment | end of combat |
| Hourglass of Undoing artifact | 5 (all monsters) |
| Refusing Myrne's offer | 5 (all monsters) |
| Rescuing NPC | 2 (all monsters) |
| Blessed Pond | varies (costs Gold) |
| Water Boon (Tier 1 Aether Spring) | 1 (all, per combat) |
| Rest Site Monster Meal | 25 (all monsters) |

---

## BOONS (Party Buffs)

### Aether Spring Boons (Permanent for run)
Tiered progression based on element path chosen:

**Tier 1** (pick 2 elements offered):
- Water: Cleanse 1 Corruption from all monsters after each combat
- Fire: 35% chance equipment from loot drops upgrades tier
- Wind: 25% chance one monster gains +1 EXP after combat
- Earth: +2 Gold after each combat

**Tier 2** (previous element + 1 random):
- Generate 1 [Element] Aether at turn start

**Tier 3** (if same element twice → pick 3a or 3b; if different → pick between their 3a):
- Water 3a: Heal all monsters 4 at turn start
- Water 3b: +1 Water Damage
- Fire 3a: Apply Power to monsters every 3 turns
- Fire 3b: +1 Fire Damage
- Wind 3a: +5% Evasion, +5% Crit Chance
- Wind 3b: +1 Wind Damage
- Earth 3a: Shield all monsters 2 at turn start
- Earth 3b: +1 Earth Damage

### Soul Altar Boons (Lasts 2 fights, one active at a time)
| Type | Effect |
|------|--------|
| Aether | Start combat: +2 Wild Aether |
| Affliction | 15% chance to double-apply debuffs |
| Age | Allies gain Aging, +1 dmg per 3 Age stacks |
| Burn | Start combat: 3 Burn on all enemies |
| Critical | +15% Crit Chance |
| Dodge | Start combat: 1 Dodge on all allies |
| Heal | +1 Healing |
| Poison | Start combat: 3 Poison on all enemies |
| Power | Start combat: 1 Power on all allies |
| Purge | Start combat: Purge 1 enemy Aether |
| Regeneration | Start combat: 3 Regen on all allies |
| Shield | +1 Shielding |
| Sidekick | Start combat: 2 Sidekick on all allies |
| Tank | +10 Max Health |
| Terror | Start combat: 3 Terror on all enemies |
| Weakness | Start combat: 3 Weakness on all enemies |

---

## WORTHINESS

Performance score gained after combat. Determines rewards/progression.

### Categories
| Category | Actions That Count |
|----------|-------------------|
| **Damage** | Direct damage via actions, traits, or applied debuffs |
| **Support** | Helping allies deal damage via buffs/debuffs/traits |
| **Healing** | Healing via actions, traits, or buffs (Regen) |
| **Prevention** | Shields, Dodge, Weakness, Redirect |
| **Disruption** | Stagger, Purge enemy Aether, Kills |
| **Aether Gen** | Generating Aether via actions, traits, or passively |
| **Bonus** | Fast wins, team participation |

### Special Bonuses
- Defeating Champion: +30
- Defeating Deity: +100

### Other Sources
- Myrne can grant 300 Worthiness to one monster (costs 7 Corruption)

---

## ON ACTION EFFECTS

Effects classified as Actions (trigger "On Action" effects):
- Sidekick Attacks
- Retaliate counter-attacks
- Trait-triggered Attacks

### Common On Action Triggers
- Aether Chance: chance to generate Random Aether
- Affliction Chance: chance to apply 2 [debuff] to self and random enemy
- Dodge/Force/Power/Regen/Sidekick Chance: chance to apply buff to self
- Buff Consumption: end of turn chance to consume 1 random buff
- Burn/Corruption/Poison/Terror Chance: chance to apply debuff to self

---

## SKILL SELECTION SYSTEM

Skills offered on level-up are weighted by party composition.

### Weight Calculation
- Base weight: 1.0
- +0.5 if Action has 2 Types and monster has both
- +0.3 per Skill on monster with overlapping Type (+7.5% relative chance)
- +0.075 per Skill on other party members with overlapping Type

**Tip**: Each Skill you pick increases the chance of seeing Skills of that Type in future level-ups. Build into your chosen Types early to see more relevant options later.

Example for Heal/Aether monster:
- Heal Skills: 1.3 weight
- Heal+Aether Traits: 1.6 weight
- Heal+Aether Actions: 2.1 weight

---

## QUICK REFERENCE

### Damage Modifiers
| Source | Modifier |
|--------|----------|
| Staggered target | +50% |
| Critical Hit (Force) | +50% (rounded up) |
| Power (per stack) | +1 per hit |
| Weakness (per stack) | -3 per hit |
| Terror | +stacks on first hit |

### Key Combat Tips
1. Break Poise before Round 3 to prevent Reset Actions
2. Stagger high-threat enemies to cancel dangerous attacks
3. Stack Power for consistent damage; Force for burst
4. Terror amplifies first hit only - pair with big single hits
5. Dodge blocks every 2nd hit - useless vs single-hit attacks
6. Manage Corruption to maintain starting HP
