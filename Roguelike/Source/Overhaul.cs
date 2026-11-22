using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Roguelike
{
    public enum LevelUpKind { Trait, Passion, Skill, Cure }

    /// <summary>
    /// Level-ups: the payout for stretch goals. Each card upgrades one squad member: a good trait, a passion, a skill
    /// jump, or the removal of a bad trait. The squad is the deck; level-ups are how it grows.
    /// </summary>
    public static class LevelUps
    {
        public const int SkillJump = 4;

        /// <summary>Traits a level-up can grant (defName, degree).</summary>
        private static readonly (string def, int degree)[] Good =
        {
            ("Tough", 0), ("Industriousness", 1), ("Industriousness", 2), ("FastLearner", 0), ("Nimble", 0), ("QuickSleeper", 0),
            ("Kind", 0), ("NaturalMood", 1), ("NaturalMood", 2), ("SpeedOffset", 1), ("SpeedOffset", 2), ("Nerves", 1), ("Nerves", 2),
            ("ShootingAccuracy", 1),
        };

        /// <summary>Traits a level-up can remove (degree null = any).</summary>
        private static readonly (string def, int? degree)[] Bad =
        {
            ("Pyromaniac", null), ("Wimp", null), ("SlowLearner", null), ("Abrasive", null), ("Greedy", null), ("Jealous", null),
            ("Gourmand", null), ("AnnoyingVoice", null), ("CreepyBreathing", null), ("Nerves", -1), ("Nerves", -2),
            ("Industriousness", -1), ("Industriousness", -2), ("NaturalMood", -1), ("NaturalMood", -2), ("SpeedOffset", -1),
            ("DrugDesire", 1), ("DrugDesire", 2),
        };

        private static IEnumerable<Pawn> Squad =>
            PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists.Where(p => p.RaceProps.Humanlike && p.story != null && p.skills != null);

        public static bool IsCurse(Trait t) => t.sourceGene == null && Bad.Any(b => b.def == t.def.defName && (b.degree == null || b.degree == t.Degree));

        private static IEnumerable<(TraitDef def, int degree)> GainableTraits(Pawn p) =>
            Good.Select(g => (def: DefDatabase<TraitDef>.GetNamedSilentFail(g.def), g.degree))
                .Where(g => g.def != null && g.def.degreeDatas.Any(d => d.degree == g.degree) && g.def.disabledWorkTags == WorkTags.None
                            && !p.story.traits.HasTrait(g.def) && !p.story.traits.allTraits.Any(t => g.def.ConflictsWith(t)))
                .Where(_ => p.story.traits.allTraits.Count < 5);

        /// <summary>Up to <paramref name="count"/> different upgrades, each for a random squad member.</summary>
        public static List<RewardCard> Generate(int count)
        {
            var squad = Squad.ToList();
            var cards = new List<RewardCard>();
            for (int attempt = 0; attempt < count * 8 && cards.Count < count && squad.Count > 0; attempt++)
            {
                var card = Roll(squad.RandomElement());
                if (card != null && !cards.Any(c => c.levelPawn == card.levelPawn && c.levelKind == card.levelKind && c.trait == card.trait && c.skill == card.skill))
                    cards.Add(card);
            }
            return cards;
        }

        private static RewardCard Roll(Pawn p)
        {
            var options = new List<(LevelUpKind kind, float weight)>();
            var traits = GainableTraits(p).ToList();
            var passions = p.skills.skills.Where(s => !s.TotallyDisabled && s.passion != Passion.Major).ToList();
            var skills = p.skills.skills.Where(s => !s.TotallyDisabled && s.Level < SkillRecord.MaxLevel).ToList();
            var curses = p.story.traits.allTraits.Where(IsCurse).ToList();
            if (traits.Count > 0) options.Add((LevelUpKind.Trait, 30f));
            if (passions.Count > 0) options.Add((LevelUpKind.Passion, 25f));
            if (skills.Count > 0) options.Add((LevelUpKind.Skill, 30f));
            if (curses.Count > 0) options.Add((LevelUpKind.Cure, 25f));
            if (options.Count == 0) return null;
            var card = new RewardCard { kind = RewardKind.LevelUp, levelPawn = p, levelKind = options.RandomElementByWeight(o => o.weight).kind };
            switch (card.levelKind)
            {
                case LevelUpKind.Trait:
                    (card.trait, card.traitDegree) = traits.RandomElement();
                    break;
                case LevelUpKind.Passion:
                    card.skill = passions.OrderByDescending(s => s.Level).Take(4).RandomElement().def; // favour what they're already good at
                    break;
                case LevelUpKind.Skill:
                    card.skill = skills.OrderByDescending(s => (int)s.passion).ThenByDescending(s => s.Level).Take(4).RandomElement().def;
                    break;
                case LevelUpKind.Cure:
                    var curse = curses.RandomElement();
                    (card.trait, card.traitDegree) = (curse.def, curse.Degree);
                    break;
            }
            return card;
        }

        /// <summary>What the card does, in a line.</summary>
        public static string Describe(RewardCard c)
        {
            var p = c.levelPawn;
            switch (c.levelKind)
            {
                case LevelUpKind.Trait:
                    var data = c.trait.DataAtDegree(c.traitDegree);
                    return $"{p.LabelShortCap} gains the trait {data.LabelCap}.\n\n{data.description.Formatted(p.Named("PAWN")).AdjustedFor(p).Resolve()}";
                case LevelUpKind.Passion:
                    var rec = p.skills.GetSkill(c.skill);
                    return $"{p.LabelShortCap}'s passion for {c.skill.skillLabel} grows: {(rec.passion == Passion.None ? "interested" : "burning")}.\n\nSkill now: {rec.Level}.";
                case LevelUpKind.Skill:
                    var s = p.skills.GetSkill(c.skill);
                    return $"{p.LabelShortCap}'s {c.skill.skillLabel} rises from {s.Level} to {System.Math.Min(SkillRecord.MaxLevel, s.Level + SkillJump)}.";
                case LevelUpKind.Cure:
                    return $"{p.LabelShortCap} sheds the trait {c.trait.DataAtDegree(c.traitDegree).LabelCap}.";
            }
            return "";
        }

        public static string Short(RewardCard c) => c.levelKind switch
        {
            LevelUpKind.Trait => "New trait: " + c.trait.DataAtDegree(c.traitDegree).LabelCap,
            LevelUpKind.Passion => "Passion: " + c.skill.LabelCap,
            LevelUpKind.Skill => $"+{SkillJump} {c.skill.skillLabel}",
            LevelUpKind.Cure => "Lose trait: " + c.trait.DataAtDegree(c.traitDegree).LabelCap,
            _ => "",
        };

        public static void Apply(RewardCard c)
        {
            var p = c.levelPawn;
            if (p == null || p.Dead || p.story == null) return;
            switch (c.levelKind)
            {
                case LevelUpKind.Trait:
                    if (!p.story.traits.HasTrait(c.trait)) p.story.traits.GainTrait(new Trait(c.trait, c.traitDegree));
                    break;
                case LevelUpKind.Passion:
                    var rec = p.skills.GetSkill(c.skill);
                    if (rec.passion != Passion.Major) rec.passion = rec.passion == Passion.None ? Passion.Minor : Passion.Major;
                    break;
                case LevelUpKind.Skill:
                    var s = p.skills.GetSkill(c.skill);
                    s.Level = System.Math.Min(SkillRecord.MaxLevel, s.Level + SkillJump);
                    s.xpSinceLastLevel = 0f;
                    break;
                case LevelUpKind.Cure:
                    var t = p.story.traits.allTraits.FirstOrDefault(x => x.def == c.trait && x.Degree == c.traitDegree);
                    if (t != null) p.story.traits.RemoveTrait(t);
                    break;
            }
            Messages.Message($"Level up: {Short(c)} for {p.LabelShortCap}.", p, MessageTypeDefOf.PositiveEvent, false);
        }
    }

    /// <summary>
    /// Wear: what the road takes out of the squad. Every cleared node with a map adds some to everyone in the squad, and
    /// only a rest site's Rest clears it. It slows work and movement, and at its worst dulls the mind.
    /// </summary>
    public static class Wear
    {
        public const float PerNode = 0.1f;

        public static HediffDef Def => RoguelikeDefOf.Roguelike_Wear;

        public static void AddForNode(Map map)
        {
            if (map == null) return;
            foreach (var p in map.mapPawns.FreeColonistsSpawned.Where(p => p.RaceProps.Humanlike))
                HealthUtility.AdjustSeverity(p, Def, PerNode);
        }

        public static float Of(Pawn p) => p.health.hediffSet.GetFirstHediffOfDef(Def)?.Severity ?? 0f;

        /// <summary>Removes all wear; returns how many pawns had any.</summary>
        public static int Clear(IEnumerable<Pawn> pawns)
        {
            int n = 0;
            foreach (var p in pawns)
            {
                var h = p.health.hediffSet.GetFirstHediffOfDef(Def);
                if (h == null) continue;
                p.health.RemoveHediff(h);
                n++;
            }
            return n;
        }
    }
}
