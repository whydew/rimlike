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
    public enum NodeKind { Expedition, CombatAlerted, CombatUnaware, Escort, Event, Elite, Rest, Merchant, Treasure }

    public enum ObjectiveKind { Stockpile, Survive, Build, KillAll, Escort, Requirements }

    /// <summary>Tunable node setup, loaded from XML.</summary>
    public class NodePresetDef : Def
    {
        public NodeKind kind = NodeKind.Expedition;
        public ObjectiveKind objective = ObjectiveKind.Stockpile;
        /// <summary>Relative chance among presets of the same kind.</summary>
        public float weight = 1f;
        public bool isBoss;
        /// <summary>The node is a hostile faction base: you assault its defenders on the base's own map.</summary>
        public bool assault;

        /// <summary>Fixed weather modifier; null for none. Its rate is offsetPerDay (°C for temperature, buildup/day for toxic, levels/day for psychic).</summary>
        public GameConditionDef condition;
        /// <summary>Roll the modifier per node from every modifier with a ModifierExtension that fits the tile (rate and grace come from it).</summary>
        public bool randomModifier;
        public float graceDays = 2f;
        public float offsetPerDay = -4f;
        public float targetDays = 8f;
        public float paceSensitivity = 4f;
        public float minPaceFactor = 0.25f;
        public float maxPaceFactor = 2.5f;
        /// <summary>Tile average temperatures (°C) this preset fits, so modifiers match the biome.</summary>
        public FloatRange tileTemperature = new FloatRange(-999f, 999f);
        /// <summary>Only on coastal tiles (floods and hurricanes).</summary>
        public bool coastalOnly;
        public int mapSize = 150;

        public List<ThingDefCountClass> quota = new List<ThingDefCountClass>();
        public List<ThingDefCountClass> buildTarget = new List<ThingDefCountClass>();
        public float surviveDays = 4f;
        /// <summary>Base-building objective: every requirement must be met, after at least minDays on the node.</summary>
        public List<NodeRequirement> requirements = new List<NodeRequirement>();
        public float minDays;
        /// <summary>Mineable resources the objective needs (e.g. components): the node map gets enough ore to mine them.</summary>
        public List<ThingDefCountClass> ensureMineable = new List<ThingDefCountClass>();

        // ---------- Eligibility: only offer objectives the squad can actually do ----------
        /// <summary>Blueprint groups that must be drafted this run.</summary>
        public List<BlueprintGroupDef> requiresBlueprints = new List<BlueprintGroupDef>();
        /// <summary>Research that must already be finished.</summary>
        public List<ResearchProjectDef> requiresResearch = new List<ResearchProjectDef>();
        /// <summary>Some colonist must have at least this level in this skill.</summary>
        public SkillDef requiresSkill;
        public int requiresSkillLevel;
        /// <summary>Crop objectives: the tile needs at least this many twelfths of growing weather (average 6–42°C).</summary>
        public int growingTwelfths;

        /// <summary>Needs drafted blueprints or finished research, so it only appears once rewards unlock it.</summary>
        public bool Gated => requiresBlueprints.Count > 0 || requiresResearch.Count > 0;

        public AcceptanceReport Eligible(WorldComponent_RoguelikeRun run, PlanetTile tile, IEnumerable<Pawn> squad)
        {
            var missingBp = requiresBlueprints.Where(b => run == null || !run.blueprints.Contains(b)).ToList();
            if (missingBp.Count > 0) return $"needs blueprints: {string.Join(", ", missingBp.Select(b => b.label))}";
            var missingRes = requiresResearch.Where(r => !r.IsFinished).ToList();
            if (missingRes.Count > 0) return $"needs research: {string.Join(", ", missingRes.Select(r => r.label))}";
            if (requiresSkill != null && squad != null && !squad.Any(p => p.skills?.GetSkill(requiresSkill) is { TotallyDisabled: false } s && s.Level >= requiresSkillLevel))
                return $"needs a colonist with {requiresSkill.skillLabel} {requiresSkillLevel}";
            // Buildings the objective asks for may need a skilled builder (hospital bed: Construction 8).
            int build = BuildSkillNeeded;
            if (build > 0 && squad != null && !squad.Any(p => p.skills?.GetSkill(SkillDefOf.Construction) is { TotallyDisabled: false } s && s.Level >= build))
                return $"needs a colonist with construction {build}";
            if (growingTwelfths > 0 && tile.Valid && GenTemperature.TwelfthsInAverageTemperatureRange(tile, 6f, 42f).Count < growingTwelfths)
                return "growing season too short here";
            return true;
        }

        public int BuildSkillNeeded =>
            requirements.Where(r => r.kind == RequirementKind.Build && r.thing != null).Select(r => r.thing.constructionSkillPrerequisite).DefaultIfEmpty(0).Max();

        public string NeedsText
        {
            get
            {
                var parts = new List<string>();
                if (requiresBlueprints.Count > 0) parts.Add("blueprints " + string.Join(", ", requiresBlueprints.Select(b => b.label)));
                if (requiresResearch.Count > 0) parts.Add("research " + string.Join(", ", requiresResearch.Select(r => r.label)));
                if (requiresSkill != null) parts.Add($"a colonist with {requiresSkill.skillLabel} {requiresSkillLevel}");
                if (BuildSkillNeeded > 0) parts.Add($"a colonist with construction {BuildSkillNeeded}");
                return parts.Count == 0 ? null : "Needs " + string.Join("; ", parts) + ".";
            }
        }

        /// <summary>Raid strength relative to the storyteller's current threat points.</summary>
        public float threatScale = 1f;
        /// <summary>Days between enemy patrol raids; 0 for none.</summary>
        public float patrolIntervalDays;
        /// <summary>Day enemy reinforcements arrive; 0 for none.</summary>
        public float reinforcementDays;

        /// <summary>Event, rest, merchant and treasure nodes resolve on the world map.</summary>
        public bool HasMap => kind is not (NodeKind.Event or NodeKind.Rest or NodeKind.Merchant or NodeKind.Treasure);

        /// <summary>For rest and treasure nodes: the fixed event to open (otherwise a random pooled event).</summary>
        public RoguelikeEventDef eventDef;

        public float RateFor(GameConditionDef modifier) =>
            randomModifier ? modifier?.GetModExtension<ModifierExtension>()?.ratePerDay ?? 0f : offsetPerDay;

        public float GraceFor(GameConditionDef modifier) =>
            randomModifier ? modifier?.GetModExtension<ModifierExtension>()?.graceDays ?? graceDays : graceDays;

        public string ObjectiveTextFor(int squad)
        {
            string text = objective switch
            {
                ObjectiveKind.Stockpile => "Stockpile " + string.Join(", ", quota.Select(q => $"{q.count} {q.thingDef.label}")),
                ObjectiveKind.Build => "Build " + string.Join(", ", buildTarget.Select(q => $"{q.count} {q.thingDef.label}")),
                ObjectiveKind.Survive => $"Hold out for {surviveDays:0.#} days",
                ObjectiveKind.KillAll => "Defeat every enemy on the map",
                ObjectiveKind.Escort => $"Keep the refugee alive for {surviveDays:0.#} days",
                ObjectiveKind.Requirements => string.Join("; ", requirements.Select(r => r.Describe(squad))).CapitalizeFirst(),
                _ => "",
            };
            if (minDays > 0f) text += $"; stay at least {minDays:0.#} days";
            return text;
        }

        public string ObjectiveText => ObjectiveTextFor(3);

        /// <summary>The objective as short lines for a card: one per requirement plus how the enemy behaves.</summary>
        public List<string> ObjectiveLines(int squad)
        {
            var lines = new List<string>();
            if (!HasMap) { lines.Add("Resolves when you arrive: no map, no clock"); return lines; }
            if (objective == ObjectiveKind.Requirements)
            {
                lines.AddRange(requirements.Where(r => r.kind != RequirementKind.NoDeaths).Select(r => r.Card(squad)));
                if (minDays > 0f) lines.Add($"Stay {minDays:0.#}+ days");
            }
            else lines.Add(ObjectiveTextFor(squad).CapitalizeFirst());
            if (kind is NodeKind.CombatAlerted or NodeKind.Elite) lines.Add("The enemy attacks at once");
            if (kind == NodeKind.CombatUnaware) lines.Add("The camp hasn't seen you: scout, then strike");
            if (patrolIntervalDays > 0f) lines.Add($"Patrols raid about every {patrolIntervalDays:0.#} days");
            if (reinforcementDays > 0f) lines.Add($"Reinforcements arrive on day {reinforcementDays:0.#}");
            return lines;
        }

        /// <summary>The weather in one line for a card, or null when the node has none.</summary>
        public string WeatherLine(GameConditionDef modifier)
        {
            if (!HasMap) return null;
            if (modifier == null) return randomModifier ? "One that fits the local climate" : null;
            return $"{modifier.LabelCap}: calm {GraceFor(modifier):0.#} days, then +{PerDayText(modifier)}/day";
        }

        /// <summary>What ends the run here besides leaving early, for a card's warning line.</summary>
        public string DangerLine =>
            objective == ObjectiveKind.Escort ? "If the refugee dies, the run ends"
            : requirements.Any(r => r.kind == RequirementKind.NoDeaths) ? "If anyone dies, the run ends"
            : null;

        private string PerDayText(GameConditionDef modifier)
        {
            float rate = RateFor(modifier);
            var worker = modifier.conditionClass;
            return typeof(GameCondition_EscalatingTemperature).IsAssignableFrom(worker) ? rate.ToStringTemperatureOffset("F1")
                : typeof(GameCondition_EscalatingToxic).IsAssignableFrom(worker) ? $"{rate:0.000} toxic buildup"
                : typeof(GameCondition_EscalatingStrikes).IsAssignableFrom(worker) ? $"{rate:0.00} strikes"
                : typeof(GameCondition_RisingFlood).IsAssignableFrom(worker) ? $"{rate:0.#} cells of water"
                : $"{rate:0.00} drone levels";
        }

        /// <summary>The node briefing for a given weather modifier and squad size.</summary>
        public string BriefingFor(GameConditionDef modifier, int squad)
        {
            if (!HasMap) return description + "\n\nResolves when your caravan arrives. No map, no clock.";
            var sb = new StringBuilder(description);
            sb.Append($"\n\nObjective: {ObjectiveTextFor(squad)}.");
            if (objective == ObjectiveKind.Requirements && squad > 0) sb.Append($" (Counts are for a squad of {squad}.)");
            if (NeedsText != null) sb.Append(" " + NeedsText);
            if (kind is NodeKind.CombatAlerted or NodeKind.Elite) sb.Append(" The enemy knows you're here and attacks at once.");
            if (kind == NodeKind.CombatUnaware) sb.Append(" The enemy camp hasn't spotted you yet: scout and prepare, then strike.");
            if (patrolIntervalDays > 0f) sb.Append($" Enemy patrols raid about every {patrolIntervalDays:0.#} days.");
            if (reinforcementDays > 0f) sb.Append($" Enemy reinforcements arrive on day {reinforcementDays:0.#}.");
            if (modifier != null)
            {
                string perDay = PerDayText(modifier);
                sb.Append($"\n\n{modifier.LabelCap}: holds off for {GraceFor(modifier):0.#} days, then worsens by about {perDay} per day with no limit. " +
                          "It worsens slower while you're ahead of pace and faster while you're behind.");
            }
            else if (randomModifier) sb.Append("\n\nWeather: a modifier that fits the local climate.");
            if (objective == ObjectiveKind.Escort) sb.Append("\n\nIf the refugee dies, the run ends.");
            if (requirements.Any(r => r.kind == RequirementKind.NoDeaths)) sb.Append("\n\nIf anyone dies here, the run ends.");
            sb.Append("\n\nLeaving before the objective is met ends the run.");
            return sb.ToString();
        }

        public string Briefing => BriefingFor(randomModifier ? null : condition, 3);
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
        /// <summary>The weather modifier this node rolled (null for none) and its daily rate and grace.</summary>
        public GameConditionDef modifier;
        public float ratePerDay;
        public float graceDays;
        /// <summary>Squad size on entry; requirement counts scale with it.</summary>
        public int squad = 3;
        /// <summary>Everyone who arrived (for the no-deaths requirement).</summary>
        public List<Pawn> entrySquad = new List<Pawn>();
        /// <summary>How much of each produced item was already here on arrival.</summary>
        private Dictionary<ThingDef, int> baselines = new Dictionary<ThingDef, int>();
        /// <summary>Days each requirement (by index) has been met so far, for requirements with holdDays.</summary>
        public List<float> heldDays = new List<float>();
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
        public float GraceDays => Mathf.Max(0f, graceDays + (Run?.GraceDaysOffset ?? 0f));

        public bool InGrace => Days < GraceDays;

        public GameCondition_RoguelikeModifier Condition =>
            map.gameConditionManager.ActiveConditions.OfType<GameCondition_RoguelikeModifier>().FirstOrDefault();

        public int Baseline(ThingDef def) => baselines.TryGetValue(def, out int n) ? n : 0;

        public string Briefing => preset.BriefingFor(modifier, squad);

        // ---------- Objective ----------

        public int Have(ThingDefCountClass q) => preset.objective == ObjectiveKind.Build
            ? map.listerBuildings.AllBuildingsColonistOfDef(q.thingDef).Count()
            : map.resourceCounter.GetCount(q.thingDef);

        public int Have(NodeRequirement r) => RequirementUtility.Have(this, r);

        private float Held(NodeRequirement r)
        {
            int i = preset.requirements.IndexOf(r);
            return i >= 0 && i < heldDays.Count ? heldDays[i] : 0f;
        }

        /// <summary>Met now, or for a held requirement, met for long enough in total.</summary>
        public bool Met(NodeRequirement r) => r.holdDays > 0f ? Held(r) >= r.holdDays : Have(r) >= r.Target(squad);

        /// <summary>0..1 toward one requirement: the count, then (if held) the days held.</summary>
        private float RequirementProgress(NodeRequirement r)
        {
            if (Met(r)) return 1f;
            float count = Mathf.Clamp01(Have(r) / (float)r.Target(squad));
            return r.holdDays > 0f ? 0.5f * count + 0.5f * Mathf.Clamp01(Held(r) / r.holdDays) : count;
        }

        /// <summary>Adds time to held requirements that are met right now.</summary>
        private void TickHolds(float days)
        {
            while (heldDays.Count < preset.requirements.Count) heldDays.Add(0f);
            for (int i = 0; i < preset.requirements.Count; i++)
            {
                var r = preset.requirements[i];
                if (r.holdDays > 0f && heldDays[i] < r.holdDays && Have(r) >= r.Target(squad)) heldDays[i] += days;
            }
        }

        public int EnemiesLeft => enemyFaction == null ? 0
            : map.mapPawns.SpawnedPawnsInFaction(enemyFaction).Count(p => !p.Dead && !p.Downed);

        public bool EscortAlive => escort is { Dead: false, Destroyed: false, Spawned: true } && escort.Map == map;

        public bool StayMet => Days >= preset.minDays;

        /// <summary>0..1 toward the objective.</summary>
        public float Progress => preset.objective switch
        {
            ObjectiveKind.Stockpile => Average(preset.quota),
            ObjectiveKind.Build => Average(preset.buildTarget),
            ObjectiveKind.Survive or ObjectiveKind.Escort => Mathf.Clamp01(Days / preset.surviveDays),
            ObjectiveKind.KillAll => initialEnemies == 0 ? 0f : Mathf.Clamp01(1f - EnemiesLeft / (float)initialEnemies),
            ObjectiveKind.Requirements => preset.requirements.Count == 0 ? 1f
                : preset.requirements.Average(RequirementProgress),
            _ => 0f,
        };

        private float Average(List<ThingDefCountClass> list) =>
            list.Count == 0 ? 1f : list.Average(q => Mathf.Clamp01(Have(q) / (float)q.count));

        public bool RequirementsMet => preset.objective switch
        {
            ObjectiveKind.Stockpile => preset.quota.All(q => Have(q) >= q.count),
            ObjectiveKind.Build => preset.buildTarget.All(q => Have(q) >= q.count),
            ObjectiveKind.Survive => Days >= preset.surviveDays,
            ObjectiveKind.Escort => Days >= preset.surviveDays && EscortAlive,
            ObjectiveKind.KillAll => initialEnemies > 0 && EnemiesLeft == 0,
            ObjectiveKind.Requirements => preset.requirements.All(Met),
            _ => false,
        };

        public bool ObjectiveMet => RequirementsMet && StayMet;

        public string ProgressText
        {
            get
            {
                string text = preset.objective switch
                {
                    ObjectiveKind.Stockpile => string.Join(", ", preset.quota.Select(q => $"{Have(q)}/{q.count} {q.thingDef.label}")),
                    ObjectiveKind.Build => string.Join(", ", preset.buildTarget.Select(q => $"{Have(q)}/{q.count} {q.thingDef.label}")),
                    ObjectiveKind.Survive => $"hold out {Mathf.Min(Days, preset.surviveDays):0.0}/{preset.surviveDays:0.#} days",
                    ObjectiveKind.Escort => $"escort {Mathf.Min(Days, preset.surviveDays):0.0}/{preset.surviveDays:0.#} days",
                    ObjectiveKind.KillAll => $"{EnemiesLeft}/{initialEnemies} enemies left",
                    ObjectiveKind.Requirements => string.Join(", ", preset.requirements.Select(r => r.Short(squad, Have(r), Held(r)))),
                    _ => "",
                };
                if (preset.minDays > 0f) text += $", day {Mathf.Min(Days, preset.minDays):0}/{preset.minDays:0}";
                return text;
            }
        }

        // ---------- Pace and threat ----------

        public float ExpectedProgress => Mathf.Clamp01(Days / preset.targetDays);

        /// <summary>Above 1 when behind pace (worsens faster), below 1 when ahead.</summary>
        public float PaceFactor =>
            Mathf.Clamp(1f + (ExpectedProgress - Progress) * preset.paceSensitivity, preset.minPaceFactor,
                Mathf.Max(1f, 1f + (preset.maxPaceFactor - 1f) * (Run?.MaxPaceFactorFactor ?? 1f)));

        public float CurrentRatePerDay => ratePerDay * (Run?.WeatherRateFactor ?? 1f) * PaceFactor;

        // ---------- Lifecycle ----------

        /// <summary>Starts the node. <paramref name="rolledModifier"/> is the run node's modifier; debug nodes roll one for this tile.</summary>
        public void Start(NodePresetDef p, GameConditionDef rolledModifier = null)
        {
            preset = p;
            startTick = Find.TickManager.TicksGame;
            completed = failed = false;
            modifier = p.randomModifier ? rolledModifier ?? ModifierPool.PickFor(map.Tile) : p.condition;
            ratePerDay = p.RateFor(modifier);
            graceDays = p.GraceFor(modifier);
            if (modifier != null) map.gameConditionManager.RegisterCondition(GameConditionMaker.MakeConditionPermanent(modifier));

            entrySquad = map.mapPawns.FreeColonists.ToList();
            squad = Mathf.Clamp(entrySquad.Count, 1, 5);
            baselines.Clear();
            foreach (var r in p.requirements.Where(r => r.kind == RequirementKind.Produce && r.thing != null))
                baselines[r.thing] = RequirementUtility.CountOnMap(map, r.thing);
            heldDays = p.requirements.Select(_ => 0f).ToList();
            foreach (var res in p.ensureMineable) RequirementUtility.EnsureMineable(map, res.thingDef, res.count);

            if (p.assault && map.Parent is Settlement { Faction: not null } enemyBase && !enemyBase.Faction.IsPlayer)
                enemyFaction = enemyBase.Faction; // the base's defenders are the enemy
            else switch (p.kind)
            {
                case NodeKind.CombatAlerted:
                case NodeKind.Elite:
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
            if (p.patrolIntervalDays > 0f && enemyFaction == null) PickEnemyFaction(); // expeditions with patrols: someone tests the defenses
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
            if (preset.requirements.Any(r => r.kind == RequirementKind.NoDeaths) && entrySquad.Any(p => p == null || p.Dead))
            {
                Fail("A squad member died, and this node allowed no deaths.");
                return;
            }
            initialEnemies = Mathf.Max(initialEnemies, EnemiesLeft);
            if (preset.objective == ObjectiveKind.Requirements) TickHolds(UpdateInterval / (float)GenDate.TicksPerDay);
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
            Mathf.Max(MinRaidPoints, StorytellerUtility.DefaultThreatPointsNow(map)) * preset.threatScale * factor * (1f + 0.2f * Row) * (Run?.RaidFactor(preset.isBoss) ?? 1f);

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
            // Some factions need more points than our floor to field any group at all.
            parms.points = Mathf.Max(parms.points, enemyFaction.def.MinPointsToGeneratePawnGroup(PawnGroupKindDefOf.Combat) * 1.1f);
            bool ok = IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
            if (!ok)
            {
                // Some factions can't use this strategy or arrival mode; let the raid worker pick its own.
                parms.raidStrategy = null;
                parms.raidArrivalMode = null;
                ok = IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
            }
            if (!ok)
            {
                // Last resort: any other hostile faction.
                parms.faction = Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: false);
                ok = parms.faction != null && IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
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
            // Not every hostile faction can field a combat group; try others so the node is always winnable.
            var candidates = new[] { enemyFaction }.Concat(Find.FactionManager.AllFactionsListForReading
                .Where(f => f != enemyFaction && !f.IsPlayer && !f.Hidden && !f.defeated && f.def.humanlikeFaction && f.HostileTo(Faction.OfPlayer)).InRandomOrder());
            List<Pawn> pawns = null;
            foreach (var faction in candidates)
            {
                if (faction == null) continue;
                var parms = new PawnGroupMakerParms
                {
                    groupKind = PawnGroupKindDefOf.Combat,
                    tile = map.Tile,
                    faction = faction,
                    points = Mathf.Max(RaidPoints(1f), faction.def.MinPointsToGeneratePawnGroup(PawnGroupKindDefOf.Combat) * 1.1f),
                    generateFightersOnly = true,
                };
                pawns = PawnGroupMakerUtility.GeneratePawns(parms, warnOnZeroResults: false).ToList();
                if (pawns.Count == 0) continue;
                enemyFaction = faction;
                break;
            }
            if (pawns == null || pawns.Count == 0)
            {
                Log.Warning("[Roguelike] No faction could field a camp; sending a raid instead.");
                SpawnRaid(1f);
                return;
            }
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
            string nextStep = runNode == null || runNode.IsBoss && runNode.act >= WorldComponent_RoguelikeRun.ActsPerRun
                ? ""
                : "\n\nIf you closed the node choice, use Leave node now to bring it back.";
            var letter = (Letter_NodeComplete)LetterMaker.MakeLetter(
                "Node complete",
                "You've met this node's objective.\n\nChoose your next node, then pack what your pawns and pack animals can carry. Everything else stays behind." + nextStep,
                RoguelikeDefOf.Roguelike_NodeComplete);
            letter.map = map;
            Find.LetterStack.ReceiveLetter(letter);
        }

        public override void MapComponentOnGUI()
        {
            if (Active && map == Find.CurrentMap && !WorldRendererUtility.WorldSelected && Find.UIRoot.screenshotMode?.FiltersCurrentEvent != true)
                NodeHud.Draw(this);
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
            Scribe_Defs.Look(ref modifier, "modifier");
            Scribe_Values.Look(ref ratePerDay, "ratePerDay");
            Scribe_Values.Look(ref graceDays, "graceDays");
            Scribe_Values.Look(ref squad, "squad", 3);
            Scribe_Collections.Look(ref entrySquad, "entrySquad", LookMode.Reference);
            Scribe_Collections.Look(ref baselines, "baselines", LookMode.Def, LookMode.Value);
            Scribe_Collections.Look(ref heldDays, "heldDays", LookMode.Value);
            heldDays ??= new List<float>();
            entrySquad ??= new List<Pawn>();
            baselines ??= new Dictionary<ThingDef, int>();
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
                        // On the run graph: pick the next node (then pack). Off the graph (debug nodes): just pack and leave.
                        var run = WorldComponent_RoguelikeRun.Get;
                        var node = run is { active: true } && m != null ? run.NodeAt(m.Tile) : null;
                        if (node != null) run.RequestNextPick(node);
                        else Find.WindowStack.Add(new Dialog_FormCaravan(m, reform: true, onClosed: () => AbandonIfEmpty(m), mapAboutToBeRemoved: true));
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
