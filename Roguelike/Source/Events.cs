using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Roguelike
{
    public enum EventOutcomeKind { Nothing, RandomRelic, RandomGear, RandomImplant, Recruit, InjureRandom, HealAll }

    public class EventOutcome
    {
        public EventOutcomeKind kind;
        /// <summary>For InjureRandom: damage amount.</summary>
        public float amount = 10f;
    }

    public class EventOption
    {
        public string label;
        public string result;
        public List<EventOutcome> outcomes = new List<EventOutcome>();
    }

    /// <summary>A text event for Event nodes: a situation and a few options with outcomes.</summary>
    public class RoguelikeEventDef : Def
    {
        public string text;
        public List<EventOption> options = new List<EventOption>();
    }

    /// <summary>Runs Event nodes: they have no map, just a choice for the arriving caravan.</summary>
    public static class EventUtility
    {
        public static void Open(RunNode node, Caravan caravan)
        {
            var ev = DefDatabase<RoguelikeEventDef>.AllDefsListForReading.RandomElement();
            var root = new DiaNode(ev.text);
            for (int i = 0; i < ev.options.Count; i++)
            {
                int index = i;
                var result = new DiaNode("");
                var opt = new DiaOption(ev.options[i].label)
                {
                    action = () => result.text = Resolve(node, caravan, ev, index),
                    link = result,
                };
                result.options.Add(new DiaOption("Continue") { resolveTree = true });
                root.options.Add(opt);
            }
            Find.WindowStack.Add(new Dialog_NodeTree(root, delayInteractivity: true, title: ev.LabelCap));
        }

        /// <summary>Applies an option's outcomes to the caravan and completes the node. Returns the result text.</summary>
        public static string Resolve(RunNode node, Caravan caravan, RoguelikeEventDef ev, int optionIndex)
        {
            var option = ev.options[optionIndex];
            var sb = new StringBuilder(option.result);
            foreach (var outcome in option.outcomes)
            {
                string line = Apply(outcome, caravan);
                if (!line.NullOrEmpty()) sb.Append("\n\n" + line);
            }
            WorldComponent_RoguelikeRun.Get?.NotifyNodeCompleted(node.tile);
            return sb.ToString();
        }

        private static string Apply(EventOutcome outcome, Caravan caravan)
        {
            var run = WorldComponent_RoguelikeRun.Get;
            var humans = caravan?.PawnsListForReading.Where(p => p.RaceProps.Humanlike && !p.Dead).ToList() ?? new List<Pawn>();
            switch (outcome.kind)
            {
                case EventOutcomeKind.RandomRelic:
                    var relic = RewardUtility.RelicPool(run).RandomElementWithFallback();
                    if (relic == null) return null;
                    run.AddRelic(relic);
                    return $"Gained relic: {relic.LabelCap}.";
                case EventOutcomeKind.RandomGear:
                case EventOutcomeKind.RandomImplant:
                    var gear = RewardUtility.GearPool(outcome.kind == EventOutcomeKind.RandomImplant).RandomElementWithFallback();
                    if (gear == null) return null;
                    RewardUtility.Apply(new RewardCard { kind = RewardKind.Gear, gear = gear }, null, caravan);
                    return $"Gained: {gear.LabelCap}.";
                case EventOutcomeKind.Recruit:
                    var recruit = RewardUtility.GenerateRecruit();
                    RewardUtility.Apply(new RewardCard { kind = RewardKind.Recruit, recruit = recruit }, null, caravan);
                    return $"{recruit.LabelShortCap} joins the squad.";
                case EventOutcomeKind.InjureRandom:
                    if (humans.Count == 0) return null;
                    var victim = humans.RandomElement();
                    var part = victim.health.hediffSet.GetNotMissingParts().Where(p => p.depth == BodyPartDepth.Outside).RandomElementWithFallback();
                    victim.TakeDamage(new DamageInfo(DamageDefOf.Cut, outcome.amount, hitPart: part));
                    return $"{victim.LabelShortCap} was hurt.";
                case EventOutcomeKind.HealAll:
                    foreach (var p in humans) HealthUtility.HealNonPermanentInjuriesAndRestoreLegs(p);
                    return "Everyone's wounds are healed.";
            }
            return null;
        }
    }
}
