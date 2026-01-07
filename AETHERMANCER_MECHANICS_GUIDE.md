# AETHERMANCER MECHANICS GUIDE (Run Only)

## COMBAT BASICS
- **Turn-based**: Player always goes first. Choose which monster acts in what order.
- All player monsters act, then all enemies act.
- **Artifacts**: Use during your turn, but only before all monsters have acted.

---

## CORE SYSTEMS

### Aether (Resource)

Aether is a key resource in combat and is strongly tied to the Elements. Each Action, except for a monster's Basic Attack, requires Aether of certain Elements and a specific amount before it can be cast. An Action's Aether cost determines its Elemental classification. For instance, an Action that costs Water and Wind Aether is considered a Water and Wind Action.

There is a type of Aether for each of the four Elements, as well as a special type called **Wild Aether**, which can substitute for any of the four when there is not enough of the required Aether for an Action. The player's Aether supply is displayed at the top of the screen during combat in the order of **Fire / Earth / Water / Wind / Wild**.

**Aether Generation:**
- At the start of each round, each monster on both sides generates Aether semi-randomly, prioritizing Aether that it lacks for its Actions
- This generation is boosted by a monster's Aether perk
- The Aethermancer also generates one random Aether each turn
- A monster can use its Basic Attack to generate extra Aether based on its Elements
- Some Actions and Traits can generate random Aether (not boosted by Aether perk)
- Aether resets at the end of every combat

**Purge and Steal:**
- **Purge** is an effect from certain Actions and Traits that destroys enemy Aether
- **Steal** transfers Aether to the opposing party
- When a player's monster uses purging or stealing effects, it prioritizes Aether that enemies need for their current turn's Actions
- If an enemy monster lacks sufficient Aether to cast its Action, it is **interrupted**, canceling the Action and forcing it to use its Basic Attack instead

### Elements
- Monsters have 2 elements (or Wild)
- Determines learnable Actions
- **No type weaknesses/resistances**

### Types

Types are attributes that each monster has. They determine which role the monster can take and influence which Skills it can learn, allowing for more strategy. Each monster has 3 Types.

**List of Types (18 total):**
| Column 1 | Column 2 |
|----------|----------|
| Aether | Power |
| Affliction | Purge |
| Age | Regeneration |
| Burn | Shield |
| Critical | Sidekick |
| Dodge | Summon |
| Force | Tank |
| Heal | Terror |
| Poison | Weakness |

### Skills

Skills refer to abilities that monsters can learn and use in combat. Active Skills, known as **Actions**, are directly used during battles, while passive Skills, referred to as **Traits**, provide ongoing benefits or effects. Each monster species starts with two Actions and a Signature Trait. Additional Skills become available as the monster levels up. A monster can only learn Skills that match at least one of its Types. For Actions, the required spent Aether must also match at least one of the monster's assigned Elements.

**Level-Up Skill Learning:**
- Upon leveling up, the monster can learn either a new Trait or Action, depending on its level
- **Traits on even levels, Actions on odd levels**
- When learning a new Skill, three Skills are randomly selected from the monster's Skill pool
- Player must choose one of the three
- Skill Rerolls (from meta-upgrades and loot drops) can reroll the three options
- Lily can replace an existing Trait or Action with one of three available Skills

**Skill Caps:**
- Each monster can have up to **4 Actions** (including the two they start with)
- Each monster can have up to **4 Traits** (excluding their Signature Trait)
- Once a cap is reached, the player can either grant the monster +5 Max Health or replace one of its current Skills

### Actions

Actions are active Skills utilized by monsters during combat. Apart from a monster's basic Attack, each Action is assigned with at least one Type and requires a set amount of Aether for use. The Action's assigned Type and required Aether Element determine which monsters can learn it. For instance, a Power Action requiring Fire and Water Aether can only be learned by Power monsters possessing either Fire or Water Elements.

**Action Mechanics:**
- Using an Action will end the monster's current turn, unless it is a **Free Action**
- The elemental damage of an Attack is determined by its elemental Aether cost (important for reducing/breaking enemy Poise)
- Each monster species begins with two Actions and their basic Attack (innate, costs no Aether)

**Starting Actions:**
- Starting Actions are Actions with **no assigned Type**, available only to monsters that possess them
- Most monsters begin with two starting Actions (some Summon monsters start with a regular Action instead)
- Because Starting Actions lack a Type, they do not appear in the pool when learning new Actions
- They can be temporarily replaced during a run by Lily or when at the 4-Action cap

### Effects Classified as Actions

