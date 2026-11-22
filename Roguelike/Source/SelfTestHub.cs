using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Roguelike
{
    /// <summary>Self-test for hub mode (<c>-roguelikeSelfTest=hub</c>).</summary>
    public static partial class SelfTestScripts
    {
        private static Dialog_Packing PackingWindow => Find.WindowStack.WindowOfType<Dialog_Packing>();

        /// <summary>Opens Send a team, picks <paramref name="node"/>, sends only <paramref name="team"/> and waits until they're there (map nodes) or the caravan is out (map-less).</summary>
        private static IEnumerable<Func<bool>> SendTeam(RunNode node, Pawn team, Action<Dialog_RewardPick> onCards = null, Action<Dialog_Packing> onPacking = null, string shots = null)
        {
            Hub.OpenSendTeam(Run);
            yield return () => PathWindow != null;
            yield return Frames(30);
            onCards?.Invoke(PathWindow);
            if (shots != null)
                foreach (var w in Screenshot(shots + "_cards", world: false)) yield return w;
            PathWindow.Pick(PathWindow.Cards.ToList().FindIndex(c => c.pathNode == node));
            yield return () => PackingWindow != null;
            var packing = PackingWindow;
            onPacking?.Invoke(packing);
            packing.SetTeam(new[] { team });
            packing.Essentials();
            if (shots != null)
                foreach (var w in Screenshot(shots + "_team", world: false)) yield return w;
            SelfTest.Check(packing.Depart(), $"node {node.id}: the team departs");
            if (node.preset.HasMap)
            {
                int until = UnityEngine.Time.frameCount + 2400;
                yield return () => team.Spawned && team.Map.Tile == node.tile || UnityEngine.Time.frameCount > until;
                if (!(team.Spawned && team.Map.Tile == node.tile))
                    SelfTest.Info($"team not at node {node.id}: state {node.state}, spawned {team.Spawned}, map {team.MapHeld?.Tile}, caravan {team.GetCaravan()?.Tile}, " +
                                  $"canEnter {Run.CanEnter(node)}, teamAway {Hub.TeamAway(Run)?.id}, windows {string.Join(", ", Find.WindowStack.Windows.Select(w => w.GetType().Name))}");
            }
            yield return Frames(30);
        }

        /// <summary>The return packing screen for <paramref name="map"/>: depart and wait until <paramref name="team"/> is back at the hub.</summary>
        private static IEnumerable<Func<bool>> GoHome(Map map, Pawn team, bool open)
        {
            // This test isn't about combat: clear raiders off the map and patch the team up so it can walk home.
            KillAll(map.mapPawns.AllPawnsSpawned.Where(p => p.HostileTo(Faction.OfPlayer)));
            if (team.health.hediffSet.hediffs.Any(h => h is Hediff_Injury)) HealthUtility.HealNonPermanentInjuriesAndRestoreLegs(team);
            if (open) Hub.OpenReturn(map);
            int openBy = UnityEngine.Time.frameCount + 2400;
            yield return () => PackingWindow != null || UnityEngine.Time.frameCount > openBy;
            if (PackingWindow == null)
                SelfTest.Info($"no return packing: map {map?.Tile} alive {Find.Maps.Contains(map)}, hub {Run.HubMap?.Tile}, team map {team.MapHeld?.Tile} downed {team.Downed} dead {team.Dead}, " +
                              $"runOver {Run.RunOver}, pendingReturn {Run.pendingReturnNodeId}, windows {string.Join(", ", Find.WindowStack.Windows.Select(w => w.GetType().Name))}");
            var packing = PackingWindow;
            SelfTest.Check(packing.Pawns.Contains(team), "the return trip takes the team");
            if (!packing.Depart()) SelfTest.Info($"return refused: downed {team.Downed}, mass {packing.MassUsage:0}/{packing.MassCapacity:0}");
            int until = UnityEngine.Time.frameCount + 2400;
            yield return () => team.Spawned && team.Map == Run.HubMap || UnityEngine.Time.frameCount > until;
            if (!(team.Spawned && team.Map == Run.HubMap))
                SelfTest.Info($"team not home: dead {team.Dead}, downed {team.Downed}, map {team.MapHeld?.Tile}, caravan {team.GetCaravan()?.Tile}, windows {string.Join(", ", Find.WindowStack.Windows.Select(w => w.GetType().Name))}");
            yield return Ticks(400);
        }

        /// <summary>Opens the act map (the radial frontier in hub mode), screenshots it and closes it.</summary>
        private static IEnumerable<Func<bool>> FrontierMapShot(string name, float zoomedOutRing = 0f)
        {
            Dialog_RunMap.Toggle();
            yield return () => Find.WindowStack.WindowOfType<Dialog_RunMap>() != null;
            foreach (var w in Screenshot(name, world: false)) yield return w;
            if (zoomedOutRing > 0f)
            {
                AccessTools.Field(typeof(Dialog_RunMap), "ringPx").SetValue(Find.WindowStack.WindowOfType<Dialog_RunMap>(), zoomedOutRing);
                foreach (var w in Screenshot(name + "_zoomed_out", world: false)) yield return w;
            }
            Dialog_RunMap.Toggle();
            yield return Frames(10);
        }

        private static IEnumerable<Func<bool>> HubRun()
        {
            foreach (var w in WaitForGame()) yield return w;
            var run = Run;
            var grid = Find.WorldGrid;

            // ---------- The hub and the first ring ----------
            SelfTest.Check(run.hubMode, "hub mode chosen before world generation: this is a hub run");
            var hub = run.HubNode;
            var hubMap = run.HubMap;
            SelfTest.Check(hub != null && hub.depth == 0 && hub.state == RunNodeState.Completed && hubMap != null && Find.CurrentMap == hubMap, "landed on a hub site, which is now the hub");
            var otherSites = run.nodes.Where(n => n.depth == 0 && n != hub).ToList();
            SelfTest.Check(otherSites.Count >= 1 && otherSites.All(n => n.state == RunNodeState.Skipped), $"the other {otherSites.Count} hub sites are dropped");
            var markers = Find.WorldObjects.AllWorldObjects.OfType<RunNodeMarker>().ToList();
            SelfTest.Check(markers.Count == run.nodes.Count(n => n.state != RunNodeState.Skipped) && markers.All(m => m.Node.state != RunNodeState.Skipped), $"markers only for the hub and the frontier ({markers.Count})");
            var ring = run.nodes.Where(n => n.depth == 1).ToList();
            SelfTest.Check(ring.Count == 4 && ring.All(n => n.state == RunNodeState.Available && n.parent == hub.id) && hub.next.OrderBy(i => i).SequenceEqual(ring.Select(n => n.id)), $"{ring.Count} frontier nodes open around the hub");
            SelfTest.Check(ring.All(n => !n.IsBoss && n.preset.kind is not (NodeKind.Hub or NodeKind.Elite)), "the first ring has no bosses or elites");
            SelfTest.Check(ring.Select(n => grid[n.tile].PrimaryBiome).Distinct().Count() >= 2, $"the first ring spans biomes ({string.Join(", ", ring.Select(n => grid[n.tile].PrimaryBiome.label))})");
            SelfTest.Check(!NodeComp(hubMap).Active, "the hub has no objective of its own");
            foreach (var n in run.nodes) SelfTest.Info($"node {n.id} depth {n.depth} {n.state} {n.preset.defName} reward {n.reward} tile {n.tile}");

            // ---------- Hub weather ----------
            var pressure = run.pressure;
            var cond = pressure.Condition(hubMap);
            SelfTest.Check(cond != null && cond.intensity == 0f && pressure.InGrace, $"the hub's weather ({pressure.modifier?.defName}) is registered and holds off at first");
            pressure.startTick -= (int)((pressure.graceDays + 1f) * GenDate.TicksPerDay);
            pressure.Tick(run, hubMap, 1f);
            SelfTest.Check(cond != null && cond.intensity != 0f && Math.Sign(cond.intensity) == Math.Sign(pressure.ratePerDay), $"after grace the hub's weather worsens ({cond?.IntensityText(cond.intensity)})");
            SelfTest.Check(pressure.nextSiegeTick > Find.TickManager.TicksGame, "a siege is scheduled");
            CameraJumper.TryJump(new GlobalTargetInfo(hubMap.Center, hubMap));
            foreach (var w in Screenshot("hub_s1_hub", world: false)) yield return w;
            Find.WorldCameraDriver.JumpTo(grid.GetTileCenter(hub.tile));
            foreach (var w in FrontierMapShot("hub_s1_frontier")) yield return w;
            CameraJumper.TryJump(new GlobalTargetInfo(hubMap.Center, hubMap));

            int colonists = hubMap.mapPawns.FreeColonistsSpawnedCount;
            var goer = hubMap.mapPawns.FreeColonistsSpawned.First();

            // ---------- 1. A quick node: send a team, clear it, come home ----------
            var target = ring[0];
            target.preset = Preset("Roguelike_Ambush");
            target.modifier = null;
            target.reward = RunRewardKind.Cards;
            target.offered = true;
            bool shotCards = false;
            foreach (var w in SendTeam(target, goer,
                         cards =>
                         {
                             SelfTest.Check(cards.Cards.Select(c => c.pathNode.id).OrderBy(i => i).SequenceEqual(ring.Select(n => n.id)), "Send a team offers every open frontier node");
                             shotCards = true;
                         },
                         packing => SelfTest.Check(packing.Candidates.Count(p => p.IsColonist) == colonists && packing.Pawns.Count == Math.Max(1, colonists - 1), $"by default all colonists but one go ({packing.Pawns.Count} of {colonists})"),
                         shots: "hub_s2_send")) yield return w;
            SelfTest.Check(shotCards, "send-a-team cards opened");
            var nodeMap = Current.Game.FindMap(target.tile);
            SelfTest.Check(nodeMap != null && nodeMap.mapPawns.FreeColonistsSpawned.SequenceEqual(new[] { goer }), "only the chosen team arrived at the node");
            SelfTest.Check(Find.Maps.Contains(hubMap) && hubMap.mapPawns.FreeColonistsSpawnedCount == colonists - 1, $"the hub stays loaded with everyone else ({hubMap.mapPawns.FreeColonistsSpawnedCount})");
            SelfTest.Check(target.state == RunNodeState.Current && run.CurrentNode == target, "the node is current");
            SelfTest.Check(ring.Where(n => n != target).All(n => n.state == RunNodeState.Available), "the rest of the frontier stays open");
            SelfTest.Check(NodeComp(nodeMap) is { Active: true } c1 && c1.preset == target.preset && c1.EnemiesLeft > 0, "the node's objective started, enemies attack");
            SelfTest.Check(Hub.CantSendReason(run) != null && !run.CanEnter(ring[1]), "only one frontier team at a time");
            yield return Ticks(500);
            SelfTest.Check(target.state == RunNodeState.Current && !run.RunOver, "the node stays current while the team is there");
            foreach (var w in Screenshot("hub_s3_node", world: false)) yield return w;

            float before = pressure.Condition(hubMap).intensity;
            int points = run.pointsEarned;
            NodeComp(nodeMap).Complete();
            SelfTest.Check(target.state == RunNodeState.Completed && run.deepestCleared == 1 && run.pointsEarned == points + 1, "the quick node is cleared: +1 point, depth 1 reached");
            var kids = target.next.Select(run.NodeById).ToList();
            SelfTest.Check(kids.Count >= 2 && kids.Count <= 3 && kids.All(k => k.depth == 2 && k.parent == target.id && k.state == RunNodeState.Available), $"clearing it revealed {kids.Count} nodes one step further out");
            SelfTest.Check(kids.All(k => Find.WorldObjects.AllWorldObjects.OfType<RunNodeMarker>().Any(m => m.nodeId == k.id)), "the new nodes have markers");
            float after = pressure.Condition(hubMap).intensity;
            SelfTest.Check(Math.Abs(after - before * 0.5f) < 0.0001f, $"a quick node halves the hub's weather ({before:0.###} -> {after:0.###})");
            foreach (var w in PickFirstReward(target)) yield return w;
            foreach (var w in GoHome(nodeMap, goer, open: false)) yield return w;
            SelfTest.Check(hubMap.mapPawns.FreeColonistsSpawnedCount == PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists.Count(), "the return packing opened by itself and the team is home");
            SelfTest.Check(Current.Game.FindMap(target.tile) == null && target.state == RunNodeState.Completed && !run.RunOver, "the node map is gone, the node stays cleared, the run goes on");

            // ---------- 2. A failed node and a pull-back: the node reopens, the run goes on ----------
            var second = ring[1];
            second.preset = Preset("Roguelike_RefugeeEscort");
            second.modifier = null;
            second.reward = RunRewardKind.Cards;
            second.offered = true;
            foreach (var w in SendTeam(second, goer)) yield return w;
            var secondMap = Current.Game.FindMap(second.tile);
            NodeComp(secondMap).Fail("Self-test: the refugee is gone.");
            SelfTest.Check(second.state == RunNodeState.Available && !run.RunOver, "a failed node reopens and the run goes on");
            SelfTest.Check(!run.CanEnter(second) && !run.CanEnter(ring[2]) && Hub.CantSendReason(run) != null, "no new frontier team can leave while the failed team is still out");
            foreach (var w in GoHome(secondMap, goer, open: true)) yield return w;
            SelfTest.Check(Current.Game.FindMap(second.tile) == null && run.CanEnter(second), "once the team is home it can be tried again");

            var third = ring[2];
            third.preset = Preset("Roguelike_Ambush");
            third.modifier = null;
            third.reward = RunRewardKind.Cards;
            third.offered = true;
            foreach (var w in SendTeam(third, goer)) yield return w;
            foreach (var w in GoHome(Current.Game.FindMap(third.tile), goer, open: true)) yield return w;
            SelfTest.Check(third.state == RunNodeState.Available && run.CurrentNode == null && !run.RunOver, "pulling back before the objective leaves the node open");

            // ---------- 3. A long node: an expedition breaks the hub's weather ----------
            third.preset = Preset("Roguelike_GreatGranary");
            third.modifier = ModifierPool.PickFor(third.tile);
            third.reward = RunRewardKind.Relics;
            foreach (var w in SendTeam(third, goer)) yield return w;
            var thirdMap = Current.Game.FindMap(third.tile);
            var oldCond = pressure.Condition(hubMap);
            points = run.pointsEarned;
            NodeComp(thirdMap).Complete();
            var newCond = pressure.Condition(hubMap);
            SelfTest.Check(oldCond != null && newCond != null && newCond != oldCond && newCond.intensity == 0f && pressure.InGrace && !hubMap.gameConditionManager.ActiveConditions.Contains(oldCond),
                $"an expedition breaks the hub's weather; {pressure.modifier.defName} rolls in after {pressure.graceDays:0.#} days of grace");
            SelfTest.Check(run.pointsEarned == points + 2, "a long node is worth 2 points");
            SelfTest.Check(RewardWindow != null && RewardWindow.Cards.All(c => c.kind == RewardKind.Relic), "a long node offers relics");
            foreach (var w in PickFirstReward(third)) yield return w;
            // Expeditions have stretch goals: the team stays until it chooses to go home.
            yield return Frames(30);
            SelfTest.Check(NodeComp(thirdMap).StretchOpen && PackingWindow == null && run.pendingReturnNodeId < 0, "with stretch goals left the team stays instead of heading home");
            foreach (var w in GoHome(thirdMap, goer, open: true)) yield return w;

            // ---------- 4. A map-less node: the team comes straight home ----------
            var ev = ring[3];
            ev.preset = Preset("Roguelike_Encounter");
            ev.modifier = null;
            ev.reward = RunRewardKind.None;
            ev.offered = true;
            foreach (var w in SendTeam(ev, goer)) yield return w;
            yield return () => EventWindow != null;
            SelfTest.Check(ev.state == RunNodeState.Current, "an event node opens its dialog on arrival");
            var caravan = Find.WorldObjects.Caravans.First(c => c.IsPlayerControlled && c.Tile == ev.tile);
            EventWindow.Close(false);
            EventUtility.Resolve(ev, caravan, DefDatabase<RoguelikeEventDef>.AllDefsListForReading.First(), 0);
            SelfTest.Check(ev.state == RunNodeState.Completed, "the event resolves and clears the node");
            yield return () => goer.Spawned && goer.Map == hubMap;
            SelfTest.Check(!Find.WorldObjects.Caravans.Any(c => c.IsPlayerControlled), "after an event the team walks straight home");

            // ---------- 5. A harvest trip: no objective, no reward, no relief ----------
            var sites = Hub.HarvestSites(run).ToList();
            SelfTest.Check(sites.Contains(target) && sites.Contains(third) && !sites.Contains(ev), $"cleared map nodes are harvest sites ({sites.Count})");
            float pressureBefore = pressure.Condition(hubMap).intensity;
            Hub.OpenHarvestTrip(run, target);
            yield return () => PackingWindow != null;
            PackingWindow.SetTeam(new[] { goer });
            PackingWindow.Depart();
            yield return () => goer.Spawned && goer.Map.Tile == target.tile;
            var harvestMap = goer.Map;
            SelfTest.Check(!NodeComp(harvestMap).Active && RewardWindow == null && target.state == RunNodeState.Completed, "the harvest site has no objective or reward");
            yield return Ticks(300);
            SelfTest.Check(pressure.Condition(hubMap).intensity >= pressureBefore, "a harvest trip doesn't push back the hub's weather");
            foreach (var w in Screenshot("hub_s5_harvest", world: false)) yield return w;
            foreach (var w in GoHome(harvestMap, goer, open: true)) yield return w;

            // ---------- 6. A siege ----------
            pressure.nextSiegeTick = Find.TickManager.TicksGame + 2000;
            int sieges = pressure.sieges;
            yield return Until(() => pressure.sieges > sieges);
            var raiders = hubMap.mapPawns.AllPawnsSpawned.Where(p => p.HostileTo(Faction.OfPlayer) && !p.Dead).ToList();
            SelfTest.Check(raiders.Count > 0 && pressure.siegeWarned == false, $"the announced siege attacks the hub ({raiders.Count} raiders)");
            SelfTest.Check(pressure.nextSiegeTick >= Find.TickManager.TicksGame + (int)((HubPressure.SiegeEveryDays - 0.1f) * GenDate.TicksPerDay), "the next siege is scheduled");
            KillAll(raiders);

            // ---------- 7. Save and load ----------
            int nodeCount = run.nodes.Count, hubId = run.hubNodeId, deepest = run.deepestCleared;
            var modifier = pressure.modifier;
            GameDataSaveLoader.SaveGame("rltest_hub");
            yield return Frames(30);
            var oldGame = Current.Game;
            GameDataSaveLoader.LoadGame("rltest_hub");
            yield return () => Current.Game != oldGame && Current.ProgramState == ProgramState.Playing && Find.CurrentMap != null && Run != null;
            yield return Frames(60);
            run = Run;
            pressure = run.pressure;
            hubMap = run.HubMap;
            SelfTest.Check(run.hubMode && run.nodes.Count == nodeCount && run.hubNodeId == hubId && run.deepestCleared == deepest, "after loading: hub run, frontier and depth kept");
            SelfTest.Check(hubMap != null && pressure.modifier == modifier && pressure.Condition(hubMap) != null, "after loading: the hub and its weather are back");
            SelfTest.Check(run.nodes.Where(n => n.depth > 0).All(n => n.parent >= 0) && Find.WorldObjects.AllWorldObjects.OfType<RunNodeMarker>().Count() == run.nodes.Count(n => n.state != RunNodeState.Skipped), "after loading: node depths, parents and markers kept");
            try { File.Delete(GenFilePaths.FilePathForSavedGame("rltest_hub")); } catch (Exception ex) { SelfTest.Info("couldn't delete the test save: " + ex.Message); }

            // ---------- 8. Push out to the final boss, then endless ----------
            var node = run.nodes.Where(n => n.state == RunNodeState.Completed && n.depth == run.deepestCleared && n.next.Count > 0).First();
            int metaWins = MetaProgress.S.wins;
            while (node.depth < Hub.FinalDepth)
            {
                var children = node.next.Select(run.NodeById).ToList();
                int d = node.depth + 1;
                if (d % Hub.BossEvery == 0)
                    SelfTest.Check(children.Count == 1 && children[0].IsBoss && children[0].preset.isBoss, $"depth {d}: a single boss waits");
                else if (d % Hub.EliteEvery == 0)
                    SelfTest.Check(children.Count(k => k.preset.kind == NodeKind.Elite) == 1, $"depth {d}: one of the {children.Count} nodes is an elite");
                SelfTest.Check(children.All(k => k.act == 1 + (d - 1) / Hub.BossEvery), $"depth {d}: difficulty tier {1 + (d - 1) / Hub.BossEvery}");
                var next = children.First(k => k.state == RunNodeState.Available);
                run.Choose(next);
                SelfTest.Check(run.act == next.act, $"depth {d}: entering sets the difficulty tier");
                run.NotifyNodeCompleted(next.tile);
                if (d >= Hub.FinalDepth) { node = next; break; }
                if (next.reward != RunRewardKind.None)
                {
                    yield return () => RewardWindow != null;
                    foreach (var w in PickFirstReward(next)) yield return w;
                }
                yield return Frames(10);
                node = next;
            }
            SelfTest.Check(run.runWon && run.RunOver && MetaProgress.S.wins == metaWins + 1, $"the depth {Hub.FinalDepth} boss wins the run");
            yield return () => Find.WindowStack.WindowOfType<Dialog_RunEnd>() != null;
            foreach (var w in Screenshot("hub_s8_victory", world: false)) yield return w;
            Find.WindowStack.WindowOfType<Dialog_RunEnd>().KeepGoing();
            SelfTest.Check(run.endless && !run.RunOver, "Keep going: the run carries on, endless");
            SelfTest.Check(node.next.Count > 0 && node.next.All(id => run.NodeById(id).depth == Hub.FinalDepth + 1 && run.NodeById(id).state == RunNodeState.Available), "the frontier keeps spreading past the final boss");
            foreach (var w in FrontierMapShot("hub_s8_frontier", zoomedOutRing: 24f)) yield return w;

            // ---------- 9. An endless run still ends on a wipe, without counting twice ----------
            int runs = MetaProgress.S.runs;
            KillAll(PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists.ToList());
            yield return Until(() => run.runLost);
            SelfTest.Check(run.RunOver && MetaProgress.S.runs == runs, "losing the squad ends the endless run; it isn't counted as another run");

            // ---------- 10. A new hub run through the real setup pages ----------
            NewRun.Start();
            yield return () => Current.ProgramState == ProgramState.Entry && Find.WindowStack.WindowOfType<Page_RoguelikeScenario>() != null;
            var scenarioPage = Find.WindowStack.WindowOfType<Page_RoguelikeScenario>();
            SelfTest.Check(MetaProgress.HubMode, "the starter page remembers hub mode");
            Page_RoguelikeScenario.SetMode(false);
            SelfTest.Check(!MetaProgress.HubMode, "the mode buttons switch to classic");
            Page_RoguelikeScenario.SetMode(true);
            if (scenarioPage.Selected == null) scenarioPage.Selected = scenarioPage.Scenarios.First();
            foreach (var w in Screenshot("hub_s9_starters", world: false)) yield return w;
            AccessTools.Method(typeof(Page_RoguelikeScenario), "DoNext").Invoke(scenarioPage, null);
            yield return () => Current.ProgramState == ProgramState.Entry && Find.WindowStack.WindowOfType<Page_RoguelikeLanding>() != null;
            yield return Frames(60);
            var fresh = WorldComponent_RoguelikeRun.Get;
            var landing = Find.WindowStack.WindowOfType<Page_RoguelikeLanding>();
            SelfTest.Check(fresh is { hubMode: true, active: true, RunOver: false } && landing.Starts.Count == 3 && landing.Starts.All(n => n.preset.kind == NodeKind.Hub && n.depth == 0),
                $"new hub run: the landing page offers {landing.Starts.Count} hub sites");
            landing.Selected = 1;
            var hubTile = landing.Starts[1].tile;
            foreach (var w in Screenshot("hub_s9_landing", world: false)) yield return w;
            for (int i = 0; i < 16 && Current.ProgramState != ProgramState.Playing; i++)
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
                if (page is Page_RoguelikeIdeo ideoPage) { yield return Frames(30); ideoPage.Selected = 0; }
                if (page is Page_RoguelikePawns pawnPage)
                {
                    yield return Frames(30);
                    foreach (var p in pawnPage.Pawns.Take(pawnPage.MaxPick).ToList()) pawnPage.Toggle(p);
                }
                if ((bool)AccessTools.Method(page.GetType(), "CanDoNext").Invoke(page, null))
                    AccessTools.Method(page.GetType(), "DoNext").Invoke(page, null);
                yield return Frames(60);
            }
            yield return () => Current.ProgramState == ProgramState.Playing && Find.CurrentMap != null;
            yield return Frames(120);
            var started = WorldComponent_RoguelikeRun.Get;
            SelfTest.Check(started is { hubMode: true, RunOver: false } && started.HubNode?.tile == hubTile && started.HubMap == Find.CurrentMap, "new hub run: the chosen site is the hub");
            SelfTest.Check(started.nodes.Count(n => n.depth == 1 && n.state == RunNodeState.Available) == 4 && started.pressure.Condition(started.HubMap) != null, "new hub run: the first ring and the hub's weather are set up");
        }
    }
}
