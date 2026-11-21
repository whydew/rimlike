using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

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

    /// <summary>Tunable node setup, loaded from XML.</summary>
    public class NodePresetDef : Def
    {
        public GameConditionDef condition;
        public float graceDays = 2f;
        public float offsetPerDay = -4f;
        public float targetDays = 8f;
        public float paceSensitivity = 4f;
        public float minPaceFactor = 0.25f;
        public float maxPaceFactor = 2.5f;
        public int mapSize = 150;
        public bool isBoss;
        /// <summary>Tile average temperatures (°C) this preset fits, so modifiers match the biome.</summary>
        public FloatRange tileTemperature = new FloatRange(-999f, 999f);
        public List<ThingDefCountClass> quota = new List<ThingDefCountClass>();

        public string QuotaText => string.Join(", ", quota.Select(q => $"{q.count} {q.thingDef.label}"));

        public string Briefing =>
            $"{description}\n\nQuota: {QuotaText}.\n\n{condition.LabelCap}: holds off for {graceDays:0.#} days, then changes about " +
            $"{offsetPerDay.ToStringTemperatureOffset("F0")} per day with no limit. It changes slower while you're ahead of pace and faster while you're behind.\n\nLeaving before the quota is met ends the run.";
    }

    /// <summary>Temperature offset driven by the node component; no cap.</summary>
    public class GameCondition_EscalatingTemperature : GameCondition
    {
        public float offset;

        public override float TemperatureOffset() => offset;

        public override string Label => $"{base.Label} ({offset.ToStringTemperatureOffset("F0")})";

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref offset, "offset");
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

    /// <summary>Per-map node state: preset, objective, and the pace-driven threat clock.</summary>
    public class MapComponent_RoguelikeNode : MapComponent
    {
        private const int UpdateInterval = 250;

        public NodePresetDef preset;
        public int startTick;
        public bool completed;

        public MapComponent_RoguelikeNode(Map map) : base(map) { }

        public bool Active => preset != null;

        public float Days => (Find.TickManager.TicksGame - startTick) / (float)GenDate.TicksPerDay;

        public bool InGrace => Days < preset.graceDays;

        public int Have(ThingDefCountClass q) => map.resourceCounter.GetCount(q.thingDef);

        public bool QuotaMet => preset.quota.All(q => Have(q) >= q.count);

        /// <summary>0..1, averaged over quota entries.</summary>
        public float Progress => preset.quota.Count == 0 ? 1f : preset.quota.Average(q => Mathf.Clamp01(Have(q) / (float)q.count));

        public float ExpectedProgress => Mathf.Clamp01(Days / preset.targetDays);

        /// <summary>Above 1 when behind pace (worsens faster), below 1 when ahead.</summary>
        public float PaceFactor =>
            Mathf.Clamp(1f + (ExpectedProgress - Progress) * preset.paceSensitivity, preset.minPaceFactor, preset.maxPaceFactor);

        public GameCondition_EscalatingTemperature Condition =>
            map.gameConditionManager.ActiveConditions.OfType<GameCondition_EscalatingTemperature>().FirstOrDefault();

        public string QuotaSummary => string.Join(", ", preset.quota.Select(q => $"{Have(q)}/{q.count} {q.thingDef.label}"));

        public void Start(NodePresetDef p)
        {
            preset = p;
            startTick = Find.TickManager.TicksGame;
            completed = false;
            map.gameConditionManager.RegisterCondition(GameConditionMaker.MakeConditionPermanent(p.condition));
        }

        public override void MapComponentTick()
        {
            if (!Active || completed || Find.TickManager.TicksGame % UpdateInterval != 0) return;
            if (QuotaMet)
            {
                Complete();
                return;
            }
            AdvanceThreat(UpdateInterval / (float)GenDate.TicksPerDay);
        }

        /// <summary>Moves the weather by <paramref name="days"/> worth of change at the current pace.</summary>
        public void AdvanceThreat(float days)
        {
            var cond = Condition;
            if (cond == null || InGrace) return;
            cond.offset += preset.offsetPerDay * PaceFactor * days;
        }

        public void Complete()
        {
            completed = true;
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
            return c.completed ? "Node complete: leave via letter" : "Node: " + c.QuotaSummary;
        }

        public override TaggedString GetExplanation()
        {
            var c = Comp;
            if (c == null || !c.Active) return "";
            var sb = new StringBuilder();
            sb.AppendLine($"{c.preset.LabelCap}: {c.preset.description}");
            sb.AppendLine();
            sb.AppendLine("Stockpile quota:");
            foreach (var q in c.preset.quota) sb.AppendLine($"  - {q.thingDef.LabelCap}: {c.Have(q)} / {q.count}");
            var cond = c.Condition;
            if (cond != null && !c.completed)
            {
                sb.AppendLine();
                sb.AppendLine($"{cond.def.LabelCap}: {cond.offset.ToStringTemperatureOffset("F1")} now.");
                if (c.InGrace)
                {
                    sb.Append($"Holding off for {c.preset.graceDays - c.Days:0.0} more days.");
                }
                else
                {
                    float factor = c.PaceFactor;
                    string pace = factor < 0.95f ? "ahead of pace, slowed" : factor > 1.05f ? "behind pace, sped up" : "on pace";
                    sb.Append($"Changing {(c.preset.offsetPerDay * factor).ToStringTemperatureOffset("F1")} per day (x{factor:0.00}, {pace}).");
                }
            }
            return sb.ToString().TrimEnd();
        }
    }
}