While Actions usually refer to the Skills that the player actively selects during battle, some other effects are also classified as Actions. **This classification is important for effects that trigger "On Action".**

Effects classified as Actions include:
- **Sidekick Attacks**
- **Retaliate effects that trigger an Attack:**
  - Brace, Lightning Bolt Trap, Pheromone Trap, Reflective Trap, Thorns, Volcanic Guard, Wild Mark
- **Traits that trigger an Attack:**
  - Aether Breath, Decay, Fearless, Hundred Handed, Lamentation of Woes

### On Action Effects

Many effects in the game trigger "On Action". These include:

**Traits with On Action triggers:**
- **Accelerated Generation** (Aether): On Action, if it was this monster's second Action of the same turn: Generates Wild Aether
- **Converter** (Aether + Affliction): On Action: Applies 1 Affliction to self and converts 1 random Aether into Wild Aether
- **Grim Servitude** (Summon + Terror): Aura: On Action from an allied Minion: Consumes 1 Essence on that Minion to apply 5 Terror to target enemy
- **Protective Servitude** (Summon): Aura: On Action from an allied Minion: Consumes 1 Essence on that Minion to heal and Shield its attached monster by 6
- **Torment Doubling** (Affliction): On Action, if the Action applied debuffs to enemies: Applies those debuffs another time to the enemies and to self

**Signature Traits with On Action:**
- **Monster Chef** (Domovoy): On Action, if the action didn't consume Cooking stacks: Applies Cooking to self and Regeneration to the ally with the lowest Regeneration
- **Putrid Odor** (Ooze): On Action: Applies 2 Poison to self. Whenever Poison triggers on this monster: Enemies also take damage from it
- **Putrid Odor (Shifted)** (Ooze Shifted): On Action: Applies 2 Burn to self. Whenever Burn triggers on this monster: Enemies also take damage from it

**Other On Action Effects (from Perks/Equipment):**
| Effect | Trigger |
|--------|---------|
| Aether Chance | On Action: Chance to generate Random Aether |
| Affliction Chance | On Action: Chance to apply 2 Affliction to self and to a random enemy |
| Dodge Chance | On Action: Chance to apply Dodge to self |
| Force Chance | On Action: Chance to apply Force to self |
| Power Chance | On Action: Chance to apply Power to self |
| Regeneration Chance | On Action: Chance to apply Regeneration to self |
| Sidekick Chance | On Action: Chance to apply Sidekick to self |
| Buff Consumption | On Action: At the end of your turn: Chance to consume 1 random buff on self |
| Burn Chance | On Action: Chance to apply 2 Burn to self |
| Corruption Chance | On Action: Chance to apply 1 Corruption to self |
| Poison Chance | On Action: Chance to apply 2 Poison to self |
| Terror Chance | On Action: Chance to apply 2 Terror to self |

**Burn Interaction with Actions:**
When a monster with Burn consumes Aether to use an Action or gets Interrupted: Deals X Fire damage to it (X = stack count)

### Traits

Traits are passive Skills from monsters that occur during combat. Some Traits are continuously active and may provide additional Modifiers, while others trigger in response to specific conditions. A few Traits can only trigger a limited number of times per combat. Each Trait is assigned with at least one Type, which determines which monsters can learn it.

**Trait Rules:**
- A Trait can only be learned once by a monster unless it is replaced
- Multiple monsters in the party can learn the same Trait, **except for Aura Traits**
- An **Aura Trait** can only be learned by a single monster at a time and will not appear on any other monsters in the party unless the former monster is removed or the Trait is replaced

**Signature Traits:**
- Exclusive to each monster species
- Every monster possesses a Signature Trait and always starts with this passive Skill
- Signature Traits cannot be replaced
- Designed to have strong potential to be built around
- Shifted monsters either retain the Signature Trait of their normal variant or have a slightly altered version

### Maverick Skills

Maverick Skills are special Skills that combine two different Types (not all dual-Type Skills are Maverick). A monster can only learn a Maverick Skill if:
1. Its Type matches at least one of the required Types
2. There is a monster in the party with the other required Type

If a monster possesses both required Types, it can learn these Skills without needing another monster in the party.

**Maverick Behavior:**
- When a Maverick Skill is among possible Skills, it has a **50% chance** of appearing on the Skill selection screen
- Only one Maverick Skill can appear at a time during Skill selection
- Designed to enable synergies between different monster Types
- Once learned, the Skill remains available even if there are no longer any monsters with the other required Type

### Skill Selection System (Weighting)

