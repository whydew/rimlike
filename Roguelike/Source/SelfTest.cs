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
    /// <summary>
    /// Runs the self-test script one step per frame; a step yields a condition to wait for.
    /// Driven from Root.Update so a script survives scene changes (main menu and back).
    /// </summary>
    [HarmonyPatch(typeof(Root), nameof(Root.Update))]
    public static class SelfTestRunner
    {
        private static IEnumerator<Func<bool>> script;
        private static Func<bool> waiting;
        private static int waitStartFrame;
        private static bool finished;
        private const int TimeoutFrames = 60 * 120;

        public static void Postfix()
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

        private static void Finish()
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
            "newrun" => NewRunScript(),
            "disasters" => Disasters(),
            "polish" => Polish(),
            "gating" => Gating(),
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
            int acts = WorldComponent_RoguelikeRun.ActsPerRun;
            SelfTest.Check(run is { active: true }, "run is active after world generation");
            SelfTest.Check(run.nodes.Count == 15 * acts, $"all {acts} acts are laid out up front ({run.nodes.Count} nodes)");
            for (int a = 1; a <= acts; a++)
            {
                var sizes = Enumerable.Range(0, 6).Select(r => run.Row(a, r).Count()).ToList();
                SelfTest.Check(sizes.SequenceEqual(new[] { 3, 3, 3, 3, 2, 1 }), $"act {a}: row sizes 3,3,3,3,2,1 (got {string.Join(",", sizes)})");
                var boss = run.Row(a, 5).First();
                float span = Find.WorldGrid.ApproxDistanceInTiles(run.Row(a, 0).ElementAt(1).tile, boss.tile);
                SelfTest.Check(span >= 25f, $"act {a}: nodes are spread out ({span:0} tiles from the start row to the boss)");
                if (a < acts)
                    SelfTest.Check(boss.next.OrderBy(i => i).SequenceEqual(run.Row(a + 1, 0).Select(n => n.id).OrderBy(i => i)), $"act {a}: the boss leads to the starting nodes of act {a + 1}");
                bool crossing = false;
                for (int r = 0; r < 5; r++)
                {
                    var edges = run.Row(a, r).SelectMany(n => n.next.Select(id => (n.col, run.NodeById(id).col))).ToList();
                    crossing |= edges.Any(e => edges.Any(f => e.Item1 < f.Item1 && e.Item2 > f.Item2));
                }
                SelfTest.Check(!crossing, $"act {a}: no edges cross");
            }
            SelfTest.Check(run.nodes.Select(n => n.tile).Distinct().Count() == run.nodes.Count, "every node has its own tile");
            float minGap = run.nodes.SelectMany(x => run.nodes.Where(y => y.id > x.id).Select(y => Find.WorldGrid.ApproxDistanceInTiles(x.tile, y.tile))).Min();
            SelfTest.Check(minGap >= 2f, $"nodes are well apart (closest pair {minGap:0.0} tiles)");
            SelfTest.Check(run.nodes.Where(n => !(n.IsBoss && n.act == acts)).All(n => n.next.Count > 0), "every node but the final boss leads somewhere");
            SelfTest.Check(run.nodes.All(n => n.preset != null && n.preset.isBoss == n.IsBoss), "every node has a preset of the right kind");
            SelfTest.Check(Find.WorldObjects.AllWorldObjects.OfType<RunNodeMarker>().Count() == run.nodes.Count, "a marker for every node of every act");
            var npcBases = Find.WorldObjects.Settlements.Where(s => s.Faction != null && !s.Faction.IsPlayer).ToList();
            var assaultTiles = run.nodes.Where(n => n.preset.assault).Select(n => n.tile).ToList();
            SelfTest.Check(npcBases.All(s => assaultTiles.Contains(s.Tile)) && npcBases.Count == assaultTiles.Count,
                $"NPC settlements cleared; the {npcBases.Count} remaining are the elite and boss bases");
            foreach (var n in run.nodes)
                SelfTest.Info($"act {n.act} node {n.id} row {n.row} col {n.col} tile {n.tile} {n.preset.defName} -> [{string.Join(",", n.next)}]");
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
            comp.startTick -= (int)((comp.GraceDays + 0.5f) * GenDate.TicksPerDay);
            comp.AdvanceThreat(1f);
            float off = comp.Condition.intensity;
            SelfTest.Check(off != 0f && Math.Sign(off) == Math.Sign(comp.ratePerDay), $"after grace the weather moves the right way ({comp.Condition.IntensityText(off)}, pace x{comp.PaceFactor:0.00})");

            foreach (var w in Screenshot("s1_act_start", world: true)) yield return w;

            var node = start;
            Caravan caravan = null;
            bool shotCards = false, shotPacking = false;
            for (int step = 0; step < 40; step++)
            {
                int actBefore = run.act;
                if (node.preset.HasMap) NodeComp(map).Complete();
                SelfTest.Check(node.state == RunNodeState.Completed, $"act {actBefore} node {node.id}: completed");
                if (node.IsBoss && actBefore >= WorldComponent_RoguelikeRun.ActsPerRun)
                {
                    SelfTest.Check(run.runWon && !run.runLost, "final boss cleared: run won");
                    break;
                }
                if (node.reward != RunRewardKind.None)
                    foreach (var w in PickFirstReward(node)) yield return w;
                if (node.IsBoss) SelfTest.Check(run.act == actBefore + 1 && !run.RunOver, $"act {actBefore} boss cleared: act {run.act} begins");
                var nextIds = node.next;
                SelfTest.Check(nextIds.All(id => run.NodeById(id).state == RunNodeState.Available), $"node {node.id}: its {nextIds.Count} next nodes are available");

                var nextNode = run.NodeById(nextIds.Last());
                if (step == 1 && nextNode.preset.HasMap && !nextNode.IsBoss)
                {
                    // Make sure the path crosses an event node so map-less nodes are exercised in a real run.
                    nextNode.preset = Preset("Roguelike_Encounter");
                    nextNode.reward = RunRewardKind.None;
                }
                var siblings = nextIds.Where(id => id != nextNode.id).ToList();

                // Choose the next node from the cards.
                yield return () => PathWindow != null;
                var picker = PathWindow;
                SelfTest.Check(picker.Cards.Select(c => c.pathNode.id).OrderBy(i => i).SequenceEqual(nextIds.OrderBy(i => i)), $"node {node.id}: next-node cards match its {nextIds.Count} connections");
                if (!shotCards)
                {
                    shotCards = true;
                    foreach (var w in Screenshot("s6_next_node_cards", world: false)) yield return w;
                }
                picker.Pick(picker.Cards.ToList().FindIndex(c => c.pathNode == nextNode));
                yield return Frames(90);
                SelfTest.Info("after pick: windows " + string.Join(", ", Find.WindowStack.Windows.Select(w => w.GetType().Name)) + $"; pending {run.pendingPickNodeId}");
                yield return () => PathWindow == null;

                if (node.preset.HasMap)
                {
                    // Pack within the carry limit, then go straight to the chosen node.
                    yield return () => Find.WindowStack.WindowOfType<Dialog_Packing>() != null;
                    var packing = Find.WindowStack.WindowOfType<Dialog_Packing>();
                    if (!shotPacking)
                    {
                        shotPacking = true;
                        packing.Essentials();
                        foreach (var w in Screenshot("s6_packing", world: false)) yield return w;
                        // Nothing gets past the carry limit, whatever is clicked.
                        foreach (var g in packing.Groups.OrderByDescending(g => g.unitMass * g.available).Take(5)) packing.Set(g, g.available);
                        SelfTest.Check(packing.MassUsage <= packing.MassCapacity + 0.01f, $"node {node.id}: packing never exceeds the carry limit ({packing.MassUsage:0}/{packing.MassCapacity:0} kg, {packing.Groups.Count} kinds of things)");
                        packing.MostValuable();
                        SelfTest.Check(packing.MassUsage <= packing.MassCapacity + 0.01f && packing.Groups.Any(g => g.chosen > 0), $"node {node.id}: 'most valuable' fills the packs within the limit");
                        // Backing out of packing returns to the next-node choice.
                        packing.Close();
                        yield return () => PathWindow != null;
                        SelfTest.Check(run.NodeById(node.id).state == RunNodeState.Completed && nextNode.state == RunNodeState.Available, $"node {node.id}: backing out of packing brings back the next-node cards");
                        PathWindow.Pick(PathWindow.Cards.ToList().FindIndex(c => c.pathNode == nextNode));
                        yield return () => Find.WindowStack.WindowOfType<Dialog_Packing>() != null;
                        packing = Find.WindowStack.WindowOfType<Dialog_Packing>();
                    }
                    packing.Essentials();
                    var food = packing.Groups.Where(g => g.category == Dialog_Packing.Category.Food).Sum(g => g.chosen);
                    SelfTest.Info($"packing: mass {packing.MassUsage:0}/{packing.MassCapacity:0} kg, pawns {packing.Pawns.Count}, food items {food}");
                    SelfTest.Check(packing.Depart(), $"node {node.id}: the squad packs up within the carry limit");
                }

                if (nextNode.preset.HasMap)
                {
                    yield return () => Current.Game.FindMap(nextNode.tile) is { } m && m.mapPawns.FreeColonistsSpawnedCount > 0;
                    yield return Frames(30);
                    map = Current.Game.FindMap(nextNode.tile);
                    SelfTest.Check(map.mapPawns.FreeColonistsSpawnedCount > 0, $"entered node {nextNode.id} directly: colonists arrived");
                    CheckStartedNode(map, nextNode);
                    if (nextNode.preset.assault) SelfTest.Check(map.Parent is Settlement, $"node {nextNode.id}: assault on an enemy base map");
                    if (nextNode.IsBoss && nextNode.act == 1)
                    {
                        Find.CameraDriver.JumpToCurrentMapLoc(map.mapPawns.FreeColonistsSpawned.First().Position);
                        foreach (var w in Screenshot("s6_boss_base", world: false)) yield return w;
                    }
                }
                else
                {
                    yield return () => EventWindow != null;
                    // Merchants complete as soon as the shop opens; other map-less nodes wait for a choice.
                    SelfTest.Check(nextNode.state == (nextNode.preset.kind == NodeKind.Merchant ? RunNodeState.Completed : RunNodeState.Current), $"{nextNode.preset.kind} node {nextNode.id}: dialog opened on arrival");
                    caravan = Find.WorldObjects.Caravans.First(c => c.IsPlayerControlled);
                    EventWindow.Close(false);
                    string result = EventUtility.Resolve(nextNode, caravan, DefDatabase<RoguelikeEventDef>.AllDefsListForReading.First(), 0);
                    SelfTest.Check(!result.NullOrEmpty() && nextNode.state == RunNodeState.Completed, $"event node {nextNode.id}: resolved ({result.Split('\n')[0]})");
                    map = null;
                }
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

        /// <summary>Drops a stack of items next to the squad.</summary>
        private static void Give(Map map, string def, int count)
        {
            var thingDef = DefDatabase<ThingDef>.GetNamed(def);
            var near = map.mapPawns.FreeColonistsSpawned.First().Position;
            while (count > 0)
            {
                var t = ThingMaker.MakeThing(thingDef);
                t.stackCount = Math.Min(count, thingDef.stackLimit);
                count -= t.stackCount;
                GenPlace.TryPlaceThing(t, near, map, ThingPlaceMode.Near);
            }
        }

        /// <summary>
        /// Builds a roofed room with walls (granite blocks unless <paramref name="stuff"/> is given), one door and
        /// <paramref name="beds"/> beds, or a table and chairs for a dining room. Returns the walls placed.
        /// </summary>
        /// <summary>One granite wall on a random free cell; 1 if placed.</summary>
        private static readonly List<Thing> looseWalls = new List<Thing>();

        private static int SpawnStoneWall(Map map)
        {
            if (!CellFinder.TryFindRandomCell(map, c => c.Standable(map) && c.GetEdifice(map) == null && c.GetFirstPawn(map) == null && !c.CloseToEdge(map, 5), out var cell)) return 0;
            var b = ThingMaker.MakeThing(ThingDefOf.Wall, DefDatabase<ThingDef>.GetNamed("BlocksGranite"));
            b.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(b, cell, map, WipeMode.Vanish);
            looseWalls.Add(b);
            return 1;
        }

        private static int BuildRoom(Map map, int innerW, int innerH, int beds, ThingDef stuff, bool dining = false, bool art = false)
        {
            stuff ??= DefDatabase<ThingDef>.GetNamed("BlocksGranite");
            int w = innerW + 2, h = innerH + 2;
            if (!CellFinder.TryFindRandomCell(map, c =>
                {
                    var r = new CellRect(c.x, c.z, w, h);
                    return r.InBounds(map) && r.Cells.All(x => x.Standable(map) && x.GetFirstPawn(map) == null && x.GetEdifice(map) == null && !x.Roofed(map));
                }, out var corner))
                return 0;
            var rect = new CellRect(corner.x, corner.z, w, h);
            var door = new IntVec3(rect.minX + w / 2, 0, rect.minZ);
            int walls = 0;
            foreach (var c in rect.EdgeCells)
            {
                var b = ThingMaker.MakeThing(c == door ? ThingDefOf.Door : ThingDefOf.Wall, c == door ? ThingDefOf.WoodLog : stuff);
                b.SetFaction(Faction.OfPlayer);
                GenSpawn.Spawn(b, c, map, WipeMode.Vanish);
                if (c != door) walls++;
            }
            var inner = rect.ContractedBy(1);
            foreach (var c in inner.Cells) map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
            for (int i = 0; i < beds; i++)
            {
                var bed = ThingMaker.MakeThing(ThingDefOf.Bed, ThingDefOf.WoodLog);
                bed.SetFaction(Faction.OfPlayer);
                GenSpawn.Spawn(bed, new IntVec3(inner.minX + i * 1, 0, inner.minZ), map, Rot4.North, WipeMode.Vanish);
            }
            if (dining)
            {
                var table = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Table2x2c"), ThingDefOf.WoodLog);
                table.SetFaction(Faction.OfPlayer);
                GenSpawn.Spawn(table, inner.CenterCell, map, Rot4.North, WipeMode.Vanish);
                var chair = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("DiningChair"), ThingDefOf.WoodLog);
                chair.SetFaction(Faction.OfPlayer);
                GenSpawn.Spawn(chair, inner.CenterCell + new IntVec3(0, 0, -1), map, Rot4.North, WipeMode.Vanish);
            }
            if (art)
            {
                // A legendary gold sculpture in the corner makes the room impressive.
                var sculpture = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("SculptureSmall"), ThingDefOf.Gold);
                sculpture.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Legendary, ArtGenerationContext.Colony);
                sculpture.SetFaction(Faction.OfPlayer);
                GenSpawn.Spawn(sculpture, new IntVec3(inner.maxX, 0, inner.maxZ), map, WipeMode.Vanish);
            }
            return walls;
        }

        /// <summary>Enters a debug node with <paramref name="preset"/> from the current map and waits for it.</summary>
        private static IEnumerable<Func<bool>> EnterDebugNode(string preset, GameConditionDef modifier = null)
        {
            var from = Find.CurrentMap;
            NodeLauncher.Enter(from, Preset(preset), modifier);
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

            // Base-building requirements, minimum stay and the per-node modifier.
            var cold = DefDatabase<GameConditionDef>.GetNamed("Roguelike_EscalatingCold");
            foreach (var w in EnterDebugNode("Roguelike_GreatGranary", cold)) yield return w;
            map = Find.CurrentMap;
            comp = NodeComp(map);
            SelfTest.Check(comp.modifier == cold && comp.Condition != null && Math.Abs(comp.ratePerDay + 1.4f) < 0.01f, $"granary: rolled modifier {comp.modifier?.defName} at {comp.ratePerDay} per day");
            int squadSize = comp.squad;
            SelfTest.Check(squadSize == map.mapPawns.FreeColonistsSpawnedCount, $"granary: requirement counts scale with the squad ({squadSize})");
            Give(map, "RawCorn", 1500);
            Give(map, "MealSimple", 20 * squadSize);
            BuildRoom(map, 6, 4, squadSize, null);
            yield return Ticks(300);
            var corn = comp.preset.requirements.First(r => r.kind == RequirementKind.Produce);
            SelfTest.Check(comp.Have(corn) == 1500, $"granary: produced corn counts from arrival ({comp.Have(corn)})");
            SelfTest.Check(comp.RequirementsMet && !comp.completed, $"granary: requirements met but the minimum stay holds it open ({comp.ProgressText})");
            comp.startTick -= (int)(comp.preset.minDays * GenDate.TicksPerDay);
            yield return Ticks(300);
            SelfTest.Check(comp.completed, "granary: completes once the minimum stay has passed");

            // Stone fortress: only stone walls count, and the walls must then be held for 10 days.
            foreach (var w in EnterDebugNode("Roguelike_StoneFortress", cold)) yield return w;
            map = Find.CurrentMap;
            comp = NodeComp(map);
            var walls = comp.preset.requirements.First(r => r.kind == RequirementKind.Build && r.thing == ThingDefOf.Wall);
            int woodWalls = BuildRoom(map, 3, 3, 0, ThingDefOf.WoodLog);
            yield return Ticks(60);
            SelfTest.Check(woodWalls > 0 && comp.Have(walls) == 0, $"stone fortress: {woodWalls} wooden walls don't count as stone");
            int stone = 0, doorsBuilt = 0;
            SelfTest.Check(comp.enemyFaction != null && comp.nextPatrolTick > 0, "stone fortress: patrols are scheduled to test the walls");
            BuildRoom(map, 5, 5, comp.squad, null); // shelter
            stone = map.listerBuildings.AllBuildingsColonistOfDef(ThingDefOf.Wall).Count(x => x.Stuff != ThingDefOf.WoodLog);
            for (int tries = 0; stone < walls.Target(comp.squad) && tries < 5000; tries++) stone += SpawnStoneWall(map);
            doorsBuilt = map.listerBuildings.AllBuildingsColonistOfDef(ThingDefOf.Door).Count();
            while (doorsBuilt < 12) { BuildRoom(map, 2, 2, 0, null); doorsBuilt++; }
            yield return Ticks(300);
            int wallIndex = comp.preset.requirements.IndexOf(walls);
            SelfTest.Check(comp.Have(walls) >= walls.Target(comp.squad) && !comp.RequirementsMet && comp.heldDays[wallIndex] > 0f && comp.ProgressText.Contains("held"),
                $"stone fortress: walls built, the hold clock runs but isn't done ({comp.ProgressText})");
            float heldBefore = comp.heldDays[wallIndex];
            var someWalls = looseWalls.Where(x => x.Spawned).Take(comp.Have(walls) - walls.Target(comp.squad) + 5).ToList();
            foreach (var wall in someWalls) wall.Destroy();
            yield return Ticks(600);
            SelfTest.Check(Math.Abs(comp.heldDays[wallIndex] - heldBefore) < 0.001f, "hold: the clock pauses (doesn't reset) while the walls are short");
            for (int tries = 0; tries < 50 && comp.Have(walls) < walls.Target(comp.squad); tries++) SpawnStoneWall(map);
            comp.heldDays[wallIndex] = walls.holdDays - 0.001f;
            yield return Ticks(300);
            SelfTest.Check(comp.RequirementsMet, $"stone fortress: requirements met once held for {walls.holdDays} days ({comp.ProgressText})");
            comp.startTick -= (int)(comp.preset.minDays * GenDate.TicksPerDay);
            yield return Ticks(300);
            SelfTest.Check(comp.completed, "stone fortress: completes after the minimum stay");

            // Private quarters: bedrooms and a dining room, plain ones first, then impressive ones.
            foreach (var w in EnterDebugNode("Roguelike_PrivateQuarters", cold)) yield return w;
            map = Find.CurrentMap;
            comp = NodeComp(map);
            var bedrooms = comp.preset.requirements.First(r => r.kind == RequirementKind.Bedrooms);
            var dining = comp.preset.requirements.First(r => r.kind == RequirementKind.DiningRoom);
            BuildRoom(map, 4, 4, 1, null);
            BuildRoom(map, 7, 6, 0, null, dining: true);
            yield return Ticks(300);
            SelfTest.Check(comp.Have(bedrooms) == 0 && comp.Have(dining) == 0, "private quarters: plain rooms don't meet the quality bar");
            for (int i = 0; i < comp.squad; i++) BuildRoom(map, 4, 4, 1, null, art: true);
            BuildRoom(map, 7, 6, 0, null, dining: true, art: true);
            yield return Ticks(300);
            string scores = string.Join(", ", map.regionGrid.AllRooms.Where(r => r.ProperRoom && r.Role != null).Select(r => $"{r.Role.label} {r.GetStat(RoomStatDefOf.Impressiveness):0}"));
            SelfTest.Check(comp.Have(bedrooms) >= comp.squad, $"private quarters: {comp.Have(bedrooms)} impressive bedrooms recognized ({scores})");
            SelfTest.Check(comp.Have(dining) == 1, "private quarters: the impressive dining room is recognized");
            SelfTest.Check(bedrooms.Card(comp.squad).Contains("slightly impressive"), $"private quarters: the card names the quality ({bedrooms.Card(comp.squad)})");
            Find.CameraDriver.JumpToCurrentMapLoc(map.mapPawns.FreeColonistsSpawned.First().Position);
            foreach (var w in Screenshot("s7_rooms", world: false)) yield return w;

            // Renewable campus: the map holds enough ore for the components and steel it needs.
            foreach (var w in EnterDebugNode("Roguelike_RenewableCampus", cold)) yield return w;
            map = Find.CurrentMap;
            int CanMine(ThingDef res) => RequirementUtility.CountOnMap(map, res) + map.listerThings.AllThings
                .Where(t => t.def.building?.mineableThing == res).Sum(t => t.def.building.EffectiveMineableYield);
            int components = CanMine(ThingDefOf.ComponentIndustrial), steel = CanMine(ThingDefOf.Steel);
            SelfTest.Check(components >= 24 && steel >= 900, $"renewable campus: enough to mine ({components} components, {steel} steel)");

            // Seasonal endurance under a psychic drone: a death fails it.
            var psychic = DefDatabase<GameConditionDef>.GetNamed("Roguelike_EscalatingPsychic");
            foreach (var w in EnterDebugNode("Roguelike_SeasonalEndurance", psychic)) yield return w;
            map = Find.CurrentMap;
            comp = NodeComp(map);
            var colonist = map.mapPawns.FreeColonistsSpawned.First();
            comp.Condition.intensity = 2.5f;
            var drone = DefDatabase<ThoughtDef>.GetNamed("Roguelike_PsychicDrone");
            var state = drone.Worker.CurrentState(colonist);
            SelfTest.Check(state.Active && state.StageIndex == 1, $"psychic modifier: medium drone mood penalty at level 2.5 (stage {state.StageIndex})");
            comp.Condition.intensity = 0f;
            map.mapPawns.FreeColonistsSpawned.Last().Kill(null);
            yield return Ticks(300);
            SelfTest.Check(comp.failed && !comp.completed, "seasonal endurance: a death fails the node");

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

            // Rest site options.
            var rest = DefDatabase<RoguelikeEventDef>.GetNamed("Roguelike_Event_RestSite");
            var trainee = caravan.PawnsListForReading.First(p => p.RaceProps.Humanlike);
            float xpBefore = trainee.skills.skills.Sum(s => s.XpTotalEarned + s.xpSinceLastLevel);
            EventUtility.Resolve(fake, caravan, rest, 1);
            SelfTest.Check(trainee.skills.skills.Sum(s => s.XpTotalEarned + s.xpSinceLastLevel) > xpBefore, "rest site: training raises a skill");
            var patient = caravan.PawnsListForReading.First(p => p.RaceProps.Humanlike);
            patient.TakeDamage(new DamageInfo(DamageDefOf.Cut, 8f));
            int Bad() => caravan.PawnsListForReading.Where(p => p.RaceProps.Humanlike).Sum(p => p.health.hediffSet.hediffs.Count(h => h.def.isBad && h.Visible));
            int badBefore = Bad();
            EventUtility.Resolve(fake, caravan, rest, 2);
            SelfTest.Check(Bad() < badBefore, $"rest site: field surgery treats a condition ({badBefore} -> {Bad()})");

            // Treasure: forcing the vault gives two relics and hurts someone.
            relics = run.relics.Count;
            EventUtility.Resolve(fake, caravan, DefDatabase<RoguelikeEventDef>.GetNamed("Roguelike_Event_Treasure"), 1);
            SelfTest.Check(run.relics.Count == relics + 2, $"treasure: forcing the vault gives two relics ({run.relics.Count - relics})");

            // Merchant: buy with caravan silver.
            EventUtility.Resolve(fake, caravan, DefDatabase<RoguelikeEventDef>.GetNamed("Roguelike_Event_Stash"), 0);
            var extra = ThingMaker.MakeThing(ThingDefOf.Silver);
            extra.stackCount = 1000;
            CaravanInventoryUtility.GiveThing(caravan, extra);
            int silver = ShopUtility.Silver(caravan);
            SelfTest.Check(silver >= 1250, $"silver: the stash and gifts reach the caravan ({silver})");
            var shopNode = new RunNode { id = -1, tile = caravan.Tile, preset = Preset("Roguelike_Merchant") };
            var stock = ShopUtility.Open(shopNode, caravan);
            SelfTest.Check(stock.Count >= 3, $"merchant: offers {stock.Count} cards");
            yield return () => EventWindow != null;
            foreach (var w in Screenshot("s5_merchant", world: true)) yield return w;
            var item = stock[0];
            int price = ShopUtility.Price(item);
            SelfTest.Check(ShopUtility.Buy(stock, item, caravan) && ShopUtility.Silver(caravan) == silver - price && !stock.Contains(item), $"merchant: buying {item.Title} costs {price} silver");
            int left = ShopUtility.Silver(caravan);
            SelfTest.Check(!ShopUtility.TrySpend(caravan, left + 1) && ShopUtility.Silver(caravan) == left, "merchant: can't overspend");
            EventWindow?.Close(false);

            // Elite: a tough raid and a relic-only reward.
            int relicPool = RewardUtility.RelicPool(run).Count();
            var eliteCards = RewardUtility.Generate(run, RunRewardKind.Relics);
            SelfTest.Check(eliteCards.Count(c => c.kind == RewardKind.Relic) == Math.Min(relicPool, eliteCards.Count), $"elite reward: relic cards while the pool lasts ({relicPool} left in pool)");
        }


        private static Dialog_RewardPick RewardWindow =>
            Find.WindowStack.Windows.OfType<Dialog_RewardPick>().FirstOrDefault(w => w.Cards.Count > 0 && w.Cards[0].kind != RewardKind.Path);

        private static Dialog_RewardPick PathWindow =>
            Find.WindowStack.Windows.OfType<Dialog_RewardPick>().FirstOrDefault(w => w.Cards.Count > 0 && w.Cards[0].kind == RewardKind.Path);

        /// <summary>Closes the next-node choice when a test isn't moving on.</summary>
        private static IEnumerable<Func<bool>> DismissNextPick()
        {
            yield return Frames(10);
            Run.pendingPickNodeId = -1;
            PathWindow?.Close(false);
            yield return Frames(5);
        }

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
            float midPick = UnityEngine.Time.realtimeSinceStartup + 0.4f;
            yield return () => UnityEngine.Time.realtimeSinceStartup >= midPick;
            ScreenshotTaker.TakeNonSteamShot("rltest_s2_reward_pick");
            yield return () => RewardWindow == null;
            SelfTest.Check(run.pendingRewardNodeId < 0, $"picked card resolves the reward ({window.Cards[1].KindLabel}: {window.Cards[1].Title})");
            foreach (var w in DismissNextPick()) yield return w;

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

            var project = RewardUtility.ResearchPool().FirstOrDefault();
            SelfTest.Check(project != null, $"research pool has projects to offer ({RewardUtility.ResearchPool().Count()})");
            if (project != null)
            {
                var researchCard = new RewardCard { kind = RewardKind.Research, research = project };
                SelfTest.Check(!Dialog_RewardPick.BodyText(researchCard).NullOrEmpty(), $"research card has body text ({project.label})");
                RewardUtility.Apply(researchCard, map);
                SelfTest.Check(project.IsFinished && !RewardUtility.ResearchPool().Contains(project), $"research reward completes {project.label}");
            }

            // Research cards show what the project unlocks on a research badge.
            var researchCards = RewardUtility.ResearchPool().OrderByDescending(RewardUtility.ResearchWeight).Take(4)
                .Select(r => new RewardCard { kind = RewardKind.Research, research = r }).ToList();
            Find.WindowStack.Add(new Dialog_RewardPick(researchCards, "Self-test: research", null));
            foreach (var w in Screenshot("s2_research_cards", world: false)) yield return w;
            RewardWindow.Skip();
            yield return () => RewardWindow == null;

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
            SelfTest.Check(ScenarioListed("Roguelike_RunScenario") && !ScenarioListed("Roguelike_TribalHunters") && !ScenarioListed("Roguelike_IceNomads") && !ScenarioListed("Crashlanded"),
                "only the Crashlanded starter is listed before unlocking");

            // Earning points.
            comp.Complete();
            SelfTest.Check(s.points == 1 && run.pointsEarned == 1, $"clearing a node earns 1 meta point (have {s.points})");
            var window = RewardWindow;
            window?.Skip();
            yield return () => RewardWindow == null;
            SelfTest.Check(s.points == 2, $"skipping a reward earns 1 meta point (have {s.points})");
            foreach (var w in DismissNextPick()) yield return w;

            // Spending points.
            s.points = 30;
            SelfTest.Check(MetaProgress.TryBuy(Unlock("Roguelike_Unlock_LuckyDraw")), "an affordable unlock can be bought");
            SelfTest.Check(MetaProgress.IsUnlocked(Unlock("Roguelike_Unlock_LuckyDraw")) && s.points == 22, $"buying an unlock spends its cost (have {s.points})");
            SelfTest.Check(RewardUtility.Generate(run, RunRewardKind.Cards).Count == 5, "power unlock (Lucky draw): reward draws grow to 5 cards");
            MetaProgress.TryBuy(Unlock("Roguelike_Unlock_MedKit"));
            SelfTest.Check(MetaProgress.IsAvailable(medKit) && RewardUtility.RelicPool(run).Contains(medKit), "variety unlock: the relic joins the pool");
            var medKitUnlock = Unlock("Roguelike_Unlock_MedKit");
            MetaProgress.Toggle(medKitUnlock);
            SelfTest.Check(!RewardUtility.RelicPool(run).Contains(medKit) && MetaProgress.IsUnlocked(medKitUnlock), "variety unlock toggled off: stays owned, leaves the pool");
            MetaProgress.Toggle(medKitUnlock);
            SelfTest.Check(RewardUtility.RelicPool(run).Contains(medKit), "variety unlock toggled back on: rejoins the pool");
            MetaProgress.Toggle(Unlock("Roguelike_Unlock_LuckyDraw"));
            SelfTest.Check(MetaProgress.IsEnabled(Unlock("Roguelike_Unlock_LuckyDraw")), "power unlocks can't be toggled");
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

            // More pool unlocks: a stacking relic, a gear set, a legendary relic, and a badge for every unlock.
            var hawk = DefDatabase<RelicDef>.GetNamed("Roguelike_Relic_HawksEye");
            s.unlocked.Add("Roguelike_Unlock_HawksEye");
            run.AddRelic(hawk);
            run.AddRelic(hawk);
            var shooter = map.mapPawns.FreeColonistsSpawned.First();
            SelfTest.Check(RewardUtility.RelicPool(run).Contains(hawk) && Math.Abs((shooter.health.hediffSet.GetFirstHediffOfDef(hawk.hediff)?.Severity ?? 0f) - 2f) < 0.01f,
                "stacking relic (Hawk's Eye): picked twice, stack 2, still in the pool");
            for (int i = 0; i < 4; i++) run.AddRelic(hawk);
            SelfTest.Check(run.relics.Count(r => r == hawk) == 5 && !RewardUtility.RelicPool(run).Contains(hawk), "stacking relic: caps at 5 stacks and leaves the pool");
            run.relics.RemoveAll(r => r == hawk);
            var set = DefDatabase<GearRewardDef>.GetNamed("Roguelike_Gear_MarineSet");
            int helmets = map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("Apparel_PowerArmorHelmet")).Count;
            RewardUtility.Apply(new RewardCard { kind = RewardKind.Gear, gear = set }, map);
            SelfTest.Check(map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("Apparel_PowerArmorHelmet")).Count == helmets + 1, "gear set (marine armor): the helmet comes with the armor");
            var legendary = new RewardCard { kind = RewardKind.Relic, relic = DefDatabase<RelicDef>.GetNamed("Roguelike_Relic_LuckyCoin") };
            SelfTest.Check(legendary.KindLabel == "Legendary relic" && legendary.relic.Weight < 1f, "legendary relics are labelled and drawn less often than rare ones");
            var noBadge = DefDatabase<UnlockDef>.AllDefs.Where(u => ContentFinder<UnityEngine.Texture2D>.Get("Roguelike/Unlocks/" + u.defName.Replace("Roguelike_Unlock_", ""), reportFailure: false) == null).Select(u => u.defName).ToList();
            SelfTest.Check(noBadge.Count == 0, $"every unlock has its own badge (missing: {string.Join(", ", noBadge)})");
            s.unlocked.Remove("Roguelike_Unlock_HawksEye");

            // Ascension: each level stacks a handicap; winning at the top level unlocks the next.
            int cardsBefore = RewardUtility.Generate(run, RunRewardKind.Cards).Count;
            float weatherBefore = run.WeatherRateFactor, graceBefore = comp.GraceDays;
            run.ascension = 5;
            SelfTest.Check(Math.Abs(run.WeatherRateFactor - weatherBefore * 1.25f) < 0.01f, "Ascension 1+: weather 25% faster");
            SelfTest.Check(Math.Abs(run.RaidFactor(false) - 1.3f) < 0.01f && Math.Abs(run.RaidFactor(true) - 1.95f) < 0.01f, "Ascension 2+ and 5: raids 30% stronger, bosses 50% more");
            SelfTest.Check(RewardUtility.Generate(run, RunRewardKind.Cards).Count == cardsBefore - 1, "Ascension 3+: one fewer reward card");
            SelfTest.Check(Math.Abs(comp.GraceDays - Math.Max(0f, graceBefore - 1f)) < 0.01f, "Ascension 4+: grace one day shorter");
            run.ascension = 0;
            s.maxAscension = 0;
            MetaProgress.RecordRunEnd(true, 0);
            SelfTest.Check(s.maxAscension == 1, "winning at your highest Ascension unlocks the next level");
            MetaProgress.RecordRunEnd(true, 0);
            SelfTest.Check(s.maxAscension == 1, "winning below your highest level unlocks nothing new");

            // Unlock screen.
            Find.WindowStack.Add(new Dialog_Unlocks());
            foreach (var w in Screenshot("s4_unlocks", world: false)) yield return w;
            // Variety section, with one owned unlock switched off.
            MetaProgress.Toggle(medKitUnlock);
            UnlockList.scroll.y = 1700f;
            foreach (var w in Screenshot("s4_unlocks_variety", world: false)) yield return w;
            MetaProgress.Toggle(medKitUnlock);
            UnlockList.scroll.y = 0f;
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
            // Let drop pods open and the run's periodic upkeep (relic effects) run once.
            yield return Ticks(900);
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
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
                case "crashlanded":
                    SelfTest.Check(run.relics.Any(r => r.extraRewardChoices > 0), "Crashlanded: Black Box adds a reward choice");
                    break;
                case "PsycasterExile":
                    SelfTest.Check(PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists.Any(p => p.HasPsylink), "Psycaster Exile: a colonist has a psylink");
                    break;
                case "ImperialNoble":
                    SelfTest.Check(PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists.Any(p => p.royalty != null && p.royalty.AllTitlesForReading.Count > 0), "Imperial Noble: a colonist holds an Imperial title");
                    break;
                case "MechTender":
                    SelfTest.Check(map.mapPawns.AllPawnsSpawned.Count(p => p.RaceProps.IsMechanoid && p.Faction == Faction.OfPlayer) >= 2, "Mech-tender: two player mechs");
                    SelfTest.Check(PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists.Any(p => p.health.hediffSet.HasHediff(part.starterRelic.hediff)), "Mech-tender: Gestator Core effect applied");
                    break;
                case "VoidCultists":
                    SelfTest.Check(RelicEffects.TryCreepyEvent(map), "Void Cultists: the codex can stir up an Anomaly threat");
                    break;
                default:
                    var xenoPart = Find.Scenario.AllParts.FirstOrDefault(p => p.GetType().Name == "ScenPart_ConfigPage_ConfigureStartingPawns_Xenotypes");
                    var counts = xenoPart == null ? null : AccessTools.Field(xenoPart.GetType(), "xenotypeCounts").GetValue(xenoPart) as System.Collections.IEnumerable;
                    var first = counts?.Cast<object>().FirstOrDefault();
                    var xeno = first == null ? null : AccessTools.Field(first.GetType(), "xenotype").GetValue(first) as XenotypeDef;
                    int matching = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists.Count(p => p.genes?.Xenotype == xeno);
                    SelfTest.Check(xeno != null && matching >= 2, $"{part.starterId}: colonists are {xeno?.label} ({matching})");
                    if (part.starterRelic.hediff != null)
                        SelfTest.Check(PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists.All(p => p.health.hediffSet.HasHediff(part.starterRelic.hediff)), $"{part.starterId}: relic effect on every colonist");
                    break;
            }
            Find.CameraDriver.JumpToCurrentMapLoc(map.mapPawns.FreeColonistsSpawned.First().Position);
            foreach (var w in Screenshot("s4_starter_" + part.starterId, world: false)) yield return w;
        }

        /// <summary>Wipe, then "New run": vanilla game-over is suppressed, the same world comes back with a fresh act, and a new run starts.</summary>
        private static IEnumerable<Func<bool>> NewRunScript()
        {
            foreach (var w in WaitForGame()) yield return w;
            var run = Run;
            string seed = Find.World.info.seedString;
            foreach (var p in Find.CurrentMap.mapPawns.FreeColonistsSpawned.ToList()) p.Kill(null);
            yield return Until(() => run.RunOver);
            SelfTest.Check(run.runLost && Find.WindowStack.WindowOfType<Dialog_RunEnd>() != null, "wipe: run over and the end-of-run screen is open");
            foreach (var w in Screenshot("s5_run_end", world: false)) yield return w;
            Find.WindowStack.WindowOfType<Dialog_RunEnd>().Close(false);
            yield return Ticks(1500);
            SelfTest.Check(!Find.LetterStack.LettersListForReading.Any(l => l.def == LetterDefOf.GameEnded), "vanilla game-over is suppressed once the run is over");

            // The main menu's "New colony" opens the vanilla scenario page; the starter cards replace it.
            Find.WindowStack.Add(new Page_SelectScenario());
            SelfTest.Check(Find.WindowStack.WindowOfType<Page_RoguelikeScenario>() != null && Find.WindowStack.WindowOfType<Page_SelectScenario>() == null, "new colony: the starter cards replace the vanilla scenario list");
            Find.WindowStack.WindowOfType<Page_RoguelikeScenario>()?.Close(false);
            yield return Frames(10);

            // New run opens the starter cards; pick a different starter than last time.
            MetaProgress.S.unlocked.Add("Roguelike_Unlock_TribalHunters");
            ScenarioLister.MarkDirty();
            NewRun.Start();
            yield return () => Current.ProgramState == ProgramState.Entry && Find.WindowStack.WindowOfType<Page_RoguelikeScenario>() != null;
            var scenarioPage = Find.WindowStack.WindowOfType<Page_RoguelikeScenario>();
            yield return Frames(30);
            var tribal = scenarioPage.Scenarios.FirstOrDefault(sc => Page_RoguelikeScenario.Part(sc)?.starterId == "tribalHunters");
            SelfTest.Check(scenarioPage.Scenarios.Count >= 2 && tribal != null, $"new run: the starter cards list every available starter ({string.Join(", ", scenarioPage.Scenarios.Select(Page_RoguelikeScenario.DisplayName))})");
            SelfTest.Check(scenarioPage.Selected != null && scenarioPage.Selected != tribal, $"new run: last run's starter is preselected ({(scenarioPage.Selected == null ? "none" : Page_RoguelikeScenario.DisplayName(scenarioPage.Selected))})");
            scenarioPage.Selected = tribal;
            foreach (var w in Screenshot("s7_starters", world: false)) yield return w;
            AccessTools.Method(typeof(Page_RoguelikeScenario), "DoNext").Invoke(scenarioPage, null);
            yield return () => Current.ProgramState == ProgramState.Entry && Find.WindowStack.WindowOfType<Page_RoguelikeLanding>() != null;
            SelfTest.Check(Find.Scenario == tribal, "new run: the chosen starter is set up");
            yield return Frames(60);
            var newRun = WorldComponent_RoguelikeRun.Get;
            SelfTest.Check(Find.World.info.seedString == seed, $"new run: same world seed ({seed})");
            SelfTest.Check(newRun is { active: true, RunOver: false } && newRun.nodes.Count == 15 * WorldComponent_RoguelikeRun.ActsPerRun && newRun.Row(0).All(n => n.state == RunNodeState.Available),
                "new run: a fresh act is laid out and the starting nodes are open");

            // Click through the remaining setup pages.
            var startTile = newRun.Row(0).First().tile;
            Ideo chosenIdeo = null;
            List<Pawn> chosenPawns = null;
            for (int i = 0; i < 12 && Current.ProgramState != ProgramState.Playing; i++)
            {
                var box = Find.WindowStack.WindowOfType<Dialog_MessageBox>();
                if (box != null)
                {
                    box.buttonAAction?.Invoke();
                    box.Close(false);
                    yield return Frames(30);
                    continue;
                }
                var page = Find.WindowStack.Windows.OfType<Page>().LastOrDefault();
                if (page == null) { yield return Frames(30); continue; }
                if (page is Page_RoguelikeLanding landing)
                {
                    yield return Frames(30);
                    SelfTest.Check(landing.Starts.Count == newRun.Row(0).Count() && Find.WindowStack.WindowOfType<Page_SelectStartingSite>() == null, $"new run: the landing page shows the {landing.Starts.Count} starting nodes as cards");
                    SelfTest.Check(!(bool)AccessTools.Method(typeof(Page_RoguelikeLanding), "CanDoNext").Invoke(landing, null), "new run: a starting node must be picked");
                    SelfTest.Check(landing.Starts.Select(n => n.preset).Distinct().Count() == landing.Starts.Count, $"new run: the starting nodes are all different ({string.Join(", ", landing.Starts.Select(n => n.preset.label))})");
                    landing.Selected = landing.Starts.ToList().FindIndex(n => n.tile == startTile);
                    foreach (var w in Screenshot("s7_landing", world: false)) yield return w;
                }
                if (page is Page_RoguelikeIdeo ideoPage)
                {
                    yield return Frames(30);
                    SelfTest.Check(ideoPage.Options.Count == 3 && ideoPage.Options.Select(o => o.name).Distinct().Count() == 3, $"new run: three ideoligion cards ({string.Join(", ", ideoPage.Options.Select(o => o.name))})");
                    SelfTest.Check(!(bool)AccessTools.Method(typeof(Page_RoguelikeIdeo), "CanDoNext").Invoke(ideoPage, null), "new run: an ideoligion must be picked");
                    ideoPage.Selected = 1;
                    chosenIdeo = ideoPage.Options[1];
                    foreach (var w in Screenshot("s7_ideo", world: false)) yield return w;
                }
                if (page is Page_RoguelikePawns pawnPage)
                {
                    yield return Frames(30);
                    SelfTest.Check(pawnPage.Pawns.Count == 10, $"new run: ten candidate colonists ({pawnPage.Pawns.Count}), up to {pawnPage.MaxPick} can go");
                    for (int k = 0; k < pawnPage.Pawns.Count; k++) if (k % 3 == 0) pawnPage.Toggle(pawnPage.Pawns[k]);
                    SelfTest.Check(pawnPage.Picked.Count == Math.Min(pawnPage.MaxPick, 4), $"new run: picking is capped at {pawnPage.MaxPick} ({pawnPage.Picked.Count} picked)");
                    pawnPage.Toggle(pawnPage.Picked.Last());
                    chosenPawns = pawnPage.Picked.ToList();
                    foreach (var w in Screenshot("s7_pawns", world: false)) yield return w;
                }
                SelfTest.Info("setup page: " + page.GetType().Name);
                if ((bool)AccessTools.Method(page.GetType(), "CanDoNext").Invoke(page, null))
                    AccessTools.Method(page.GetType(), "DoNext").Invoke(page, null);
                yield return Frames(60);
            }
            yield return () => Current.ProgramState == ProgramState.Playing && Find.CurrentMap != null;
            yield return Frames(60);
            var started = WorldComponent_RoguelikeRun.Get;
            SelfTest.Check(started is { active: true, RunOver: false } && started.CurrentNode?.tile == startTile, "new run: landed on the chosen starting node");
            SelfTest.Check(NodeComp(Find.CurrentMap) is { Active: true }, "new run: the starting node's objective is running");
            var colonists = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists.ToList();
            SelfTest.Check(chosenPawns != null && colonists.Count == chosenPawns.Count && chosenPawns.All(colonists.Contains), $"new run: exactly the chosen colonists landed ({colonists.Count})");
            SelfTest.Check(!ModsConfig.IdeologyActive || Faction.OfPlayer.ideos.PrimaryIdeo == chosenIdeo, $"new run: the chosen ideoligion is the colony's ({Faction.OfPlayer.ideos.PrimaryIdeo?.name})");
        }

        /// <summary>Meteors with aftermath, tornadoes, the hurricane and the flood, on debug nodes.</summary>
        private static IEnumerable<Func<bool>> Disasters()
        {
            foreach (var w in WaitForGame()) yield return w;
            var run = Run;
            run.CurrentNode.state = RunNodeState.Completed;

            // Meteors.
            foreach (var w in EnterDebugNode("Roguelike_GreatGranary", DefDatabase<GameConditionDef>.GetNamed("Roguelike_EscalatingMeteors"))) yield return w;
            var map = Find.CurrentMap;
            var comp = NodeComp(map);
            var meteors = comp.Condition as GameCondition_EscalatingMeteors;
            SelfTest.Check(meteors != null, "meteor field: meteor storm registered");
            comp.startTick -= (int)((comp.GraceDays + 0.5f) * GenDate.TicksPerDay);
            comp.AdvanceThreat(1f);
            SelfTest.Check(meteors.intensity > 0f, $"meteor storm escalates after grace ({meteors.IntensityText(meteors.intensity)})");
            var colonist = map.mapPawns.FreeColonistsSpawned.First();
            // Drafted pawns stand still, so the test colonist stays inside the shockwave while the meteor falls.
            colonist.drafter.Drafted = true;
            if (!CellFinder.TryFindRandomCellNear(colonist.Position, map, 9, c => c.Standable(map) && !c.Roofed(map) && c.DistanceTo(colonist.Position) >= 6f, out var target))
                target = colonist.Position + new IntVec3(6, 0, 0);
            int impacts = Skyfaller_RoguelikeMeteor.Impacts;
            GameCondition_EscalatingMeteors.SpawnMeteor(map, target, 3);
            Find.CameraDriver.JumpToCurrentMapLoc(target);
            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            yield return Frames(20);
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            foreach (var w in Screenshot("s5_meteor_falling", world: false)) yield return w;
            yield return Until(() => Skyfaller_RoguelikeMeteor.Impacts > impacts);
            SelfTest.Check(GenRadial.RadialCellsAround(target, 3f, true).Any(c => c.InBounds(map) && c.GetThingList(map).Any(t => t.def == ThingDefOf.Filth_RubbleRock)), "meteor: leaves a rubble crater");
            SelfTest.Check(colonist.stances.stunner.Stunned || colonist.Dead || colonist.Downed, "meteor: the shockwave stuns nearby pawns");
            SelfTest.Check(map.listerThings.ThingsOfDef(ThingDefOf.ChunkSlagSteel).Any(), "meteor: drops salvage");
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            foreach (var w in Screenshot("s5_meteor_aftermath", world: false)) yield return w;
            foreach (var c in map.mapPawns.FreeColonistsSpawned) HealthUtility.HealNonPermanentInjuriesAndRestoreLegs(c);
            foreach (var f in map.listerThings.ThingsOfDef(ThingDefOf.Fire).ToList()) f.Destroy();

            // Tornadoes.
            foreach (var w in EnterDebugNode("Roguelike_GreatGranary", DefDatabase<GameConditionDef>.GetNamed("Roguelike_EscalatingTornadoes"))) yield return w;
            map = Find.CurrentMap;
            var tornadoes = NodeComp(map).Condition as GameCondition_EscalatingTornadoes;
            tornadoes.Strike(map);
            SelfTest.Check(map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("Tornado")).Any(), "tornado season: a tornado touches down");
            foreach (var t in map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("Tornado")).ToList()) t.Destroy();

            // Hurricane.
            foreach (var w in EnterDebugNode("Roguelike_GreatGranary", DefDatabase<GameConditionDef>.GetNamed("Roguelike_EscalatingStorm"))) yield return w;
            map = Find.CurrentMap;
            var storm = NodeComp(map).Condition as GameCondition_EscalatingStorm;
            SelfTest.Check(storm?.ForcedWeather()?.defName == "RainyThunderstorm", "hurricane: forces a thunderstorm");
            int events = map.weatherManager.eventHandler.LiveEventsListForReading.Count;
            storm.Strike(map);
            SelfTest.Check(map.weatherManager.eventHandler.LiveEventsListForReading.Count > events, "hurricane: lightning strikes");

            // Flood.
            foreach (var w in EnterDebugNode("Roguelike_GreatGranary", DefDatabase<GameConditionDef>.GetNamed("Roguelike_RisingFlood"))) yield return w;
            map = Find.CurrentMap;
            var flood = NodeComp(map).Condition as GameCondition_RisingFlood;
            int Water(TerrainDef d) => map.AllCells.Count(c => c.GetTerrain(map) == d);
            int shallowBefore = Water(TerrainDefOf.WaterShallow), deepBefore = Water(TerrainDefOf.WaterDeep);
            flood.intensity = 16f;
            flood.ApplyEffects(map);
            SelfTest.Check(Water(TerrainDefOf.WaterShallow) > shallowBefore + map.Size.x, $"flood: the front turns ground to shallow water (edge {flood.edge})");
            SelfTest.Check(Water(TerrainDefOf.WaterDeep) > deepBefore, "flood: deep water follows behind the front");
            Find.CameraDriver.JumpToCurrentMapLoc(map.Center);
            foreach (var w in Screenshot("s5_flood", world: false)) yield return w;
        }

        /// <summary>Node icons, the HUD panel and relic rarity.</summary>
        private static IEnumerable<Func<bool>> Polish()
        {
            foreach (var w in WaitForGame()) yield return w;
            var run = Run;
            var kinds = Enum.GetNames(typeof(NodeKind)).Append("Boss").ToList();
            SelfTest.Check(kinds.All(NodeIcons.Has), $"node icons loaded for all {kinds.Count} kinds");
            var markers = Find.WorldObjects.AllWorldObjects.OfType<RunNodeMarker>().ToList();
            SelfTest.Check(markers.All(m => m.ExpandingIcon == NodeIcons.For(m.Node)), "every marker uses its kind's icon");
            CameraJumper.TryJump(markers.First(m => m.Node.state == RunNodeState.Current));
            Find.WorldCameraDriver.JumpTo(Find.WorldGrid.GetTileCenter(run.Row(2).First().tile));
            foreach (var w in Screenshot("s5_icons", world: true)) yield return w;

            // HUD after the grace period.
            var map = Find.CurrentMap;
            var comp = NodeComp(map);
            comp.startTick -= (int)((comp.GraceDays + 1f) * GenDate.TicksPerDay);
            comp.AdvanceThreat(1.5f);
            CameraJumper.TryJump(new GlobalTargetInfo(map.mapPawns.FreeColonistsSpawned.First().Position, map));
            foreach (var w in Screenshot("s5_hud", world: false)) yield return w;

            // Rarity.
            var rare = DefDatabase<RelicDef>.GetNamed("Roguelike_Relic_WorkGloves");
            var uncommon = DefDatabase<RelicDef>.GetNamed("Roguelike_Relic_Barometer");
            var common = DefDatabase<RelicDef>.GetNamed("Roguelike_Relic_HardyBoots");
            var cards = new List<RewardCard> { new RewardCard { kind = RewardKind.Relic, relic = common }, new RewardCard { kind = RewardKind.Relic, relic = uncommon }, new RewardCard { kind = RewardKind.Relic, relic = rare } };
            SelfTest.Check(cards[0].KindLabel == "Relic" && cards[1].KindLabel == "Uncommon relic" && cards[2].KindLabel == "Rare relic", "relic cards show their rarity");
            SelfTest.Check(rare.Weight < uncommon.Weight && uncommon.Weight < common.Weight, "rarer relics are drawn less often");
            Find.WindowStack.Add(new Dialog_RewardPick(cards, "Self-test: rarity", null));
            foreach (var w in Screenshot("s5_rarity", world: false)) yield return w;
            Find.WindowStack.WindowOfType<Dialog_RewardPick>()?.Close(false);
        }

        /// <summary>Objectives offered only when the squad can do them: blueprints, research, skills, season; re-rolled at offer time.</summary>
        private static IEnumerable<Func<bool>> Gating()
        {
            foreach (var w in WaitForGame()) yield return w;
            var run = Run;
            var map = Find.CurrentMap;
            var squad = map.mapPawns.FreeColonistsSpawned.ToList();
            var start = run.CurrentNode;

            SelfTest.Check(run.nodes.All(n => !n.preset.Gated), "no gated objective is assigned when the map is generated");
            SelfTest.Check(run.Row(0).All(n => n.preset.requiresSkill == null), "starting nodes never need a skill");

            var campus = Preset("Roguelike_RenewableCampus");
            var report = campus.Eligible(run, start.tile, squad);
            SelfTest.Check(!report.Accepted && report.Reason.Contains("blueprints"), $"gated objective is refused without its blueprints ({report.Reason})");
            run.blueprints.Add(DefDatabase<BlueprintGroupDef>.GetNamed("Roguelike_BP_Power"));
            run.blueprints.Add(DefDatabase<BlueprintGroupDef>.GetNamed("Roguelike_BP_Climate"));
            report = campus.Eligible(run, start.tile, squad);
            SelfTest.Check(!report.Accepted && report.Reason.Contains("research"), $"then refused until its research is done ({report.Reason})");
            foreach (var r in campus.requiresResearch.Where(r => !r.IsFinished)) Find.ResearchManager.FinishProject(r, doCompletionLetter: false);
            var builder0 = squad.First(p => !p.skills.GetSkill(SkillDefOf.Construction).TotallyDisabled);
            builder0.skills.GetSkill(SkillDefOf.Construction).Level = Math.Max(builder0.skills.GetSkill(SkillDefOf.Construction).Level, campus.BuildSkillNeeded);
            SelfTest.Check(campus.Eligible(run, start.tile, squad).Accepted, $"and offered once blueprints, research and a builder (construction {campus.BuildSkillNeeded}) are in place");

            // Buildings that need a skilled builder gate the objective too (hospital bed and vitals monitor: Construction 8).
            var hospital = Preset("Roguelike_FieldHospital");
            foreach (var b in hospital.requiresBlueprints) if (!run.blueprints.Contains(b)) run.blueprints.Add(b);
            foreach (var r in hospital.requiresResearch.Where(r => !r.IsFinished)) Find.ResearchManager.FinishProject(r, doCompletionLetter: false);
            foreach (var p in squad) p.skills.GetSkill(SkillDefOf.Construction).Level = 3;
            report = hospital.Eligible(run, start.tile, squad);
            SelfTest.Check(!report.Accepted && report.Reason.Contains("construction 8"), $"field hospital needs a skilled builder ({report.Reason})");
            var builder = squad.FirstOrDefault(p => !p.skills.GetSkill(SkillDefOf.Construction).TotallyDisabled);
            if (builder != null) builder.skills.GetSkill(SkillDefOf.Construction).Level = 10;
            SelfTest.Check(builder != null && hospital.Eligible(run, start.tile, squad).Accepted, "and is offered once someone can build it");

            var garden = Preset("Roguelike_MedicinalGarden");
            foreach (var p in squad) p.skills.GetSkill(SkillDefOf.Plants).Level = 3;
            report = garden.Eligible(run, start.tile, squad);
            SelfTest.Check(!report.Accepted && (report.Reason.ToLowerInvariant().Contains("plants") || report.Reason.Contains("growing")), $"medicinal garden needs a skilled grower ({report.Reason})");
            squad.First(p => !p.skills.GetSkill(SkillDefOf.Plants).TotallyDisabled).skills.GetSkill(SkillDefOf.Plants).Level = 10;
            int twelfths = GenTemperature.TwelfthsInAverageTemperatureRange(start.tile, 6f, 42f).Count;
            SelfTest.Check(garden.Eligible(run, start.tile, squad).Accepted == (twelfths >= garden.growingTwelfths),
                $"with a skilled grower it depends only on the season ({twelfths} growing twelfths here)");

            // Offer time: an objective the squad can't do is re-rolled into one it can.
            var next = run.NodeById(start.next.First());
            next.preset = Preset("Roguelike_UniformWorkshop");
            next.offered = false;
            run.blueprints.RemoveAll(b => b.defName == "Roguelike_BP_Crafting");
            run.RefreshObjective(next, squad);
            SelfTest.Check(next.preset.Eligible(run, next.tile, squad).Accepted && next.preset != Preset("Roguelike_UniformWorkshop"),
                $"an objective the squad can't do is re-rolled when offered ({next.preset.defName})");
            SelfTest.Check(next.preset.randomModifier == false || next.modifier != null, "the re-rolled objective has a weather modifier");

            // A gated objective completes like any other.
            run.CurrentNode.state = RunNodeState.Completed;
            foreach (var w in EnterDebugNode("Roguelike_RenewableCampus", DefDatabase<GameConditionDef>.GetNamed("Roguelike_EscalatingCold"))) yield return w;
            map = Find.CurrentMap;
            var comp = NodeComp(map);
            foreach (var def in new[] { "SolarGenerator", "SolarGenerator", "SolarGenerator", "WindTurbine", "WindTurbine", "Battery", "Battery", "Battery", "Battery", "Heater" })
                SelfTest.Check(SpawnBuilding(map, DefDatabase<ThingDef>.GetNamed(def)), $"renewable campus: placed a {def}");
            BuildRoom(map, 6, 4, comp.squad, null);
            yield return Ticks(300);
            SelfTest.Check(comp.RequirementsMet, $"renewable campus: requirements met ({comp.ProgressText})");
            comp.startTick -= (int)(comp.preset.minDays * GenDate.TicksPerDay);
            yield return Ticks(300);
            SelfTest.Check(comp.completed, "renewable campus: completes after the minimum stay");
        }

        /// <summary>Spawns a player building of <paramref name="def"/> on the first free spot near the squad.</summary>
        private static bool SpawnBuilding(Map map, ThingDef def)
        {
            var near = map.mapPawns.FreeColonistsSpawned.First().Position;
            foreach (var cell in GenRadial.RadialCellsAround(near, 40f, false))
            {
                if (cell.DistanceToEdge(map) < 15) continue; // wind turbines scan a long strip around them
                var rect = GenAdj.OccupiedRect(cell, Rot4.North, def.size).ExpandedBy(1);
                if (!rect.InBounds(map) || !rect.Cells.All(c => c.Standable(map) && c.GetFirstPawn(map) == null && c.GetEdifice(map) == null)) continue;
                var b = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
                b.SetFaction(Faction.OfPlayer);
                GenSpawn.Spawn(b, cell, map, Rot4.North, WipeMode.Vanish);
                return true;
            }
            return false;
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
