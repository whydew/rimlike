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

        /// <summary>Starter loadout id; locked starters are hidden until bought with meta points.</summary>
        public string starterId = "crashlanded";
        public bool requiresUnlock;
        /// <summary>The starter's fixed signature relic.</summary>
        public RelicDef starterRelic;

        public override string Summary(Scenario scen) =>
            "Travel a branching act of nodes on the act map, ending in a boss. Each node has an objective and worsening weather." +
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
            if (run.hubMode)
            {
                // The hub has no objective: it's home. The frontier is where the objectives are.
                Find.LetterStack.ReceiveLetter("Your hub", "This colony is yours for the whole run. Build it up.\n\n" +
                    "Use Send a team (top right) to send colonists out to the frontier: nodes on the world map that spread outward as you clear them, " +
                    $"harder the further out they are. Elites wait every {Hub.EliteEvery} nodes out, bosses every {Hub.BossEvery}, and the final boss at depth {Hub.FinalDepth}.\n\n" +
                    "The hub keeps running while teams are away. Its weather keeps worsening until frontier nodes push it back, and sieges come regularly. " +
                    "Cleared nodes can be revisited with a harvest trip for their resources.", LetterDefOf.NeutralEvent, new LookTargets(map.Center, map));
                return;
            }
            NodeLauncher.StartOn(map, node.preset, new LookTargets(map.Center, map), node.modifier);
        }
    }
}

namespace Roguelike
{
    /// <summary>During a run, leaving a node only takes what the squad can carry: an overweight caravan is refused.</summary>
    [HarmonyLib.HarmonyPatch(typeof(Dialog_FormCaravan), "CheckForErrors")]
    public static class Patch_FormCaravan_CarryLimit
    {
        public static void Postfix(Dialog_FormCaravan __instance, ref bool __result)
        {
            if (!__result || WorldComponent_RoguelikeRun.Get is not { active: true, RunOver: false }) return;
            var t = HarmonyLib.Traverse.Create(__instance);
            if (!t.Field("reform").GetValue<bool>()) return;
            float usage = t.Property("MassUsage").GetValue<float>(), capacity = t.Property("MassCapacity").GetValue<float>();
            if (usage <= capacity) return;
            Messages.Message($"Too heavy: {usage:0} kg packed, but the squad can only carry {capacity:0} kg.", MessageTypeDefOf.RejectInput, false);
            __result = false;
        }
    }
}
