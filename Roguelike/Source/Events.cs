using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Roguelike
{
    public enum EventOutcomeKind { Nothing, RandomRelic, RandomGear, RandomImplant, Recruit, InjureRandom, HealAll, TrainSkills, FixWorst, Silver }

    public class EventOutcome
    {
        public EventOutcomeKind kind;
        /// <summary>InjureRandom: damage. TrainSkills: XP per colonist. Silver: amount.</summary>
        public float amount = 10f;
    }

    public class EventOption
    {
        public string label;
        public string result;
        public List<EventOutcome> outcomes = new List<EventOutcome>();
    }

    /// <summary>A text event: a situation and a few options with outcomes. Used by event, rest and treasure nodes.</summary>
    public class RoguelikeEventDef : Def
    {
        public string text;
        /// <summary>False for events tied to a node kind (rest, treasure) so they never show up as random encounters.</summary>
        public bool pooled = true;
        public List<EventOption> options = new List<EventOption>();
    }

    /// <summary>Runs map-less nodes: a choice (event, rest, treasure) or a shop (merchant) for the arriving caravan.</summary>
    public static class EventUtility
    {
        public static void Open(RunNode node, Caravan caravan)
        {
            if (node.preset.kind == NodeKind.Merchant)
            {
                ShopUtility.Open(node, caravan);
                return;
            }
            var ev = node.preset.eventDef ?? DefDatabase<RoguelikeEventDef>.AllDefsListForReading.Where(e => e.pooled).RandomElement();
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
                case EventOutcomeKind.TrainSkills:
                    foreach (var p in humans.Where(p => p.skills != null))
                    {
                        var best = p.skills.skills.Where(s => !s.TotallyDisabled).OrderByDescending(s => (int)s.passion).ThenByDescending(s => s.Level).FirstOrDefault();
                        best?.Learn(outcome.amount, direct: true);
                    }
                    return "Everyone trains their best skill.";
                case EventOutcomeKind.FixWorst:
                    var hurt = humans.Where(p => HealthUtility.TryGetWorstHealthCondition(p, out _, out _)).ToList();
                    if (hurt.Count == 0) return "Nobody needed treatment.";
                    var patient = hurt.RandomElement();
                    var text = HealthUtility.FixWorstHealthCondition(patient);
                    return text.NullOrEmpty() ? $"{patient.LabelShortCap} was treated." : text.ToString();
                case EventOutcomeKind.Silver:
                    if (caravan == null) return null;
                    var silver = ThingMaker.MakeThing(ThingDefOf.Silver);
                    silver.stackCount = (int)outcome.amount;
                    CaravanInventoryUtility.GiveThing(caravan, silver);
                    return $"Gained {(int)outcome.amount} silver.";
            }
            return null;
        }
    }

    /// <summary>Merchant nodes: buy reward cards with the silver the caravan is carrying.</summary>
    public static class ShopUtility
    {
        public static int Price(RewardCard c) => Mathf.RoundToInt(BasePrice(c) * (1f - Mathf.Clamp01(WorldComponent_RoguelikeRun.Get?.relics.Sum(r => r.shopDiscount) ?? 0f)));

        private static int BasePrice(RewardCard c) => c.kind switch
        {
            RewardKind.Relic => 250,
            RewardKind.Blueprint => 150,
            RewardKind.Implant => 200,
            RewardKind.Recruit => 300,
            _ => 120,
        };

        public static int Silver(Caravan caravan) =>
            CaravanInventoryUtility.AllInventoryItems(caravan).Where(t => t.def == ThingDefOf.Silver).Sum(t => t.stackCount);

        public static bool TrySpend(Caravan caravan, int amount)
        {
            if (Silver(caravan) < amount) return false;
            foreach (var t in CaravanInventoryUtility.AllInventoryItems(caravan).Where(t => t.def == ThingDefOf.Silver).ToList())
            {
                if (amount <= 0) break;
                int take = System.Math.Min(amount, t.stackCount);
                amount -= take;
                if (take == t.stackCount) t.Destroy();
                else t.SplitOff(take).Destroy();
            }
            return true;
        }

        /// <summary>Opens the shop; visiting completes the node, buying is optional.</summary>
        public static List<RewardCard> Open(RunNode node, Caravan caravan)
        {
            var run = WorldComponent_RoguelikeRun.Get;
            var stock = new List<RewardCard>();
            for (int i = 0; i < 4; i++)
            {
                var card = RewardUtility.Generate(run, RunRewardKind.Cards).FirstOrDefault(c => stock.All(s => s.Title != c.Title));
                if (card != null) stock.Add(card);
            }
            run.NotifyNodeCompleted(node.tile);
            Find.WindowStack.Add(new Dialog_NodeTree(ShopNode(stock, caravan), title: node.preset.LabelCap));
            return stock;
        }

        private static DiaNode ShopNode(List<RewardCard> stock, Caravan caravan)
        {
            var root = new DiaNode($"A trader's wagon waits by the road. Your caravan carries {Silver(caravan)} silver.");
            foreach (var card in stock)
            {
                int price = Price(card);
                var opt = new DiaOption($"Buy {card.KindLabel.ToLower()}: {card.Title} ({price} silver)");
                opt.action = () => Buy(stock, card, caravan);
                opt.linkLateBind = () => ShopNode(stock, caravan);
                if (Silver(caravan) < price) opt.Disable("not enough silver");
                root.options.Add(opt);
            }
            root.options.Add(new DiaOption("Leave") { resolveTree = true });
            return root;
        }

        public static bool Buy(List<RewardCard> stock, RewardCard card, Caravan caravan)
        {
            if (!stock.Contains(card) || !TrySpend(caravan, Price(card))) return false;
            stock.Remove(card);
            RewardUtility.Apply(card, null, caravan);
            return true;
        }
    }
}
