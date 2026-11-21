using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace Roguelike
{
    [DefOf]
    public static class RoguelikeDefOf
    {
        public static WorldObjectDef Roguelike_Node;
        public static WorldObjectDef Roguelike_RunNode;
        public static LetterDef Roguelike_NodeComplete;

        static RoguelikeDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(RoguelikeDefOf));
    }

    /// <summary>Expedition: build up a camp under a clock. Combat nodes: kill the enemy. Escort: keep someone alive. Event: a choice, no map.</summary>
    public enum NodeKind { Expedition, CombatAlerted, CombatUnaware, Escort, Event }

    public enum ObjectiveKind { Stockpile, Survive, Build, KillAll, Escort }

    /// <summary>Tunable node setup, loaded from XML.</summary>
    public class NodePresetDef : Def
    {
        public NodeKind kind = NodeKind.Expedition;
        public ObjectiveKind objective = ObjectiveKind.Stockpile;
        /// <summary>Relative chance among presets of the same kind.</summary>
        public float weight = 1f;
        public bool isBoss;

        /// <summary>Weather modifier; null for none. Its rate is offsetPerDay (°C for temperature, buildup/day for toxic, levels/day for psychic).</summary>
        public GameConditionDef condition;
        public float graceDays = 2f;
        public float offsetPerDay = -4f;
        public float targetDays = 8f;
        public float paceSensitivity = 4f;
        public float minPaceFactor = 0.25f;
        public float maxPaceFactor = 2.5f;
        /// <summary>Tile average temperatures (°C) this preset fits, so modifiers match the biome.</summary>
        public FloatRange tileTemperature = new FloatRange(-999f, 999f);
        public int mapSize = 150;

        public List<ThingDefCountClass> quota = new List<ThingDefCountClass>();
        public List<ThingDefCountClass> buildTarget = new List<ThingDefCountClass>();
        public float surviveDays = 4f;

        /// <summary>Raid strength relative to the storyteller's current threat points.</summary>
        public float threatScale = 1f;
        /// <summary>Days between enemy patrol raids; 0 for none.</summary>
        public float patrolIntervalDays;
        /// <summary>Day enemy reinforcements arrive; 0 for none.</summary>
        public float reinforcementDays;

        public bool HasMap => kind != NodeKind.Event;

        public string ObjectiveText => objective switch
        {
            ObjectiveKind.Stockpile => "Stockpile " + string.Join(", ", quota.Select(q => $"{q.count} {q.thingDef.label}")),
            ObjectiveKind.Build => "Build " + string.Join(", ", buildTarget.Select(q => $"{q.count} {q.thingDef.label}")),
            ObjectiveKind.Survive => $"Hold out for {surviveDays:0.#} days",
            ObjectiveKind.KillAll => "Defeat every enemy on the map",
            ObjectiveKind.Escort => $"Keep the refugee alive for {surviveDays:0.#} days",
            _ => "",
        };

        public string Briefing
        {
            get
            {
                if (kind == NodeKind.Event) return description + "\n\nA choice awaits when your caravan arrives. No map, no clock.";
                var sb = new StringBuilder(description);
                sb.Append($"\n\nObjective: {ObjectiveText}.");
                if (kind == NodeKind.CombatAlerted) sb.Append(" The enemy knows you're here and attacks at once.");
                if (kind == NodeKind.CombatUnaware) sb.Append(" The enemy camp hasn't spotted you yet: scout and prepare, then strike.");
                if (patrolIntervalDays > 0f) sb.Append($" Enemy patrols raid about every {patrolIntervalDays:0.#} days.");
                if (reinforcementDays > 0f) sb.Append($" Enemy reinforcements arrive on day {reinforcementDays:0.#}.");
                if (condition != null)
                {
                    var worker = condition.conditionClass;
                    string rate = typeof(GameCondition_EscalatingTemperature).IsAssignableFrom(worker) ? offsetPerDay.ToStringTemperatureOffset("F0")
                        : typeof(GameCondition_EscalatingToxic).IsAssignableFrom(worker) ? $"{offsetPerDay:0.00} toxic buildup"
                        : $"{offsetPerDay:0.#} drone levels";
                    sb.Append($"\n\n{condition.LabelCap}: holds off for {graceDays:0.#} days, then worsens by about {rate} per day with no limit. " +
                              "It worsens slower while you're ahead of pace and faster while you're behind.");
                }
                if (objective == ObjectiveKind.Escort) sb.Append("\n\nIf the refugee dies, the run ends.");
                sb.Append("\n\nLeaving before the objective is met ends the run.");
                return sb.ToString();
            }
        }
    }

    // ===================== Weather modifiers =====================

    /// <summary>A node modifier whose intensity the node component drives up over time, with no cap.</summary>
    public abstract class GameCondition_RoguelikeModifier : GameCondition
    {
        public float intensity;

        public abstract string IntensityText(float value);

        /// <summary>Called by the node every 250 ticks.</summary>
        public virtual void ApplyEffects(Map map) { }

        public override string Label => $"{base.Label} ({IntensityText(intensity)})";

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref intensity, "intensity");
        }
    }

    /// <summary>Temperature offset in °C.</summary>
    public class GameCondition_EscalatingTemperature : GameCondition_RoguelikeModifier
    {
        public override float TemperatureOffset() => intensity;

        public override string IntensityText(float value) => value.ToStringTemperatureOffset("F0");
    }

    /// <summary>Toxic buildup per day on pawns outdoors; once strong it seeps indoors at half rate.</summary>
    public class GameCondition_EscalatingToxic : GameCondition_RoguelikeModifier
    {
        private const float IndoorThreshold = 0.3f;
        private static StatDef toxicResistance;

        public override string IntensityText(float value) => $"{value:0.00} buildup/day";

        public override void ApplyEffects(Map map)
        {
            if (intensity <= 0f) return;
            toxicResistance ??= DefDatabase<StatDef>.GetNamedSilentFail("ToxicResistance");
            foreach (var pawn in map.mapPawns.AllPawnsSpawned.ToList())
            {
                if (pawn.Dead || !pawn.RaceProps.IsFlesh) continue;
                float exposure = pawn.Position.Roofed(map) ? (intensity >= IndoorThreshold ? 0.5f : 0f) : 1f;
                if (exposure <= 0f) continue;
                float resist = toxicResistance != null ? Mathf.Clamp01(pawn.GetStatValue(toxicResistance)) : 0f;
                HealthUtility.AdjustSeverity(pawn, HediffDefOf.ToxicBuildup, intensity * exposure * (1f - resist) * 250f / GenDate.TicksPerDay);
            }
        }
    }

    /// <summary>A psychic drone that climbs through levels; the mood penalty comes from <see cref="ThoughtWorker_RoguelikePsychic"/>.</summary>
    public class GameCondition_EscalatingPsychic : GameCondition_RoguelikeModifier
    {
        private static readonly string[] Levels = { "faint", "low", "medium", "high", "extreme" };

        /// <summary>-1 below level 1, then 0..3 for low..extreme.</summary>
        public int Stage => intensity < 1f ? -1 : Mathf.Min(3, Mathf.FloorToInt(intensity) - 1);

        public override string IntensityText(float value) => Levels[Mathf.Clamp(Mathf.FloorToInt(value), 0, Levels.Length - 1)];
    }

    public class ThoughtWorker_RoguelikePsychic : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!p.Spawned) return false;
            var cond = p.Map.gameConditionManager.ActiveConditions.OfType<GameCondition_EscalatingPsychic>().FirstOrDefault();
            int stage = cond?.Stage ?? -1;
            return stage < 0 ? ThoughtState.Inactive : ThoughtState.ActiveAtStage(stage);
        }
    }

    /// <summary>Owns a node map; removes it (and itself) once no colonists remain.</summary>
    public class RoguelikeNode : MapParent
    {
        public override bool ShouldRemoveMapNow(out bool alsoRemoveWorldObject)
        {
            alsoRemoveWorldObject = true;
            return !Map.mapPawns.AnyPawnBlockingMapRemoval;
        }
    }

    // ===================== Node state =====================

    /// <summary>Per-map node state: preset, objective, enemies, and the pace-driven threat clock.</summary>
    public class MapComponent_RoguelikeNode : MapComponent
    {
        private const int UpdateInterval = 250;

        public NodePresetDef preset;
        public int startTick;
        public bool completed;
        public bool failed;
        public Faction enemyFaction;
        public int initialEnemies;
        public Pawn escort;
        public int nextPatrolTick = -1;
        public int reinforcementTick = -1;
        public int raidsLaunched;

        public MapComponent_RoguelikeNode(Map map) : base(map) { }

        public bool Active => preset != null;

        public float Days => (Find.TickManager.TicksGame - startTick) / (float)GenDate.TicksPerDay;

        private static WorldComponent_RoguelikeRun Run => WorldComponent_RoguelikeRun.Get is { active: true } r ? r : null;

        private int Row => Run?.NodeAt(map.Tile)?.row ?? 0;

        /// <summary>Grace period including relic bonuses.</summary>
        public float GraceDays => preset.graceDays + (Run?.GraceDaysOffset ?? 0f);

        public bool InGrace => Days < GraceDays;

        public GameCondition_RoguelikeModifier Condition =>
            map.gameConditionManager.ActiveConditions.OfType<GameCondition_RoguelikeModifier>().FirstOrDefault();

        // ---------- Objective ----------

        public int Have(ThingDefCountClass q) => preset.objective == ObjectiveKind.Build
            ? map.listerBuildings.AllBuildingsColonistOfDef(q.thingDef).Count()
            : map.resourceCounter.GetCount(q.thingDef);

        public int EnemiesLeft => enemyFaction == null ? 0
            : map.mapPawns.SpawnedPawnsInFaction(enemyFaction).Count(p => !p.Dead && !p.Downed);

        public bool EscortAlive => escort is { Dead: false, Destroyed: false, Spawned: true } && escort.Map == map;

        /// <summary>0..1 toward the objective.</summary>
        public float Progress => preset.objective switch
        {
            ObjectiveKind.Stockpile => Average(preset.quota),
            ObjectiveKind.Build => Average(preset.buildTarget),
            ObjectiveKind.Survive or ObjectiveKind.Escort => Mathf.Clamp01(Days / preset.surviveDays),
            ObjectiveKind.KillAll => initialEnemies == 0 ? 0f : Mathf.Clamp01(1f - EnemiesLeft / (float)initialEnemies),
            _ => 0f,
        };

        private float Average(List<ThingDefCountClass> list) =>
            list.Count == 0 ? 1f : list.Average(q => Mathf.Clamp01(Have(q) / (float)q.count));

        public bool ObjectiveMet => preset.objective switch
        {
            ObjectiveKind.Stockpile => preset.quota.All(q => Have(q) >= q.count),
            ObjectiveKind.Build => preset.buildTarget.All(q => Have(q) >= q.count),
            ObjectiveKind.Survive => Days >= preset.surviveDays,
            ObjectiveKind.Escort => Days >= preset.surviveDays && EscortAlive,
            ObjectiveKind.KillAll => initialEnemies > 0 && EnemiesLeft == 0,
            _ => false,
        };

        public string ProgressText => preset.objective switch
        {
            ObjectiveKind.Stockpile => string.Join(", ", preset.quota.Select(q => $"{Have(q)}/{q.count} {q.thingDef.label}")),
            ObjectiveKind.Build => string.Join(", ", preset.buildTarget.Select(q => $"{Have(q)}/{q.count} {q.thingDef.label}")),
            ObjectiveKind.Survive => $"hold out {Mathf.Min(Days, preset.surviveDays):0.0}/{preset.surviveDays:0.#} days",
            ObjectiveKind.Escort => $"escort {Mathf.Min(Days, preset.surviveDays):0.0}/{preset.surviveDays:0.#} days",
            ObjectiveKind.KillAll => $"{EnemiesLeft}/{initialEnemies} enemies left",
            _ => "",
        };

        // ---------- Pace and threat ----------

        public float ExpectedProgress => Mathf.Clamp01(Days / preset.targetDays);

        /// <summary>Above 1 when behind pace (worsens faster), below 1 when ahead.</summary>
        public float PaceFactor =>
            Mathf.Clamp(1f + (ExpectedProgress - Progress) * preset.paceSensitivity, preset.minPaceFactor,
                Mathf.Max(1f, 1f + (preset.maxPaceFactor - 1f) * (Run?.MaxPaceFactorFactor ?? 1f)));

        public float CurrentRatePerDay => preset.offsetPerDay * (Run?.WeatherRateFactor ?? 1f) * PaceFactor;

        // ---------- Lifecycle ----------

        public void Start(NodePresetDef p)
        {
            preset = p;
            startTick = Find.TickManager.TicksGame;
            completed = failed = false;
            if (p.condition != null) map.gameConditionManager.RegisterCondition(GameConditionMaker.MakeConditionPermanent(p.condition));
            switch (p.kind)
            {
                case NodeKind.CombatAlerted:
                    PickEnemyFaction();
                    SpawnRaid(1f);
                    break;
                case NodeKind.CombatUnaware:
                    PickEnemyFaction();
                    SpawnCamp();
                    break;
                case NodeKind.Escort:
                    PickEnemyFaction();
                    SpawnEscort();
                    break;
            }
            if (p.patrolIntervalDays > 0f) nextPatrolTick = startTick + (int)(p.patrolIntervalDays * GenDate.TicksPerDay);
            if (p.reinforcementDays > 0f) reinforcementTick = startTick + (int)(p.reinforcementDays * GenDate.TicksPerDay);
            initialEnemies = Mathf.Max(initialEnemies, EnemiesLeft);
        }

        public override void MapComponentTick()
        {
            if (!Active || completed || failed || Find.TickManager.TicksGame % UpdateInterval != 0) return;

            if (preset.objective == ObjectiveKind.Escort && !EscortAlive)
            {
                Fail("The refugee you were escorting is gone.");
                return;
            }
            initialEnemies = Mathf.Max(initialEnemies, EnemiesLeft);
            if (ObjectiveMet)
            {
                Complete();
                return;
            }

            int now = Find.TickManager.TicksGame;
            if (nextPatrolTick > 0 && now >= nextPatrolTick)
            {
                SpawnRaid(0.5f, "Enemy patrol");
                nextPatrolTick = now + (int)(preset.patrolIntervalDays * GenDate.TicksPerDay);
            }
            if (reinforcementTick > 0 && now >= reinforcementTick)
            {
                SpawnRaid(1f, "Enemy reinforcements");
                reinforcementTick = -1;
            }

            AdvanceThreat(UpdateInterval / (float)GenDate.TicksPerDay);
            Condition?.ApplyEffects(map);
        }

        /// <summary>Moves the weather by <paramref name="days"/> worth of change at the current pace.</summary>
        public void AdvanceThreat(float days)
        {
            var cond = Condition;
            if (cond == null || InGrace) return;
            cond.intensity += CurrentRatePerDay * days;
        }

        // ---------- Enemies ----------

        private void PickEnemyFaction()
        {
            enemyFaction ??= Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: false)
                             ?? Find.FactionManager.RandomEnemyFaction();
        }

        private float RaidPoints(float factor) =>
            // Early-game storyteller points are tiny; keep a floor so a fight is a fight.
            Mathf.Max(MinRaidPoints, StorytellerUtility.DefaultThreatPointsNow(map)) * preset.threatScale * factor * (1f + 0.2f * Row);

        private const float MinRaidPoints = 120f;

        /// <summary>A raid that walks in from the edge and attacks at once.</summary>
        public void SpawnRaid(float factor, string label = null)
        {
            if (enemyFaction == null) return;
            var parms = new IncidentParms
            {
                target = map,
                faction = enemyFaction,
                points = RaidPoints(factor),
                raidStrategy = RaidStrategyDefOf.ImmediateAttack,
                raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn,
                forced = true,
            };
            if (label != null) parms.customLetterLabel = label;
            bool ok = IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
            if (!ok)
            {
                // Some factions can't use this strategy or arrival mode; let the raid worker pick its own.
                parms.raidStrategy = null;
                parms.raidArrivalMode = null;
                ok = IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
            }
            if (ok)
            {
                raidsLaunched++;
                if (parms.faction != null) enemyFaction = parms.faction;
            }
            else Log.Warning($"[Roguelike] Raid failed to spawn (faction {enemyFaction?.def.defName}, {parms.points:0} points).");
            initialEnemies = Mathf.Max(initialEnemies, EnemiesLeft);
        }

        /// <summary>An enemy camp on the far side of the map that defends its position until engaged.</summary>
        private void SpawnCamp()
        {
            var parms = new PawnGroupMakerParms
            {
                groupKind = PawnGroupKindDefOf.Combat,
                tile = map.Tile,
                faction = enemyFaction,
                points = RaidPoints(1f),
                generateFightersOnly = true,
            };
            var pawns = PawnGroupMakerUtility.GeneratePawns(parms).ToList();
            if (pawns.Count == 0) return;
            var colonists = map.mapPawns.FreeColonistsSpawned.Select(c => c.Position).ToList();
            var center = colonists.Count > 0 ? colonists[0] : map.Center;
            if (!CellFinder.TryFindRandomCell(map, c => c.Standable(map) && !c.Roofed(map) && c.DistanceTo(center) > map.Size.x * 0.45f
                                                        && map.reachability.CanReach(center, c, Verse.AI.PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors)), out var campCell))
                campCell = map.Center;
            foreach (var pawn in pawns)
            {
                var cell = CellFinder.TryFindRandomCellNear(campCell, map, 6, c => c.Standable(map), out var found) ? found : campCell;
                GenSpawn.Spawn(pawn, cell, map);
            }
            LordMaker.MakeNewLord(enemyFaction, new LordJob_DefendPoint(campCell), map, pawns);
            initialEnemies = pawns.Count;
        }

        /// <summary>A friendly refugee who waits by the squad; the run fails if they die.</summary>
        private void SpawnEscort()
        {
            var faction = Find.FactionManager.RandomNonHostileFaction(allowNonHumanlike: false);
            var request = new PawnGenerationRequest(PawnKindDefOf.Villager, faction, forceGenerateNewPawn: true, mustBeCapableOfViolence: false);
            escort = PawnGenerator.GeneratePawn(request);
            var near = map.mapPawns.FreeColonistsSpawned.FirstOrDefault()?.Position ?? map.Center;
            var cell = CellFinder.TryFindRandomCellNear(near, map, 5, c => c.Standable(map), out var found) ? found : near;
            GenSpawn.Spawn(escort, cell, map);
            LordMaker.MakeNewLord(escort.Faction, new LordJob_DefendPoint(cell, 6f, 10f, addFleeToil: false), map, new[] { escort });
        }

        // ---------- Outcomes ----------

        public void Fail(string reason)
        {
            failed = true;
            var run = Run;
            if (run != null && run.NodeAt(map.Tile)?.state == RunNodeState.Current) run.Lose(reason);
            else Messages.Message(reason, MessageTypeDefOf.NegativeEvent);
        }

        public void Complete()
        {
            completed = true;
            if (escort != null && EscortAlive)
            {
                // The refugee heads off safely.
                escort.DeSpawn();
                Find.WorldPawns.PassToWorld(escort);
            }
            var run = WorldComponent_RoguelikeRun.Get;
            var runNode = run is { active: true } ? run.NodeAt(map.Tile) : null;
            run?.NotifyNodeCompleted(map.Tile);
            string nextStep = runNode == null || runNode.IsBoss
                ? ""
                : "\n\nThen pick your next node on the world map: select the caravan and right-click a highlighted node.";
            var letter = (Letter_NodeComplete)LetterMaker.MakeLetter(
                "Node complete",
                "You've met this node's objective.\n\nLeave now to form a caravan with what your pawns and pack animals can carry. Everything else stays behind." + nextStep,
                RoguelikeDefOf.Roguelike_NodeComplete);
            letter.map = map;
            Find.LetterStack.ReceiveLetter(letter);
        }

        public override void ExposeData()
        {
            Scribe_Defs.Look(ref preset, "preset");
            Scribe_Values.Look(ref startTick, "startTick");
            Scribe_Values.Look(ref completed, "completed");
            Scribe_Values.Look(ref failed, "failed");
            Scribe_References.Look(ref enemyFaction, "enemyFaction");
            Scribe_Values.Look(ref initialEnemies, "initialEnemies");
            Scribe_References.Look(ref escort, "escort");
            Scribe_Values.Look(ref nextPatrolTick, "nextPatrolTick", -1);
            Scribe_Values.Look(ref reinforcementTick, "reinforcementTick", -1);
            Scribe_Values.Look(ref raidsLaunched, "raidsLaunched");
        }
    }

    /// <summary>Completion letter. "Leave" opens the instant caravan-reform screen.</summary>
    public class Letter_NodeComplete : ChoiceLetter
    {
        public Map map;

        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                if (ArchivedOnly)
                {
                    yield return Option_Close;
                    yield break;
                }
                var leave = new DiaOption("Leave node now")
                {
                    resolveTree = true,
                    action = () =>
                    {
                        Find.LetterStack.RemoveLetter(this);
                        var m = map;
                        Find.WindowStack.Add(new Dialog_FormCaravan(m, reform: true, onClosed: () => AbandonIfEmpty(m), mapAboutToBeRemoved: true));
                    }
                };
                if (map == null || !Find.Maps.Contains(map)) leave.Disable("node map is gone");
                yield return leave;
                yield return Option_Postpone;
            }
        }

        /// <summary>The starting node is a player settlement, which never removes itself; abandon it once everyone has left.</summary>
        internal static void AbandonIfEmpty(Map m)
        {
            if (m == null || !Find.Maps.Contains(m) || m.mapPawns.AnyPawnBlockingMapRemoval) return;
            if (m.Parent is Settlement settlement) settlement.Abandon(false);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            // The map is gone once the player has left the node; don't save a dangling reference.
            if (Scribe.mode == LoadSaveMode.Saving && map != null && !Find.Maps.Contains(map)) map = null;
            Scribe_References.Look(ref map, "map");
        }
    }

    /// <summary>HUD line: objective progress and the threat clock.</summary>
    public class Alert_RoguelikeNode : Alert
    {
        public Alert_RoguelikeNode() => defaultPriority = AlertPriority.High;

        private static MapComponent_RoguelikeNode Comp => Find.CurrentMap?.GetComponent<MapComponent_RoguelikeNode>();

        public override AlertReport GetReport() => Comp is { Active: true };

        public override string GetLabel()
        {
            var c = Comp;
            if (c == null || !c.Active) return "";
            if (c.failed) return "Node failed";
            return c.completed ? "Node complete: leave via letter" : "Node: " + c.ProgressText;
        }

        public override TaggedString GetExplanation()
        {
            var c = Comp;
            if (c == null || !c.Active) return "";
            var sb = new StringBuilder();
            sb.AppendLine($"{c.preset.LabelCap}: {c.preset.description}");
            sb.AppendLine();
            sb.AppendLine($"Objective: {c.preset.ObjectiveText}.");
            sb.AppendLine($"Progress: {c.ProgressText} ({c.Progress:P0}).");
            if (c.nextPatrolTick > 0) sb.AppendLine($"Next patrol in {(c.nextPatrolTick - Find.TickManager.TicksGame).ToStringTicksToPeriod()}.");
            if (c.reinforcementTick > 0) sb.AppendLine($"Reinforcements in {(c.reinforcementTick - Find.TickManager.TicksGame).ToStringTicksToPeriod()}.");
            var cond = c.Condition;
            if (cond != null && !c.completed)
            {
                sb.AppendLine();
                sb.AppendLine($"{cond.def.LabelCap}: {cond.IntensityText(cond.intensity)} now.");
                if (c.InGrace)
                {
                    sb.Append($"Holding off for {c.GraceDays - c.Days:0.0} more days.");
                }
                else
                {
                    float factor = c.PaceFactor;
                    string pace = factor < 0.95f ? "ahead of pace, slowed" : factor > 1.05f ? "behind pace, sped up" : "on pace";
                    string rate = cond is GameCondition_EscalatingTemperature ? c.CurrentRatePerDay.ToStringTemperatureOffset("F1") : c.CurrentRatePerDay.ToString("0.00");
                    sb.Append($"Worsening {rate} per day (x{factor:0.00}, {pace}).");
                }
            }
            return sb.ToString().TrimEnd();
        }
    }
}
