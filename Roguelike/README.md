# Roguelike (prototype)

A throwaway probe of the core node loop from [the design doc](../docs/roguelike-design.md): land on a chosen node, fill a stockpile quota while one weather modifier gets worse, then leave instantly with what you can carry.

## Build

```
cd Source
dotnet build -c Release
```

This targets RimWorld 1.7 (`net10.0`, `Krafs.Rimworld.Ref 1.7.5005-unstable`, Harmony from the Workshop) and copies About, Defs and Assemblies to `RimWorld/Mods/Roguelike`. Pass `-p:DeployDir=none` to skip the copy.

## Test

1. Enable **Harmony**, then **Roguelike (prototype)** below it.
2. New game → scenario **Roguelike run** → pick a storyteller and make a world.
3. On the landing screen, three **Start:** markers appear on the world map. Select one (its briefing shows in the inspect pane) and press Next. Only these tiles are allowed.
4. After landing, the node starts. The alert on the right shows quota progress and the weather. Temperatures use your game's unit setting.
5. The weather holds off for `graceDays`, then changes each day. It changes slower while you're ahead of the expected pace and faster while you're behind; the alert tooltip shows the current multiplier.
6. When the quota is stockpiled, a letter arrives. **Leave node now** opens the instant caravan screen. Once everyone has left, the settlement is abandoned.

Debug actions (Development mode): **Roguelike → Enter test node...** (start a node from any colony), **Complete node objective**, **Advance threat 1 day**.

Tune nodes in `Defs/Roguelike_Prototype.xml`: `graceDays`, `offsetPerDay`, `targetDays`, `paceSensitivity`, `minPaceFactor`, `maxPaceFactor`, `quota`. No recompile needed.

## Known gaps

- The reform-caravan screen doesn't block going over mass capacity; an overweight caravan just can't move. Balance later.
- Quotas aren't checked against what the map can supply.
- The progress readout is plain text; a nicer graphic comes later.
