using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Roguelike
{
    /// <summary>
    /// Hub mode: one permanent colony (the hub) and a frontier of nodes that spreads outward from it. Teams leave the hub,
    /// clear a node and come home; clearing a node reveals 2–3 more further out and pushes back the hub's weather.
    /// Difficulty grows with depth (distance from the hub, in nodes). Elites every 3 deep, a boss every 5, the final boss at 15.
    /// </summary>
    public static class Hub
    {
        public const int FinalDepth = 15;
        public const int BossEvery = 5;
        public const int EliteEvery = 3;
        /// <summary>Nodes revealed around the hub at the start, and below each cleared node.</summary>
        private const int FirstRing = 4;
        private static readonly IntRange Children = new IntRange(2, 3);

        public static NodePresetDef HubPreset => DefDatabase<NodePresetDef>.GetNamed("Roguelike_Hub");

        public static bool Active => WorldComponent_RoguelikeRun.Get is { active: true, hubMode: true };

        // ---------- Generation ----------

        /// <summary>World generation: three candidate hub sites, spread apart; the landing screen picks one.</summary>
        public static bool Generate(WorldComponent_RoguelikeRun run)
        {
            foreach (var old in Find.WorldObjects.AllWorldObjects.OfType<RunNodeMarker>().ToList()) old.Destroy();
            run.nodes = new List<RunNode>();
            run.hubMode = true;
            run.act = 1;
            // Like classic nodes, each site and frontier node is a random tile in a random biome; the planet is never shown.
            var byBiome = WorldComponent_RoguelikeRun.TilesByBiome();
            for (int i = 0; i < 3; i++)
            {
                var tile = RandomTile(run, byBiome, run.nodes.Select(n => Find.WorldGrid[n.tile].PrimaryBiome), mild: true);
                if (tile.Valid) AddCandidate(run, tile);
            }
            if (run.nodes.Count == 0)
            {
                Log.Error("[Roguelike] Couldn't find a hub site on this world.");
                return false;
            }
            run.active = true;
            return true;
        }

        private static void AddCandidate(WorldComponent_RoguelikeRun run, PlanetTile tile)
        {
            var node = new RunNode
            {
                id = run.nodes.Count, row = 0, col = run.nodes.Count, depth = 0, tile = tile,
                type = RunNodeType.Normal, state = RunNodeState.Available, reward = RunRewardKind.None,
                preset = HubPreset, modifier = ModifierPool.PickFor(tile),
            };
            run.nodes.Add(node);
            AddMarker(node);
        }

        private static void AddMarker(RunNode node)
        {
            var marker = (RunNodeMarker)WorldObjectMaker.MakeWorldObject(RoguelikeDefOf.Roguelike_RunNode);
            marker.Tile = node.tile;
            marker.nodeId = node.id;
            Find.WorldObjects.Add(marker);
        }

        /// <summary>Landed on a hub site: the others go away, the first ring of the frontier appears and the hub's weather starts.</summary>
        public static RunNode Begin(WorldComponent_RoguelikeRun run, PlanetTile tile)
        {
            var hub = run.NodeAt(tile);
            if (hub == null || hub.depth != 0 || hub.state != RunNodeState.Available) return null;
            run.hubNodeId = hub.id;
            hub.state = RunNodeState.Completed;
            foreach (var other in run.nodes.Where(n => n != hub && n.depth == 0))
            {
                other.state = RunNodeState.Skipped;
                Find.WorldObjects.AllWorldObjects.OfType<RunNodeMarker>().FirstOrDefault(m => m.nodeId == other.id)?.Destroy();
            }
            Reveal(run, hub, FirstRing);
            var map = Current.Game.FindMap(tile);
            if (map != null) run.pressure.Start(run, map, hub.modifier);
            return hub;
        }

        /// <summary>New nodes one step further out than <paramref name="parent"/>: a single boss on boss depths, otherwise 2–3, one of them an elite on elite depths.</summary>
        public static List<RunNode> Reveal(WorldComponent_RoguelikeRun run, RunNode parent, int count = -1)
        {
            int depth = parent.depth + 1;
            bool boss = depth % BossEvery == 0;
            bool eliteDepth = !boss && depth % EliteEvery == 0;
            if (count < 0) count = Children.RandomInRange;
            if (boss) count = 1;
            var added = new List<RunNode>();
            var byBiome = WorldComponent_RoguelikeRun.TilesByBiome();
            for (int i = 0; i < count; i++)
            {
                var tile = RandomTile(run, byBiome, added.Select(n => Find.WorldGrid[n.tile].PrimaryBiome), mild: false);
                if (!tile.Valid) break;
                var node = new RunNode
                {
                    id = run.nodes.Count, depth = depth, parent = parent.id,
                    act = 1 + (depth - 1) / BossEvery, row = (depth - 1) % BossEvery, col = i, tile = tile,
                    type = boss ? RunNodeType.Boss : RunNodeType.Normal, state = RunNodeState.Available,
                };
                run.nodes.Add(node);
                NodeKind? kind = boss ? null : eliteDepth && i == 0 ? NodeKind.Elite : FrontierKinds.RandomElementByWeight(k => k.weight).kind;
                run.AssignPreset(node, kind);
                node.reward = RewardFor(node);
                parent.next.Add(node.id);
                if (node.preset.assault) run.PlaceEnemyBase(node);
                AddMarker(node);
                added.Add(node);
            }
            if (added.Count == 0) Log.Warning($"[Roguelike] No room for frontier nodes beyond node {parent.id}.");
            return added;
        }

        /// <summary>Longer nodes pay more: expeditions and elites offer relics, other fights cards (sometimes recruits).</summary>
        private static RunRewardKind RewardFor(RunNode n)
        {
            if (n.IsBoss) return n.depth >= FinalDepth ? RunRewardKind.None : RunRewardKind.Cards;
            if (!n.preset.HasMap) return RunRewardKind.None;
            if (n.preset.kind is NodeKind.Elite or NodeKind.Expedition) return RunRewardKind.Relics;
            return Rand.Chance(0.3f) ? RunRewardKind.Recruits : RunRewardKind.Cards;
        }

        private static readonly (NodeKind kind, float weight)[] FrontierKinds =
        {
            (NodeKind.Expedition, 25f), (NodeKind.CombatAlerted, 18f), (NodeKind.CombatUnaware, 14f), (NodeKind.Escort, 8f), (NodeKind.Event, 12f),
            (NodeKind.Merchant, 6f), (NodeKind.Treasure, 5f), (NodeKind.Rest, 5f),
        };

        /// <summary>
        /// A random tile in a random biome (weighted like vanilla's settlement picks), avoiding <paramref name="avoid"/>'s
        /// biomes while others are left, and clear of every other node. Hub sites keep to mild biomes.
        /// </summary>
        private static PlanetTile RandomTile(WorldComponent_RoguelikeRun run, Dictionary<BiomeDef, List<PlanetTile>> byBiome, IEnumerable<BiomeDef> avoid, bool mild)
        {
            if (byBiome.Count == 0) return PlanetTile.Invalid;
            var grid = Find.WorldGrid;
            var biomes = byBiome.Keys.ToList();
            if (mild && biomes.Where(b => b.settlementSelectionWeight >= WorldComponent_RoguelikeRun.LandingBiomeWeight).ToList() is { Count: > 0 } m) biomes = m;
            var avoided = avoid.ToList();
            if (biomes.Where(b => !avoided.Contains(b)).ToList() is { Count: > 0 } fresh) biomes = fresh;
            for (int tries = 0; tries < 60; tries++)
            {
                var tile = byBiome[biomes.RandomElementByWeight(b => b.settlementSelectionWeight)].RandomElement();
                if (run.nodes.All(n => grid.ApproxDistanceInTiles(n.tile, tile) >= WorldComponent_RoguelikeRun.MinNodeGapTiles)) return tile;
            }
            return PlanetTile.Invalid;
        }

        // ---------- Node outcomes ----------

        /// <summary>A frontier node's objective is met: points, push back the hub's weather, reveal what lies beyond, then reward and head home.</summary>
        public static void Completed(WorldComponent_RoguelikeRun run, RunNode node)
        {
            node.state = RunNodeState.Completed;
            bool longNode = IsLong(node);
            int points = node.IsBoss ? 5 : longNode ? 2 : 1;
            run.pointsEarned += points;
            MetaProgress.Award(points);
            run.deepestCleared = Mathf.Max(run.deepestCleared, node.depth);
            run.pressure.Relieve(run, longNode, node);
            Reveal(run, node);
            if (node.IsBoss && node.depth >= FinalDepth && !run.runWon)
            {
                run.runWon = true;
                MetaProgress.RecordRunEnd(true, run.ascension);
                run.pendingReturnNodeId = node.id;
                Find.WindowStack.Add(new Dialog_RunEnd(true, $"You beat the final boss, {FinalDepth} nodes out from your hub. Keep going to push the frontier further."));
                return;
            }
            if (node.IsBoss)
                Find.LetterStack.ReceiveLetter($"Checkpoint: depth {node.depth}", $"The boss is beaten. The frontier beyond is harder. The final boss waits at depth {FinalDepth}.", LetterDefOf.PositiveEvent);
            var map = Current.Game.FindMap(node.tile);
            if (node.preset.HasMap) Wear.AddForNode(map);
            // Rewards land with the team, or at the hub if the team has no map. With stretch goals left, the team stays until it leaves.
            if (node.reward != RunRewardKind.None) run.OfferReward(node, map ?? run.HubMap);
            else if (!WorldComponent_RoguelikeRun.StaysForStretch(node)) run.pendingReturnNodeId = node.id;
        }

        /// <summary>Expeditions, elites and bosses are the long or hard nodes: they clear the hub's weather fully.</summary>
        public static bool IsLong(RunNode node) => node.IsBoss || node.preset.kind is NodeKind.Expedition or NodeKind.Elite;

        /// <summary>A failed objective (escort lost, someone died) doesn't end a hub run: the node reopens for another try.</summary>
        public static void Failed(WorldComponent_RoguelikeRun run, RunNode node, string reason)
        {
            node.state = RunNodeState.Available;
            node.offered = false;
            Find.LetterStack.ReceiveLetter($"Failed: {node.preset.LabelCap}", reason + "\n\nThe node is still open, but the team has to come home first. Use Return to hub.", LetterDefOf.NegativeEvent);
        }

        // ---------- Ticking ----------

        public static void Tick(WorldComponent_RoguelikeRun run)
        {
            // The team pulled back (or everyone there died) before the objective was met: the node stays open.
            var current = run.CurrentNode;
            if (current != null && current.preset.HasMap)
            {
                var map = Current.Game.FindMap(current.tile);
                if (map == null || !map.mapPawns.AnyPawnBlockingMapRemoval)
                {
                    current.state = RunNodeState.Available;
                    Messages.Message($"Nobody of yours is left at {current.preset.label}. The node stays open and can be tried again.", MessageTypeDefOf.NeutralEvent, false);
                }
            }
            var hubMap = run.HubMap;
            if (hubMap != null) run.pressure.Tick(run, hubMap, 250f / GenDate.TicksPerDay);
        }

        /// <summary>Opens the trip home once nothing else is in the way.</summary>
        public static void Update(WorldComponent_RoguelikeRun run)
        {
            if (run.pendingReturnNodeId < 0 || run.RunOver || Current.ProgramState != ProgramState.Playing || LongEventHandler.ShouldWaitForEvent) return;
            if (Find.WindowStack.WindowOfType<Dialog_RewardPick>() != null || Find.WindowStack.WindowOfType<Dialog_NodeTree>() != null
                || Find.WindowStack.WindowOfType<Dialog_RunEnd>() != null || Find.WindowStack.WindowOfType<Dialog_Packing>() != null) return;
            var node = run.NodeById(run.pendingReturnNodeId);
            run.pendingReturnNodeId = -1;
            if (node == null) return;
            var map = Current.Game.FindMap(node.tile);
            if (map != null && map.mapPawns.FreeColonistsSpawnedCount > 0) OpenReturn(map);
            else if (Find.WorldObjects.Caravans.FirstOrDefault(c => c.IsPlayerControlled && c.Tile == node.tile) is { } caravan) EnterHub(caravan);
        }

        // ---------- Trips ----------

        public static IEnumerable<RunNode> Frontier(WorldComponent_RoguelikeRun run) =>
            run.nodes.Where(n => n.depth > 0 && n.state == RunNodeState.Available);

        /// <summary>Cleared frontier nodes with a map you can go back to for resources (not enemy bases).</summary>
        public static IEnumerable<RunNode> HarvestSites(WorldComponent_RoguelikeRun run) =>
            run.nodes.Where(n => n.depth > 0 && n.state == RunNodeState.Completed && n.preset.HasMap && !n.preset.assault && !InPlay(n));

        /// <summary>The node's own map is still loaded with its objective running (a team there, or staying for stretch goals).</summary>
        private static bool InPlay(RunNode n) =>
            Current.Game.FindMap(n.tile) is { } m && m.GetComponent<MapComponent_RoguelikeNode>() is { Active: true };

        /// <summary>The frontier node a team is out at: the current node, or one whose objective is still running there (failed, or staying for stretch goals).</summary>
        public static RunNode TeamAway(WorldComponent_RoguelikeRun run) =>
            run.CurrentNode ?? run.nodes.FirstOrDefault(n => n.depth > 0 && InPlay(n) && Current.Game.FindMap(n.tile).mapPawns.AnyPawnBlockingMapRemoval);

        public static string CantSendReason(WorldComponent_RoguelikeRun run)
        {
            if (run.RunOver) return "The run is over.";
            if (TeamAway(run) is { } out_) return $"A team is already out at {out_.preset.label}.";
            if (!Frontier(run).Any()) return "No frontier nodes are open.";
            return null;
        }

        /// <summary>From the hub: choose a frontier node from cards (a page at a time, as many as fit on screen), then choose the team and pack.</summary>
        public static void OpenSendTeam(WorldComponent_RoguelikeRun run, int page = 0)
        {
            var hubMap = run.HubMap;
            string why = CantSendReason(run);
            if (why != null || hubMap == null)
            {
                Messages.Message(why ?? "The hub is gone.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            var all = Frontier(run).OrderBy(n => n.depth).ThenBy(n => n.id).ToList();
            int perPage = Mathf.Clamp(Mathf.FloorToInt((UI.screenWidth - 80f) / (Dialog_RewardPick.CardWidth + 30f)), 1, 6);
            int pages = Mathf.CeilToInt(all.Count / (float)perPage);
            page %= pages;
            var options = all.Skip(page * perPage).Take(perPage).ToList();
            var colonists = hubMap.mapPawns.FreeColonistsSpawned.ToList();
            foreach (var n in options.Where(n => !n.offered))
            {
                run.RefreshObjective(n, colonists, options.Where(o => o != n && o.parent == n.parent).Select(o => o.preset));
                n.offered = true;
            }
            var cards = options.Select(n => new RewardCard { kind = RewardKind.Path, pathNode = n }).ToList();
            // With more than one page, the button leads to the next page; the last page's button closes.
            bool last = page == pages - 1;
            Find.WindowStack.Add(new Dialog_RewardPick(cards, pages > 1 ? $"Send a team to the frontier (page {page + 1} of {pages})" : "Send a team to the frontier", card =>
            {
                if (card?.pathNode != null) OpenOutbound(run, card.pathNode);
                else if (!last) OpenSendTeam(run, page + 1);
            }, allowSkip: true, title: "Where to?", skipLabel: last ? "Not now" : "More nodes"));
        }

        private static void OpenOutbound(WorldComponent_RoguelikeRun run, RunNode node)
        {
            var hubMap = run.HubMap;
            var marker = Find.WorldObjects.AllWorldObjects.OfType<RunNodeMarker>().FirstOrDefault(m => m.nodeId == node.id);
            if (hubMap == null || marker == null) return;
            Find.WindowStack.Add(new Dialog_Packing(hubMap, (node.IsBoss ? "Boss: " : "") + node.preset.LabelCap, NodeIcons.For(node),
                onDeparted: caravan => RunNodeMarker.GoTo(caravan, marker), onCancelled: null, chooseTeam: true, backLabel: "Cancel"));
        }

        public static void OpenHarvest(WorldComponent_RoguelikeRun run)
        {
            var hubMap = run.HubMap;
            var sites = HarvestSites(run).ToList();
            if (hubMap == null || sites.Count == 0)
            {
                Messages.Message("No cleared nodes to harvest yet.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            var options = sites.Select(n => new FloatMenuOption($"{n.preset.LabelCap} (depth {n.depth}, {Find.WorldGrid[n.tile].PrimaryBiome.label})", () => OpenHarvestTrip(run, n))).ToList();
            Find.WindowStack.Add(new FloatMenu(options, "Harvest trip: no objective, no reward"));
        }

        /// <summary>Choose the side team and pack for a harvest trip to <paramref name="site"/>.</summary>
        public static void OpenHarvestTrip(WorldComponent_RoguelikeRun run, RunNode site)
        {
            var hubMap = run.HubMap;
            if (hubMap == null) return;
            Find.WindowStack.Add(new Dialog_Packing(hubMap, "Harvest: " + site.preset.LabelCap, NodeIcons.For(site),
                onDeparted: caravan => EnterHarvest(caravan, site), onCancelled: null, chooseTeam: true, backLabel: "Cancel"));
        }

        /// <summary>A cleared node's map, fresh, with no objective, clock or reward: just its land and resources.</summary>
        public static void EnterHarvest(Caravan caravan, RunNode node)
        {
            var run = WorldComponent_RoguelikeRun.Get;
            if (run == null || !HarvestSites(run).Contains(node))
            {
                Messages.Message($"{node.preset.LabelCap} can't be harvested right now; the team heads home.", MessageTypeDefOf.RejectInput, false);
                EnterHub(caravan);
                return;
            }
            LongEventHandler.QueueLongEvent(() =>
            {
                var map = GetOrGenerateMapUtility.GetOrGenerateMap(node.tile, new IntVec3(node.preset.mapSize, 1, node.preset.mapSize), RoguelikeDefOf.Roguelike_Node);
                caravan.Tile = node.tile;
                CaravanEnterMapUtility.Enter(caravan, map, CaravanEnterMode.Edge, CaravanDropInventoryMode.DoNotDrop);
                Current.Game.CurrentMap = map;
                CameraJumper.TryJump(new GlobalTargetInfo(map.Center, map));
                Messages.Message($"Harvest trip to {node.preset.label}: no objective and no reward. Gather what you can, then return to the hub.", MessageTypeDefOf.NeutralEvent, false);
            }, "GeneratingMapForNewEncounter", false, null);
        }

        /// <summary>Pack what the team on <paramref name="map"/> can carry and go home.</summary>
        public static void OpenReturn(Map map)
        {
            var run = WorldComponent_RoguelikeRun.Get;
            if (map == null || run?.HubMap == null || map == run.HubMap) return;
            Find.WindowStack.Add(new Dialog_Packing(map, "Home: your hub", NodeIcons.For(run.HubNode),
                onDeparted: EnterHub, onCancelled: null, backLabel: "Stay for now"));
        }

        /// <summary>The team walks back into the hub and unloads what it carried.</summary>
        public static void EnterHub(Caravan caravan)
        {
            var run = WorldComponent_RoguelikeRun.Get;
            var hubMap = run?.HubMap;
            if (caravan == null || hubMap == null) return;
            caravan.pather.StopDead();
            caravan.Tile = hubMap.Tile;
            CaravanEnterMapUtility.Enter(caravan, hubMap, CaravanEnterMode.Edge, CaravanDropInventoryMode.UnloadIndividually);
            Current.Game.CurrentMap = hubMap;
            CameraJumper.TryJump(new GlobalTargetInfo(hubMap.Center, hubMap));
        }

        // ---------- Sieges ----------

        public static void SpawnSiege(Map map, float points)
        {
            var faction = Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: false) ?? Find.FactionManager.RandomEnemyFaction();
            if (faction == null) return;
            var parms = new IncidentParms
            {
                target = map, faction = faction, forced = true,
                points = Mathf.Max(points, faction.def.MinPointsToGeneratePawnGroup(PawnGroupKindDefOf.Combat) * 1.1f),
                raidStrategy = RaidStrategyDefOf.ImmediateAttack, raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn,
                customLetterLabel = "Siege",
            };
            if (IncidentDefOf.RaidEnemy.Worker.TryExecute(parms)) return;
            parms.raidStrategy = null;
            parms.raidArrivalMode = null;
            if (!IncidentDefOf.RaidEnemy.Worker.TryExecute(parms)) Log.Warning($"[Roguelike] Siege raid failed to spawn ({faction.def.defName}, {parms.points:0} points).");
        }
    }

    /// <summary>
    /// The hub's weather: one escalating modifier on the hub map. Clearing a frontier node pushes it back (half for quick
    /// nodes, fully for long ones, which also rolls a new modifier after a grace period). Plus announced siege raids.
    /// </summary>
    public class HubPressure : IExposable
    {
        public const float FirstSiegeDays = 15f, SiegeEveryDays = 12f;

        public GameConditionDef modifier;
        public int startTick;
        public float graceDays, ratePerDay;
        public int nextSiegeTick = -1;
        public bool siegeWarned;
        public int sieges;

        public float Days => (Find.TickManager.TicksGame - startTick) / (float)GenDate.TicksPerDay;

        public bool InGrace => Days < graceDays;

        public GameCondition_RoguelikeModifier Condition(Map map) =>
            map?.gameConditionManager.ActiveConditions.OfType<GameCondition_RoguelikeModifier>().FirstOrDefault(c => c.def == modifier);

        /// <summary>Weather speed at the hub: relics, Ascension 1+, and +10% for each depth cleared.</summary>
        public static float RateFactor(WorldComponent_RoguelikeRun run) =>
            run.relics.Aggregate(1f, (f, r) => f * r.weatherRateFactor) * (run.ascension >= 1 ? 1.25f : 1f) * (1f + 0.1f * run.deepestCleared);

        public float CurrentRate(WorldComponent_RoguelikeRun run) => ratePerDay * RateFactor(run);

        public void Start(WorldComponent_RoguelikeRun run, Map map, GameConditionDef def)
        {
            modifier = def ?? ModifierPool.PickFor(map.Tile);
            startTick = Find.TickManager.TicksGame;
            var ext = modifier?.GetModExtension<ModifierExtension>();
            ratePerDay = ext?.ratePerDay ?? 0f;
            graceDays = Mathf.Max(0f, (ext?.graceDays ?? 4f) + run.GraceDaysOffset);
            if (modifier != null) map.gameConditionManager.RegisterCondition(GameConditionMaker.MakeConditionPermanent(modifier));
            if (nextSiegeTick < 0) nextSiegeTick = startTick + (int)(FirstSiegeDays * GenDate.TicksPerDay);
        }

        public void Tick(WorldComponent_RoguelikeRun run, Map map, float days)
        {
            var cond = Condition(map);
            if (cond != null)
            {
                if (!InGrace) cond.intensity += CurrentRate(run) * days;
                cond.ApplyEffects(map);
            }
            int now = Find.TickManager.TicksGame;
            if (nextSiegeTick > 0 && !siegeWarned && now >= nextSiegeTick - GenDate.TicksPerDay)
            {
                siegeWarned = true;
                Find.LetterStack.ReceiveLetter("Siege tomorrow", "Scouts report a war party heading for the hub. It arrives in about a day. Each siege is bigger than the last, and bigger the deeper you've pushed.", LetterDefOf.ThreatSmall, new LookTargets(map.Center, map));
            }
            if (nextSiegeTick > 0 && now >= nextSiegeTick)
            {
                Hub.SpawnSiege(map, SiegePoints(run, map));
                sieges++;
                siegeWarned = false;
                nextSiegeTick = now + (int)(SiegeEveryDays * GenDate.TicksPerDay);
            }
        }

        public float SiegePoints(WorldComponent_RoguelikeRun run, Map map) =>
            Mathf.Max(150f, StorytellerUtility.DefaultThreatPointsNow(map)) * (1f + 0.15f * run.deepestCleared + 0.1f * sieges) * (run.ascension >= 2 ? 1.3f : 1f);

        /// <summary>A node was cleared: a long one ends the weather (a new one rolls in after grace), a quick one halves it.</summary>
        public void Relieve(WorldComponent_RoguelikeRun run, bool full, RunNode node)
        {
            var map = run.HubMap;
            var cond = Condition(map);
            if (map == null) return;
            if (full)
            {
                string old = modifier?.label;
                cond?.End();
                Start(run, map, ModifierPool.PickFor(map.Tile));
                Find.LetterStack.ReceiveLetter("The hub's weather breaks",
                    $"Clearing {node.preset.label} broke the {old ?? "weather"} over the hub. Next comes {modifier?.label ?? "nothing"}, holding off for {graceDays:0.#} days.",
                    LetterDefOf.PositiveEvent, new LookTargets(map.Center, map));
            }
            else if (cond != null && cond.intensity != 0f)
            {
                cond.intensity *= 0.5f;
                Messages.Message($"Clearing {node.preset.label} pushed back the {modifier.label} at the hub (now {cond.IntensityText(cond.intensity)}).", MessageTypeDefOf.PositiveEvent, false);
            }
        }

        public void ExposeData()
        {
            Scribe_Defs.Look(ref modifier, "modifier");
            Scribe_Values.Look(ref startTick, "startTick");
            Scribe_Values.Look(ref graceDays, "graceDays");
            Scribe_Values.Look(ref ratePerDay, "ratePerDay");
            Scribe_Values.Look(ref nextSiegeTick, "nextSiegeTick", -1);
            Scribe_Values.Look(ref siegeWarned, "siegeWarned");
            Scribe_Values.Look(ref sieges, "sieges");
        }
    }

    /// <summary>Hub-mode panel and buttons: the hub's weather and next siege at the hub, Return to hub everywhere else.</summary>
    public static class HubHud
    {
        private static readonly Color GoodC = new Color(0.4f, 0.78f, 0.38f);
        private static readonly Color GraceC = new Color(0.3f, 0.55f, 0.38f);
        private static readonly Color BadC = new Color(0.92f, 0.32f, 0.27f);

        public static void Draw(Map map, MapComponent_RoguelikeNode comp)
        {
            var run = WorldComponent_RoguelikeRun.Get;
            if (run is not { active: true, hubMode: true }) return;
            var area = NodeHud.Area;
            if (map == run.HubMap) DrawHub(run, map, area);
            else
            {
                // Below the node panel's own Act map / Leave node buttons.
                float y = comp.Active ? area.yMax + 36f : area.y;
                if (!comp.Active) DrawHarvestPanel(run, map, new Rect(area.x, y, area.width, 48f));
                if (!comp.Active) y += 56f;
                var r = new Rect(area.xMax - 200f, y, 200f, 34f);
                bool done = !comp.Active || comp.completed || comp.failed;
                if (Style.Button(r, done ? "Return to hub" : "Pull back to hub", done ? Style.Gold : Style.Parchment))
                {
                    if (done) Hub.OpenReturn(map);
                    else Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "Pull your team back to the hub? The objective isn't met: the node stays open and can be tried again, but you lose this map and get no reward.",
                        () => Hub.OpenReturn(map)));
                }
            }
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static void DrawHarvestPanel(WorldComponent_RoguelikeRun run, Map map, Rect r)
        {
            var node = run.NodeAt(map.Tile);
            Panel(r, Style.Parchment);
            Text.Font = GameFont.Small;
            Style.ShadowLabel(new Rect(r.x + 12f, r.y + 4f, r.width - 20f, 22f), node != null ? $"Harvest: {node.preset.LabelCap}" : "Away from the hub", Style.Parchment);
            Text.Font = GameFont.Tiny;
            Style.ShadowLabel(new Rect(r.x + 12f, r.y + 26f, r.width - 20f, 18f), "No objective, no clock, no reward. Gather, then go home.", new Color(1f, 1f, 1f, 0.6f));
        }

        private static void DrawHub(WorldComponent_RoguelikeRun run, Map map, Rect r)
        {
            var p = run.pressure;
            var cond = p.Condition(map);
            Panel(r, Style.Gold);
            var iconRect = new Rect(r.x + 10f, r.y + 10f, 52f, 52f);
            var icon = NodeIcons.For(run.HubNode);
            Style.GlowAround(iconRect, new Color(Style.Gold.r, Style.Gold.g, Style.Gold.b, 0.25f + 0.1f * Style.Pulse(1.5f)), 1.6f);
            if (icon != null) Style.Tex(iconRect, icon, Color.white);
            var inner = new Rect(iconRect.xMax + 10f, r.y + 8f, r.xMax - iconRect.xMax - 20f, r.height - 16f);
            Text.Font = GameFont.Small;
            Style.ShadowLabel(new Rect(inner.x, inner.y, inner.width, 22f), "Your hub", Style.Parchment);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperRight;
            Style.ShadowLabel(new Rect(inner.x, inner.y + 3f, inner.width, 18f), run.endless ? $"ENDLESS · DEPTH {run.deepestCleared}" : $"DEPTH {run.deepestCleared}/{Hub.FinalDepth}", Style.Gold);
            Text.Anchor = TextAnchor.UpperLeft;

            // Weather bar: grace left, then intensity against a soft scale.
            var wbar = new Rect(inner.x, inner.y + 24f, inner.width, 18f);
            if (cond == null) Label(wbar, "No weather at the hub.");
            else if (p.InGrace)
            {
                Bar(wbar, Mathf.Clamp01((p.graceDays - p.Days) / Mathf.Max(0.01f, p.graceDays)), GraceC);
                Label(wbar, $"{cond.def.LabelCap}: holding off {p.graceDays - p.Days:0.0} more days");
            }
            else
            {
                float days = p.Days - p.graceDays;
                Bar(wbar, Mathf.Clamp01(days / 20f), days > 10f ? BadC : GoodC);
                Label(wbar, $"{cond.def.LabelCap}: {cond.IntensityText(cond.intensity)}, worsening");
            }
            var sbar = new Rect(inner.x, inner.y + 46f, inner.width, 18f);
            float untilSiege = (p.nextSiegeTick - Find.TickManager.TicksGame) / (float)GenDate.TicksPerDay;
            Bar(sbar, Mathf.Clamp01(1f - untilSiege / HubPressure.SiegeEveryDays), untilSiege < 1f ? BadC : new Color(0.92f, 0.72f, 0.28f));
            Label(sbar, $"Next siege in {Mathf.Max(0f, untilSiege):0.0} days");
            Text.Anchor = TextAnchor.UpperLeft;
            var away = run.CurrentNode;
            Style.ShadowLabel(new Rect(inner.x, inner.y + 70f, inner.width, 18f),
                away != null ? $"Team out at {away.preset.label} (depth {away.depth})" : "Clear frontier nodes to push the weather back.", new Color(1f, 1f, 1f, 0.6f));
            TooltipHandler.TipRegion(r, () =>
                "Your hub runs the whole time. Its weather keeps worsening until you clear frontier nodes: quick nodes halve it, " +
                "expeditions, elites and bosses break it (a new one rolls in after a grace period). Sieges come every " +
                $"{HubPressure.SiegeEveryDays:0} days, bigger each time and the deeper you've pushed.", 0x48_55_42);

            // Buttons under the panel.
            string why = Hub.CantSendReason(run);
            var send = new Rect(r.xMax - 200f, r.yMax + 8f, 200f, 34f);
            if (Style.Button(send, "Send a team", Style.Gold, why == null)) Hub.OpenSendTeam(run);
            if (why != null) TooltipHandler.TipRegion(send, why);
            var harvest = new Rect(send.x - 160f, send.y, 152f, 34f);
            bool canHarvest = Hub.HarvestSites(run).Any();
            if (Style.Button(harvest, "Harvest trip", Style.Parchment, canHarvest)) Hub.OpenHarvest(run);
            if (Style.Button(new Rect(harvest.x - 128f, send.y, 120f, 34f), "Frontier map", Style.Gold)) Dialog_RunMap.Toggle();
            TooltipHandler.TipRegion(harvest, canHarvest ? "Send a side team back to a cleared node for its resources. No objective, no reward, no weather relief." : "Clear a frontier node first.");
        }

        private static void Panel(Rect r, Color accent)
        {
            Style.Tex(new Rect(r.x + 4f, r.y + 8f, r.width, r.height).ExpandedBy(12f), Style.SoftGlow, new Color(0f, 0f, 0f, 0.55f));
            Style.Tex(r, Style.CardBody, new Color(1f, 1f, 1f, 0.94f));
            Style.Border(r, new Color(accent.r * 0.7f, accent.g * 0.7f, accent.b * 0.7f, 0.9f), 1f);
            Style.Box(new Rect(r.x, r.y, 3f, r.height), accent);
        }

        private static void Label(Rect bar, string text)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            Style.ShadowLabel(bar, text, Color.white);
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static void Bar(Rect r, float fill, Color c)
        {
            Style.Box(r, new Color(0f, 0f, 0f, 0.45f));
            Style.Border(r, new Color(1f, 1f, 1f, 0.08f), 1f);
            if (fill <= 0.001f) return;
            var f = new Rect(r.x + 1f, r.y + 1f, (r.width - 2f) * Mathf.Clamp01(fill), r.height - 2f);
            Style.Box(f, new Color(c.r * 0.6f, c.g * 0.6f, c.b * 0.6f, 0.95f));
            Style.Box(new Rect(f.x, f.y, f.width, f.height * 0.5f), new Color(c.r, c.g, c.b, 0.55f));
        }
    }
}
