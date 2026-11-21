# Roguelike (prototype)

A throwaway probe of the run loop from [the design doc](../docs/roguelike-design.md): land on a starting node, clear nodes along a branching act on the world map, and beat the boss. Each node is a stockpile quota under one worsening weather modifier.

## Build

```
cd Source
dotnet build -c Release
```

This targets RimWorld 1.7 (`net10.0`, `Krafs.Rimworld.Ref 1.7.5005-unstable`, Harmony from the Workshop) and copies About, Defs and Assemblies to `RimWorld/Mods/Roguelike`. Pass `-p:DeployDir=none` to skip the copy.

## Test

1. Enable **Harmony**, then **Roguelike (prototype)** below it.
2. New game → scenario **Roguelike run** → pick a storyteller and make a world.
3. On the landing screen the act appears on the world map: 15 node markers in rows, joined by lines, ending in a red **Boss** node. Select a bottom-row node (its briefing shows in the inspect pane) and press Next. Only bottom-row nodes are allowed.
4. After landing, the node starts. The alert on the right shows quota progress and the weather (in your temperature unit). The weather holds off for `graceDays`, then changes each day, slower while you're ahead of pace and faster while you're behind.
5. When the quota is stockpiled, a letter arrives. **Leave node now** opens the instant caravan screen; the node map is removed once everyone has left.
6. On the world map, lines to your next choices turn gold. Select the caravan and right-click a highlighted node → **Travel to and enter**. Other nodes refuse with a reason. Choosing one skips its siblings.
7. Clear the boss node to win the act.

Debug helpers (Development mode): the caravan float menu also has **(Dev: instantly)**; debug actions **Roguelike → Enter test node...** (start a node from any colony), **Complete node objective**, **Advance threat 1 day**.

Tune nodes in `Defs/Roguelike_Prototype.xml`: `graceDays`, `offsetPerDay`, `targetDays`, `paceSensitivity`, `minPaceFactor`, `maxPaceFactor`, `tileTemperature`, `isBoss`, `quota`. No recompile needed. Act shape (row sizes and spacing) is in `Source/RunMap.cs`.

## Known gaps

- The reform-caravan screen doesn't block going over mass capacity; an overweight caravan just can't move.
- Quotas aren't checked against what the map can supply.
- Caravans can still wander or settle off the act graph; only entering nodes is restricted.
- The run ends (with a letter) if the squad is wiped or you leave the current node before its quota is met. There is no run-end screen yet (stage 4).
- The boss is a bigger quota with a harsher clock until stage 3.

## Self-test

`Tools/selftest.ps1 -Mode <mode> [-OutDir <dir>]` launches RimWorld with `-quicktest -roguelikeSelfTest=<mode>`, which starts the run scenario on a starting node and drives it from `Source/SelfTest.cs`. Results are `[RLTEST]` lines in the player log; screenshots are saved as `rltest_*.png`. The script backs up your mod list, enables Harmony + this mod for the run, and restores the mod list afterwards. Close RimWorld first.

Modes: `win` (clear a path to the boss), `leave` (leave a node early), `wipe` (lose the squad).