When a monster learns a new Skill, the selection uses a weighted system. Each Skill begins with a weight of **1.0**. When a monster learns a Skill, the weight of all other Skills of the same Type that the monster has not yet learned increases.

**Weight Modifiers:**
- If the Skill is an Action with two Types and the monster has both Types: **+0.5 weight**
- Each Skill on the monster that overlaps at least one Type: **+0.3 weight**
- Each Skill on other monsters that overlaps at least one Type: **+0.075 weight**

**Example (Wyrmling: Heal/Aether/Power with Aether Breath and Healing Brew):**
- Skills with Heal: 1.3 (1.0 + 0.3)
- Traits with both Heal and Aether: 1.6 (1.0 + 0.3 + 0.3)
- Actions with both Heal and Aether: 2.1 (1.0 + 0.5 + 0.3 + 0.3)
- If two Power Skills on other allies, Skills with Power: 1.45 (1.0 + 0.3 + 0.075 + 0.075)

**Note:** Signature Traits and starting Actions do not have any type assigned. Some Skills have acquisition requirements (e.g., a Skill affecting a particular buff/debuff may require the monster or an ally to possess a Skill capable of applying that buff/debuff).

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
- Triggers **Interrupt** (see Interrupt section below)
- Enemy takes **+50% damage** from all sources until end of their next turn
- Required for crafting Mementos

---

## INTERRUPT

An enemy is Interrupted when their planned Aether-consuming attack cannot be taken anymore, or when they are Staggered.

### Methods to Cause an Interrupt

1. **Stagger (Poise Break)** - Breaking an enemy's Poise to 0 always causes an Interrupt, regardless of what action was planned

2. **Aether Deprivation** - Purging or Stealing the Aether required for an enemy's planned attack, forcing it to fall back to its Basic Attack (only triggers Interrupt if an Aether action was planned)

3. **Death** - Killing an enemy while an Aether-consuming Action was planned (does not trigger Interrupt if only a Basic Attack was planned)

### On Interrupt Traits

| Trait | Type | Effect |
|-------|------|--------|
| **Exploit** | Purge + Sidekick | On Interrupt: Applies Sidekick to self |
| **Shock** | Purge | On Interrupt: Deals a 6 Fire/Wind damage Hit to that enemy |
| **Tormentor** | Purge + Terror | On Interrupt: Applies 5 Terror to that enemy and heals self for 10 |

### On Interrupt Actions

| Action | Effect |
|--------|--------|
| **Ball Lightning** | 2x4 Fire/Wind damage, Purges 2 enemy Aether. On Interrupt: Deals 2 damage Hit to all enemies |

### Other On Interrupt Effects

| Source | Effect |
|--------|--------|
| **Belt** (Equipment) | On Interrupt: Apply Power to self |
| **Burn** (Debuff) | When Interrupted: Takes stacks as Fire damage |

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

## CURRENCY

### Gold (Run-Only)
- Used to buy items from the Merchant
- Used to cleanse Corruption at Blessed Ponds
- Resets to 0 each run (can be upgraded via Gustavo)

**Acquisition:**
- Destroying boxes during exploration
- Refusing Sir Tiberion's offer: 15 Gold
- Refusing Alioth's offer: 15 Gold
- Winning a battle: 5 Gold
- Scrapping equipment (varies by tier)
- Loot Drops

### Aether Crystals (Persistent)
- Used to buy meta-upgrades from vendors in Pilgrim's Rest
- Persists between runs

**Acquisition:**
- Collecting during exploration
- Increasing monster Worthiness (up to III): 50 Aether Crystals
- Defeating a Champion monster: 30 Aether Crystals
- Loot Drops

### Monster Souls
- Consumable used as currency for Points of Interest
- Used to Rebirth monsters from Monster Shrines

### Lurker's Teeth
- Used to purchase decorations from Ivy
- Obtained from Lurker encounters (2 immediately, 2-3 more for correct quiz answer)

---

## EQUIPMENT

### Basics
- Items that give special effects to equipped monsters during combat
- One piece of equipment per monster; **cannot be removed, only swapped**
- When obtaining equipment: equip to a monster OR scrap for Gold
- Swapping gives old equipment to another monster (chain until scrapped or equipped)

### Tiers
| Tier | Color |
|------|-------|
| Common | Pink |
| Rare | Blue |
| Epic | Orange |

### Acquisition
- Battle drops
- Zone loot
- Merchant (base cost: 10 Gold)
- Sir Tiberion (trade or upgrade)

### Affixes
Equipment can have Affixes: general modifiers with additional combat effects beyond the base effect.

