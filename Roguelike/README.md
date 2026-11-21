# Roguelike (prototype)

A run-based squad roguelike for RimWorld 1.7, built from [the design doc](../docs/roguelike-design.md). Pick a starter, land on a node of a branching act laid out on the world map, clear nodes under worsening weather, draft rewards, and beat the boss. Meta points earned across runs buy unlocks.

## Build

```
cd Source
dotnet build -c Release
```

This targets RimWorld 1.7 (`net10.0`, `Krafs.Rimworld.Ref 1.7.5005-unstable`, Harmony from the Workshop) and copies About, Defs and Assemblies to `RimWorld/Mods/Roguelike`. Pass `-p:DeployDir=none` to skip the copy.

## Play

1. Enable **Harmony**, then **Roguelike (prototype)** below it.
2. New game → scenario **Roguelike: Crashlanded** (Tribal Hunters and Ice Nomads appear once unlocked). With the mod enabled only roguelike starts are listed; disable the mod to play standard RimWorld. → storyteller → world.
3. On the landing screen the act is on the world map: 15 nodes in rows joined by lines, ending in a red **Boss**. Select a bottom-row node (its briefing shows in the inspect pane) and press Next.
4. Each node has an objective; the alert on the right shows progress and the weather. Node kinds:
   - **Expedition:** stockpile a quota, build a target, or hold out for some days.
   - **Ambush:** a raid attacks at once; defeat every enemy.
   - **Raider camp:** a hidden camp across the map. Patrol raids, timed reinforcements and the weather push you to attack.
   - **Escort:** keep a refugee alive for a few days. If they die, the run ends.
   - **Event:** a choice on arrival, no map.
5. The weather modifier (cold, heat, toxic buildup or a psychic drone) holds off for a grace period, then worsens every day with no cap: slower while you're ahead of pace, faster while behind.
6. Clearing a node opens the reward screen: pick 1 card (relic, gear, implant, blueprint or recruit) or skip. Recruit nodes offer only recruits. Un-drafted blueprint groups are hidden from the build menu.
7. Then a letter: **Leave node now** opens the instant caravan screen. On the world map, select the caravan and right-click a node at the end of a gold line → **Go to**: the caravan goes straight in, no travel. Choosing a node skips its siblings.
8. The run ends on beating the boss, a squad wipe, a failed escort, or leaving a node early. The end-of-run screen shows stats and points.

**Meta points:** +1 per node cleared, +5 for the boss, +1 per skipped reward. Spend them from the end-of-run screen or in **Options → Mod settings → Roguelike** on starters, extra relics/blueprints/gear in the pools, and permanent bonuses. Progress is stored in the mod settings file, not in saves.

Debug helpers (Development mode): the caravan float menu has **(Dev: instantly)**; debug actions **Roguelike → Enter test node...**, **Complete node objective**, **Advance threat 1 day**.

## Tuning

All content is XML in `Defs/`, no recompile needed:
- `Roguelike_Prototype.xml`: weather modifiers, node presets (kind, objective, modifier and its rate, grace, pace, enemy pressure), events, the Crashlanded run scenario.
- `Roguelike_Rewards.xml`: relics, gear and implant rewards, blueprint groups.
- `Roguelike_Meta.xml`: starter scenarios and unlocks.

Act shape (row sizes and spacing) is in `Source/RunMap.cs`.

## Self-test

`Tools/selftest.ps1 -Mode <mode> [-Scenario <ScenarioDef>] [-OutDir <dir>]` launches RimWorld with `-quicktest -roguelikeSelfTest=<mode>`, which starts a run on a starting node and drives it from `Source/SelfTest.cs`. Results are `[RLTEST]` lines in the player log; screenshots are saved as `rltest_*.png`. The script backs up your mod list, enables Harmony + this mod for the run, and restores it afterwards. Meta progress is reset in memory only and never written. Close RimWorld first.

| Mode | Covers |
|---|---|
| `win` | Act graph, travel, rewards after each node, an event node on the path, the boss raid, winning |
| `leave` | Leaving a node early ends the run |
| `wipe` | Losing the squad ends the run |
| `rewards` | Card screen, every reward kind, relic effects, blueprint build-menu gate |
| `variety` | Every node kind, objective and weather modifier, and every event |
| `meta` | Points, buying unlocks, locked content, end-of-run and unlock screens |
| `starter` | With `-Scenario Roguelike_RunScenario`, `Roguelike_TribalHunters` or `Roguelike_IceNomads`: pawns, kit and signature relic |

## Known gaps

- The reform-caravan screen doesn't block going over mass capacity; an overweight caravan just can't move.
- Quotas aren't checked against what the map can supply.
- Node markers reuse vanilla world icons; card art is vanilla item icons.
- One act only; Ascension, more node types and the custom disasters are stage 5.
