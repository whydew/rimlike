using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Roguelike
{
    /// <summary>
    /// "New run" from the end-of-run screen: back to the main menu, then straight to the landing screen of a
    /// freshly generated copy of the same world (same seed and settings), with the same starter and storyteller.
    /// The new run's act graph is laid out again during world generation.
    /// </summary>
    public static class NewRun
    {
        private class Pending
        {
            public ScenarioDef scenario;
            public StorytellerDef storyteller;
            public DifficultyDef difficultyDef;
            public Difficulty difficulty;
            public string seed;
            public float coverage;
            public OverallRainfall rainfall;
            public OverallTemperature temperature;
            public OverallPopulation population;
            public LandmarkDensity landmarks;
            public List<FactionDef> factions;
            public float pollution;
        }

        private static Pending pending;

        public static bool IsPending => pending != null;

        public static void Start()
        {
            var info = Find.World.info;
            pending = new Pending
            {
                scenario = DefDatabase<ScenarioDef>.AllDefs.FirstOrDefault(d => d.scenario.name == Find.Scenario.name)
                           ?? DefDatabase<ScenarioDef>.GetNamed("Roguelike_RunScenario"),
                storyteller = Find.Storyteller.def,
                difficultyDef = Find.Storyteller.difficultyDef,
                difficulty = Find.Storyteller.difficulty,
                seed = info.seedString,
                coverage = info.planetCoverage,
                rainfall = info.overallRainfall,
                temperature = info.overallTemperature,
                population = info.overallPopulation,
                landmarks = info.landmarkDensity,
                factions = info.factions?.ToList(),
                pollution = info.pollution,
            };
            GenScene.GoToMainMenu();
        }

        /// <summary>Called in the main menu scene: set up the game and jump to the page after world creation.</summary>
        internal static void Begin()
        {
            var p = pending;
            pending = null;
            if (p == null) return;

            Game.ClearCaches();
            Current.Game = new Game { InitData = new GameInitData() };
            Current.Game.Scenario = p.scenario.scenario;
            Current.Game.Scenario.PreConfigure();
            Find.GameInitData.startedFromEntry = true;
            Current.Game.storyteller = new Storyteller(p.storyteller, p.difficultyDef, p.difficulty);

            // The scenario's page chain includes storyteller and world pages; resume after the world page.
            Page page = Current.Game.Scenario.GetFirstConfigPage();
            while (page != null && page is not Page_CreateWorldParams) page = page.next;
            var afterWorld = page?.next;

            LongEventHandler.QueueLongEvent(() =>
            {
                Find.GameInitData.ResetWorldRelatedMapInitData();
                Current.Game.World = WorldGenerator.GenerateWorld(p.coverage, p.seed, p.rainfall, p.temperature, p.population, p.landmarks, p.factions, p.pollution);
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    if (afterWorld != null) Find.WindowStack.Add(afterWorld);
                    else Log.Error("[Roguelike] New run: couldn't find the landing page in the scenario's setup pages.");
                    Find.World.renderer.RegenerateAllLayersNow();
                });
            }, "GeneratingWorld", doAsynchronously: true, null);
        }
    }

    [HarmonyPatch(typeof(Root_Entry), nameof(Root_Entry.Start))]
    public static class Patch_RootEntry_Start_NewRun
    {
        public static void Postfix()
        {
            if (NewRun.IsPending) LongEventHandler.ExecuteWhenFinished(NewRun.Begin);
        }
    }

    /// <summary>Once a run is over, the mod's end-of-run screen replaces vanilla's game-over and new-wanderers flow.</summary>
    [HarmonyPatch(typeof(GameEnder), nameof(GameEnder.CheckOrUpdateGameOver))]
    public static class Patch_GameEnder_SuppressDuringRun
    {
        public static bool Prefix() => WorldComponent_RoguelikeRun.Get is not { active: true, RunOver: true };
    }
}
