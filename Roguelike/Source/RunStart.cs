using System.Collections.Generic;
using System.Linq;
using System.Text;
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

    /// <summary>A starting-node choice shown on the world map during landing-site selection.</summary>
    public class StartNodeMarker : WorldObject
    {
        public NodePresetDef preset;

        public override string Label => "Start: " + (preset?.label ?? def.label);

        public override string GetInspectString()
        {
            var sb = new StringBuilder(base.GetInspectString());
            if (preset != null)
            {
                if (sb.Length > 0) sb.AppendLine();
                // Inspect strings must not contain blank lines.
                sb.Append(preset.Briefing.Replace("\n\n", "\n"));
            }
            return sb.ToString();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref preset, "preset");
        }
    }

    /// <summary>Scenario part for a run: places starting nodes after world generation and starts the chosen one.</summary>
    public class ScenPart_RoguelikeRun : ScenPart
    {
        private const int StartNodeCount = 3;
        private const float MinNodeSpacingTiles = 12f;

        public static bool ActiveInCurrentScenario => Find.Scenario?.AllParts.OfType<ScenPart_RoguelikeRun>().Any() == true;

        public static IEnumerable<StartNodeMarker> Markers => Find.WorldObjects.AllWorldObjects.OfType<StartNodeMarker>();

        public static StartNodeMarker MarkerAt(PlanetTile tile) => Markers.FirstOrDefault(m => m.Tile == tile);

        public override string Summary(Scenario scen) => "Choose one of several starting nodes on the world map. Each node has an objective and worsening weather.";

        public override void PostWorldGenerate()
        {
            var presets = DefDatabase<NodePresetDef>.AllDefsListForReading.InRandomOrder().ToList();
            var placed = new List<PlanetTile>();
            for (int i = 0; i < StartNodeCount; i++)
            {
                if (!TryFindSpacedTile(placed, out var tile)) break;
                var marker = (StartNodeMarker)WorldObjectMaker.MakeWorldObject(RoguelikeDefOf.Roguelike_StartNode);
                marker.Tile = tile;
                marker.preset = presets[i % presets.Count];
                Find.WorldObjects.Add(marker);
                placed.Add(tile);
            }
        }

        private static bool TryFindSpacedTile(List<PlanetTile> placed, out PlanetTile tile)
        {
            for (int attempt = 0; attempt < 100; attempt++)
            {
                tile = TileFinder.RandomStartingTile();
                var t = tile;
                if (tile.Valid && placed.All(p => Find.WorldGrid.ApproxDistanceInTiles(p, t) >= MinNodeSpacingTiles)) return true;
            }
            tile = PlanetTile.Invalid;
            return false;
        }

        public override void PostGameStart()
        {
            var map = Find.CurrentMap;
            var chosen = MarkerAt(Find.GameInfo.startingTile);
            var preset = chosen?.preset;
            foreach (var m in Markers.ToList()) m.Destroy();
            if (map == null || preset == null)
            {
                Log.Warning("[Roguelike] No starting node found at the landing tile; this run has no node objective.");
                return;
            }
            NodeLauncher.StartOn(map, preset, new LookTargets(map.Center, map));
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
            if (!Find.WorldInterface.SelectedTile.Valid && selector.NumSelectedObjects == 1 && selector.SelectedObjects[0] is StartNodeMarker picked)
            {
                Find.WorldInterface.SelectedTile = picked.Tile;
            }

            if (ScenPart_RoguelikeRun.MarkerAt(Find.WorldInterface.SelectedTile) == null)
            {
                Messages.Message("Choose one of the starting nodes to begin the run.", MessageTypeDefOf.RejectInput, false);
                __result = false;
                return false;
            }
            return true;
        }
    }

    /// <summary>Points the camera at the first starting node when the landing page opens.</summary>
    [HarmonyPatch(typeof(Page_SelectStartingSite), nameof(Page_SelectStartingSite.PostOpen))]
    public static class Patch_SelectStartingSite_PostOpen
    {
        public static void Postfix()
        {
            if (!ScenPart_RoguelikeRun.ActiveInCurrentScenario) return;
            var first = ScenPart_RoguelikeRun.Markers.FirstOrDefault();
            if (first != null) Find.WorldCameraDriver.JumpTo(Find.WorldGrid.GetTileCenter(first.Tile));
        }
    }
}
