using System.Linq;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Roguelike
{
    public static class NodeLauncher
    {
        /// <summary>Starts a node on <paramref name="map"/> and briefs the player.</summary>
        public static void StartOn(Map map, NodePresetDef preset, LookTargets look)
        {
            map.GetComponent<MapComponent_RoguelikeNode>().Start(preset);
            RelicEffects.OnNodeStart(map);
            Find.LetterStack.ReceiveLetter("Node: " + preset.LabelCap, preset.Briefing, LetterDefOf.NeutralEvent, look);
        }

        /// <summary>Debug path: moves every colonist and colony animal on <paramref name="source"/> into a fresh node map.</summary>
        public static void Enter(Map source, NodePresetDef preset)
        {
            var pawns = source.mapPawns.FreeColonistsSpawned.Concat(source.mapPawns.SpawnedColonyAnimals).ToList();
            if (pawns.Count == 0)
            {
                Messages.Message("No colonists on this map to send.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (!TileFinder.TryFindNewSiteTile(out PlanetTile tile, source.Tile, 2, 8))
            {
                Messages.Message("Couldn't find a free tile for the node.", MessageTypeDefOf.RejectInput, false);
                return;
            }

            // Pawns keep only what they're already wearing and carrying.
            var caravan = CaravanExitMapUtility.ExitMapAndCreateCaravan(pawns, Faction.OfPlayer, source.Tile, source.Tile, PlanetTile.Invalid, sendMessage: false);
            var map = GetOrGenerateMapUtility.GetOrGenerateMap(tile, new IntVec3(preset.mapSize, 1, preset.mapSize), RoguelikeDefOf.Roguelike_Node);
            CaravanEnterMapUtility.Enter(caravan, map, CaravanEnterMode.Edge);

            Current.Game.CurrentMap = map;
            CameraJumper.TryJump(pawns[0]);
            StartOn(map, preset, pawns[0]);
        }
    }

    public static class RoguelikeDebugActions
    {
        [DebugAction("Roguelike", "Enter test node...", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void EnterNode()
        {
            var options = DefDatabase<NodePresetDef>.AllDefs
                .Select(p => new FloatMenuOption(p.LabelCap, () => NodeLauncher.Enter(Find.CurrentMap, p)))
                .ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }

        [DebugAction("Roguelike", "Complete node objective", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void CompleteNode()
        {
            var c = Find.CurrentMap.GetComponent<MapComponent_RoguelikeNode>();
            if (c is { Active: true, completed: false }) c.Complete();
        }

        [DebugAction("Roguelike", "Advance threat 1 day", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void AdvanceThreat()
        {
            var c = Find.CurrentMap.GetComponent<MapComponent_RoguelikeNode>();
            if (c is not { Active: true }) return;
            // Shift the node clock back a day so grace and pace both move, then apply a day of change.
            c.startTick -= GenDate.TicksPerDay;
            c.AdvanceThreat(1f);
        }
    }
}
