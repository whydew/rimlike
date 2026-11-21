using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Roguelike
{
    /// <summary>
    /// Full-screen pick-1 card screen. Cards are dealt from a deck below the screen with a little overshoot and spin,
    /// float gently while you decide, and lift and glow with a light sweep under the cursor. On pick, the chosen card
    /// flashes, flips and bursts into sparks while the rest fall away. RimWorld's UI is immediate-mode, so every
    /// frame's transform is computed from the time since the screen opened.
    /// </summary>
    public class Dialog_RewardPick : Window
    {
        internal const float CardWidth = 236f;
        internal const float CardHeight = 392f;
        private const float CardGap = 30f;
        private const float DealDuration = 0.55f;
        private const float DealStagger = 0.11f;
        private const float ExitDuration = 0.75f;

        private readonly List<RewardCard> cards;
        private readonly string subtitle;
        private readonly Action<RewardCard> onPicked;
        private readonly bool allowSkip;
        private readonly string title;
        private readonly float[] hover;
        private readonly float[] hoverStart;
        private readonly List<Style.Particle> particles = new List<Style.Particle>();
        private float openedAt = -1f;
        private int picked = -1;
        private bool skipped;
        private float pickedAt;
        private bool burst;
        private bool resolved;

        public Dialog_RewardPick(List<RewardCard> cards, string subtitle, Action<RewardCard> onPicked, bool allowSkip = true, string title = "Choose a reward")
        {
            this.allowSkip = allowSkip;
            this.title = title;
            this.cards = cards;
            this.subtitle = subtitle;
            this.onPicked = onPicked;
            hover = new float[cards.Count];
            hoverStart = new float[cards.Count];
            forcePause = true;
            absorbInputAroundWindow = true;
            doCloseX = false;
            closeOnClickedOutside = false;
            closeOnCancel = false;
            closeOnAccept = false;
            doWindowBackground = false;
            drawShadow = false;
        }

        public IReadOnlyList<RewardCard> Cards => cards;

        public override Vector2 InitialSize => new Vector2(UI.screenWidth, UI.screenHeight);

        protected override float Margin => 0f;

        private static float Now => Style.Now;

        /// <summary>Picks a card as if the player clicked it (used by the self-test).</summary>
        public void Pick(int index)
        {
            if (picked >= 0 || skipped || index < 0 || index >= cards.Count) return;
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
            bool leaving = picked >= 0 || skipped;
            float exit = leaving ? Mathf.Clamp01((Now - pickedAt) / ExitDuration) : 0f;

            // Backdrop: darken and vignette the game, fading in.
            float bg = Style.EaseOutCubic(t / 0.35f) * (1f - Style.EaseInCubic(exit));
            Style.Box(inRect, new Color(0.02f, 0.02f, 0.025f, 0.7f * bg));
            Style.Tex(inRect.ExpandedBy(inRect.width * 0.12f), Style.Vignette, new Color(0f, 0f, 0f, 0.9f * bg));

            float totalWidth = cards.Count * CardWidth + (cards.Count - 1) * CardGap;
            float x0 = inRect.center.x - totalWidth / 2f;
            float y0 = inRect.center.y - CardHeight / 2f + 20f;

            // Title drops in with a golden glow behind it, then a rule line draws out.
            float titleIn = Style.EaseOutCubic((t - 0.05f) / 0.45f) * (1f - exit);
            var titleRect = new Rect(inRect.x, y0 - 120f - (1f - titleIn) * 14f, inRect.width, 44f);
            Style.GlowAround(new Rect(inRect.center.x - 240f, titleRect.y + 4f, 480f, 36f), new Color(Style.Gold.r, Style.Gold.g, Style.Gold.b, 0.16f * titleIn), 1.6f, Style.SoftGlow);
            var oldColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, titleIn);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Style.ShadowLabel(titleRect, title, Style.Gold);
            Text.Font = GameFont.Small;
            Style.ShadowLabel(new Rect(inRect.x, y0 - 78f, inRect.width, 26f), subtitle, new Color(Style.Parchment.r, Style.Parchment.g, Style.Parchment.b, 0.8f));
            float rule = Style.EaseOutCubic((t - 0.25f) / 0.5f);
            Style.Box(new Rect(inRect.center.x - 170f * rule, y0 - 48f, 340f * rule, 1f), new Color(Style.Gold.r, Style.Gold.g, Style.Gold.b, 0.55f));
            Style.Tex(new Rect(inRect.center.x - 4f, y0 - 52f, 8f, 8f), Style.Glow, new Color(Style.Gold.r, Style.Gold.g, Style.Gold.b, rule));
            GUI.color = oldColor;
            Text.Anchor = TextAnchor.UpperLeft;

            var deck = new Vector2(inRect.center.x, inRect.yMax + CardHeight * 0.6f);
            int hovered = -1;
            for (int i = 0; i < cards.Count; i++)
                if (hover[i] > 0.5f) hovered = i;

            for (int i = 0; i < cards.Count; i++)
            {
                var slot = new Rect(x0 + i * (CardWidth + CardGap), y0, CardWidth, CardHeight);
                float fan = i - (cards.Count - 1) / 2f;

                // Deal in from the deck: fly up, spin into place with a little overshoot, fade in.
                float deal = Mathf.Clamp01((t - 0.15f - i * DealStagger) / DealDuration);
                Vector2 center = Vector2.LerpUnclamped(deck, slot.center, Style.EaseOutBack(deal));
                float spin = (1f - Style.EaseOutCubic(deal)) * fan * 16f;
                float alpha = Style.EaseOutCubic(deal * 1.6f);
                float scale = Mathf.Lerp(0.7f, 1f, Style.EaseOutCubic(deal));

                // Idle float once dealt.
                bool dealt = deal >= 1f;
                if (dealt)
                {
                    center.y += Mathf.Sin(Now * 1.6f + i * 1.3f) * 3f;
                    spin += Mathf.Sin(Now * 1.1f + i * 2.1f) * 0.6f;
                }

                // Hover: lift, grow, straighten, glow, sweep a highlight across.
                bool interactive = dealt && !leaving;
                bool over = interactive && Mouse.IsOver(slot);
                if (over && hover[i] <= 0.001f)
                {
                    hoverStart[i] = Now;
                    SoundDefOf.Mouseover_Standard.PlayOneShotOnCamera();
                }
                if (Event.current.type == EventType.Repaint)
                    hover[i] = Mathf.MoveTowards(hover[i], over ? 1f : 0f, Time.unscaledDeltaTime * 7f);
                float h = Style.EaseOutCubic(hover[i]);
                center.y -= 18f * h;
                scale *= 1f + 0.07f * h;
                spin *= 1f - h;
                if (!leaving && hovered >= 0 && hovered != i) alpha *= 0.8f; // let the hovered card stand out

                // Exit: the picked card rises to the middle, flashes and flips away; the others drop spinning.
                float flip = 1f, flash = 0f;
                if (leaving)
                {
                    if (i == picked)
                    {
                        float up = Style.EaseOutCubic(exit * 1.8f);
                        center = Vector2.Lerp(center, new Vector2(inRect.center.x, inRect.center.y - 10f), up);
                        scale *= 1f + 0.18f * up;
                        flash = Mathf.Clamp01(1f - Mathf.Abs(exit - 0.45f) / 0.15f);
                        flip = exit < 0.5f ? 1f : Mathf.Cos((exit - 0.5f) / 0.5f * Mathf.PI * 0.5f);
                        if (!burst && exit >= 0.45f)
                        {
                            burst = true;
                            Style.Burst(particles, center, cards[i].KindColor, 52);
                        }
                    }
                    else
                    {
                        float fall = Style.EaseInCubic(exit * 1.2f);
                        center.y += fall * 520f;
                        spin += fall * (picked < 0 ? fan * 20f : (i < picked ? -28f : 28f));
                        alpha *= 1f - Style.EaseOutCubic(exit * 1.3f);
                    }
                }

                if (alpha <= 0.01f || flip <= 0.01f) continue;
                var r = new Rect(center.x - CardWidth * scale * flip / 2f, center.y - CardHeight * scale / 2f, CardWidth * scale * flip, CardHeight * scale);
                var matrix = GUI.matrix;
                if (Mathf.Abs(spin) > 0.01f) GUIUtility.RotateAroundPivot(spin, r.center);
                GUI.color = new Color(1f, 1f, 1f, alpha);
                if (flip > 0.35f) DrawCard(r, cards[i], h, Now - hoverStart[i], flash, i);
                else DrawCardBack(r, cards[i].KindColor);
                GUI.color = Color.white;
                GUI.matrix = matrix;

                if (interactive && Widgets.ButtonInvisible(slot)) Pick(i);
            }

            Style.UpdateAndDraw(particles);

            // Skip button fades in once the cards are down.
            if (allowSkip && !leaving)
            {
                var skipRect = new Rect(inRect.center.x - 90f, y0 + CardHeight + 44f, 180f, 38f);
                GUI.color = new Color(1f, 1f, 1f, Style.EaseOutCubic((t - 0.6f) / 0.4f));
                if (Widgets.ButtonText(skipRect, "Skip reward")) Skip();
                GUI.color = Color.white;
            }

            if (leaving && !resolved && exit >= 1f)
            {
                resolved = true;
                var choice = picked >= 0 ? cards[picked] : null;
                RewardUtility.DiscardUnpicked(cards, choice);
                onPicked?.Invoke(choice);
                Close();
            }
        }

        private static Texture2D researchBadge, researchFrame;
        private static Texture2D ResearchBadge => researchBadge ??= ContentFinder<Texture2D>.Get("Roguelike/Unlocks/Research", reportFailure: false);
        private static Texture2D ResearchFrame => researchFrame ??= ContentFinder<Texture2D>.Get("Roguelike/Unlocks/ResearchFrame", reportFailure: false);

        /// <summary>What a research project unlocks that has an icon (a building, an item), for its card art.</summary>
        private static BuildableDef ResearchArt(ResearchProjectDef r) =>
            r.UnlockedDefs.OfType<BuildableDef>().Where(d => d.uiIcon != null && d.uiIcon != BaseContent.BadTex)
                .OrderBy(d => d is ThingDef { category: ThingCategory.Building } ? 0 : 1).FirstOrDefault(); // buildings first: a smithy says more than an axe

        private static bool IsRare(RewardCard card) =>
            card.kind == RewardKind.Relic && card.relic.rarity == RelicRarity.Rare || card.kind == RewardKind.Path && card.pathNode.IsBoss;

        internal static void DrawCard(Rect r, RewardCard card, float hover, float hoverAge, float flash, int index)
        {
            float a = GUI.color.a;
            var kind = card.KindColor;
            bool rare = IsRare(card);

            // Drop shadow and coloured aura; rare cards breathe.
            Style.Tex(new Rect(r.x + 10f, r.y + 22f, r.width, r.height).ExpandedBy(20f), Style.SoftGlow, new Color(0f, 0f, 0f, 0.7f));
            float aura = 0.1f + 0.4f * hover + (rare ? 0.18f * Style.Pulse(2.4f, index) : 0f);
            Style.GlowAround(r, new Color(kind.r, kind.g, kind.b, aura), 1.3f, Style.SoftGlow);

            // Body, frame and inner bevel.
            Style.Tex(r, Style.CardBody, Color.white);
            var frame = Color.Lerp(kind * 0.8f, Color.white, 0.3f * hover);
            Style.Border(r, new Color(frame.r, frame.g, frame.b, 1f), 2f);
            Style.Border(r.ContractedBy(5f), new Color(kind.r, kind.g, kind.b, 0.25f), 1f);
            Style.Box(new Rect(r.x + 2f, r.y + 2f, r.width - 4f, 1f), new Color(1f, 1f, 1f, 0.12f)); // top highlight

            // Banner with the card's kind.
            var banner = new Rect(r.x + 2f, r.y + 2f, r.width - 4f, 28f);
            Style.Box(banner, new Color(kind.r * 0.45f, kind.g * 0.45f, kind.b * 0.45f, 0.95f));
            Style.Tex(banner, Style.BannerFade, new Color(kind.r, kind.g, kind.b, 0.55f));
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Style.ShadowLabel(banner, card.KindLabel.ToUpperInvariant(), new Color(1f, 0.97f, 0.9f));

            // Recruits use the same layout as the squad cards at the start of a run.
            if (card.kind == RewardKind.Recruit)
            {
                Text.Anchor = TextAnchor.UpperLeft;
                Page_RoguelikePawns.DrawPawn(new Rect(r.x + 10f, banner.yMax + 8f, r.width - 20f, r.yMax - banner.yMax - 16f), card.recruit, kind);
                TooltipHandler.TipRegion(r, () => Page_RoguelikePawns.Tooltip(card.recruit), card.recruit.thingIDNumber);
                if (hover > 0.05f) Style.SheenSweep(r, Mathf.Repeat(hoverAge * 0.8f, 1.7f), 0.2f * hover);
                if (flash > 0f) Style.Box(r, new Color(1f, 1f, 1f, 0.8f * flash));
                return;
            }

            // Title.
            Text.Font = GameFont.Small;
            var titleRect = new Rect(r.x + 10f, banner.yMax + 6f, r.width - 20f, 42f);
            Style.ShadowLabel(titleRect, card.Title, Style.Parchment);
            Text.Anchor = TextAnchor.UpperLeft;

            // Art on a glowing plinth, bobbing gently.
            float bob = Mathf.Sin(Style.Now * 2f + index) * 2f;
            float artSize = card.kind == RewardKind.Path ? 60f : 100f; // node cards need the room for their objective
            var art = new Rect(r.center.x - artSize / 2f, titleRect.yMax + 4f + bob, artSize, artSize);
            Style.GlowAround(art, new Color(kind.r, kind.g, kind.b, 0.3f + 0.3f * hover), 1.8f);
            DrawArt(art, card);

            // Ornamental divider.
            float dy = titleRect.yMax + artSize + 16f;
            Style.Box(new Rect(r.x + 24f, dy, r.width - 48f, 1f), new Color(kind.r, kind.g, kind.b, 0.5f));
            Style.Tex(new Rect(r.center.x - 6f, dy - 6f, 12f, 12f), Style.Glow, new Color(kind.r, kind.g, kind.b, 0.9f));

            // Body text.
            var body = new Rect(r.x + 12f, dy + 8f, r.width - 24f, r.yMax - dy - 14f);
            Text.Font = GameFont.Tiny;
            var old = GUI.color;
            GUI.color = new Color(0.88f, 0.86f, 0.8f, a);
            string text = BodyText(card);
            if (card.kind == RewardKind.Path)
            {
                DrawPathBody(body, card.pathNode, kind, a);
                TooltipHandler.TipRegion(r, text); // the full briefing
            }
            else
            {
                Widgets.Label(body, text);
                if (Text.CalcHeight(text, body.width) > body.height) TooltipHandler.TipRegion(r, text); // full text when it doesn't fit
            }
            GUI.color = old;
            Text.Font = GameFont.Small;

            // Light sweep on hover, and the pick flash.
            if (hover > 0.05f) Style.SheenSweep(r, Mathf.Repeat(hoverAge * 0.8f, 1.7f), 0.2f * hover);
            if (flash > 0f) Style.Box(r, new Color(1f, 1f, 1f, 0.8f * flash));
        }

        /// <summary>
        /// A node card's text in sections: a short description, the objective as bullets, the weather, any run-ending
        /// danger, and the reward pinned to the bottom. Bullets that don't fit become "+N more" (the tooltip has it all).
        /// </summary>
        private static void DrawPathBody(Rect body, RunNode node, Color kind, float a)
        {
            var p = node.preset;
            var dim = new Color(0.8f, 0.78f, 0.72f, a);
            var text = new Color(0.92f, 0.9f, 0.85f, a);
            var head = new Color(Mathf.Lerp(kind.r, 1f, 0.35f), Mathf.Lerp(kind.g, 1f, 0.35f), Mathf.Lerp(kind.b, 1f, 0.35f), a);
            Text.Font = GameFont.Tiny;

            // Reward, pinned to the bottom in gold.
            var reward = new Rect(body.x - 2f, body.yMax - 34f, body.width + 4f, 34f);
            Style.Box(reward, new Color(Style.Gold.r * 0.35f, Style.Gold.g * 0.35f, Style.Gold.b * 0.35f, 0.6f * a));
            Style.Box(new Rect(reward.x, reward.y, reward.width, 1f), new Color(Style.Gold.r, Style.Gold.g, Style.Gold.b, 0.6f * a));
            Style.ShadowLabel(new Rect(reward.x + 6f, reward.y + 1f, reward.width, 14f), "REWARD", new Color(Style.Gold.r, Style.Gold.g, Style.Gold.b, a));
            Style.ShadowLabel(new Rect(reward.x + 6f, reward.y + 15f, reward.width - 8f, 18f), node.RewardShort, new Color(1f, 0.95f, 0.82f, a));

            float y = body.y, bottom = reward.y - 4f;
            // Description: at most two lines.
            string desc = p.description;
            float dh = Mathf.Min(Text.CalcHeight(desc, body.width), 30f);
            GUI.color = dim;
            Widgets.Label(new Rect(body.x, y, body.width, dh), desc);
            y += dh + 6f;

            // Leave room for the weather and danger lines below the objective.
            string weather = p.WeatherLine(node.Weather), danger = p.DangerLine;
            float tail = (weather != null ? 18f + Text.CalcHeight(weather, body.width - 6f) : 0f) + (danger != null ? Text.CalcHeight(danger, body.width) + 2f : 0f);

            Style.ShadowLabel(new Rect(body.x, y, body.width, 14f), p.HasMap ? "OBJECTIVE" : "ON ARRIVAL", head);
            y += 14f;
            var lines = p.ObjectiveLines(3);
            for (int i = 0; i < lines.Count; i++)
            {
                string line = "• " + lines[i];
                float lh = Text.CalcHeight(line, body.width - 6f);
                bool last = i == lines.Count - 1;
                if (y + lh + (last ? 0f : 14f) > bottom - tail)
                {
                    GUI.color = dim;
                    Widgets.Label(new Rect(body.x + 4f, y, body.width - 6f, 14f), $"+{lines.Count - i} more (hover for all)");
                    y += 14f;
                    break;
                }
                GUI.color = text;
                Widgets.Label(new Rect(body.x + 4f, y, body.width - 6f, lh), line);
                y += lh;
            }
            y += 4f;

            if (weather != null)
            {
                Style.ShadowLabel(new Rect(body.x, y, body.width, 14f), "WEATHER", head);
                y += 14f;
                float wh = Text.CalcHeight(weather, body.width - 6f);
                GUI.color = text;
                Widgets.Label(new Rect(body.x + 4f, y, body.width - 6f, wh), weather);
                y += wh + 4f;
            }
            if (danger != null)
            {
                GUI.color = new Color(1f, 0.5f, 0.45f, a);
                Widgets.Label(new Rect(body.x, y, body.width, Text.CalcHeight(danger, body.width)), danger);
            }
            GUI.color = new Color(1f, 1f, 1f, a);
        }

        private static void DrawCardBack(Rect r, Color kind)
        {
            Style.Tex(r, Style.CardBody, Color.white);
            Style.Box(r, new Color(kind.r * 0.5f, kind.g * 0.5f, kind.b * 0.5f, 0.6f));
            Style.Border(r, new Color(kind.r, kind.g, kind.b, 1f), 2f);
            Style.GlowAround(r, new Color(kind.r, kind.g, kind.b, 0.6f), 0.6f);
        }

        private static void DrawArt(Rect art, RewardCard card)
        {
            var tint = GUI.color;
            switch (card.kind)
            {
                case RewardKind.Recruit:
                    GUI.DrawTexture(art, PortraitsCache.Get(card.recruit, art.size, Rot4.South, new Vector3(0f, 0f, 0.1f), 1.5f));
                    break;
                case RewardKind.Research:
                    var unlocked = ResearchArt(card.research);
                    if (unlocked != null && ResearchFrame != null)
                    {
                        GUI.DrawTexture(art, ResearchFrame);
                        Widgets.DefIcon(art.ContractedBy(20f), unlocked, unlocked is ThingDef td ? GenStuff.DefaultStuffFor(td) : null, color: tint);
                        GUI.color = tint;
                    }
                    else if (ResearchBadge != null) GUI.DrawTexture(art, ResearchBadge);
                    break;
                case RewardKind.Path when NodeIcons.For(card.pathNode) != null:
                    GUI.DrawTexture(art, NodeIcons.For(card.pathNode));
                    break;
                case RewardKind.Relic when card.relic.iconThing != null:
                    Widgets.DefIcon(art, card.relic.iconThing, color: tint);
                    break;
                case RewardKind.Gear:
                case RewardKind.Implant:
                    Widgets.DefIcon(art, card.gear.thing, card.gear.stuff, color: tint);
                    break;
                case RewardKind.Blueprint when card.blueprint.buildings.Count > 0:
                    Widgets.DefIcon(art, card.blueprint.buildings[0], GenStuff.DefaultStuffFor(card.blueprint.buildings[0]), color: tint);
                    break;
            }
            GUI.color = tint;
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
                case RewardKind.Research:
                    var unlocks = card.research.UnlockedDefs.Where(d => d is BuildableDef || d is RecipeDef).Select(d => d.label).Distinct().Take(4).ToList();
                    return "Completes this research right away." + (unlocks.Count > 0 ? "\nUnlocks: " + string.Join(", ", unlocks) + "." : "")
                           + "\n\n" + card.research.description.Split('\n')[0];
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