### Equipment List (Selected)
| Equipment | Effect (Epic) |
|-----------|---------------|
| **Armor** | Turn start: Shield self 4 |
| **Banner** | Aura: All allies +10% Crit Chance, +10% Evasion |
| **Belt** | On Interrupt: Apply Power to self |
| **Blade** | Crit Chance +20% |
| **Boots** | Evasion +25% |
| **Chain** | On single-target attack (if enemies have 4+ Aether): Purge 1 enemy Aether |
| **Drum** | On Support Action: Cleanse 4 debuffs, +1 Temp Power to target |
| **Lance** | First hit of each attack = Wild Damage. Wild Damage +4 |
| **Meteorite** | On Support Action: Convert 1 random Aether to Wild, Shield self 4 |
| **Ocarina** | On Support Action: Heal lowest health ally 7 |
| **Orb** | Aether +1 (no HP penalty at Epic) |
| **Salve** | End of combat: Cleanse 4 Corruption from self |
| **Scepter** | End of turn: +1 Temp Power, Heal self 6 |
| **Skull** | 25% chance to double-apply debuffs |
| **Staff** | Hit +1 |
| **Talisman** | 25% chance to double-apply buffs to self |
| **Tome** | On single-target attack: Apply 2 Weakness |

---

## ARTIFACTS

### Basics
- Items the Aethermancer uses during combat (not equipped to monsters)
- One artifact use per turn, **only before all monsters have acted**
- Default: 2 charges per artifact
- Alioth can upgrade an artifact to 3 charges

**Charge Restoration:**
- Fully restored at Rest Sites
- +1 to all artifact charges at start of Champion battles (meta-upgrade)

### Acquisition
- **Monster Flask**: Always start with this artifact
- Random Loot Drops
- Purchased from Merchant
- Given by Alioth

### Artifact List
| Artifact | Effect |
|----------|--------|
| **Monster Flask** | Heal target ally 15 (upgradeable via meta-upgrades) |
| **Aethermancer Whip** | Deal 2x2 damage, Steal 1 enemy Aether |
| **Antidote Flask** | Heal target 15, Cleanse 5 debuffs |
| **Chromatic Scroll** | Break 3 Poise on target enemy |
| **Deck of Cards** | Consume all Aether, generate 7 random Aether |
| **Heroic Horn** | Apply Power to target ally |
| **Hexing Harp** | Purge 2 enemy Aether |
| **Hourglass of Undoing** | Heal all monsters 5, Cleanse 5 Corruption from all |
| **Rod of Command** | Reactivate target monster |
| **Shielding Dust** | Shield all allies 10 |
| **Tea of Many Spices** | Target generates 1 random Aether, Heal 7, Shield 7 |
| **Void Crystal** | Generate 2 Wild Aether |

---

## MEMENTOS

### Basics
- Items associated with individual monster species
- Unlocking a Memento adds that monster to Monster Shrine pool
- **Mementos are permanent** (persist across runs)

### Capturing Mementos
1. **Stagger** an enemy monster (break its Poise to 0)
2. Use the **Capture Memento** action while enemy is Staggered
3. Requires a **Blank Memento** in inventory

### Blank Memento Sources
- 1 free at start of every run
- Memento Start meta-upgrade
- Purchase from Merchant
- Random Loot Drop

---

## REBIRTH (Monster Shrines)

### Basics
- Monster Shrines are structures for reviving/adding monsters to party
- Found in main areas and Rest Sites
- Each shrine can only be used once
- No limit to monsters revived per run (max 3 in party)

### Available Monsters
You can rebirth monsters you have a **Soulbond** with (at run start) or whose **Mementos** you've unlocked.

### Mechanics
- Rebirthed monsters start at Level 1
- Skills available depend on: monster types, elements, party synergies, past choices
- If party is full (3 monsters), shrine can replace one monster

---

## EXPLORATION

### Aethermancer Abilities
| Ability | Effect |
|---------|--------|
| **Void Jump** | Climb walls via Void Crystals; escape monsters (they can't climb) |
| **Void Blitz** | Pre-combat: Target enemy starts with **-2 Poise** (or -3 from high ground) |
| **Void Parry** | Counter ambushes with same effect as Void Blitz |
| **Fast Travel** | Teleport between finished combats, NPCs, Aether Springs via map |

### Zone Structure
- Multiple small sections with monsters, NPCs, objects
- Portal rooms with 3 choices (icons show: NPC, Aether Spring, loot, ? = random)
- Final section has Champion Monster (marked on ground)
- After Champion: Rest Site → Next biome

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
