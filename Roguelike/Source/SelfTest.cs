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
            "rewards" => Rewards(),
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
            float until = UnityEngine.Time.realtimeSinceStartup + 1.5f;
            yield return () => UnityEngine.Time.realtimeSinceStartup >= until;
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
                foreach (var w in PickFirstReward(node)) yield return w;
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

        private static Dialog_RewardPick RewardWindow => Find.WindowStack.WindowOfType<Dialog_RewardPick>();

        /// <summary>The reward screen opened for <paramref name="node"/>: pick the first card and wait for it to close.</summary>
        private static IEnumerable<Func<bool>> PickFirstReward(RunNode node)
        {
            var window = RewardWindow;
            if (!SelfTest.Check(window != null && window.Cards.Count >= 3, $"node {node.id}: reward screen opened with {window?.Cards.Count ?? 0} cards")) yield break;
            if (node.reward == RunRewardKind.Recruits)
                SelfTest.Check(window.Cards.All(c => c.kind == RewardKind.Recruit), $"node {node.id}: recruit node offers only recruits");
            yield return Frames(40);
            window.Pick(0);
            yield return () => RewardWindow == null;
            SelfTest.Check(Run.pendingRewardNodeId < 0, $"node {node.id}: reward resolved ({window.Cards[0].KindLabel}: {window.Cards[0].Title})");
        }

        private static int CountOnMap(Map map, string thingDef) =>
            map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed(thingDef)).Sum(t => t.stackCount);

        private static bool BuildVisible(string thingDef) => new Designator_Build(DefDatabase<ThingDef>.GetNamed(thingDef)).Visible;

        /// <summary>Card screen, every reward kind, relic effects and the blueprint build-menu gate.</summary>
        private static IEnumerable<Func<bool>> Rewards()
        {
            foreach (var w in WaitForGame()) yield return w;
            var run = Run;
            var map = Find.CurrentMap;
            var node = run.CurrentNode;
            var colonist = map.mapPawns.FreeColonistsSpawned.First();

            // Build-menu gate.
            SelfTest.Check(!BuildVisible("Heater"), "un-drafted blueprint building (heater) is hidden from the build menu");
            SelfTest.Check(BuildVisible("Wall"), "ungated building (wall) stays in the build menu");

            // Reward screen after clearing a node.
            node.reward = RunRewardKind.Cards;
            NodeComp(map).Complete();
            var window = RewardWindow;
            SelfTest.Check(window != null && window.Cards.Count == 3, $"reward screen shows 3 cards (got {window?.Cards.Count ?? 0})");
            SelfTest.Check(window != null && window.Cards.All(c => !c.Title.NullOrEmpty() && !Dialog_RewardPick.BodyText(c).NullOrEmpty()), "every card has a title and body text");
            SelfTest.Check(run.pendingRewardNodeId == node.id, "reward is pending until picked");
            Find.CameraDriver.JumpToCurrentMapLoc(colonist.Position);
            foreach (var w in Screenshot("s2_reward_cards", world: false)) yield return w;
            window.Pick(1);
            yield return () => RewardWindow == null;
            SelfTest.Check(run.pendingRewardNodeId < 0, $"picked card resolves the reward ({window.Cards[1].KindLabel}: {window.Cards[1].Title})");

            // Every reward kind applies.
            var liners = DefDatabase<RelicDef>.GetNamed("Roguelike_Relic_InsulatedLiners");
            RewardUtility.Apply(new RewardCard { kind = RewardKind.Relic, relic = liners }, map);
            SelfTest.Check(run.relics.Contains(liners) && colonist.health.hediffSet.HasHediff(liners.hediff), "hediff relic: owned and colonists get its effect");

            var comp = NodeComp(map);
            float grace = comp.GraceDays;
            RewardUtility.Apply(new RewardCard { kind = RewardKind.Relic, relic = DefDatabase<RelicDef>.GetNamed("Roguelike_Relic_Barometer") }, map);
            SelfTest.Check(Math.Abs(comp.GraceDays - grace - 1.5f) < 0.01f, $"rule relic: grace {grace:0.0} -> {comp.GraceDays:0.0} days");

            float cap = MassUtility.Capacity(colonist);
            RewardUtility.Apply(new RewardCard { kind = RewardKind.Relic, relic = DefDatabase<RelicDef>.GetNamed("Roguelike_Relic_PackFrame") }, map);
            SelfTest.Check(Math.Abs(MassUtility.Capacity(colonist) - cap * 1.4f) < 0.01f, $"carry relic: colonist capacity {cap:0} -> {MassUtility.Capacity(colonist):0} kg");

            int parkas = CountOnMap(map, "Apparel_Parka");
            RewardUtility.Apply(new RewardCard { kind = RewardKind.Gear, gear = DefDatabase<GearRewardDef>.GetNamed("Roguelike_Gear_Parka") }, map);
            SelfTest.Check(CountOnMap(map, "Apparel_Parka") == parkas + 1, "gear reward appears next to the squad");

            int meds = CountOnMap(map, "MedicineIndustrial");
            RewardUtility.Apply(new RewardCard { kind = RewardKind.Gear, gear = DefDatabase<GearRewardDef>.GetNamed("Roguelike_Gear_Medicine") }, map);
            SelfTest.Check(CountOnMap(map, "MedicineIndustrial") == meds + 12, "stacked gear reward delivers the full count");

            int arms = CountOnMap(map, "BionicArm");
            RewardUtility.Apply(new RewardCard { kind = RewardKind.Implant, gear = DefDatabase<GearRewardDef>.GetNamed("Roguelike_Implant_BionicArm") }, map);
            SelfTest.Check(CountOnMap(map, "BionicArm") == arms + 1, "implant reward appears next to the squad");

            RewardUtility.Apply(new RewardCard { kind = RewardKind.Blueprint, blueprint = DefDatabase<BlueprintGroupDef>.GetNamed("Roguelike_BP_Climate") }, map);
            SelfTest.Check(BuildVisible("Heater") && !BuildVisible("Sandbags"), "drafted blueprint group unlocks only its buildings");

            int colonists = map.mapPawns.FreeColonistsSpawnedCount;
            var recruit = RewardUtility.GenerateRecruit();
            RewardUtility.Apply(new RewardCard { kind = RewardKind.Recruit, recruit = recruit }, map);
            SelfTest.Check(map.mapPawns.FreeColonistsSpawnedCount == colonists + 1 && recruit.IsColonist, "recruit joins the squad on the map");
            yield return Ticks(300);
            SelfTest.Check(recruit.health.hediffSet.HasHediff(liners.hediff), "newcomers get hediff relic effects too");

            // Recruit-only draw and the recruit card layout.
            var recruitCards = RewardUtility.Generate(run, RunRewardKind.Recruits);
            SelfTest.Check(recruitCards.Count == 3 && recruitCards.All(c => c.kind == RewardKind.Recruit && c.recruit != null), "recruit node draw: 3 recruit cards");
            SelfTest.Check(recruitCards.All(c => Dialog_RewardPick.RecruitSummary(c.recruit).Contains("Skills:") && Dialog_RewardPick.RecruitSummary(c.recruit).Contains("Health:")), "recruit cards list skills and health");
            Find.WindowStack.Add(new Dialog_RewardPick(recruitCards, "Self-test: recruits", null));
            foreach (var w in Screenshot("s2_recruit_cards", world: false)) yield return w;
            RewardWindow.Skip();
            yield return () => RewardWindow == null;
            SelfTest.Check(recruitCards.All(c => !c.recruit.Spawned && !c.recruit.IsColonist || c.recruit.Faction == Faction.OfPlayer && !c.recruit.Spawned), "skipped recruits do not join");
            SelfTest.Info("relics now: " + string.Join(", ", run.relics.Select(r => r.label)));
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
