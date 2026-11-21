using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Roguelike
{
    /// <summary>
    /// Pick-1 card screen. Cards fan in from below, grow on hover, and on pick the chosen card
    /// flies up while the others fade. RimWorld's UI has no animation system, so every frame's
    /// position, scale and alpha is computed from the time since the window opened.
    /// </summary>
    public class Dialog_RewardPick : Window
    {
        private const float CardWidth = 230f;
        private const float CardHeight = 380f;
        private const float CardGap = 26f;
        private const float EnterDuration = 0.35f;
        private const float EnterStagger = 0.09f;
        private const float ExitDuration = 0.4f;

        private readonly List<RewardCard> cards;
        private readonly string subtitle;
        private readonly Action<RewardCard> onPicked;
        private readonly bool allowSkip;
        private readonly string title;
        private readonly float[] hover;
        private float openedAt = -1f;
        private int picked = -1;
        private bool skipped;
        private float pickedAt;
        private bool resolved;

        public Dialog_RewardPick(List<RewardCard> cards, string subtitle, Action<RewardCard> onPicked, bool allowSkip = true, string title = "Choose a reward")
        {
            this.allowSkip = allowSkip;
            this.title = title;
            this.cards = cards;
            this.subtitle = subtitle;
            this.onPicked = onPicked;
            hover = new float[cards.Count];
            forcePause = true;
            absorbInputAroundWindow = true;
            doCloseX = false;
            closeOnClickedOutside = false;
            closeOnCancel = false;
            closeOnAccept = false;
            doWindowBackground = true;
        }

        public IReadOnlyList<RewardCard> Cards => cards;

        public override Vector2 InitialSize =>
            new Vector2(Mathf.Max(600f, cards.Count * (CardWidth + CardGap) + 60f), CardHeight + 190f);

        private static float Now => Time.realtimeSinceStartup;

        private static float EaseOut(float t) => 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);

        /// <summary>Picks a card as if the player clicked it (used by the self-test).</summary>
        public void Pick(int index)
        {
            if (picked >= 0 || skipped) return;
            picked = index;
            pickedAt = Now;
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        public void Skip()
        {
            if (picked >= 0 || skipped) return;
            skipped = true;
            pickedAt = Now;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (openedAt < 0f) openedAt = Now;
            float t = Now - openedAt;

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 36f), title);
            Text.Font = GameFont.Small;
            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            Widgets.Label(new Rect(inRect.x, inRect.y + 36f, inRect.width, 24f), subtitle);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            float totalWidth = cards.Count * CardWidth + (cards.Count - 1) * CardGap;
            float x0 = inRect.x + (inRect.width - totalWidth) / 2f;
            float y0 = inRect.y + 80f;

            for (int i = 0; i < cards.Count; i++)
            {
                var baseRect = new Rect(x0 + i * (CardWidth + CardGap), y0, CardWidth, CardHeight);

                // Fan in: slide up and fade in, one card after another.
                float enter = EaseOut((t - i * EnterStagger) / EnterDuration);
                float dy = (1f - enter) * 140f;
                float alpha = enter;
                float scale = 0.92f + 0.08f * enter;

                // Hover: grow a little.
                bool interactive = picked < 0 && !skipped && enter >= 1f;
                bool over = interactive && Mouse.IsOver(baseRect);
                hover[i] = Mathf.MoveTowards(hover[i], over ? 1f : 0f, Time.unscaledDeltaTime * 8f);
                scale *= 1f + 0.06f * hover[i];

                // Exit: the picked card flies up and fades, the rest fade out.
                if (picked >= 0 || skipped)
                {
                    float e = EaseOut((Now - pickedAt) / ExitDuration);
                    if (i == picked)
                    {
                        dy -= 180f * e;
                        scale *= 1f + 0.15f * e;
                        alpha *= 1f - e * e;
                    }
                    else alpha *= 1f - e;
                }

                var r = ScaleAround(baseRect, scale);
                r.y += dy;
                GUI.color = new Color(1f, 1f, 1f, alpha);
                DrawCard(r, cards[i], hover[i]);
                GUI.color = Color.white;

                if (interactive && Widgets.ButtonInvisible(baseRect)) Pick(i);
            }

            // Skip button.
            var skipRect = new Rect(inRect.center.x - 80f, inRect.yMax - 40f, 160f, 36f);
            if (allowSkip && picked < 0 && !skipped && Widgets.ButtonText(skipRect, "Skip reward")) Skip();

            if ((picked >= 0 || skipped) && !resolved && Now - pickedAt >= ExitDuration)
            {
                resolved = true;
                var choice = picked >= 0 ? cards[picked] : null;
                RewardUtility.DiscardUnpicked(cards, choice);
                onPicked?.Invoke(choice);
                Close();
            }
        }

        private static Rect ScaleAround(Rect r, float s)
        {
            var c = r.center;
            return new Rect(c.x - r.width * s / 2f, c.y - r.height * s / 2f, r.width * s, r.height * s);
        }

        private static void DrawCard(Rect r, RewardCard card, float hover)
        {
            float a = GUI.color.a;
            var kind = card.KindColor;

            // Body and border.
            Widgets.DrawBoxSolid(r, new Color(0.12f, 0.12f, 0.13f, 0.97f * a));
            var border = Color.Lerp(kind * 0.8f, Color.white, hover * 0.4f);
            border.a = a;
            Widgets.DrawBoxSolidWithOutline(r, Color.clear, border, 2);

            // Kind banner.
            var banner = new Rect(r.x, r.y, r.width, 26f);
            Widgets.DrawBoxSolid(banner, new Color(kind.r, kind.g, kind.b, 0.85f * a));
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(0f, 0f, 0f, a);
            Widgets.Label(banner, card.KindLabel.ToUpperInvariant());

            // Title.
            GUI.color = new Color(1f, 1f, 1f, a);
            var titleRect = new Rect(r.x + 8f, banner.yMax + 4f, r.width - 16f, 44f);
            Widgets.Label(titleRect, card.Title);
            Text.Anchor = TextAnchor.UpperLeft;

            // Art.
            var art = new Rect(r.center.x - 48f, titleRect.yMax + 2f, 96f, 96f);
            DrawArt(art, card);

            // Body text.
            var body = new Rect(r.x + 10f, art.yMax + 8f, r.width - 20f, r.yMax - art.yMax - 14f);
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.9f, 0.9f, 0.9f, a);
            Widgets.Label(body, BodyText(card));
            Text.Font = GameFont.Small;
            GUI.color = new Color(1f, 1f, 1f, a);
        }

        private static void DrawArt(Rect art, RewardCard card)
        {
            switch (card.kind)
            {
                case RewardKind.Recruit:
                    GUI.DrawTexture(art, PortraitsCache.Get(card.recruit, art.size, Rot4.South));
                    break;
                case RewardKind.Path when NodeIcons.For(card.pathNode) != null:
                    GUI.DrawTexture(art, NodeIcons.For(card.pathNode));
                    break;
                case RewardKind.Relic when card.relic.iconThing != null:
                    Widgets.DefIcon(art, card.relic.iconThing, color: GUI.color);
                    break;
                case RewardKind.Gear:
                case RewardKind.Implant:
                    Widgets.DefIcon(art, card.gear.thing, card.gear.stuff, color: GUI.color);
                    break;
                case RewardKind.Blueprint when card.blueprint.buildings.Count > 0:
                    Widgets.DefIcon(art, card.blueprint.buildings[0], GenStuff.DefaultStuffFor(card.blueprint.buildings[0]), color: GUI.color);
                    break;
            }
        }

        public static string BodyText(RewardCard card)
        {
            switch (card.kind)
            {
                case RewardKind.Relic:
                    return card.relic.description;
                case RewardKind.Gear:
                case RewardKind.Implant:
                    return (card.kind == RewardKind.Implant ? "Install with surgery. " : "") + card.gear.thing.description.Split('\n')[0];
                case RewardKind.Blueprint:
                    return "Adds to your build menu for this run:\n" + string.Join(", ", card.blueprint.buildings.Select(b => b.label));
                case RewardKind.Recruit:
                    return RecruitSummary(card.recruit);
                case RewardKind.Path:
                    return card.pathNode.RewardText + "\n" + card.pathNode.Briefing.Replace("\n\n", "\n");
            }
            return "";
        }

        /// <summary>Everything the player needs to judge a recruit: xenotype, backstory, traits, skills with passions, health.</summary>
        public static string RecruitSummary(Pawn p)
        {
            var sb = new StringBuilder();
            string xeno = ModsConfig.BiotechActive && p.genes != null ? p.genes.XenotypeLabelCap + ", " : "";
            sb.AppendLine($"{xeno}{p.gender.GetLabel().CapitalizeFirst()}, age {p.ageTracker.AgeBiologicalYears}");
            if (p.story?.Childhood != null) sb.AppendLine("Childhood: " + p.story.Childhood.TitleCapFor(p.gender));
            if (p.story?.Adulthood != null) sb.AppendLine("Adulthood: " + p.story.Adulthood.TitleCapFor(p.gender));
            if (p.story?.traits != null && p.story.traits.allTraits.Count > 0)
                sb.AppendLine("Traits: " + string.Join(", ", p.story.traits.allTraits.Select(t => t.LabelCap)));
            if (p.skills != null)
            {
                var skills = p.skills.skills
                    .OrderByDescending(s => s.TotallyDisabled ? -1 : s.Level)
                    .Select(s => s.TotallyDisabled
                        ? $"{s.def.skillLabel.CapitalizeFirst()} —"
                        : $"{s.def.skillLabel.CapitalizeFirst()} {s.Level}{new string('+', (int)s.passion)}");
                sb.AppendLine("Skills: " + string.Join(", ", skills));
            }
            var health = p.health.hediffSet.hediffs.Where(h => h.Visible).Select(h => h.LabelCap).Distinct().ToList();
            sb.Append("Health: " + (health.Count == 0 ? "healthy" : string.Join(", ", health)));
            return sb.ToString();
        }
    }
}
