# Rimlike: Roguelike Runs

![Rimlike banner](About/Preview.png)

A run-based squad roguelike for RimWorld 1.7, built from [the design doc](../docs/roguelike-design.md). Pick a starter, land on a node of a branching act laid out on the world map, clear nodes under worsening weather, draft rewards, and beat the boss. Meta points earned across runs buy unlocks.

## Build

```
cd Source
dotnet build -c Release
```

This targets RimWorld 1.7 (`net10.0`, `Krafs.Rimworld.Ref 1.7.5005-unstable`, Harmony from the Workshop) and copies About, Defs, Assemblies and Textures to `RimWorld/Mods/Roguelike`. Pass `-p:DeployDir=none` to skip the copy. A built `Assemblies/Roguelike.dll` is checked in, so the folder works as a mod without building.

## Play

1. Enable **Harmony**, then **Rimlike: Roguelike Runs** below it.
2. **New colony** opens the starter cards: **Crashlanded**, plus any starters you've unlocked (Tribal Hunters, Ice Nomads, and with the DLCs one starter per Biotech xenotype, Mech-tender, Psycaster Exile, Imperial Noble and Void Cultists). With the mod enabled only roguelike starts are offered; disable the mod to play standard RimWorld. → storyteller → world → landing site. With Ideology, pick one of three generated ideoligion cards. Then pick your squad from ten candidate cards that fit the starter, up to the starter's squad size.
3. On the landing screen the whole run is on the world map: three acts of 15 nodes, spread across the planet and joined by lines, each node with an icon for its kind. NPC settlements are cleared away; the only bases left are the elite and boss nodes. Select a bottom-row node of act 1 and press Next. Select a bottom-row node (its briefing shows in the inspect pane) and press Next.
4. Each node has an objective; the panel at the top right shows progress and the weather. Node kinds:
   - **Expedition:** classic base building over roughly 25–45 in-game days: grow a granary of corn, a rice or crop rotation, a medicine garden, a stone fortress, private quarters, or a whole season with no deaths. Each has requirements that scale with your squad and a minimum stay, under a weather modifier rolled to fit the climate.
   - **Ambush:** a raid attacks at once; defeat every enemy.
   - **Raider camp:** a hidden camp across the map. Patrol raids, timed reinforcements and the weather push you to attack.
   - **Escort:** keep a refugee alive for a few days. If they die, the run ends.
   - **Elite:** storm a hostile faction base; the reward is a choice of relics. Bosses are bigger bases with reinforcements.
   - **Event, rest site, treasure, merchant:** resolved on the world map, no map. Rest sites heal, train or treat; treasure gives relics; merchants sell cards for the silver your caravan carries.
5. The modifier (cold, heat, toxic buildup, a psychic drone, meteors, tornadoes, a hurricane or a tidal surge on the coast) holds off for a grace period, then worsens every day with no cap: slower while you're ahead of pace, faster while behind.
6. Clearing a node opens the reward screen: pick 1 card (relic, gear, implant, blueprint, research or recruit) or skip. Research cards finish a project right away, favouring ones that open gated objectives. Relics come in common, uncommon and rare. Recruit nodes offer only recruits. Un-drafted blueprint groups are hidden from the build menu.
7. Then pick your next node from the cards (one per connected node), and pack on the packing screen: item tiles by category (click to add, right-click to remove, shift for all or none), **Essentials** and **Most valuable** presets, and a carry meter that won't let you pack past what the squad and pack animals can carry. **Back to the node choice** returns to the cards. The squad goes straight into the chosen node, no travel. Choosing a node skips its siblings. Closed the cards by accident? Use **Leave node now** on the completion letter.
8. Beating an act boss gives a card and lays out the next, harder act nearby. The run is won on the act-3 boss and lost on a squad wipe, a failed escort, or leaving a node early. The end-of-run screen shows stats and points, with **New run** to start again on the same world: choose a starter (last run's is preselected), then ideoligion and squad.

**Meta points:** +1 per node cleared, +5 per boss, +1 per skipped reward. Spend them from the end-of-run screen or in **Options → Mod settings → Roguelike** on starters, extra relics/blueprints/gear in the pools, and permanent bonuses. Progress is stored in the mod settings file, not in saves. Winning unlocks the next **Ascension** level (up to 5), chosen in the same list; each level stacks a handicap.

Debug helpers (Development mode): the caravan float menu has **(Dev: instantly)**; debug actions **Roguelike → Enter test node...**, **Complete node objective**, **Advance threat 1 day**.

## Tuning

All content is XML in `Defs/`, no recompile needed:
- `Roguelike_Prototype.xml`: weather modifiers, node presets (kind, objective, modifier and its rate, grace, pace, enemy pressure), events, the Crashlanded run scenario.
- `Roguelike_Rewards.xml`: relics, gear and implant rewards, blueprint groups.
- `Roguelike_Meta.xml`: starter scenarios and unlocks.
- `Roguelike_Disasters.xml`: meteor, tornado, hurricane and tidal-surge modifiers and their nodes.
- `Roguelike_DLCStarters.xml`: DLC starters, their relics and unlocks (generated; loads only with each DLC).

Art is generated, no assets are copied from anywhere: `Tools/make-node-icons.ps1` (world-map node icons), `Tools/make-unlock-icons.ps1` (unlock and starter badges) and `Tools/make-mod-art.ps1` (`About/ModIcon.png` and `About/Preview.png`). UI textures are made in code (`Source/Style.cs`).

Act shape (row sizes and spacing) is in `Source/RunMap.cs`.

## Self-test

`Tools/selftest.ps1 -Mode <mode> [-Scenario <ScenarioDef>] [-OutDir <dir>]` launches RimWorld with `-quicktest -roguelikeSelfTest=<mode>`, which starts a run on a starting node and drives it from `Source/SelfTest.cs`. Results are `[RLTEST]` lines in the player log; screenshots are saved as `rltest_*.png`. The script backs up your mod list, enables Harmony + this mod for the run, and restores it afterwards. Meta progress is reset in memory only and never written. Close RimWorld first.

| Mode | Covers |
|---|---|
| `win` | Act graph, travel, rewards after each node, map-less nodes on the path, all three acts and their bosses, winning |
| `leave` | Leaving a node early ends the run |
| `wipe` | Losing the squad ends the run |
| `rewards` | Card screen (incl. a mid-pick screenshot), every reward kind incl. research, relic effects, blueprint build-menu gate |
| `variety` | Every node kind, objective and weather modifier, and every event |
| `meta` | Points, buying unlocks, locked content, end-of-run and unlock screens |
| `meta` (also) | Ascension effects and unlocking |
| `starter` | With `-Scenario <ScenarioDef>` (any roguelike starter, including the DLC ones): pawns, xenotypes, kit and signature relic |
| `newrun` | Wipe, vanilla game-over suppressed, New colony and New run open the starter cards, then the ideoligion and squad cards, into a fresh run with exactly the chosen squad and ideoligion |
| `disasters` | Meteor aftermath, tornado, hurricane, tidal surge |
| `polish` | Node icons, HUD panel, relic rarity |

## Known gaps

- Only 6 of the 20 gated objective candidates from the pacing research are in.
- Card art is vanilla item icons on procedural card frames; node icons are simple generated glyphs.
- Several DLC signature relics are simplified versions of the design (see the design doc).
