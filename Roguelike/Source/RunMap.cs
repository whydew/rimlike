using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Roguelike
{
    public enum RunNodeType { Normal, Boss }

    /// <summary>Locked: not reachable yet. Available: can be entered next. Skipped: a sibling was chosen instead.</summary>
    public enum RunNodeState { Locked, Available, Current, Completed, Skipped }

    /// <summary>What clearing the node offers: a mixed card draw, recruits only, or nothing (boss).</summary>
    public enum RunRewardKind { Cards, Recruits, None }

    /// <summary>One node of the act graph.</summary>
    public class RunNode : IExposable
    {
        public int id;
        public int row;
        public int col;
        public PlanetTile tile = PlanetTile.Invalid;
        public RunNodeType type;
        public RunNodeState state;
        public RunRewardKind reward;
        public NodePresetDef preset;
        public List<int> next = new List<int>();

        public bool IsBoss => type == RunNodeType.Boss;

        public string RewardText => reward switch
        {
            RunRewardKind.Recruits => "Reward: choose 1 of 3 recruits.",
            RunRewardKind.Cards => "Reward: choose 1 of 3 cards (relic, gear, implant or blueprint).",
            _ => "Reward: clears the act.",
        };

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            Scribe_Values.Look(ref row, "row");
            Scribe_Values.Look(ref col, "col");
            Scribe_Values.Look(ref tile, "tile");
            Scribe_Values.Look(ref type, "type");
            Scribe_Values.Look(ref state, "state");
            Scribe_Values.Look(ref reward, "reward");
            Scribe_Defs.Look(ref preset, "preset");
            Scribe_Collections.Look(ref next, "next", LookMode.Value);
            next ??= new List<int>();
        }
    }

    /// <summary>
    /// Run state: the act graph laid out on world tiles, which node is current, and whether the run is won.
    /// Lives on the world so it is created during world generation (before landing-site selection) and saved with the game.
    /// </summary>
    public class WorldComponent_RoguelikeRun : WorldComponent
    {
        /// <summary>Nodes per row, start row first and boss row last (~15 nodes).</summary>
        private static readonly int[] RowSizes = { 3, 3, 3, 3, 2, 1 };
        private const float RowSpacingTiles = 8f;
        private const float ColSpacingTiles = 7f;
        private const float MinNodeGapTiles = 3f;
        private const float SearchRadiusTiles = 4f;

        public bool active;
        public bool runWon;
        public bool runLost;
        public List<RunNode> nodes = new List<RunNode>();
        public List<RelicDef> relics = new List<RelicDef>();
        public List<BlueprintGroupDef> blueprints = new List<BlueprintGroupDef>();
        /// <summary>Node whose reward screen hasn't been resolved yet (reopened after loading a save).</summary>
        public int pendingRewardNodeId = -1;
        /// <summary>Meta points earned during this run (shown on the end-of-run screen).</summary>
        public int pointsEarned;

        public bool RunOver => runWon || runLost;

        // ---------- Relics and blueprints ----------

        public float GraceDaysOffset => relics.Sum(r => r.graceDaysOffset) + MetaProgress.GraceDaysOffset;
        public float WeatherRateFactor => relics.Aggregate(1f, (f, r) => f * r.weatherRateFactor);
        public float MaxPaceFactorFactor => relics.Aggregate(1f, (f, r) => f * r.maxPaceFactorFactor);

        public bool IsBuildingLocked(BuildableDef def) =>
            def != null && BlueprintGroupDef.Gated.Contains(def) && !blueprints.Any(b => b.buildings.Contains(def));

        public void AddRelic(RelicDef relic)
        {
            if (relic == null || relics.Contains(relic)) return;
            relics.Add(relic);
            ApplyRelicHediffs();
        }

        /// <summary>Gives every colonist (including newcomers) the status effects of hediff relics.</summary>
        public void ApplyRelicHediffs()
        {
            var defs = relics.Where(r => r.hediff != null).Select(r => r.hediff).ToList();
            if (defs.Count == 0) return;
            foreach (var pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists)
                foreach (var def in defs)
                    RelicEffects.EnsureHediff(pawn, def);
        }

        /// <summary>Opens the reward screen for a just-cleared node.</summary>
        public void OfferReward(RunNode node, Map map)
        {
            if (node == null || node.reward == RunRewardKind.None) return;
            pendingRewardNodeId = node.id;
            var cards = RewardUtility.Generate(this, node.reward);
            Find.WindowStack.Add(new Dialog_RewardPick(cards, $"{node.preset.LabelCap} cleared", card =>
            {
                if (card != null) RewardUtility.Apply(card, map, map == null ? Find.WorldObjects.Caravans.FirstOrDefault(c => c.IsPlayerControlled) : null);
                else
                {
                    // Skipping a reward is worth a meta point.
                    pointsEarned++;
                    MetaProgress.Award(1);
                }
                pendingRewardNodeId = -1;
            }));
        }

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            if (fromLoad && active && pendingRewardNodeId >= 0)
            {
                var node = NodeById(pendingRewardNodeId);
                LongEventHandler.ExecuteWhenFinished(() => OfferReward(node, Current.Game.FindMap(node.tile)));
            }
        }

        public WorldComponent_RoguelikeRun(World world) : base(world) { }

        public static WorldComponent_RoguelikeRun Get => Find.World?.GetComponent<WorldComponent_RoguelikeRun>();

        public RunNode NodeById(int id) => id >= 0 && id < nodes.Count ? nodes[id] : null;

        public RunNode NodeAt(PlanetTile tile) => nodes.FirstOrDefault(n => n.tile == tile);

        public RunNode CurrentNode => nodes.FirstOrDefault(n => n.state == RunNodeState.Current);

        public IEnumerable<RunNode> Row(int row) => nodes.Where(n => n.row == row);

        // ---------- State transitions ----------

        /// <summary>Called once the player has landed on a starting node.</summary>
        public RunNode BeginAt(PlanetTile tile)
        {
            var start = NodeAt(tile);
            if (start == null || start.row != 0) return null;
            Choose(start);
            return start;
        }

        public bool CanEnter(RunNode node) => active && !RunOver && node != null && node.state == RunNodeState.Available;

        /// <summary>Ends the run as a loss: a squad wipe, or leaving the current node before its objective was met.</summary>
        public void Lose(string reason)
        {
            if (!active || RunOver) return;
            runLost = true;
            MetaProgress.RecordRunEnd(false);
            Find.WindowStack.Add(new Dialog_RunEnd(false, reason));
        }

        public override void WorldComponentTick()
        {
            if (!active || RunOver || Find.TickManager.TicksGame % 250 != 0) return;
            if (!PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists.Any())
            {
                Lose("Your whole squad is gone.");
                return;
            }
            ApplyRelicHediffs();
            // Everyone left the current node before its objective was met: its map is gone, or (for the starting
            // settlement, which stays loaded) nobody of ours is left on it, not even in an incoming drop pod.
            var current = CurrentNode;
            var currentMap = current != null && current.preset.HasMap ? Current.Game.FindMap(current.tile) : null;
            if (current != null && current.preset.HasMap && (currentMap == null || !currentMap.mapPawns.AnyPawnBlockingMapRemoval))
            {
                Lose($"You left {current.preset.label} before completing its objective.");
            }
        }

        /// <summary>The player commits to <paramref name="node"/>; every other open choice is skipped.</summary>
        public void Choose(RunNode node)
        {
            foreach (var n in nodes.Where(n => n.state == RunNodeState.Available && n != node)) n.state = RunNodeState.Skipped;
            node.state = RunNodeState.Current;
        }

        /// <summary>Objective met on the node at <paramref name="tile"/>: unlock its next nodes, or win on the boss.</summary>
        public void NotifyNodeCompleted(PlanetTile tile)
        {
            var node = NodeAt(tile);
            if (!active || RunOver || node == null || node.state != RunNodeState.Current) return;
            node.state = RunNodeState.Completed;
            int points = node.IsBoss ? 5 : 1;
            pointsEarned += points;
            MetaProgress.Award(points);
            if (node.IsBoss)
            {
                runWon = true;
                MetaProgress.RecordRunEnd(true);
                Find.WindowStack.Add(new Dialog_RunEnd(true, "You beat the boss and cleared the act."));
                return;
            }
            foreach (int id in node.next) NodeById(id).state = RunNodeState.Available;
            OfferReward(node, Current.Game.FindMap(tile));
        }

        // ---------- Generation ----------

        /// <summary>Builds the act graph on this world and places a marker on every node. Call after world generation.</summary>
        public bool GenerateAct()
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                var start = TileFinder.RandomStartingTile();
                if (!start.Valid) continue;
                int span = Mathf.RoundToInt(RowSpacingTiles * (RowSizes.Length - 1));
                if (!TileFinder.TryFindPassableTileWithTraversalDistance(start, Mathf.RoundToInt(span * 0.9f), Mathf.RoundToInt(span * 1.2f), out var boss,
                        t => TileFinder.IsValidTileForNewSettlement(t)))
                    continue;
                var laidOut = TryLayout(start, boss);
                if (laidOut == null) continue;

                nodes = laidOut;
                ConnectRows();
                AssignPresets();
                foreach (var n in Row(0)) n.state = RunNodeState.Available;
                foreach (var n in nodes)
                {
                    var marker = (RunNodeMarker)WorldObjectMaker.MakeWorldObject(RoguelikeDefOf.Roguelike_RunNode);
                    marker.Tile = n.tile;
                    marker.nodeId = n.id;
                    Find.WorldObjects.Add(marker);
                }
                active = true;
                return true;
            }
            Log.Error("[Roguelike] Couldn't lay out an act on this world. Try a larger planet coverage.");
            return false;
        }

        /// <summary>Places rows along the great circle from start to boss, columns spread sideways, each snapped to the nearest usable tile.</summary>
        private static List<RunNode> TryLayout(PlanetTile start, PlanetTile boss)
        {
            var grid = Find.WorldGrid;
            var surface = grid.Surface;
            int count = surface.TilesCount;
            var centers = new Vector3[count];
            for (int i = 0; i < count; i++) centers[i] = grid.GetTileCenter(surface.PlanetTileForID(i));

            Vector3 a = grid.GetTileCenter(start), b = grid.GetTileCenter(boss);
            float radius = a.magnitude;
            Vector3 side = Vector3.Cross(a, b).normalized;
            float tileSize = grid.AverageTileSize;

            var result = new List<RunNode>();
            for (int r = 0; r < RowSizes.Length; r++)
            {
                int n = RowSizes[r];
                Vector3 rowCenter = Vector3.Slerp(a, b, r / (float)(RowSizes.Length - 1));
                for (int c = 0; c < n; c++)
                {
                    PlanetTile tile;
                    if (r == 0 && n % 2 == 1 && c == n / 2) tile = start;
                    else if (r == RowSizes.Length - 1 && n == 1) tile = boss;
                    else
                    {
                        Vector3 target = (rowCenter + side * ((c - (n - 1) / 2f) * ColSpacingTiles * tileSize)).normalized * radius;
                        tile = NearestUsableTile(target, centers, surface, start, boss, result, tileSize);
                        if (!tile.Valid) return null;
                    }
                    result.Add(new RunNode
                    {
                        id = result.Count, row = r, col = c, tile = tile,
                        type = r == RowSizes.Length - 1 ? RunNodeType.Boss : RunNodeType.Normal,
                        state = RunNodeState.Locked,
                    });
                }
            }
            return result;
        }

        private static PlanetTile NearestUsableTile(Vector3 target, Vector3[] centers, PlanetLayer surface, PlanetTile start, PlanetTile boss, List<RunNode> placed, float tileSize)
        {
            // Start and boss are fixed anchors placed without snapping, so keep every other node clear of them too.
            var taken = placed.Select(p => p.tile).Append(start).Append(boss).ToList();
            float maxSqr = SearchRadiusTiles * tileSize * SearchRadiusTiles * tileSize;
            var candidates = new List<(int id, float d)>();
            for (int i = 0; i < centers.Length; i++)
            {
                float d = (centers[i] - target).sqrMagnitude;
                if (d <= maxSqr) candidates.Add((i, d));
            }
            foreach (var (id, _) in candidates.OrderBy(x => x.d))
            {
                var tile = surface.PlanetTileForID(id);
                if (!TileFinder.IsValidTileForNewSettlement(tile)) continue;
                if (taken.Any(t => t == tile || Find.WorldGrid.ApproxDistanceInTiles(t, tile) < MinNodeGapTiles)) continue;
                if (!Find.WorldReachability.CanReach(start, tile)) continue;
                return tile;
            }
            return PlanetTile.Invalid;
        }

        /// <summary>Slay-the-Spire style edges: each node links to the nearest node in the next row, sometimes a neighbor too, with no crossings; every node gets an incoming edge.</summary>
        private void ConnectRows()
        {
            for (int r = 0; r < RowSizes.Length - 1; r++)
            {
                var from = Row(r).OrderBy(n => n.col).ToList();
                var to = Row(r + 1).OrderBy(n => n.col).ToList();
                var edges = new List<(int i, int j)>();

                // Mandatory edges first (both mappings are monotonic, so they never cross each other)...
                var nearest = new int[from.Count];
                for (int i = 0; i < from.Count; i++)
                {
                    nearest[i] = Mathf.RoundToInt(Pos(i, from.Count) * (to.Count - 1));
                    AddEdge(edges, i, nearest[i]);
                }
                for (int j = 0; j < to.Count; j++)
                {
                    if (edges.Any(e => e.j == j)) continue;
                    AddEdge(edges, Mathf.RoundToInt(Pos(j, to.Count) * (from.Count - 1)), j);
                }
                // ...then optional branches, only where they cross nothing already placed.
                for (int i = 0; i < from.Count; i++)
                {
                    if (!Rand.Chance(0.5f)) continue;
                    int j2 = nearest[i] + (Rand.Bool ? 1 : -1);
                    if (j2 >= 0 && j2 < to.Count && !Crosses(edges, i, j2)) AddEdge(edges, i, j2);
                }
                foreach (var (i, j) in edges) from[i].next.Add(to[j].id);
            }
        }

        private static float Pos(int index, int count) => count == 1 ? 0.5f : index / (float)(count - 1);

        private static void AddEdge(List<(int i, int j)> edges, int i, int j)
        {
            if (!edges.Contains((i, j))) edges.Add((i, j));
        }

        private static bool Crosses(List<(int i, int j)> edges, int i, int j) =>
            edges.Any(e => (e.i < i && e.j > j) || (e.i > i && e.j < j));

        private void AssignPresets()
        {
            var all = DefDatabase<NodePresetDef>.AllDefsListForReading;
            foreach (var n in nodes)
            {
                float temp = Find.WorldGrid[n.tile].temperature;
                // Starting nodes are always expeditions (the landing site is a fresh camp); later rows mix node kinds.
                var kind = n.IsBoss ? (NodeKind?)null : n.row == 0 ? NodeKind.Expedition : MiddleRowKinds.RandomElementByWeight(k => k.weight).kind;
                var pool = all.Where(p => p.isBoss == n.IsBoss && (kind == null || p.kind == kind)).ToList();
                if (pool.Count == 0) pool = all.Where(p => !p.isBoss && p.kind == NodeKind.Expedition).ToList();
                var fitting = pool.Where(p => p.tileTemperature.Includes(temp)).ToList();
                var options = fitting.Count > 0 ? fitting : pool;
                n.preset = options.RandomElementByWeight(p => p.weight);
                // Events resolve on the spot; about a quarter of other middle nodes offer recruits instead of a mixed card draw.
                n.reward = n.IsBoss || n.preset.kind == NodeKind.Event ? RunRewardKind.None
                    : n.row > 0 && Rand.Chance(0.25f) ? RunRewardKind.Recruits : RunRewardKind.Cards;
            }
        }

        private static readonly (NodeKind kind, float weight)[] MiddleRowKinds =
        {
            (NodeKind.Expedition, 40f), (NodeKind.CombatAlerted, 20f), (NodeKind.CombatUnaware, 15f), (NodeKind.Escort, 10f), (NodeKind.Event, 15f),
        };

        // ---------- Drawing ----------

        public override void WorldComponentUpdate()
        {
            if (!active || !WorldRendererUtility.WorldSelected) return;
            foreach (var n in nodes)
            {
                foreach (int id in n.next)
                {
                    var to = NodeById(id);
                    RunLines.Draw(n.tile, to.tile, EdgeStyle(n, to));
                }
            }
        }

        private static RunLines.Style EdgeStyle(RunNode from, RunNode to)
        {
            bool fromTaken = from.state == RunNodeState.Current || from.state == RunNodeState.Completed;
            if (fromTaken && to.state == RunNodeState.Available) return RunLines.Style.Open;
            if (fromTaken && (to.state == RunNodeState.Current || to.state == RunNodeState.Completed)) return RunLines.Style.Taken;
            if (to.state == RunNodeState.Skipped || from.state == RunNodeState.Skipped) return RunLines.Style.Dead;
            return RunLines.Style.Future;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref active, "active");
            Scribe_Values.Look(ref runWon, "runWon");
            Scribe_Values.Look(ref runLost, "runLost");
            Scribe_Values.Look(ref pendingRewardNodeId, "pendingRewardNodeId", -1);
            Scribe_Values.Look(ref pointsEarned, "pointsEarned");
            Scribe_Collections.Look(ref relics, "relics", LookMode.Def);
            Scribe_Collections.Look(ref blueprints, "blueprints", LookMode.Def);
            relics ??= new List<RelicDef>();
            blueprints ??= new List<BlueprintGroupDef>();
            Scribe_Collections.Look(ref nodes, "nodes", LookMode.Deep);
            nodes ??= new List<RunNode>();
        }
    }

    /// <summary>Line drawing for act-graph edges on the world map.</summary>
    [StaticConstructorOnStartup]
    public static class RunLines
    {
        public enum Style { Future, Open, Taken, Dead }

        private const int Segments = 8;
        private const float Lift = 1.003f;

        private static readonly Material Future = Mat(new Color(0.8f, 0.8f, 0.8f, 0.45f));
        private static readonly Material Open = Mat(new Color(1f, 0.85f, 0.3f, 0.95f));
        private static readonly Material Taken = Mat(new Color(1f, 1f, 1f, 0.9f));
        private static readonly Material Dead = Mat(new Color(0.4f, 0.4f, 0.4f, 0.3f));

        private static Material Mat(Color c) => MaterialPool.MatFrom(GenDraw.LineTexPath, ShaderDatabase.WorldOverlayTransparent, c, 3590);

        public static void Draw(PlanetTile a, PlanetTile b, Style style)
        {
            var grid = Find.WorldGrid;
            Vector3 pa = grid.GetTileCenter(a), pb = grid.GetTileCenter(b);
            var mat = style switch { Style.Open => Open, Style.Taken => Taken, Style.Dead => Dead, _ => Future };
            float width = style == Style.Open ? 2f : 1.2f;
            // Split into short arcs so the line follows the planet's curve instead of cutting under it.
            Vector3 prev = pa * Lift;
            for (int i = 1; i <= Segments; i++)
            {
                Vector3 p = Vector3.Slerp(pa, pb, i / (float)Segments) * Lift;
                GenDraw.DrawWorldLineBetween(prev, p, mat, width);
                prev = p;
            }
        }
    }

    /// <summary>World-map marker for one act-graph node.</summary>
    public class RunNodeMarker : WorldObject
    {
        public int nodeId = -1;

        public RunNode Node => WorldComponent_RoguelikeRun.Get?.NodeById(nodeId);

        public override string Label
        {
            get
            {
                var n = Node;
                if (n?.preset == null) return def.label;
                string name = (n.IsBoss ? "Boss: " : "") + n.preset.label;
                return n.state switch
                {
                    RunNodeState.Current => name + " (here)",
                    RunNodeState.Completed => name + " (cleared)",
                    RunNodeState.Skipped => name + " (skipped)",
                    _ => name,
                };
            }
        }

        public override Color ExpandingIconColor
        {
            get
            {
                var n = Node;
                if (n == null) return base.ExpandingIconColor;
                return n.state switch
                {
                    RunNodeState.Available => n.IsBoss ? new Color(1f, 0.4f, 0.3f) : Color.white,
                    RunNodeState.Current => new Color(0.5f, 1f, 0.5f),
                    RunNodeState.Completed => new Color(0.55f, 0.55f, 0.55f),
                    RunNodeState.Skipped => new Color(0.3f, 0.3f, 0.3f, 0.6f),
                    _ => n.IsBoss ? new Color(0.7f, 0.25f, 0.2f) : new Color(0.65f, 0.65f, 0.65f, 0.8f),
                };
            }
        }

        public override string GetInspectString()
        {
            var n = Node;
            var sb = new StringBuilder(base.GetInspectString());
            if (n?.preset == null) return sb.ToString();
            void Line(string s) { if (sb.Length > 0) sb.AppendLine(); sb.Append(s); }
            Line($"Row {n.row + 1}{(n.IsBoss ? " (boss)" : "")} · {StateText(n.state)}");
            if (n.next.Count > 0) Line($"Leads to {n.next.Count} node{(n.next.Count == 1 ? "" : "s")}.");
            // Inspect strings must not contain blank lines.
            Line(n.RewardText);
            Line(n.preset.Briefing.Replace("\n\n", "\n"));
            return sb.ToString();
        }

        private static string StateText(RunNodeState s) => s switch
        {
            RunNodeState.Available => "available: right-click with a caravan to travel here",
            RunNodeState.Current => "current node",
            RunNodeState.Completed => "cleared",
            RunNodeState.Skipped => "skipped",
            _ => "locked",
        };

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Caravan caravan)
        {
            foreach (var o in base.GetFloatMenuOptions(caravan)) yield return o;
            var report = CaravanArrivalAction_EnterRunNode.CanEnter(this);
            if (!report.Accepted)
            {
                if (!report.FailReason.NullOrEmpty()) yield return new FloatMenuOption($"Go to {Label} ({report.FailReason})", null);
                yield break;
            }
            // No overland travel: the caravan goes straight into the chosen node.
            var marker = this;
            yield return new FloatMenuOption("Go to " + Label, () => GoTo(caravan, marker), revalidateWorldClickTarget: this);
        }

        public static void GoTo(Caravan caravan, RunNodeMarker marker)
        {
            if (!CaravanArrivalAction_EnterRunNode.CanEnter(marker).Accepted) return;
            caravan.pather.StopDead();
            caravan.Tile = marker.Tile;
            new CaravanArrivalAction_EnterRunNode(marker).Arrived(caravan);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref nodeId, "nodeId", -1);
        }
    }

    /// <summary>Caravan arrival: generate the node map, bring the caravan in and start the node.</summary>
    public class CaravanArrivalAction_EnterRunNode : CaravanArrivalAction
    {
        private RunNodeMarker marker;

        public CaravanArrivalAction_EnterRunNode() { }

        public CaravanArrivalAction_EnterRunNode(RunNodeMarker marker) => this.marker = marker;

        public override string Label => "Enter " + marker.Label;

        public override string ReportString => "Traveling to " + marker.Label;

        public static FloatMenuAcceptanceReport CanEnter(RunNodeMarker marker)
        {
            var run = WorldComponent_RoguelikeRun.Get;
            var node = marker?.Node;
            if (marker == null || !marker.Spawned || run == null || node == null) return false;
            if (run.RunOver) return FloatMenuAcceptanceReport.WithFailReason("run is over");
            if (node.state == RunNodeState.Current) return FloatMenuAcceptanceReport.WithFailReason("you are already here");
            if (node.state != RunNodeState.Available) return FloatMenuAcceptanceReport.WithFailReason("not connected to your current node");
            return true;
        }

        public override FloatMenuAcceptanceReport StillValid(Caravan caravan, PlanetTile destinationTile)
        {
            var baseReport = base.StillValid(caravan, destinationTile);
            if (!baseReport) return baseReport;
            if (marker != null && marker.Tile != destinationTile) return false;
            return CanEnter(marker);
        }

        public override void Arrived(Caravan caravan)
        {
            var m = marker;
            LongEventHandler.QueueLongEvent(() => DoEnter(caravan, m), "GeneratingMapForNewEncounter", false, null);
        }

        private static void DoEnter(Caravan caravan, RunNodeMarker marker)
        {
            var run = WorldComponent_RoguelikeRun.Get;
            var node = marker.Node;
            if (!run.CanEnter(node))
            {
                Messages.Message("That node can't be entered any more.", caravan, MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (!node.preset.HasMap)
            {
                // Event nodes resolve right on the world map.
                run.Choose(node);
                EventUtility.Open(node, caravan);
                return;
            }
            var size = new IntVec3(node.preset.mapSize, 1, node.preset.mapSize);
            var map = GetOrGenerateMapUtility.GetOrGenerateMap(marker.Tile, size, RoguelikeDefOf.Roguelike_Node);
            var pawns = caravan.PawnsListForReading.ToList();
            CaravanEnterMapUtility.Enter(caravan, map, CaravanEnterMode.Edge, CaravanDropInventoryMode.DoNotDrop);
            run.Choose(node);
            Current.Game.CurrentMap = map;
            CameraJumper.TryJump(new GlobalTargetInfo(map.Center, map));
            NodeLauncher.StartOn(map, node.preset, new LookTargets(pawns));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref marker, "marker");
        }
    }
}
