using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Roguelike
{
    /// <summary>
    /// In-game self-test, only active when RimWorld is launched with
    /// <c>-quicktest -roguelikeSelfTest=&lt;mode&gt;</c>. It drives a real run and logs
    /// <c>[RLTEST]</c> PASS/FAIL lines to the player log, takes screenshots, then quits.
    /// </summary>
    public static class SelfTest
    {
        public const string Tag = "[RLTEST]";

        public static string Mode => GenCommandLine.TryGetCommandLineArg("roguelikeSelfTest", out var m) ? m : null;

        public static bool Enabled => Mode != null;

        public static int Passed, Failed;

        public static bool Check(bool ok, string what)
        {
            if (ok) Passed++; else Failed++;
            Log.Message($"{Tag} {(ok ? "PASS" : "FAIL")}: {what}");
            return ok;
        }

        public static void Info(string what) => Log.Message($"{Tag} INFO: {what}");
    }

    /// <summary>With the self-test flag, quicktest starts the run scenario on a starting node instead of vanilla Crashlanded.</summary>
    [HarmonyPatch(typeof(Root_Play), nameof(Root_Play.SetupForQuickTestPlay))]
    public static class Patch_QuickTest_RunScenario
    {
        public static bool Prefix()
        {
            if (!SelfTest.Enabled) return true;
            Current.ProgramState = ProgramState.Entry;
            Game.ClearCaches();
            Current.Game = new Game();
            Current.Game.InitData = new GameInitData();
            Current.Game.Scenario = DefDatabase<ScenarioDef>.GetNamed("Roguelike_RunScenario").scenario;
            Find.Scenario.PreConfigure();
            Current.Game.storyteller = new Storyteller(StorytellerDefOf.Cassandra, DifficultyDefOf.Rough);
            Current.Game.World = WorldGenerator.GenerateWorld(0.3f, "roguelike-selftest", OverallRainfall.Normal, OverallTemperature.Normal, OverallPopulation.Normal, LandmarkDensity.Normal);
            var start = WorldComponent_RoguelikeRun.Get?.Row(0).FirstOrDefault();
            if (start != null) Find.GameInitData.startingTile = start.tile;
            else Find.GameInitData.ChooseRandomStartingTile();
            Find.GameInitData.mapSize = 150;
            Find.Scenario.PostIdeoChosen();
            return false;
        }
    }

    /// <summary>Runs the self-test script one step per frame; a step yields a condition to wait for.</summary>
    public class GameComponent_SelfTest : GameComponent
    {
        private IEnumerator<Func<bool>> script;
        private Func<bool> waiting;
        private int waitStartFrame;
        private bool finished;
        private const int TimeoutFrames = 60 * 120;

        public GameComponent_SelfTest(Game game) { }

        public override void GameComponentUpdate()
        {
            if (!SelfTest.Enabled || finished) return;
            if (script == null)
            {
                SelfTest.Info($"starting mode '{SelfTest.Mode}'");
                script = SelfTestScripts.For(SelfTest.Mode).GetEnumerator();
            }
            try
            {
                if (waiting != null)
                {
                    if (UnityEngine.Time.frameCount - waitStartFrame > TimeoutFrames)
                    {
                        SelfTest.Check(false, "step timed out");
                        Finish();
                        return;
                    }
                    if (LongEventHandler.ShouldWaitForEvent || !waiting()) return;
                    waiting = null;
                }
                if (!script.MoveNext())
                {
                    Finish();
                    return;
                }
                waiting = script.Current;
                waitStartFrame = UnityEngine.Time.frameCount;
            }
            catch (Exception ex)
            {
                SelfTest.Check(false, "exception: " + ex);
                Finish();
            }
        }

        private void Finish()
        {
            finished = true;
            Log.Message($"{SelfTest.Tag} DONE mode={SelfTest.Mode} passed={SelfTest.Passed} failed={SelfTest.Failed}");
            // Give the log and any queued screenshot a moment to flush.
            LongEventHandler.ExecuteWhenFinished(() => LongEventHandler.QueueLongEvent(Root.Shutdown, "Shutdown", false, null));
        }
    }

    /// <summary>The self-test scripts. Each yield is a wait condition.</summary>
    public static class SelfTestScripts
    {
        public static IEnumerable<Func<bool>> For(string mode) => mode switch
        {
            "win" => Win(),
            "leave" => LeaveEarly(),
            "wipe" => Wipe(),
            _ => Unknown(mode),
        };

        private static IEnumerable<Func<bool>> Unknown(string mode)
        {
            SelfTest.Check(false, $"unknown self-test mode '{mode}'");
            yield break;
        }

        // ---------- helpers ----------

        private static WorldComponent_RoguelikeRun Run => WorldComponent_RoguelikeRun.Get;

        private static Func<bool> Frames(int n)
        {
            int until = UnityEngine.Time.frameCount + n;
            return () => UnityEngine.Time.frameCount >= until;
        }

        private static Func<bool> Ticks(int n)
        {
            Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
            int until = Find.TickManager.TicksGame + n;
            return () =>
            {
                if (Find.TickManager.Paused) Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
                return Find.TickManager.TicksGame >= until;
            };
        }

        private static IEnumerable<Func<bool>> Screenshot(string name, bool world)
        {
            if (world) CameraJumper.TryShowWorld();
            yield return Frames(45);
            ScreenshotTaker.TakeNonSteamShot("rltest_" + name);
            SelfTest.Info($"screenshot rltest_{name}.png");
            yield return Frames(20);
        }

        private static IEnumerable<Func<bool>> WaitForGame()
        {
            yield return () => Current.ProgramState == ProgramState.Playing && Find.CurrentMap != null;
            yield return Frames(60);
            // Crashlanded colonists arrive in drop pods; let time run until they are all out.
            Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
            yield return () =>
            {
                if (Find.TickManager.Paused) Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
                var map = Find.CurrentMap;
                return map.mapPawns.FreeColonistsSpawnedCount > 0
                       && map.mapPawns.FreeColonistsSpawnedCount == PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists.Count();
            };
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            SelfTest.Info($"{Find.CurrentMap.mapPawns.FreeColonistsSpawnedCount} colonists on the map");
        }

        private static MapComponent_RoguelikeNode NodeComp(Map map) => map?.GetComponent<MapComponent_RoguelikeNode>();

        private static Caravan LeaveMap(Map map)
        {
            var pawns = map.mapPawns.FreeColonistsSpawned.Concat(map.mapPawns.SpawnedColonyAnimals).ToList();
            var caravan = CaravanExitMapUtility.ExitMapAndCreateCaravan(pawns, Faction.OfPlayer, map.Tile, map.Tile, PlanetTile.Invalid, sendMessage: false);
            if (Find.Maps.Contains(map)) map.Parent.CheckRemoveMapNow();
            Letter_NodeComplete.AbandonIfEmpty(map);
            return caravan;
        }

        private static void CheckGraph()
        {
            var run = Run;
            SelfTest.Check(run is { active: true }, "run is active after world generation");
            SelfTest.Check(run.nodes.Count == 15, $"act has 15 nodes (got {run.nodes.Count})");
            var sizes = Enumerable.Range(0, 6).Select(r => run.Row(r).Count()).ToList();
            SelfTest.Check(sizes.SequenceEqual(new[] { 3, 3, 3, 3, 2, 1 }), $"row sizes 3,3,3,3,2,1 (got {string.Join(",", sizes)})");
            SelfTest.Check(run.nodes.Select(n => n.tile).Distinct().Count() == run.nodes.Count, "every node has its own tile");
            float minGap = run.nodes.SelectMany(a => run.nodes.Where(b => b.id > a.id).Select(b => Find.WorldGrid.ApproxDistanceInTiles(a.tile, b.tile))).Min();
            SelfTest.Check(minGap >= 2.9f, $"nodes are at least ~3 tiles apart (min {minGap:0.0})");
            SelfTest.Check(run.nodes.Count(n => n.IsBoss) == 1 && run.nodes.Last().IsBoss, "exactly one boss, in the last row");
            SelfTest.Check(run.nodes.Where(n => !n.IsBoss).All(n => n.next.Count > 0), "every non-boss node leads somewhere");
            SelfTest.Check(run.nodes.Where(n => n.row > 0).All(n => run.nodes.Any(p => p.next.Contains(n.id))), "every non-start node has an incoming edge");
            bool crossing = false;
            for (int r = 0; r < 5; r++)
            {
                var edges = run.Row(r).SelectMany(a => a.next.Select(id => (a.col, run.NodeById(id).col))).ToList();
                crossing |= edges.Any(e => edges.Any(f => (e.Item1 < f.Item1 && e.Item2 > f.Item2)));
            }
            SelfTest.Check(!crossing, "no edges cross");
            SelfTest.Check(run.nodes.All(n => n.preset != null && n.preset.isBoss == n.IsBoss), "every node has a preset of the right kind");
            SelfTest.Check(Find.WorldObjects.AllWorldObjects.OfType<RunNodeMarker>().Count() == 15, "15 markers on the world map");
            foreach (var n in run.nodes)
                SelfTest.Info($"node {n.id} row {n.row} col {n.col} tile {n.tile} {n.preset.defName} -> [{string.Join(",", n.next)}]");
        }

        private static void CheckStartedNode(Map map, RunNode node)
        {
            var comp = NodeComp(map);
            SelfTest.Check(comp is { Active: true } && comp.preset == node.preset, $"node {node.id}: map has the node objective ({node.preset.defName})");
            SelfTest.Check(comp?.Condition != null, $"node {node.id}: weather condition registered");
            SelfTest.Check(comp != null && comp.InGrace && comp.Condition != null && comp.Condition.offset == 0f, $"node {node.id}: weather holds off during grace");
        }

        // ---------- scripts ----------

        /// <summary>Clears every node along a path to the boss and checks the run is won.</summary>
        private static IEnumerable<Func<bool>> Win()
        {
            foreach (var w in WaitForGame()) yield return w;
            var run = Run;
            CheckGraph();
            var start = run.CurrentNode;
            SelfTest.Check(start != null && start.row == 0, "landed on a starting node, now current");
            SelfTest.Check(run.Row(0).Count(n => n.state == RunNodeState.Skipped) == 2, "the other starting nodes are skipped");
            var map = Find.CurrentMap;
            CheckStartedNode(map, start);

            // Grace then pace-driven change.
            var comp = NodeComp(map);
            comp.startTick -= (int)((comp.preset.graceDays + 0.5f) * GenDate.TicksPerDay);
            comp.AdvanceThreat(1f);
            float off = comp.Condition.offset;
            SelfTest.Check(off != 0f && Math.Sign(off) == Math.Sign(comp.preset.offsetPerDay), $"after grace the weather moves the right way ({off:0.0}°C, pace x{comp.PaceFactor:0.00})");

            foreach (var w in Screenshot("s1_act_start", world: true)) yield return w;

            var node = start;
            for (int step = 0; step < 10; step++)
            {
                NodeComp(map).Complete();
                SelfTest.Check(node.state == RunNodeState.Completed, $"node {node.id}: completed");
                if (node.IsBoss)
                {
                    SelfTest.Check(run.runWon && !run.runLost, "boss cleared: run won");
                    break;
                }
                SelfTest.Check(node.next.All(id => run.NodeById(id).state == RunNodeState.Available), $"node {node.id}: its {node.next.Count} next nodes are available");

                var locked = run.nodes.FirstOrDefault(n => n.state == RunNodeState.Locked);
                if (locked != null)
                {
                    var lockedMarker = Find.WorldObjects.AllWorldObjects.OfType<RunNodeMarker>().First(m => m.nodeId == locked.id);
                    SelfTest.Check(!CaravanArrivalAction_EnterRunNode.CanEnter(lockedMarker).Accepted, $"locked node {locked.id} refuses entry");
                }

                var caravan = LeaveMap(map);
                yield return Ticks(600);
                SelfTest.Check(!run.RunOver, $"node {node.id}: leaving after completion does not end the run");
                SelfTest.Check(Current.Game.FindMap(node.tile) == null, $"node {node.id}: map removed after leaving");

                var nextNode = run.NodeById(node.next.Last());
                var siblings = node.next.Where(id => id != nextNode.id).ToList();
                var marker = Find.WorldObjects.AllWorldObjects.OfType<RunNodeMarker>().First(m => m.nodeId == nextNode.id);
                SelfTest.Check(CaravanArrivalAction_EnterRunNode.CanEnter(marker).Accepted, $"next node {nextNode.id} accepts entry");
                caravan.Tile = nextNode.tile;
                caravan.pather.StopDead();
                new CaravanArrivalAction_EnterRunNode(marker).Arrived(caravan);
                yield return () => Current.Game.FindMap(nextNode.tile) != null;
                yield return Frames(30);

                map = Current.Game.FindMap(nextNode.tile);
                SelfTest.Check(nextNode.state == RunNodeState.Current, $"entered node {nextNode.id}: now current");
                SelfTest.Check(siblings.All(id => run.NodeById(id).state == RunNodeState.Skipped), $"entered node {nextNode.id}: siblings skipped");
                SelfTest.Check(map.mapPawns.FreeColonistsSpawnedCount > 0, $"entered node {nextNode.id}: colonists arrived");
                CheckStartedNode(map, nextNode);
                if (step == 0)
                    foreach (var w in Screenshot("s1_act_after_first_move", world: true)) yield return w;
                node = nextNode;
            }
            SelfTest.Check(run.runWon, "run ends won");
            foreach (var w in Screenshot("s1_act_won", world: true)) yield return w;
        }

        /// <summary>Leaving the current node before its objective is met ends the run.</summary>
        private static IEnumerable<Func<bool>> LeaveEarly()
        {
            foreach (var w in WaitForGame()) yield return w;
            var run = Run;
            var node = run.CurrentNode;
            LeaveMap(Find.CurrentMap);
            yield return Ticks(600);
            SelfTest.Check(run.runLost && !run.runWon, $"leaving node {node?.id} early ends the run");
            var other = run.nodes.First(n => n.row == 1);
            SelfTest.Check(!run.CanEnter(other), "no node can be entered after the run is lost");
        }

        /// <summary>Losing the whole squad ends the run.</summary>
        private static IEnumerable<Func<bool>> Wipe()
        {
            foreach (var w in WaitForGame()) yield return w;
            var run = Run;
            foreach (var p in Find.CurrentMap.mapPawns.FreeColonistsSpawned.ToList()) p.Kill(null);
            yield return Ticks(600);
            var alive = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists.ToList();
            SelfTest.Info($"alive colonists after wipe: {alive.Count} [{string.Join(", ", alive.Select(p => $"{p.LabelShort} dead={p.Dead} spawned={p.Spawned} map={p.MapHeld}"))}]");
            SelfTest.Check(run.runLost, "squad wipe ends the run");
        }
    }
}
