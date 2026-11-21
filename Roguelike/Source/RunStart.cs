using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Roguelike
{
    [StaticConstructorOnStartup]
    public static class RoguelikeInit
    {
        static RoguelikeInit() => new Harmony("matt.roguelike").PatchAll(typeof(RoguelikeInit).Assembly);
    }

    /// <summary>Scenario part for a run: lays out the act after world generation and starts the chosen starting node.</summary>
    public class ScenPart_RoguelikeRun : ScenPart
    {
        public static bool ActiveInCurrentScenario => Find.Scenario?.AllParts.OfType<ScenPart_RoguelikeRun>().Any() == true;

        /// <summary>The start-row node at <paramref name="tile"/>, if the player may land there.</summary>
        public static RunNode StartNodeAt(PlanetTile tile)
        {
            var node = WorldComponent_RoguelikeRun.Get?.NodeAt(tile);
            return node != null && node.row == 0 && node.state == RunNodeState.Available ? node : null;
        }

        /// <summary>Starter loadout id; locked starters are hidden until bought with meta points.</summary>
        public string starterId = "crashlanded";
        public bool requiresUnlock;
        /// <summary>The starter's fixed signature relic.</summary>
        public RelicDef starterRelic;

        public override string Summary(Scenario scen) =>
            "Travel a branching act of nodes on the world map, ending in a boss. Each node has an objective and worsening weather." +
            (starterRelic != null ? $"\n\nSignature relic: {starterRelic.LabelCap}. {starterRelic.description}" : "");

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref starterId, "starterId", "crashlanded");
            Scribe_Values.Look(ref requiresUnlock, "requiresUnlock");
            Scribe_Defs.Look(ref starterRelic, "starterRelic");
        }

        public override void PostWorldGenerate() => WorldComponent_RoguelikeRun.Get?.GenerateAct();

        /// <summary>Catch a squad wipe the moment the last colonist dies, before the game can hand the player a new joiner.</summary>
        public override void Notify_PawnDied(Corpse corpse)
        {
            var pawn = corpse?.InnerPawn;
            var run = WorldComponent_RoguelikeRun.Get;
            // Bloodstained Codex: an entity killed on the current node grants a relic, once per node.
            if (pawn != null && ModsConfig.AnomalyActive && pawn.RaceProps.IsAnomalyEntity && run is { active: true, RunOver: false } && run.relics.Any(r => r.voidCodex)
                && corpse.MapHeld != null && run.CurrentNode?.tile == corpse.MapHeld.Tile && run.codexTile != corpse.MapHeld.Tile)
            {
                var relic = RewardUtility.RelicPool(run).RandomElementWithFallback();
                if (relic != null)
                {
                    run.codexTile = corpse.MapHeld.Tile;
                    run.AddRelic(relic);
                    Messages.Message($"Bloodstained Codex: the entity's death yields {relic.LabelCap}.", MessageTypeDefOf.PositiveEvent);
                }
            }
            if (pawn == null || pawn.Faction != Faction.OfPlayer || !pawn.RaceProps.Humanlike) return;
            if (!PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists.Any())
                WorldComponent_RoguelikeRun.Get?.Lose("Your whole squad is gone.");
        }

        public override void PostGameStart()
        {
            var map = Find.CurrentMap;
            var run = WorldComponent_RoguelikeRun.Get;
            var node = run?.BeginAt(Find.GameInfo.startingTile);
            if (map == null || node?.preset == null)
            {
                Log.Warning("[Roguelike] No starting node found at the landing tile; this run has no node objective.");
                return;
            }
            run.ascension = MetaProgress.SelectedAscension;
            run.AddRelic(starterRelic);
            // Power unlocks that hand out starting supplies.
            foreach (var t in MetaProgress.StartThings)
            {
                int left = t.count;
                while (left > 0)
                {
                    var thing = ThingMaker.MakeThing(t.thingDef, t.thingDef.MadeFromStuff ? GenStuff.DefaultStuffFor(t.thingDef) : null);
                    thing.stackCount = System.Math.Min(left, t.thingDef.stackLimit);
                    left -= thing.stackCount;
                    GenPlace.TryPlaceThing(thing, MapGenerator.PlayerStartSpot.IsValid ? MapGenerator.PlayerStartSpot : map.Center, map, ThingPlaceMode.Near);
                }
            }
            RelicEffects.OnRunStart(map);
            NodeLauncher.StartOn(map, node.preset, new LookTargets(map.Center, map));
        }
    }

    /// <summary>During a run, landing is only allowed on a starting node. Selecting the marker itself counts.</summary>
    [HarmonyPatch(typeof(Page_SelectStartingSite), "CanDoNext")]
    public static class Patch_SelectStartingSite_CanDoNext
    {
        public static bool Prefix(ref bool __result)
        {
            if (!ScenPart_RoguelikeRun.ActiveInCurrentScenario) return true;

            var selector = Find.WorldSelector;
            if (!Find.WorldInterface.SelectedTile.Valid && selector.NumSelectedObjects == 1 && selector.SelectedObjects[0] is RunNodeMarker picked)
            {
                Find.WorldInterface.SelectedTile = picked.Tile;
            }

            if (ScenPart_RoguelikeRun.StartNodeAt(Find.WorldInterface.SelectedTile) == null)
            {
                Messages.Message("Choose one of the starting nodes (bottom row) to begin the run.", MessageTypeDefOf.RejectInput, false);
                __result = false;
                return false;
            }
            return true;
        }
    }

    /// <summary>Points the camera at the starting row when the landing page opens.</summary>
    [HarmonyPatch(typeof(Page_SelectStartingSite), nameof(Page_SelectStartingSite.PostOpen))]
    public static class Patch_SelectStartingSite_PostOpen
    {
        public static void Postfix()
        {
            if (!ScenPart_RoguelikeRun.ActiveInCurrentScenario) return;
            var start = WorldComponent_RoguelikeRun.Get?.Row(0).Skip(1).FirstOrDefault() ?? WorldComponent_RoguelikeRun.Get?.Row(0).FirstOrDefault();
            if (start != null) Find.WorldCameraDriver.JumpTo(Find.WorldGrid.GetTileCenter(start.tile));
        }
    }
}
