# Roguelike Mod — Design Doc

Status: brainstorm locked, pre-prototype. Working title TBD.

A RimWorld mod that turns the game into a run-based squad roguelike. Structure from Slay the Spire (branching act map, draft rewards, relics, bosses); per-node camp building from Against the Storm (fresh map each node, drafted blueprints, a threat clock).

## Pillars

1. **The squad is the deck.** Persistent pawns, gear, traits and injuries carry the run. Permadeath.
2. **Every camp is disposable.** Each node is a fresh map; you build only with what the run has given you, then leave it behind.
3. **Pressure, not grind.** Every node has a clock that keeps getting worse until you finish or die.

## Dependencies

- **Requires:** base game only.
- **Optional:** Royalty (psycasts as rewards), Biotech (xenotype recruits, mechanitor starter, noxious haze), Anomaly (entity elites and bosses). DLC content gated with `MayRequire` in XML and `ModsConfig.*Active` in C#.
- **Not used:** Odyssey or the gravship.

## Run structure

1. **Start:** pick a starter loadout (see [Starter loadouts](#starter-loadouts)). You get its pawns, its fixed signature relic and a small kit. On the landing screen, the first row of nodes shows on the world map; you land on the one you pick, so the first landing is the first node.
2. **Act map:** a branching node graph ending in a boss, laid out on the RimWorld world map. Each node is a world tile, so its biome comes from the planet. Each node previews its type, objective, threat modifier and reward. Connections between nodes are drawn on the world map. The caravan can only travel to connected next nodes, and travel time costs food, which ties into the carrying rule. Runs reuse the same world and seed; each run places a fresh node graph on it.
3. **Play the node** (below).
4. **Win the node:** a letter announces the objective is complete. Acknowledging it ends the node instantly.
5. **Rewards:** pick 1 of 3 cards (blueprint, relic, gear, implant, or occasionally a recruit).
6. Repeat until the boss.

### Losing

The run ends when either:
- the squad is wiped, or
- a node's objective is failed.

Only some objectives can fail outright, without the squad dying (e.g. an escort dies, a target escapes, a hold point is overrun). Every other objective has no fail state: the threat modifier keeps getting worse until the objective is done or the squad dies.

### Carrying between nodes

Only what pawns and pack animals can carry, using vanilla caravan mass capacity. Pack animals add room to carry things but eat, can get hurt and slow the caravan. Losing them loses supplies.

## Node types

| Node | Description |
|---|---|
| **Expedition** | Fresh map. Build a camp from drafted blueprints, complete the objective (resource quota, build target, survive N days, kill a target) before the threat modifier gets too strong. |
| **Combat: Alerted** | Enemy knows you're there and attacks immediately. Pure fight. |
| **Combat: Unaware** | Land unseen. Scout, build a camp, then start the assault yourself. The game pushes you to attack via any of: the node's threat modifier, enemy patrols that can spot you, enemy reinforcements arriving after N days. |
| **Elite** | Harder combat (mech cluster, Empire detachment, Anomaly entity). Better rewards. |
| **Event** | Text choice event with RimWorld flavor. |
| **Rest** | Heal, train a skill, or do one surgery. |
| **Merchant** | Curated trader stock. |
| **Treasure** | Relic, sometimes with a risk attached. |
| **Boss** | End of act. |

## Threat modifier (the clock)

- Each node rolls **one** modifier, chosen to fit its biome, and shown on the map.
- It **gets stronger over time with no cap**. It does not change into a different type.
- **Grace period:** it holds off for the first few days of a node.
- **Pace-driven:** after the grace period, it worsens slower while the player is ahead of the objective's expected pace and faster while they're behind.
- Temperatures display in the player's chosen unit (°C, °F or K).
- Examples:
  - **Cold:** starts as a mild snap and ends with instant frostbite outdoors.
  - **Heat:** heatstroke, outdoor work stops, crops wither.
  - **Toxic:** fallout builds up faster and reaches indoors.
  - **Psychic:** the drone gets stronger and causes mental breaks.
  - **Meteors:** more frequent and bigger.
- Implementation: one custom `GameCondition` per modifier that scales a vanilla effect along a curve over time.
- Later: tornadoes, hurricanes, tsunamis. Each is a new system, so they come after the first playable version.

### Custom meteors

The vanilla meteor is too plain. Ours add:
- A warning marker or shadow, then an incoming streak, impact flash, screen shake and dust (`FleckDef`/`EffecterDef`).
- What's left after impact: a crater, fires, a shockwave that stuns or knocks pawns down, and sometimes a quake that damages walls nearby.
- Needs new textures and effect work.

## Drafting and rewards

- **Pick 1 of 3** for blueprints (building and research groups you may use this run), relics, gear and implants.
- **Recruits:** dedicated recruit rewards, plus a low chance to appear in any reward pool. Each recruit card shows everything: traits, skills and passions, backstory, health, xenotype.
- **Skipping** a pick may give a small consolation reward.
- **Curses:** bad events can add negative relics or pawns (a pyromaniac, a shamed pawn, a lingering debuff).

### Card-pick screen

Should look and animate like a card pick in Slay the Spire, with RimWorld-native art (no copied assets):
- Cards fan in, grow when hovered, and on pick the chosen card flies off while the rest fade.
- RimWorld's UI is immediate-mode (`Widgets`, `Window.DoWindowContents`) and has no animation system, so the animation is hand-written, easing size and position over time.
- Art needed: card frames, rarity borders, and icons for each blueprint and relic.

## Starter loadouts

Each starter has a **fixed** signature relic that bends one core system (clock, carrying, drafting, squad, combat, events), so each starter changes how a run plays, not just its numbers. Only Crashlanded is available at first; meta-progression unlocks the rest.

### Base game

| Starter | Squad and kit | Signature relic | Bends |
|---|---|---|---|
| **Crashlanded** | 3 balanced pawns, basic gear | **Ship's Black Box**: one extra choice at every reward screen | Drafting |
| **Tribal Hunters** | 5 weak pawns, bows, 2 pack animals | **Herd Bond**: pack animals heal fully between nodes and carry 50% more | Carrying |
| **Rich Explorer** | 1 pawn, great gear, lots of silver | **Orbital Credit Line**: call a trader once per node; merchants 25% cheaper | Economy |
| **Mercenary Band** | 4 fighters, poor builders | **Bounty Ledger**: combat nodes give an extra reward; Expedition clocks run 10% faster | Combat / clock |
| **Ice Nomads** | 3 tough pawns, tundra gear | **Thermal Lining**: cold and heat modifiers worsen 30% slower | Clock |
| **Escaped Prisoners** | 4 injured pawns, prison clothes, no gear | **Broken Shackles**: recruits show up far more often; downed enemies can be recruited on the spot | Squad |
| **Lone Doctor** | 1 pawn with high Medicine, medical kit | **Field Surgeon's Bag**: surgery is possible between any two nodes, and wounds never get infected | Squad / rest |

### Royalty

| Starter | Squad and kit | Signature relic | Bends |
|---|---|---|---|
| **Psycaster Exile** | 1 psycaster + 2 pawns | **Eltex Shard**: psyfocus refills at the start of each node; the psycaster starts with one random psycast | Combat |
| **Imperial Noble** | Noble + 2 servants; the noble refuses manual labor | **Imperial Writ**: call in an Imperial trooper shuttle once per act | Combat |

### Biotech (one per xenotype)

| Starter | Xenotype | Signature relic | Bends |
|---|---|---|---|
| **Vampire Coven** | Sanguophage | **Hemogen Chalice**: feeding on downed enemies heals the feeder; pawns are weak in sunlight | Squad sustain |
| **Mech-tender** | Mechanitor (any xenotype) | **Gestator Core**: rebuild one destroyed mech after each node | Squad |
| **Burrowers** | Dirtmole | **Deep Drill**: very fast mining; underground rooms ignore surface weather | Clock |
| **Think Tank** | Genie | **Research Grant**: every blueprint draft includes one rare option | Drafting |
| **Envoys** | Highmate | **Silver Tongue**: events get extra peaceful options; recruits join happy | Events |
| **Shock Troopers** | Hussar | **Go-Juice Cache**: start every fight dosed; skipping a dose brings withdrawal | Combat |
| **Firebrands** | Impid | **Ember Heart**: heat modifiers buff instead of hurt; fire resistance | Clock (inverted) |
| **Old Blood** | Neanderthal | **Thick Skull**: downed pawns rarely die, but advanced blueprints never appear | Drafting (restricted) |
| **Hog Riders** | Pigskin | **Iron Gut**: eat anything; food costs for travel halved | Carrying / travel |
| **Rust Clan** | Waster | **Toxic Lungs**: toxic modifiers are harmless; scavenged chemfuel and drugs show up more | Clock |
| **Frost Raiders** | Yttakin | **Blizzard Howl**: combat bonuses that grow as cold modifiers worsen | Clock (inverted) |

Baseliners are covered by Crashlanded.

### Anomaly

| Starter | Squad and kit | Signature relic | Bends |
|---|---|---|---|
| **Void Cultists** | 3 pawns, ritual gear | **Bloodstained Codex**: killing entities gives relics; every node has a chance of a creepy event | Risk / reward |

## Relics

Passive effects for the whole run. Examples:
- **Cryptosleep Shard:** the first pawn downed each fight is stabilized.
- **Luciferium Vial:** +30% all stats, but needs a dose every 3 nodes.
- **Psychic Harmonizer:** pawns share their mood.
- **Glitterworld Med Kit:** rest sites fully heal one injury.

## Meta-progression

Each run unlocks a bit more for the next one, through points earned per node cleared. Both kinds of unlock:
- **Variety:** new blueprints, relics, starters and xenotypes added to the draft pools.
- **Power:** permanent upgrades such as better starting gear.

**Ascension** balances power creep. After a win, players can start runs at a higher Ascension level. Each level adds one stacking handicap, such as threat modifiers that ramp faster, tougher elites, fewer reward choices, or worse starting gear. Ascension is not part of the MVP.

## First playable version (MVP)

- One act, one boss.
- Starters: Crashlanded, Tribal Hunters, Ice Nomads.
- Nodes: Expedition, Combat (both kinds), Event.
- Persistent squad of 3–6, permadeath; carrying limited to caravan mass capacity.
- Threat modifiers: cold, heat, toxic, psychic (vanilla-scaled). Custom meteors are a stretch goal.
- About 10 blueprint groups, about 12 relics, recruit drafting.
- Card-pick screen with basic animation.
- Simple meta-unlock list.
- Node length target: 1–2 in-game seasons (about 20–40 min). Tune by feel.

## Roadmap

Each stage ends with a playtest gate; the next stage starts only once the gate passes.

| Stage | Scope | Gate | Status |
|---|---|---|---|
| **0. Core node loop** | Pick a starting node on the world map; one weather modifier with grace period and pace-driven ramp; stockpile quota; instant leave on completion | Is one node fun? | ✅ Done (2026-11-21) |
| **1. Run skeleton** | Run state (act, visited nodes, run over on wipe or failed objective); one act of ~15 nodes in branching rows plus a boss, drawn on the world map; caravan travel limited to connected next nodes; node previews | Does chaining nodes feel like a run? | ✅ Done (2026-11-21, in-game self-test) |
| **2. Rewards and drafting** | Card-pick screen (pick 1 of 3, basic animation); relics (~12), gear, implants, recruits; blueprint drafting limits the build menu | Do drafts make runs feel different? | |
| **3. Node variety** | Combat (Alerted, Unaware), Event nodes; survive, build, kill and escort (failable) objectives; toxic and psychic modifiers; boss fight | Is a full act replayable? | |
| **4. Starters and meta** | Crashlanded, Tribal Hunters, Ice Nomads; end-of-run screen; meta points and unlocks stored outside saves. Completes the MVP | Is a new run worth starting? | |
| **5. Post-MVP** | Ascension; elite, rest, merchant, treasure nodes; acts 2–3; DLC starters; custom meteors and disasters; card art and polish | | |

Biggest risks: blueprint drafting (patching build and research menus), hand-animated card UI, and meta-progression storage outside saves.

## Open questions

- Should quotas be checked against what the node map can supply? (Playtest 1: the steel quota was met on a map with no mineable steel.)

The core-loop prototype lives in [`Roguelike/`](../Roguelike/README.md).

