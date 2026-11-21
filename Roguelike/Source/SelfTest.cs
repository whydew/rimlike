using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

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
            MetaProgress.ResetForSelfTest();
            Current.ProgramState = ProgramState.Entry;
            Game.ClearCaches();
            Current.Game = new Game();
            Current.Game.InitData = new GameInitData();
            string scenario = GenCommandLine.TryGetCommandLineArg("roguelikeScenario", out var s) ? s : "Roguelike_RunScenario";
            Current.Game.Scenario = DefDatabase<ScenarioDef>.GetNamed(scenario).scenario;
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
            "variety" => Variety(),
            "meta" => Meta(),
            "starter" => Starter(),
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

        /// <summary>Runs time until <paramref name="done"/> (a run-ending screen may pause the game, so this does not count ticks).</summary>
        private static Func<bool> Until(Func<bool> done)
        {
            Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
            return () =>
            {
                if (done()) return true;
                if (Find.TickManager.Paused) Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
                return false;
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

        private static Caravan LeaveMap(Map map, bool abandon = true)
        {
            var pawns = map.mapPawns.FreeColonistsSpawned.Concat(map.mapPawns.SpawnedColonyAnimals).ToList();
            var caravan = CaravanExitMapUtility.ExitMapAndCreateCaravan(pawns, Faction.OfPlayer, map.Tile, map.Tile, PlanetTile.Invalid, sendMessage: false);
            if (Find.Maps.Contains(map)) map.Parent.CheckRemoveMapNow();
            if (abandon) Letter_NodeComplete.AbandonIfEmpty(map);
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
            if (node.preset.condition != null)
            {
                SelfTest.Check(comp?.Condition != null, $"node {node.id}: weather condition registered");
                SelfTest.Check(comp != null && comp.InGrace && comp.Condition != null && comp.Condition.intensity == 0f, $"node {node.id}: weather holds off during grace");
            }
            if (node.preset.objective == ObjectiveKind.KillAll)
                SelfTest.Check(comp != null && comp.EnemiesLeft > 0, $"node {node.id}: enemies are on the map ({comp?.EnemiesLeft})");
        }

        private static Dialog_NodeTree EventWindow => Find.WindowStack.WindowOfType<Dialog_NodeTree>();

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
            float off = comp.Condition.intensity;
            SelfTest.Check(off != 0f && Math.Sign(off) == Math.Sign(comp.preset.offsetPerDay), $"after grace the weather moves the right way ({comp.Condition.IntensityText(off)}, pace x{comp.PaceFactor:0.00})");

            foreach (var w in Screenshot("s1_act_start", world: true)) yield return w;

            var node = start;
            Caravan caravan = null;
            for (int step = 0; step < 10; step++)
            {
                if (node.preset.HasMap) NodeComp(map).Complete();
                SelfTest.Check(node.state == RunNodeState.Completed, $"node {node.id}: completed");
                if (node.IsBoss)
                {
                    SelfTest.Check(run.runWon && !run.runLost, "boss cleared: run won");
                    break;
                }
                if (node.reward != RunRewardKind.None)
                    foreach (var w in PickFirstReward(node)) yield return w;
                SelfTest.Check(node.next.All(id => run.NodeById(id).state == RunNodeState.Available), $"node {node.id}: its {node.next.Count} next nodes are available");

                var locked = run.nodes.FirstOrDefault(n => n.state == RunNodeState.Locked);
                if (locked != null)
                {
                    var lockedMarker = Find.WorldObjects.AllWorldObjects.OfType<RunNodeMarker>().First(m => m.nodeId == locked.id);
                    SelfTest.Check(!CaravanArrivalAction_EnterRunNode.CanEnter(lockedMarker).Accepted, $"locked node {locked.id} refuses entry");
                }

                if (node.preset.HasMap)
                {
                    caravan = LeaveMap(map);
                    yield return Ticks(600);
                    SelfTest.Check(Current.Game.FindMap(node.tile) == null, $"node {node.id}: map removed after leaving");
                }
                else yield return Ticks(600);
                SelfTest.Check(!run.RunOver, $"node {node.id}: moving on after completion does not end the run");

                var nextNode = run.NodeById(node.next.Last());
                if (step == 1 && nextNode.preset.HasMap && !nextNode.IsBoss)
                {
                    // Make sure the path crosses an event node so the arrival dialog is exercised in a real run.
                    nextNode.preset = Preset("Roguelike_Encounter");
                    nextNode.reward = RunRewardKind.None;
                }
                var siblings = node.next.Where(id => id != nextNode.id).ToList();
                var marker = Find.WorldObjects.AllWorldObjects.OfType<RunNodeMarker>().First(m => m.nodeId == nextNode.id);
                SelfTest.Check(CaravanArrivalAction_EnterRunNode.CanEnter(marker).Accepted, $"next node {nextNode.id} ({nextNode.preset.kind}) accepts entry");
                caravan.Tile = nextNode.tile;
                caravan.pather.StopDead();
                new CaravanArrivalAction_EnterRunNode(marker).Arrived(caravan);

                if (nextNode.preset.HasMap)
                {
                    yield return () => Current.Game.FindMap(nextNode.tile) != null;
                    yield return Frames(30);
                    map = Current.Game.FindMap(nextNode.tile);
                    SelfTest.Check(map.mapPawns.FreeColonistsSpawnedCount > 0, $"entered node {nextNode.id}: colonists arrived");
                    CheckStartedNode(map, nextNode);
                    if (nextNode.IsBoss)
                    {
                        Find.CameraDriver.JumpToCurrentMapLoc(map.mapPawns.FreeColonistsSpawned.First().Position);
                        foreach (var w in Screenshot("s3_boss", world: false)) yield return w;
                    }
                }
                else
                {
                    yield return () => EventWindow != null;
                    SelfTest.Check(nextNode.state == RunNodeState.Current, $"event node {nextNode.id}: dialog opened on arrival");
                    foreach (var w in Screenshot("s3_event_on_arrival", world: true)) yield return w;
                    EventWindow.Close(false);
                    string result = EventUtility.Resolve(nextNode, caravan, DefDatabase<RoguelikeEventDef>.AllDefsListForReading.First(), 0);
                    SelfTest.Check(!result.NullOrEmpty() && nextNode.state == RunNodeState.Completed, $"event node {nextNode.id}: resolved ({result.Split('\n')[0]})");
                    map = null;
                }
                SelfTest.Check(nextNode.state is RunNodeState.Current or RunNodeState.Completed, $"entered node {nextNode.id}: it is the current node");
                SelfTest.Check(siblings.All(id => run.NodeById(id).state == RunNodeState.Skipped), $"entered node {nextNode.id}: siblings skipped");
                if (step == 0)
                    foreach (var w in Screenshot("s1_act_after_first_move", world: true)) yield return w;
                node = nextNode;
            }
            SelfTest.Check(run.runWon, "run ends won");
            foreach (var w in Screenshot("s1_act_won", world: true)) yield return w;
        }

        private static void KillAll(IEnumerable<Pawn> pawns)
        {
            foreach (var p in pawns.ToList()) if (!p.Dead) p.Kill(null);
        }

        private static NodePresetDef Preset(string name) => DefDatabase<NodePresetDef>.GetNamed(name);

        /// <summary>Enters a debug node with <paramref name="preset"/> from the current map and waits for it.</summary>
        private static IEnumerable<Func<bool>> EnterDebugNode(string preset)
        {
            var from = Find.CurrentMap;
            NodeLauncher.Enter(from, Preset(preset));
            yield return () => Find.CurrentMap != from && NodeComp(Find.CurrentMap) is { Active: true };
            yield return Frames(20);
        }

        /// <summary>Every node kind, objective and modifier, using debug nodes off the run graph.</summary>
        private static IEnumerable<Func<bool>> Variety()
        {
            foreach (var w in WaitForGame()) yield return w;
            var run = Run;
            // Debug nodes move the squad off the run graph; treat the starting node as done so leaving it is not a loss.
            run.CurrentNode.state = RunNodeState.Completed;

            // Alerted combat.
            foreach (var w in EnterDebugNode("Roguelike_Ambush")) yield return w;
            var map = Find.CurrentMap;
            var comp = NodeComp(map);
            SelfTest.Check(comp.enemyFaction != null && comp.enemyFaction.HostileTo(Faction.OfPlayer), $"ambush: hostile enemy faction ({comp.enemyFaction?.Name})");
            SelfTest.Check(comp.EnemiesLeft > 0, $"ambush: raid is on the map ({comp.EnemiesLeft} enemies, {comp.raidsLaunched} raids, faction {comp.enemyFaction?.def.defName}, hostiles on map {map.mapPawns.AllPawnsSpawned.Count(p => p.HostileTo(Faction.OfPlayer))})");
            Find.CameraDriver.JumpToCurrentMapLoc(map.mapPawns.FreeColonistsSpawned.First().Position);
            foreach (var w in Screenshot("s3_ambush", world: false)) yield return w;
            KillAll(map.mapPawns.SpawnedPawnsInFaction(comp.enemyFaction));
            yield return Ticks(300);
            SelfTest.Check(comp.completed, "ambush: defeating every enemy completes the node");

            // Unaware combat camp with patrols, reinforcements and toxic weather.
            foreach (var w in EnterDebugNode("Roguelike_RaiderCamp")) yield return w;
            map = Find.CurrentMap;
            comp = NodeComp(map);
            int campers = comp.EnemiesLeft;
            SelfTest.Check(campers > 0, $"raider camp: camp spawned ({campers} enemies)");
            var squad = map.mapPawns.FreeColonistsSpawned.ToList();
            float nearest = map.mapPawns.SpawnedPawnsInFaction(comp.enemyFaction).Min(e => squad.Min(c => c.Position.DistanceTo(e.Position)));
            SelfTest.Check(nearest > 40f, $"raider camp: camp is far from the squad ({nearest:0} cells)");
            SelfTest.Check(map.mapPawns.SpawnedPawnsInFaction(comp.enemyFaction).All(e => e.GetLord()?.LordJob is Verse.AI.Group.LordJob_DefendPoint), "raider camp: enemies hold their camp until engaged");
            int raids = comp.raidsLaunched;
            comp.nextPatrolTick = Find.TickManager.TicksGame + 1;
            yield return Ticks(300);
            SelfTest.Check(comp.raidsLaunched == raids + 1 && comp.nextPatrolTick > Find.TickManager.TicksGame, $"raider camp: a patrol raid arrives and the next one is scheduled ({comp.EnemiesLeft} enemies)");

            comp.reinforcementTick = Find.TickManager.TicksGame + 1;
            yield return Ticks(300);
            SelfTest.Check(comp.raidsLaunched == raids + 2 && comp.reinforcementTick < 0, $"raider camp: reinforcements arrive once ({comp.EnemiesLeft} enemies)");
            SelfTest.Check(comp.Condition is GameCondition_EscalatingToxic, "raider camp: toxic weather registered");
            comp.startTick -= (int)(comp.GraceDays * GenDate.TicksPerDay + 1000);
            comp.Condition.intensity = 1.5f;
            yield return Ticks(1500);
            float tox = squad.Max(c => c.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.ToxicBuildup)?.Severity ?? 0f);
            SelfTest.Check(tox > 0f, $"toxic modifier: colonists build up toxins ({tox:0.00})");
            KillAll(map.mapPawns.SpawnedPawnsInFaction(comp.enemyFaction));
            yield return Ticks(300);
            SelfTest.Check(comp.completed, "raider camp: clearing the camp and raids completes the node");
            foreach (var c in squad) HealthUtility.HealNonPermanentInjuriesAndRestoreLegs(c);
            foreach (var c in squad) { var h = c.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.ToxicBuildup); if (h != null) c.health.RemoveHediff(h); }

            // Escort: the refugee dying fails the node.
            foreach (var w in EnterDebugNode("Roguelike_RefugeeEscort")) yield return w;
            map = Find.CurrentMap;
            comp = NodeComp(map);
            SelfTest.Check(comp.EscortAlive && !comp.escort.HostileTo(Faction.OfPlayer), "escort: a friendly refugee is on the map");
            comp.escort.Kill(null);
            yield return Ticks(300);
            SelfTest.Check(comp.failed && !comp.completed, "escort: the refugee dying fails the node");
            SelfTest.Check(!run.RunOver, "escort: a failed debug node off the run graph does not end the run");

            // Survive under a psychic drone.
            foreach (var w in EnterDebugNode("Roguelike_PsychicHoldout")) yield return w;
            map = Find.CurrentMap;
            comp = NodeComp(map);
            var colonist = map.mapPawns.FreeColonistsSpawned.First();
            comp.Condition.intensity = 2.5f;
            var drone = DefDatabase<ThoughtDef>.GetNamed("Roguelike_PsychicDrone");
            var state = drone.Worker.CurrentState(colonist);
            SelfTest.Check(state.Active && state.StageIndex == 1, $"psychic modifier: medium drone mood penalty at level 2.5 (stage {state.StageIndex})");
            comp.startTick -= (int)(comp.preset.surviveDays * GenDate.TicksPerDay);
            yield return Ticks(300);
            SelfTest.Check(comp.completed, "hold-out: surviving the days completes the node");

            // Build target.
            foreach (var w in EnterDebugNode("Roguelike_FrostFort")) yield return w;
            map = Find.CurrentMap;
            comp = NodeComp(map);
            var origin = map.mapPawns.FreeColonistsSpawned.First().Position;
            int placed = 0;
            foreach (var cell in GenRadial.RadialCellsAround(origin, 12f, false).Where(c => c.InBounds(map) && c.Standable(map) && c.GetFirstPawn(map) == null))
            {
                var def = placed < 30 ? ThingDefOf.Wall : placed < 32 ? ThingDefOf.Door : null;
                if (def == null) break;
                var b = ThingMaker.MakeThing(def, ThingDefOf.WoodLog);
                b.SetFaction(Faction.OfPlayer);
                GenSpawn.Spawn(b, cell, map);
                placed++;
            }
            yield return Ticks(300);
            SelfTest.Check(comp.completed, $"build target: {placed} walls and doors complete the node ({comp.ProgressText})");

            // Events: every option outcome against a caravan.
            var caravan = LeaveMap(map);
            yield return Ticks(300);
            var fake = new RunNode { id = -1, tile = caravan.Tile, preset = Preset("Roguelike_Encounter") };
            int relics = run.relics.Count;
            EventUtility.Resolve(fake, caravan, DefDatabase<RoguelikeEventDef>.GetNamed("Roguelike_Event_Shrine"), 0);
            SelfTest.Check(run.relics.Count == relics + 1, "event: the shrine grants a relic");
            int members = caravan.PawnsListForReading.Count;
            EventUtility.Resolve(fake, caravan, DefDatabase<RoguelikeEventDef>.GetNamed("Roguelike_Event_Refugee"), 0);
            SelfTest.Check(caravan.PawnsListForReading.Count == members + 1, "event: the traveler joins the caravan");
            int items = CaravanInventoryUtility.AllInventoryItems(caravan).Count;
            EventUtility.Resolve(fake, caravan, DefDatabase<RoguelikeEventDef>.GetNamed("Roguelike_Event_CargoPod"), 0);
            SelfTest.Check(CaravanInventoryUtility.AllInventoryItems(caravan).Count > items, "event: the cargo pod's gear goes into the caravan");
            EventUtility.Resolve(fake, caravan, DefDatabase<RoguelikeEventDef>.GetNamed("Roguelike_Event_HotSpring"), 0);
            SelfTest.Check(caravan.PawnsListForReading.Where(p => p.RaceProps.Humanlike).All(p => !p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Any(i => !i.IsPermanent())), "event: the hot spring heals injuries");
            EventUtility.Open(fake, caravan);
            yield return () => EventWindow != null;
            foreach (var w in Screenshot("s3_event", world: true)) yield return w;
            EventWindow.Close(false);
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
            int expectedCards = 3 + run.relics.Sum(r => r.extraRewardChoices) + MetaProgress.ExtraRewardChoices;
            SelfTest.Check(window != null && window.Cards.Count == expectedCards, $"reward screen shows {expectedCards} cards (got {window?.Cards.Count ?? 0})");
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
            SelfTest.Check(recruitCards.Count == expectedCards && recruitCards.All(c => c.kind == RewardKind.Recruit && c.recruit != null), $"recruit node draw: {expectedCards} recruit cards");
            SelfTest.Check(recruitCards.All(c => Dialog_RewardPick.RecruitSummary(c.recruit).Contains("Skills:") && Dialog_RewardPick.RecruitSummary(c.recruit).Contains("Health:")), "recruit cards list skills and health");
            Find.WindowStack.Add(new Dialog_RewardPick(recruitCards, "Self-test: recruits", null));
            foreach (var w in Screenshot("s2_recruit_cards", world: false)) yield return w;
            RewardWindow.Skip();
            yield return () => RewardWindow == null;
            SelfTest.Check(recruitCards.All(c => !c.recruit.Spawned && !c.recruit.IsColonist || c.recruit.Faction == Faction.OfPlayer && !c.recruit.Spawned), "skipped recruits do not join");
            SelfTest.Info("relics now: " + string.Join(", ", run.relics.Select(r => r.label)));
        }

        private static UnlockDef Unlock(string name) => DefDatabase<UnlockDef>.GetNamed(name);

        private static bool ScenarioListed(string defName) =>
            ScenarioLister.ScenariosInCategory(ScenarioCategory.FromDef).Any(s => s == DefDatabase<ScenarioDef>.GetNamed(defName).scenario);

        /// <summary>Meta points, unlocks, locked content, the end-of-run and unlock screens.</summary>
        private static IEnumerable<Func<bool>> Meta()
        {
            foreach (var w in WaitForGame()) yield return w;
            var run = Run;
            var s = MetaProgress.S;
            var map = Find.CurrentMap;
            var comp = NodeComp(map);

            SelfTest.Check(s.points == 0 && s.unlocked.Count == 0, "self-test starts from empty meta progress (in memory only)");
            SelfTest.Check(run.relics.Contains(DefDatabase<RelicDef>.GetNamed("Roguelike_Relic_BlackBox")), "Crashlanded starts with its signature relic (Ship's Black Box)");
            SelfTest.Check(RewardUtility.Generate(run, RunRewardKind.Cards).Count == 4, "Black Box: reward draws have 4 cards");

            // Locked content.
            var medKit = DefDatabase<RelicDef>.GetNamed("Roguelike_Relic_MedKit");
            SelfTest.Check(!RewardUtility.RelicPool(run).Contains(medKit), "locked relic is not in the relic pool");
            SelfTest.Check(!RewardUtility.BlueprintPool(run).Contains(DefDatabase<BlueprintGroupDef>.GetNamed("Roguelike_BP_Power")), "locked blueprint group is not in the draft pool");
            SelfTest.Check(ScenarioListed("Roguelike_RunScenario") && !ScenarioListed("Roguelike_TribalHunters") && !ScenarioListed("Roguelike_IceNomads"),
                "only the Crashlanded starter is listed before unlocking");

            // Earning points.
            comp.Complete();
            SelfTest.Check(s.points == 1 && run.pointsEarned == 1, $"clearing a node earns 1 meta point (have {s.points})");
            var window = RewardWindow;
            window?.Skip();
            yield return () => RewardWindow == null;
            SelfTest.Check(s.points == 2, $"skipping a reward earns 1 meta point (have {s.points})");

            // Spending points.
            s.points = 30;
            SelfTest.Check(MetaProgress.TryBuy(Unlock("Roguelike_Unlock_LuckyDraw")), "an affordable unlock can be bought");
            SelfTest.Check(MetaProgress.IsUnlocked(Unlock("Roguelike_Unlock_LuckyDraw")) && s.points == 22, $"buying an unlock spends its cost (have {s.points})");
            SelfTest.Check(RewardUtility.Generate(run, RunRewardKind.Cards).Count == 5, "power unlock (Lucky draw): reward draws grow to 5 cards");
            MetaProgress.TryBuy(Unlock("Roguelike_Unlock_MedKit"));
            SelfTest.Check(MetaProgress.IsAvailable(medKit) && RewardUtility.RelicPool(run).Contains(medKit), "variety unlock: the relic joins the pool");
            float grace = comp.GraceDays;
            MetaProgress.TryBuy(Unlock("Roguelike_Unlock_HeadStart"));
            SelfTest.Check(Math.Abs(comp.GraceDays - grace - 1f) < 0.01f, $"power unlock (Head start): grace {grace:0.0} -> {comp.GraceDays:0.0} days");
            MetaProgress.TryBuy(Unlock("Roguelike_Unlock_TribalHunters"));
            SelfTest.Check(ScenarioListed("Roguelike_TribalHunters") && !ScenarioListed("Roguelike_IceNomads"), "starter unlock: Tribal Hunters is now listed");
            SelfTest.Check(!MetaProgress.TryBuy(Unlock("Roguelike_Unlock_TribalHunters")), "an unlock can't be bought twice");
            int before = s.points;
            s.points = 1;
            SelfTest.Check(!MetaProgress.TryBuy(Unlock("Roguelike_Unlock_IceNomads")) && s.points == 1, "can't buy without enough points");
            s.points = before;

            // Unlock screen.
            Find.WindowStack.Add(new Dialog_Unlocks());
            foreach (var w in Screenshot("s4_unlocks", world: false)) yield return w;
            Find.WindowStack.WindowOfType<Dialog_Unlocks>()?.Close(false);

            // End-of-run screen on a loss.
            int runs = s.runs;
            run.Lose("Self-test: the run ends here.");
            SelfTest.Check(run.runLost && s.runs == runs + 1, "a loss is recorded in meta progress");
            SelfTest.Check(Find.WindowStack.WindowOfType<Dialog_RunEnd>() != null, "the end-of-run screen opens");
            foreach (var w in Screenshot("s4_run_end", world: false)) yield return w;
        }

        /// <summary>The starter given by -roguelikeScenario starts with its pawns, kit and signature relic.</summary>
        private static IEnumerable<Func<bool>> Starter()
        {
            yield return () => Current.ProgramState == ProgramState.Playing && Find.CurrentMap != null;
            yield return Frames(120);
            var run = Run;
            var part = Find.Scenario.AllParts.OfType<ScenPart_RoguelikeRun>().First();
            var map = Find.CurrentMap;
            SelfTest.Info($"starter '{part.starterId}': {map.mapPawns.FreeColonistsSpawnedCount} colonists, {map.mapPawns.SpawnedColonyAnimals.Count} animals, relics {string.Join(", ", run.relics.Select(r => r.label))}");
            SelfTest.Check(part.starterRelic != null && run.relics.Contains(part.starterRelic), $"starter '{part.starterId}': signature relic {part.starterRelic?.label} is owned");
            SelfTest.Check(run.CurrentNode != null && NodeComp(map) is { Active: true }, $"starter '{part.starterId}': landed on a starting node with an objective");
            switch (part.starterId)
            {
                case "tribalHunters":
                    SelfTest.Check(PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists.Count() == 5, "Tribal Hunters: five colonists");
                    SelfTest.Check(map.mapPawns.SpawnedColonyAnimals.Count(a => a.def.defName == "Muffalo") == 2, "Tribal Hunters: two pack muffalo");
                    var muffalo = map.mapPawns.SpawnedColonyAnimals.First(a => a.def.defName == "Muffalo");
                    SelfTest.Check(MassUtility.Capacity(muffalo) > muffalo.BodySize * 35f * 1.49f, "Herd Bond: pack animals carry 50% more");
                    break;
                case "iceNomads":
                    int parkas = map.listerThings.ThingsOfDef(ThingDefOf.Apparel_Parka).Count
                                 + map.mapPawns.FreeColonistsSpawned.Sum(p => p.apparel.WornApparel.Count(a => a.def == ThingDefOf.Apparel_Parka));
                    SelfTest.Check(parkas >= 3, $"Ice Nomads: three parkas ({parkas})");
                    SelfTest.Check(Math.Abs(run.WeatherRateFactor - 0.7f) < 0.01f, "Thermal Lining: weather worsens 30% slower");
                    break;
                default:
                    SelfTest.Check(run.relics.Any(r => r.extraRewardChoices > 0), "Crashlanded: Black Box adds a reward choice");
                    break;
            }
            Find.CameraDriver.JumpToCurrentMapLoc(map.mapPawns.FreeColonistsSpawned.First().Position);
            foreach (var w in Screenshot("s4_starter_" + part.starterId, world: false)) yield return w;
        }

        /// <summary>Leaving the current node before its objective is met ends the run.</summary>
        private static IEnumerable<Func<bool>> LeaveEarly()
        {
            foreach (var w in WaitForGame()) yield return w;
            var run = Run;
            var node = run.CurrentNode;
            // Leave like a player forming a normal caravan: the starting settlement stays loaded.
            LeaveMap(Find.CurrentMap, abandon: false);
            yield return Until(() => run.RunOver);
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
            yield return Until(() => run.RunOver);
            var alive = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists.ToList();
            SelfTest.Info($"alive colonists after wipe: {alive.Count} [{string.Join(", ", alive.Select(p => $"{p.LabelShort} dead={p.Dead} spawned={p.Spawned} map={p.MapHeld}"))}]");
            SelfTest.Check(run.runLost, "squad wipe ends the run");
            int points = MetaProgress.S.points;
            var node = run.CurrentNode;
            run.NotifyNodeCompleted(node.tile);
            SelfTest.Check(node.state == RunNodeState.Current && MetaProgress.S.points == points && !run.runWon, "a node can't be completed after the run is lost");
        }
    }
}
