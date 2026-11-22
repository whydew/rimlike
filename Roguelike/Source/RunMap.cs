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
    public enum RunRewardKind { Cards, Recruits, None, Relics }

    /// <summary>One node of the act graph.</summary>
    public class RunNode : IExposable
    {
        public int id;
        /// <summary>Act this node belongs to (1..ActsPerRun); all acts are laid out at world generation.</summary>
        public int act = 1;
        public int row;
        public int col;
        public PlanetTile tile = PlanetTile.Invalid;
        public RunNodeType type;
        public RunNodeState state;
        public RunRewardKind reward;
        public NodePresetDef preset;
        /// <summary>Weather modifier rolled for this node (presets with randomModifier).</summary>
        public GameConditionDef modifier;
        /// <summary>Its objective has been shown on a next-node card (and is now fixed).</summary>
        public bool offered;

        public string Briefing => preset.BriefingFor(preset.randomModifier ? modifier : preset.condition, SquadSize);

        /// <summary>The squad size a node will see on arrival (requirement counts scale with it), clamped as on entry.</summary>
        private static int Choices => WorldComponent_RoguelikeRun.Get is { } run ? RewardUtility.ChoiceCount(run) : 3;

        public static int SquadSize => Mathf.Clamp(PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists.Count(), 1, 5);
        public List<int> next = new List<int>();

        public bool IsBoss => type == RunNodeType.Boss;

        /// <summary>The reward in a few words, for a card.</summary>
        public string RewardShort => reward switch
        {
            RunRewardKind.Recruits => $"Choose 1 of {Choices} recruits",
            RunRewardKind.Relics => $"Choose 1 of {Choices} relics",
            RunRewardKind.Cards => IsBoss ? "A card, then the next act" : $"Choose 1 of {Choices} cards",
            _ => IsBoss ? "Wins the run" : preset.HasMap ? "None" : "Whatever you find here",
        };

        public GameConditionDef Weather => preset.randomModifier ? modifier : preset.condition;

        /// <summary>
        /// Where the node's map is, in a few words: biome, hills, coast, then the season and temperature there now
        /// (before the game starts, the yearly average instead). Null for map-less nodes.
        /// </summary>
        public string TerrainLine
        {
            get
            {
                if (preset?.HasMap != true || !tile.Valid || Find.WorldGrid == null) return null;
                var t = Find.WorldGrid[tile];
                var parts = new List<string> { t.PrimaryBiome.LabelCap, t.hilliness.GetLabelCap() };
                if (Find.World.CoastDirectionAt(tile).IsValid) parts.Add("Coast");
                parts.Add(Current.ProgramState == ProgramState.Playing
                    ? $"{GenLocalDate.Season(tile).LabelCap()} {GenTemperature.GetTemperatureFromSeasonAtTile(Find.TickManager.TicksAbs, tile).ToStringTemperature("F0")}"
                    : $"avg {t.temperature.ToStringTemperature("F0")}");
                return string.Join(" · ", parts);
            }
        }

        public string RewardText => reward switch
        {
            RunRewardKind.Recruits => $"Reward: choose 1 of {Choices} recruits.",
            RunRewardKind.Relics => $"Reward: choose 1 of {Choices} relics.",
            RunRewardKind.Cards => IsBoss ? "Reward: choose a card, then the next act begins." : $"Reward: choose 1 of {Choices} cards (relic, gear, implant or blueprint).",
            _ => IsBoss ? "Reward: wins the run." : "Reward: none (events resolve on the spot).",
        };

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            Scribe_Values.Look(ref act, "act", 1);
            Scribe_Values.Look(ref row, "row");
            Scribe_Values.Look(ref col, "col");
            Scribe_Values.Look(ref tile, "tile");
            Scribe_Values.Look(ref type, "type");
            Scribe_Values.Look(ref state, "state");
            Scribe_Values.Look(ref reward, "reward");
            Scribe_Defs.Look(ref preset, "preset");
            Scribe_Defs.Look(ref modifier, "modifier");
            Scribe_Values.Look(ref offered, "offered");
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
        public static int RowCount => RowSizes.Length;
        /// <summary>Node tiles stay this far apart, so enemy bases and node maps never sit side by side.</summary>
        private const float MinNodeGapTiles = 3f;
        /// <summary>The first landing only uses the friendlier biomes (vanilla's settlement weight at least this).</summary>
        private const float LandingBiomeWeight = 0.5f;

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
        /// <summary>Cleared node whose "choose your next node" screen is still open or due (reopened after loading).</summary>
        public int pendingPickNodeId = -1;
        /// <summary>Level-ups earned (stretch goals) whose pick screen hasn't been shown yet.</summary>
        public int pendingLevelUps;
        /// <summary>Current act, 1..ActsPerRun. Beating the last act's boss wins the run.</summary>
        public int act = 1;
        /// <summary>Ascension level chosen for this run (0 = none).</summary>
        public int ascension;
        /// <summary>Act in which the Imperial Writ was last used (once per act).</summary>
        public int writUsedAct;
        /// <summary>Node tile where the Bloodstained Codex last paid out (once per node).</summary>
        public PlanetTile codexTile = PlanetTile.Invalid;

        public const int ActsPerRun = 3;

        public bool RunOver => runWon || runLost;

        // ---------- Relics and blueprints ----------

        public float GraceDaysOffset => relics.Sum(r => r.graceDaysOffset) + MetaProgress.GraceDaysOffset - (ascension >= 4 ? 1f : 0f);
        /// <summary>Weather speed: relics, later acts (+25% each) and Ascension 1+ (+25%).</summary>
        public float WeatherRateFactor => relics.Aggregate(1f, (f, r) => f * r.weatherRateFactor) * (1f + 0.25f * (act - 1)) * (ascension >= 1 ? 1.25f : 1f);
        /// <summary>Raid strength: later acts (+40% each), Ascension 2+ (+30%) and, for bosses, Ascension 5 (+50%).</summary>
        public float RaidFactor(bool boss) => (1f + 0.4f * (act - 1)) * (ascension >= 2 ? 1.3f : 1f) * (boss && ascension >= 5 ? 1.5f : 1f);
        /// <summary>Ascension 3+: one fewer reward card.</summary>
        public int RewardChoiceOffset => ascension >= 3 ? -1 : 0;
        public float MaxPaceFactorFactor => relics.Aggregate(1f, (f, r) => f * r.maxPaceFactorFactor);

        public bool IsBuildingLocked(BuildableDef def) =>
            def != null && BlueprintGroupDef.Gated.Contains(def) && !blueprints.Any(b => b.buildings.Contains(def));

        public void AddRelic(RelicDef relic)
        {
            if (relic == null || relics.Count(r => r == relic) >= relic.maxStacks) return;
            relics.Add(relic);
            ApplyRelicHediffs();
        }

        /// <summary>Gives every colonist (including newcomers) the status effects of hediff relics.</summary>
        public void ApplyRelicHediffs()
        {
            var defs = relics.Where(r => r.hediff != null).GroupBy(r => r.hediff).Select(g => (def: g.Key, stacks: g.Count())).ToList();
            if (defs.Count == 0) return;
            foreach (var pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists)
                foreach (var (def, stacks) in defs)
                    RelicEffects.EnsureHediff(pawn, def, stacks);
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
                if (!StaysForStretch(node)) RequestNextPick(node);
            }));
        }

        // ---------- Choosing the next node ----------

        /// <summary>Queues the "choose your next node" screen; it opens once no other run dialog is showing.</summary>
        public void RequestNextPick(RunNode from)
        {
            if (from != null && !RunOver && from.next.Any(id => NodeById(id)?.state == RunNodeState.Available)) pendingPickNodeId = from.id;
        }

        private void OpenNextPick()
        {
            var from = NodeById(pendingPickNodeId);
            var options = from?.next.Select(NodeById).Where(n => n != null && n.state == RunNodeState.Available).ToList();
            if (options == null || options.Count == 0)
            {
                pendingPickNodeId = -1;
                return;
            }
            var squad = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists.ToList();
            foreach (var n in options.Where(n => !n.offered))
            {
                RefreshObjective(n, squad, options.Where(o => o != n).Select(o => o.preset));
                n.offered = true;
            }
            var cards = options.Select(n => new RewardCard { kind = RewardKind.Path, pathNode = n }).ToList();
            Find.WindowStack.Add(new Dialog_RewardPick(cards, "Choose your next node", card =>
            {
                if (card?.pathNode == null) return;
                pendingPickNodeId = -1;
                Depart(from, card.pathNode);
            }, allowSkip: false, title: "Where next?"));
        }

        private static List<NodePresetDef> Expeditions =>
            DefDatabase<NodePresetDef>.AllDefsListForReading.Where(p => p.kind == NodeKind.Expedition && !p.isBoss && p.objective == ObjectiveKind.Requirements).ToList();

        /// <summary>
        /// When an expedition is first offered as a next node, make sure the squad can do it (blueprints, research, skills,
        /// season); re-roll if not. Objectives that drafted blueprints have unlocked sometimes take its place.
        /// </summary>
        public void RefreshObjective(RunNode n, List<Pawn> squad, IEnumerable<NodePresetDef> siblings = null)
        {
            if (n.preset.kind != NodeKind.Expedition || n.IsBoss) return;
            var eligible = Expeditions.Where(p => p.Eligible(this, n.tile, squad).Accepted).ToList();
            var used = siblings?.ToList() ?? new List<NodePresetDef>();
            // A sibling's objective is out (even this node's current one) while any other is left.
            if (eligible.Any(p => !used.Contains(p))) eligible.RemoveAll(used.Contains);
            if (eligible.Count == 0) return;
            var unlocked = eligible.Where(p => p.Gated).ToList();
            bool currentOk = eligible.Contains(n.preset);
            if (!currentOk || unlocked.Count > 0 && Rand.Chance(0.4f))
            {
                var pool = !currentOk || unlocked.Count == 0 ? eligible : unlocked;
                n.preset = pool.RandomElementByWeight(p => p.weight);
            }
            if (n.preset.randomModifier && n.modifier == null) n.modifier = ModifierPool.PickFor(n.tile);
        }

        /// <summary>
        /// Leave <paramref name="from"/> for <paramref name="to"/>: pack what the squad can carry (if leaving a map), then
        /// go straight into the chosen node. Cancelling the packing screen brings the node choice back.
        /// </summary>
        public void Depart(RunNode from, RunNode to)
        {
            var marker = Find.WorldObjects.AllWorldObjects.OfType<RunNodeMarker>().FirstOrDefault(m => m.nodeId == to.id);
            if (marker == null) return;
            var fromMap = Current.Game.FindMap(from.tile);
            if (fromMap != null && fromMap.mapPawns.FreeColonistsSpawnedCount > 0)
            {
                CameraJumper.TryJump(new GlobalTargetInfo(fromMap.Center, fromMap));
                var packing = new Dialog_Packing(fromMap, (to.IsBoss ? "Boss: " : "") + to.preset.LabelCap, NodeIcons.For(to),
                    onDeparted: caravan =>
                    {
                        Letter_NodeComplete.AbandonIfEmpty(fromMap);
                        RunNodeMarker.GoTo(caravan, marker);
                    },
                    // Backing out returns to the next-node choice.
                    onCancelled: () => pendingPickNodeId = from.id);
                Find.WindowStack.Add(packing);
                return;
            }
            var traveling = Find.WorldObjects.Caravans.Where(c => c.IsPlayerControlled).OrderBy(c => c.Tile == from.tile ? 0 : 1).FirstOrDefault();
            if (traveling != null) RunNodeMarker.GoTo(traveling, marker);
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

        /// <summary>A row of the current act.</summary>
        public IEnumerable<RunNode> Row(int row) => Row(act, row);

        public IEnumerable<RunNode> Row(int actNumber, int row) => nodes.Where(n => n.act == actNumber && n.row == row);

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
            if (node.IsBoss && node.act >= ActsPerRun)
            {
                runWon = true;
                MetaProgress.RecordRunEnd(true, ascension);
                Find.WindowStack.Add(new Dialog_RunEnd(true, $"You beat the final boss and cleared all {ActsPerRun} acts."));
                return;
            }
            // An act boss leads to the next act's starting nodes, which are already on the map.
            foreach (int id in node.next) NodeById(id).state = RunNodeState.Available;
            if (node.IsBoss)
            {
                act = node.act + 1;
                Find.LetterStack.ReceiveLetter($"Act {act} of {ActsPerRun}", "The boss is beaten. The next act lies ahead, harder than the last.", LetterDefOf.PositiveEvent);
            }
            if (node.preset.HasMap) Wear.AddForNode(Current.Game.FindMap(tile));
            if (node.reward != RunRewardKind.None) OfferReward(node, Current.Game.FindMap(tile));
            else if (!StaysForStretch(node)) RequestNextPick(node);
        }

        /// <summary>The node's objective is done but stretch goals are left: the squad stays until it chooses to leave.</summary>
        private static bool StaysForStretch(RunNode node) =>
            Current.Game.FindMap(node.tile)?.GetComponent<MapComponent_RoguelikeNode>() is { StretchOpen: true };

        /// <summary>One level-up: pick 1 of a few upgrades, each for a squad member.</summary>
        private void OpenLevelUp()
        {
            var cards = LevelUps.Generate(RewardUtility.ChoiceCount(this));
            if (cards.Count == 0)
            {
                pendingLevelUps = 0;
                return;
            }
            Find.WindowStack.Add(new Dialog_RewardPick(cards, "Stretch goal met: one of your squad grows", card =>
            {
                pendingLevelUps = Mathf.Max(0, pendingLevelUps - 1);
                if (card != null) RewardUtility.Apply(card, null);
            }, allowSkip: false, title: "Level up"));
        }

        // ---------- Generation ----------

        /// <summary>
        /// Lays out the whole run after world generation: all acts at once as rows of nodes, shown on the flat act map
        /// (<see cref="Dialog_RunMap"/>). The planet is hidden storage: each node gets a random tile anywhere on it, for a
        /// random biome, season and terrain. NPC settlements are cleared away; elite and boss nodes become hostile
        /// faction bases on their tiles instead.
        /// </summary>
        public bool GenerateAct()
        {
            RemoveFactionSettlements();
            var byBiome = TilesByBiome();
            for (int attempt = 0; attempt < 5; attempt++)
            {
                var all = new List<RunNode>();
                for (int a = 1; a <= ActsPerRun; a++) all.AddRange(LayoutAct(a, all.Count));
                if (!AssignTiles(all, byBiome)) continue;

                foreach (var old in Find.WorldObjects.AllWorldObjects.OfType<RunNodeMarker>().ToList()) old.Destroy();
                nodes = all;
                act = 1;
                for (int a = 1; a <= ActsPerRun; a++) ConnectRows(a);
                // Each act's boss leads to the next act's starting nodes.
                for (int a = 1; a < ActsPerRun; a++)
                    Row(a, RowSizes.Length - 1).First().next.AddRange(Row(a + 1, 0).Select(n => n.id));
                AssignPresets();
                PlaceEnemyBases();
                foreach (var n in Row(1, 0)) n.state = RunNodeState.Available;
                // Markers stay as the caravan's destinations; the world map itself is never shown during a run.
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
            Log.Error("[Roguelike] Couldn't find tiles for the run on this world. Try a larger planet coverage.");
            return false;
        }

        private static List<RunNode> LayoutAct(int actNumber, int firstId)
        {
            var result = new List<RunNode>();
            for (int r = 0; r < RowSizes.Length; r++)
                for (int c = 0; c < RowSizes[r]; c++)
                    result.Add(new RunNode
                    {
                        id = firstId + result.Count, act = actNumber, row = r, col = c,
                        type = r == RowSizes.Length - 1 ? RunNodeType.Boss : RunNodeType.Normal,
                        state = RunNodeState.Locked,
                    });
            return result;
        }

        /// <summary>Every tile a node could use, grouped by biome.</summary>
        private static Dictionary<BiomeDef, List<PlanetTile>> TilesByBiome()
        {
            var grid = Find.WorldGrid;
            var surface = grid.Surface;
            var byBiome = new Dictionary<BiomeDef, List<PlanetTile>>();
            for (int i = 0; i < surface.TilesCount; i++)
            {
                var tile = surface.PlanetTileForID(i);
                var biome = grid[tile].PrimaryBiome;
                if (biome == null || biome.settlementSelectionWeight <= 0f || !TileFinder.IsValidTileForNewSettlement(tile)) continue;
                if (!byBiome.TryGetValue(biome, out var list)) byBiome[biome] = list = new List<PlanetTile>();
                list.Add(tile);
            }
            return byBiome;
        }

        /// <summary>
        /// A random tile per node: pick a biome (weighted like vanilla's settlement picks, so harsh biomes are rarer),
        /// avoiding the biomes of the node's row siblings while others are left, then any tile of it clear of the others.
        /// </summary>
        private static bool AssignTiles(List<RunNode> all, Dictionary<BiomeDef, List<PlanetTile>> byBiome)
        {
            if (byBiome.Count == 0) return false;
            var grid = Find.WorldGrid;
            var taken = new List<PlanetTile>();
            foreach (var n in all)
            {
                var biomes = byBiome.Keys.ToList();
                if (n.act == 1 && n.row == 0)
                {
                    var mild = biomes.Where(b => b.settlementSelectionWeight >= LandingBiomeWeight).ToList();
                    if (mild.Count > 0) biomes = mild;
                }
                var siblingBiomes = all.Where(o => o != n && o.act == n.act && o.row == n.row && o.tile.Valid).Select(o => grid[o.tile].PrimaryBiome).ToList();
                var fresh = biomes.Where(b => !siblingBiomes.Contains(b)).ToList();
                if (fresh.Count > 0) biomes = fresh;

                n.tile = PlanetTile.Invalid;
                for (int tries = 0; tries < 40 && !n.tile.Valid; tries++)
                {
                    var tile = byBiome[biomes.RandomElementByWeight(b => b.settlementSelectionWeight)].RandomElement();
                    if (taken.All(t => grid.ApproxDistanceInTiles(t, tile) >= MinNodeGapTiles)) n.tile = tile;
                }
                if (!n.tile.Valid) return false;
                taken.Add(n.tile);
            }
            return true;
        }

        /// <summary>Clears every NPC settlement off the planet so only the run's nodes remain.</summary>
        private static void RemoveFactionSettlements()
        {
            foreach (var s in Find.WorldObjects.Settlements.Where(s => s.Faction != null && !s.Faction.IsPlayer).ToList()) s.Destroy();
        }

        /// <summary>Elite and boss nodes marked as assaults become bases of hostile factions.</summary>
        private void PlaceEnemyBases()
        {
            foreach (var n in nodes.Where(n => n.preset.assault))
            {
                var faction = Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: false);
                if (faction == null) continue;
                var settlement = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
                settlement.SetFaction(faction);
                settlement.Tile = n.tile;
                settlement.Name = SettlementNameGenerator.GenerateSettlementName(settlement);
                Find.WorldObjects.Add(settlement);
            }
        }

        /// <summary>Slay-the-Spire style edges: each node links to the nearest node in the next row, sometimes a neighbor too, with no crossings; every node gets an incoming edge.</summary>
        private void ConnectRows(int actNumber)
        {
            for (int r = 0; r < RowSizes.Length - 1; r++)
            {
                var from = Row(actNumber, r).OrderBy(n => n.col).ToList();
                var to = Row(actNumber, r + 1).OrderBy(n => n.col).ToList();
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
                var kind = n.IsBoss ? (NodeKind?)null : n.row == 0 ? NodeKind.Expedition
                    : (n.row == RowSizes.Length - 2 ? PreBossRowKinds : MiddleRowKinds).RandomElementByWeight(k => k.weight).kind;
                // Gated objectives (blueprints/research) only appear later, when offered; the season must suit crop objectives.
                var pool = all.Where(p => p.isBoss == n.IsBoss && (kind == null || p.kind == kind) && !p.Gated && (n.row > 0 || p.requiresSkill == null) && p.Eligible(this, n.tile, null).Accepted).ToList();
                if (pool.Count == 0) pool = all.Where(p => !p.isBoss && p.kind == NodeKind.Expedition).ToList();
                bool coastal = Find.World.CoastDirectionAt(n.tile).IsValid;
                var placeable = pool.Where(p => !p.coastalOnly || coastal).ToList();
                if (placeable.Count > 0) pool = placeable;
                var fitting = pool.Where(p => p.tileTemperature.Includes(temp)).ToList();
                var options = fitting.Count > 0 ? fitting : pool;
                // Nodes side by side in a row are real alternatives: avoid repeating a sibling's objective when possible.
                var taken = nodes.Where(o => o != n && o.act == n.act && o.row == n.row && o.preset != null).Select(o => o.preset).ToList();
                var fresh = options.Where(p => !taken.Contains(p)).ToList();
                if (fresh.Count > 0) options = fresh;
                n.preset = options.RandomElementByWeight(p => p.weight);
                if (n.preset.randomModifier) n.modifier = ModifierPool.PickFor(n.tile);
                // Events resolve on the spot; about a quarter of other middle nodes offer recruits instead of a mixed card draw.
                n.reward = n.IsBoss ? (n.act < ActsPerRun ? RunRewardKind.Cards : RunRewardKind.None)
                    : !n.preset.HasMap ? RunRewardKind.None
                    : n.preset.kind == NodeKind.Elite ? RunRewardKind.Relics
                    : n.row > 0 && Rand.Chance(0.25f) ? RunRewardKind.Recruits : RunRewardKind.Cards;
            }
        }

        private static readonly (NodeKind kind, float weight)[] MiddleRowKinds =
        {
            (NodeKind.Expedition, 30f), (NodeKind.CombatAlerted, 15f), (NodeKind.CombatUnaware, 12f), (NodeKind.Escort, 8f), (NodeKind.Event, 12f),
            (NodeKind.Elite, 8f), (NodeKind.Merchant, 6f), (NodeKind.Treasure, 5f), (NodeKind.Rest, 4f),
        };

        /// <summary>The row before the boss leans toward rest sites, like a campfire before a boss.</summary>
        private static readonly (NodeKind kind, float weight)[] PreBossRowKinds =
        {
            (NodeKind.Rest, 45f), (NodeKind.Merchant, 15f), (NodeKind.Expedition, 20f), (NodeKind.Elite, 10f), (NodeKind.Event, 10f),
        };

        // ---------- Drawing ----------

        public override void WorldComponentUpdate()
        {
            if (!active) return;
            // Open level-ups, then the next-node choice, once nothing else (reward screen, event dialog, long event) is in the way.
            bool clear = !RunOver && Current.ProgramState == ProgramState.Playing && !LongEventHandler.ShouldWaitForEvent
                && Find.WindowStack.WindowOfType<Dialog_RewardPick>() == null && Find.WindowStack.WindowOfType<Dialog_NodeTree>() == null
                && Find.WindowStack.WindowOfType<Dialog_FormCaravan>() == null && Find.WindowStack.WindowOfType<Dialog_Packing>() == null;
            if (clear && pendingLevelUps > 0) OpenLevelUp();
            else if (clear && pendingPickNodeId >= 0) OpenNextPick();
            // The world map is never shown during a run: whenever it would be, the flat act map covers it.
            if (WorldRendererUtility.WorldSelected && Current.ProgramState == ProgramState.Playing && Find.WindowStack.WindowOfType<Dialog_RunMap>() == null)
                Find.WindowStack.Add(new Dialog_RunMap(coversWorld: true));
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref active, "active");
            Scribe_Values.Look(ref runWon, "runWon");
            Scribe_Values.Look(ref runLost, "runLost");
            Scribe_Values.Look(ref pendingRewardNodeId, "pendingRewardNodeId", -1);
            Scribe_Values.Look(ref pointsEarned, "pointsEarned");
            Scribe_Values.Look(ref pendingPickNodeId, "pendingPickNodeId", -1);
            Scribe_Values.Look(ref pendingLevelUps, "pendingLevelUps");
            Scribe_Values.Look(ref act, "act", 1);
            Scribe_Values.Look(ref ascension, "ascension");
            Scribe_Values.Look(ref writUsedAct, "writUsedAct");
            Scribe_Values.Look(ref codexTile, "codexTile", PlanetTile.Invalid);
            Scribe_Collections.Look(ref relics, "relics", LookMode.Def);
            Scribe_Collections.Look(ref blueprints, "blueprints", LookMode.Def);
            relics ??= new List<RelicDef>();
            blueprints ??= new List<BlueprintGroupDef>();
            Scribe_Collections.Look(ref nodes, "nodes", LookMode.Deep);
            nodes ??= new List<RunNode>();
        }
    }

    /// <summary>A node's tile on the hidden planet: where the caravan goes when the node is chosen.</summary>
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
                // Map-less nodes resolve on the spot, over the act map.
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
            NodeLauncher.StartOn(map, node.preset, new LookTargets(pawns), node.modifier);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref marker, "marker");
        }
    }
}
