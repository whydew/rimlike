using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Roguelike
{
    /// <summary>
    /// The vanilla scenario, ideoligion and starting-pawn pages are swapped for the mod's card screens as they open,
    /// keeping their place in the setup page chain (prev, next and the final start action).
    /// </summary>
    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Add))]
    public static class Patch_WindowStack_ReplaceSetupPages
    {
        public static void Prefix(ref Window window)
        {
            Page replacement = window switch
            {
                Page_SelectScenario => new Page_RoguelikeScenario(),
                Page_ChooseIdeoPreset when window.GetType() == typeof(Page_ChooseIdeoPreset) => new Page_RoguelikeIdeo(),
                Page_ConfigureStartingPawns when window.GetType() == typeof(Page_ConfigureStartingPawns) => new Page_RoguelikePawns(),
                Page_SelectStartingSite when window.GetType() == typeof(Page_SelectStartingSite) && ScenPart_RoguelikeRun.ActiveInCurrentScenario => new Page_RoguelikeLanding(),
                _ => null,
            };
            if (replacement == null || window is not Page old) return;
            replacement.prev = old.prev;
            replacement.next = old.next;
            replacement.nextAct = old.nextAct;
            if (old.prev != null && old.prev.next == old) old.prev.next = replacement;
            if (old.next != null && old.next.prev == old) old.next.prev = replacement;
            window = replacement;
        }
    }

    /// <summary>A full-screen setup page in the mod's style: backdrop, heading, content, and Back / Next buttons.</summary>
    public abstract class RoguelikePage : Page
    {
        protected float openedAt = -1f;

        protected RoguelikePage()
        {
            doWindowBackground = false;
            drawShadow = false;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize => new Vector2(UI.screenWidth, UI.screenHeight);

        protected override float Margin => 0f;

        protected float Age => Style.Now - openedAt;

        protected abstract string Heading { get; }
        protected abstract string Subheading { get; }
        protected virtual string NextLabel => "Next";
        protected virtual bool NextReady => true;

        protected abstract void DrawContent(Rect area);

        /// <summary>How dark the backdrop gets; lower lets the world show through.</summary>
        protected virtual float BackdropAlpha => 1f;

        public override void DoWindowContents(Rect inRect)
        {
            if (openedAt < 0f) openedAt = Style.Now;
            float t = Age;
            Style.Backdrop(inRect, BackdropAlpha * Style.EaseOutCubic(t / 0.35f));

            float titleIn = Style.EaseOutCubic((t - 0.05f) / 0.45f);
            var titleRect = new Rect(inRect.x, 38f - (1f - titleIn) * 14f, inRect.width, 44f);
            Style.GlowAround(new Rect(inRect.center.x - 260f, titleRect.y + 4f, 520f, 36f), new Color(Style.Gold.r, Style.Gold.g, Style.Gold.b, 0.16f * titleIn), 1.6f, Style.SoftGlow);
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, titleIn);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Style.ShadowLabel(titleRect, Heading, Style.Gold);
            Text.Font = GameFont.Small;
            Style.ShadowLabel(new Rect(inRect.x, titleRect.yMax + 2f, inRect.width, 24f), Subheading, new Color(Style.Parchment.r, Style.Parchment.g, Style.Parchment.b, 0.8f));
            float rule = Style.EaseOutCubic((t - 0.25f) / 0.5f);
            Style.Box(new Rect(inRect.center.x - 200f * rule, titleRect.yMax + 34f, 400f * rule, 1f), new Color(Style.Gold.r, Style.Gold.g, Style.Gold.b, 0.55f));
            GUI.color = old;
            Text.Anchor = TextAnchor.UpperLeft;

            var content = new Rect(inRect.x + 40f, titleRect.yMax + 50f, inRect.width - 80f, inRect.height - titleRect.yMax - 50f - 80f);
            DrawContent(content);

            // Bottom bar.
            var bar = new Rect(inRect.x + 40f, inRect.yMax - 64f, inRect.width - 80f, 44f);
            if (Style.Button(new Rect(bar.x, bar.y, 180f, 42f), "Back", Style.Parchment) && CanDoBack()) DoBack();
            if (Style.Button(new Rect(bar.xMax - 220f, bar.y, 220f, 42f), NextLabel, Style.Gold, NextReady) && CanDoNext()) DoNext();
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        /// <summary>Lays out <paramref name="count"/> cards in rows, as large as fits up to <paramref name="max"/>.</summary>
        protected static List<Rect> Grid(Rect area, int count, int columns, Vector2 max, float gap = 20f)
        {
            int rows = Mathf.CeilToInt(count / (float)columns);
            float w = Mathf.Min(max.x, (area.width - (columns - 1) * gap) / columns);
            float h = Mathf.Min(max.y, (area.height - (rows - 1) * gap) / rows);
            var rects = new List<Rect>();
            for (int i = 0; i < count; i++)
            {
                int row = i / columns, col = i % columns;
                int inRow = Mathf.Min(columns, count - row * columns);
                float rowWidth = inRow * w + (inRow - 1) * gap;
                float x0 = area.center.x - rowWidth / 2f;
                float y0 = area.center.y - (rows * h + (rows - 1) * gap) / 2f;
                rects.Add(new Rect(x0 + col * (w + gap), y0 + row * (h + gap), w, h));
            }
            return rects;
        }

        /// <summary>Card entrance: rise and fade in, one after another.</summary>
        protected Rect DealIn(Rect r, int index, out float alpha)
        {
            float a = Style.EaseOutCubic((Age - 0.2f - index * 0.06f) / 0.4f);
            alpha = a;
            return new Rect(r.x, r.y + (1f - Style.EaseOutBack(Mathf.Clamp01((Age - 0.2f - index * 0.06f) / 0.5f))) * 60f, r.width, r.height);
        }
    }

    /// <summary>Starter icons: each starter's unlock badge, or a fallback badge for the free starters.</summary>
    [StaticConstructorOnStartup]
    public static class StarterIcons
    {
        private static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        public static Texture2D For(string starterId)
        {
            if (starterId == null) return null;
            if (cache.TryGetValue(starterId, out var tex)) return tex;
            var unlock = DefDatabase<UnlockDef>.AllDefs.FirstOrDefault(u => u.starterId == starterId);
            string path = unlock != null ? "Roguelike/Unlocks/" + unlock.defName.Replace("Roguelike_Unlock_", "") : "Roguelike/Unlocks/Starter_" + starterId;
            return cache[starterId] = ContentFinder<Texture2D>.Get(path, reportFailure: false);
        }
    }

    /// <summary>Pick a starter: every available roguelike scenario as a card with a short description.</summary>
    public class Page_RoguelikeScenario : RoguelikePage
    {
        private static readonly Color StarterC = new Color(0.45f, 0.8f, 0.42f);

        /// <summary>New-run mode: called with the chosen scenario instead of starting the vanilla setup chain.</summary>
        public Action<Scenario> onChosen;
        public Scenario preselect;

        private List<Scenario> scenarios;
        private Scenario selected;
        private readonly Dictionary<Scenario, float> hover = new Dictionary<Scenario, float>();
        private Vector2 scroll;

        public IReadOnlyList<Scenario> Scenarios => scenarios;
        public Scenario Selected { get => selected; set => selected = value; }

        public override string PageTitle => "Choose your starter";
        protected override string Heading => "Choose your starter";
        protected override string Subheading => $"{scenarios.Count} starter{(scenarios.Count == 1 ? "" : "s")} available. Unlock more with meta points.";
        protected override string NextLabel => "Embark";
        protected override bool NextReady => selected != null;

        public override void PreOpen()
        {
            base.PreOpen();
            scenarios = ScenarioLister.ScenariosInCategory(ScenarioCategory.FromDef)
                .OrderBy(s => Part(s)?.starterId == "crashlanded" ? 0 : Part(s)?.requiresUnlock == true ? 2 : 1)
                .ThenBy(s => DisplayName(s)).ToList();
            selected = preselect != null && scenarios.Contains(preselect) ? preselect : null;
        }

        public static ScenPart_RoguelikeRun Part(Scenario s) => s.AllParts.OfType<ScenPart_RoguelikeRun>().FirstOrDefault();

        public static string DisplayName(Scenario s) => s.name.Replace("Roguelike: ", "");

        public static int PawnCount(Scenario s)
        {
            var part = s.AllParts.OfType<ScenPart_ConfigPage_ConfigureStartingPawnsBase>().FirstOrDefault();
            return part == null ? 0 : (int)AccessTools.Property(part.GetType(), "TotalPawnCount").GetValue(part);
        }

        protected override void DrawContent(Rect area)
        {
            int columns = Mathf.Clamp(Mathf.FloorToInt((area.width + 20f) / 260f), 2, 6);
            float cardW = Mathf.Min(250f, (area.width - 16f - (columns - 1) * 20f) / columns), cardH = 340f;
            int rows = Mathf.CeilToInt(scenarios.Count / (float)columns);
            float gridW = Mathf.Min(columns, scenarios.Count) * cardW + (Mathf.Min(columns, scenarios.Count) - 1) * 20f;
            var view = new Rect(0f, 0f, area.width - 16f, Mathf.Max(area.height, rows * (cardH + 20f) + 20f));
            // Few starters: centre them vertically too.
            float top = Mathf.Max(10f, (area.height - rows * (cardH + 20f)) / 2f);
            Widgets.BeginScrollView(area, ref scroll, view);
            for (int i = 0; i < scenarios.Count; i++)
            {
                var s = scenarios[i];
                var slot = new Rect(view.center.x - gridW / 2f + (i % columns) * (cardW + 20f), top + (i / columns) * (cardH + 20f), cardW, cardH);
                var r = DealIn(slot, i, out float alpha);
                if (alpha <= 0f) continue;
                bool over = Mouse.IsOver(slot);
                float h = hover.TryGetValue(s, out var hv) ? hv : 0f;
                if (Event.current.type == EventType.Repaint) hover[s] = h = Mathf.MoveTowards(h, over ? 1f : 0f, Time.unscaledDeltaTime * 8f);
                bool chosen = s == selected;
                r.y -= 8f * Style.EaseOutCubic(h) + (chosen ? 6f : 0f);

                var old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, alpha * (selected != null && !chosen ? 0.75f : 1f));
                var part = Part(s);
                var inner = Style.CardFrame(r, StarterC, h, chosen, part?.requiresUnlock == true ? "Unlocked starter" : "Starter", i);
                var icon = new Rect(r.center.x - 40f, inner.y + 4f, 80f, 80f);
                Style.GlowAround(icon, new Color(StarterC.r, StarterC.g, StarterC.b, 0.3f + 0.3f * h), 1.6f);
                var tex = StarterIcons.For(part?.starterId);
                if (tex != null) Style.Tex(icon, tex, Color.white);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperCenter;
                Style.ShadowLabel(new Rect(inner.x, icon.yMax + 8f, inner.width, 24f), DisplayName(s), Style.Parchment);
                Text.Font = GameFont.Tiny;
                int pawns = PawnCount(s);
                string facts = (pawns > 0 ? $"{pawns} colonist{(pawns == 1 ? "" : "s")}" : "") + (part?.starterRelic != null ? $" · {part.starterRelic.LabelCap}" : "");
                Style.ShadowLabel(new Rect(inner.x, icon.yMax + 32f, inner.width, 18f), facts, new Color(StarterC.r, StarterC.g, StarterC.b, 0.95f));
                Text.Anchor = TextAnchor.UpperLeft;
                var oldC = GUI.color;
                GUI.color = new Color(0.88f, 0.86f, 0.8f, GUI.color.a);
                Widgets.Label(new Rect(inner.x, icon.yMax + 56f, inner.width, inner.yMax - icon.yMax - 56f), s.description);
                GUI.color = oldC;
                if (h > 0.05f) Style.SheenSweep(r, Mathf.Repeat(Style.Now * 0.7f, 1.6f), 0.15f * h);
                GUI.color = old;
                Text.Font = GameFont.Small;

                if (Widgets.ButtonInvisible(slot))
                {
                    if (selected == s && Event.current.clickCount >= 2 && CanDoNext()) DoNext();
                    selected = s;
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                }
            }
            Widgets.EndScrollView();
        }

        protected override bool CanDoNext() => base.CanDoNext() && selected != null;

        protected override void DoNext()
        {
            if (onChosen != null)
            {
                var chosen = selected;
                Close();
                onChosen(chosen);
                return;
            }
            Page_SelectScenario.BeginScenarioConfiguration(selected, this);
            base.DoNext();
        }
    }

    /// <summary>
    /// Pick the first node: the starting row of act 1 as path cards (objective, weather, reward), with the planet
    /// behind. Replaces vanilla's landing-site page during a run.
    /// </summary>
    public class Page_RoguelikeLanding : RoguelikePage
    {
        private List<RunNode> starts;
        private List<RewardCard> cards;
        private float[] hover, hoverStart;
        private int selected = -1;

        public IReadOnlyList<RunNode> Starts => starts;
        public int Selected { get => selected; set => selected = value; }

        public override string PageTitle => "Choose where to land";
        protected override string Heading => "Choose where to land";
        protected override string Subheading => "Your first node. The rest of the run branches out from here.";
        protected override string NextLabel => "Land here";
        protected override bool NextReady => selected >= 0;
        protected override float BackdropAlpha => 0.6f;

        public override void PreOpen()
        {
            base.PreOpen();
            starts = WorldComponent_RoguelikeRun.Get?.Row(0).ToList() ?? new List<RunNode>();
            cards = starts.Select(n => new RewardCard { kind = RewardKind.Path, pathNode = n }).ToList();
            hover = new float[starts.Count];
            hoverStart = new float[starts.Count];
            // Show the planet behind the cards, centred on the starting row.
            Find.World.renderer.wantedMode = RimWorld.Planet.WorldRenderMode.Planet;
            Find.WorldInterface.Reset();
        }

        public override void PostOpen()
        {
            base.PostOpen();
            var middle = starts.Skip(starts.Count / 2).FirstOrDefault();
            if (middle != null) Find.WorldCameraDriver.JumpTo(Find.WorldGrid.GetTileCenter(middle.tile));
        }

        public override void PostClose()
        {
            base.PostClose();
            Find.World.renderer.wantedMode = RimWorld.Planet.WorldRenderMode.None;
        }

        protected override void DrawContent(Rect area)
        {
            var rects = Grid(area, cards.Count, Mathf.Max(1, cards.Count), new Vector2(Dialog_RewardPick.CardWidth, Dialog_RewardPick.CardHeight), 34f);
            for (int i = 0; i < cards.Count; i++)
            {
                var r = DealIn(rects[i], i, out float alpha);
                if (alpha <= 0f) continue;
                bool over = Mouse.IsOver(rects[i]);
                if (over && hover[i] <= 0.001f) hoverStart[i] = Style.Now;
                if (Event.current.type == EventType.Repaint) hover[i] = Mathf.MoveTowards(hover[i], over ? 1f : 0f, Time.unscaledDeltaTime * 7f);
                float h = Style.EaseOutCubic(hover[i]);
                bool chosen = i == selected;
                r.y -= 14f * h + (chosen ? 10f : 0f);
                var old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, alpha * (selected >= 0 && !chosen ? 0.75f : 1f));
                if (chosen) Style.GlowAround(r, new Color(Style.Gold.r, Style.Gold.g, Style.Gold.b, 0.45f + 0.2f * Style.Pulse(2.6f)), 1.2f, Style.SoftGlow);
                Dialog_RewardPick.DrawCard(r, cards[i], h, Style.Now - hoverStart[i], 0f, i);
                if (chosen) Style.Border(r, Style.Gold, 3f);
                GUI.color = old;
                if (Widgets.ButtonInvisible(rects[i]))
                {
                    if (selected == i && Event.current.clickCount >= 2 && CanDoNext()) DoNext();
                    selected = i;
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                }
            }
        }

        protected override bool CanDoNext() => base.CanDoNext() && selected >= 0;

        protected override void DoNext()
        {
            var tile = starts[selected].tile;
            Find.WorldInterface.SelectedTile = tile;
            Find.GameInitData.startingTile = tile;
            base.DoNext();
        }
    }

    /// <summary>Pick one of three freshly generated ideoligions.</summary>
    public class Page_RoguelikeIdeo : RoguelikePage
    {
        private readonly List<Ideo> options = new List<Ideo>();
        private readonly float[] hover = new float[3];
        private int selected = -1;

        public IReadOnlyList<Ideo> Options => options;
        public int Selected { get => selected; set => selected = value; }

        public override string PageTitle => "Choose your ideoligion";
        protected override string Heading => "Choose your ideoligion";
        protected override string Subheading => "Your squad shares one belief system for the whole run.";
        protected override string NextLabel => "Adopt";
        protected override bool NextReady => selected >= 0;

        public override void PostOpen()
        {
            base.PostOpen();
            // As the vanilla page does: give every other faction its ideoligion before the player's is chosen.
            Find.IdeoManager.classicMode = false;
            foreach (var faction in Find.FactionManager.AllFactions)
            {
                if (faction == Faction.OfPlayer || faction.ideos == null || faction.ideos.PrimaryIdeo == null || !faction.ideos.PrimaryIdeo.memes.NullOrEmpty()) continue;
                var def = faction.def;
                faction.ideos.ChooseOrGenerateIdeo(def.fixedIdeo
                    ? new IdeoGenerationParms(def, forceNoExpansionIdeo: false, null, null, name: def.ideoName, styles: def.styles, deities: def.deityPresets, hidden: def.hiddenIdeo, description: def.ideoDescription, forcedMemes: def.forcedMemes, classicExtra: false, forceNoWeaponPreference: false, forNewFluidIdeo: false, fixedIdeo: true, requiredPreceptsOnly: def.requiredPreceptsOnly)
                    : new IdeoGenerationParms(def));
            }
            Generate();
        }

        /// <summary>Three ideoligions, each built on a different structure meme so the choice is a real one.</summary>
        private void Generate()
        {
            options.Clear();
            var used = new HashSet<MemeDef>();
            for (int attempt = 0; attempt < 30 && options.Count < 3; attempt++)
            {
                var ideo = IdeoGenerator.GenerateIdeo(new IdeoGenerationParms(Find.FactionManager.OfPlayer.def));
                var structure = ideo.memes.FirstOrDefault(m => m.category == MemeCategory.Structure);
                if (structure != null && used.Contains(structure) && attempt < 20) continue;
                if (structure != null) used.Add(structure);
                options.Add(ideo);
            }
        }

        protected override void DrawContent(Rect area)
        {
            var rects = Grid(area, options.Count, 3, new Vector2(380f, 560f), 36f);
            for (int i = 0; i < options.Count; i++)
            {
                var ideo = options[i];
                var r = DealIn(rects[i], i, out float alpha);
                if (alpha <= 0f) continue;
                bool over = Mouse.IsOver(rects[i]);
                if (Event.current.type == EventType.Repaint) hover[i] = Mathf.MoveTowards(hover[i], over ? 1f : 0f, Time.unscaledDeltaTime * 8f);
                float h = Style.EaseOutCubic(hover[i]);
                bool chosen = i == selected;
                r.y -= 10f * h + (chosen ? 8f : 0f);
                var color = Color.Lerp(ideo.Color, Color.white, 0.25f);

                var old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, alpha * (selected >= 0 && !chosen ? 0.75f : 1f));
                var inner = Style.CardFrame(r, color, h, chosen, ideo.culture?.LabelCap ?? "Ideoligion", i);

                var icon = new Rect(r.center.x - 48f, inner.y + 6f, 96f, 96f);
                Style.GlowAround(icon, new Color(color.r, color.g, color.b, 0.45f + 0.25f * h), 1.8f);
                Style.Tex(icon, ideo.Icon, ideo.Color);
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.UpperCenter;
                Style.ShadowLabel(new Rect(inner.x, icon.yMax + 6f, inner.width, 34f), ideo.name.Truncate(inner.width), Style.Parchment);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;

                // Memes with their icons.
                float y = icon.yMax + 46f;
                foreach (var meme in ideo.memes)
                {
                    var row = new Rect(inner.x + 6f, y, inner.width - 12f, 30f);
                    Style.Tex(new Rect(row.x, row.y, 28f, 28f), meme.Icon, Color.white);
                    Text.Anchor = TextAnchor.MiddleLeft;
                    Style.ShadowLabel(new Rect(row.x + 36f, row.y, row.width - 36f, 28f), meme.LabelCap, meme.category == MemeCategory.Structure ? Style.Gold : Style.Parchment);
                    TooltipHandler.TipRegion(row, meme.description);
                    y += 32f;
                }
                Text.Anchor = TextAnchor.UpperLeft;

                // A few key beliefs.
                Style.Box(new Rect(inner.x + 14f, y + 6f, inner.width - 28f, 1f), new Color(color.r, color.g, color.b, 0.5f));
                y += 14f;
                Text.Font = GameFont.Tiny;
                foreach (var precept in KeyPrecepts(ideo).Take(Mathf.Max(0, Mathf.FloorToInt((inner.yMax - y) / 18f))))
                {
                    Style.ShadowLabel(new Rect(inner.x + 6f, y, inner.width - 12f, 18f), $"{precept.def.issue.LabelCap}: {precept.def.LabelCap}", new Color(0.88f, 0.86f, 0.8f));
                    y += 18f;
                }
                Text.Font = GameFont.Small;
                if (h > 0.05f) Style.SheenSweep(r, Mathf.Repeat(Style.Now * 0.7f, 1.6f), 0.15f * h);
                GUI.color = old;
                TooltipHandler.TipRegion(rects[i], ideo.description);

                if (Widgets.ButtonInvisible(rects[i]))
                {
                    if (selected == i && Event.current.clickCount >= 2 && CanDoNext()) DoNext();
                    selected = i;
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                }
            }
        }

        private static IEnumerable<Precept> KeyPrecepts(Ideo ideo) =>
            ideo.PreceptsListForReading.Where(p => p.def.visible && p.def.issue != null && !p.def.issue.allowMultiplePrecepts)
                .OrderByDescending(p => p.def.impact);

        protected override bool CanDoNext() => base.CanDoNext() && selected >= 0;

        protected override void DoNext()
        {
            var ideo = options[selected];
            Faction.OfPlayer.ideos.SetPrimary(ideo);
            foreach (var other in Find.IdeoManager.IdeosListForReading) other.initialPlayerIdeo = false;
            ideo.initialPlayerIdeo = true;
            Find.IdeoManager.Add(ideo);
            Find.IdeoManager.RemoveUnusedStartingIdeos();
            Find.Scenario.PostIdeoChosen();
            base.DoNext();
        }
    }

    /// <summary>Pick the starting squad from ten candidates fitting the scenario, up to the scenario's squad size.</summary>
    public class Page_RoguelikePawns : RoguelikePage
    {
        private const int Candidates = 10;
        private readonly List<Pawn> picked = new List<Pawn>();
        private float[] hover;
        private int maxPick;

        public IReadOnlyList<Pawn> Pawns => Find.GameInitData.startingAndOptionalPawns;
        public IReadOnlyList<Pawn> Picked => picked;
        public int MaxPick => maxPick;

        public override string PageTitle => "Choose your squad";
        protected override string Heading => "Choose your squad";
        protected override string Subheading => $"Pick up to {maxPick} colonist{(maxPick == 1 ? "" : "s")} ({picked.Count} chosen). Hover a card for the full story.";
        protected override string NextLabel => "Begin the run";
        protected override bool NextReady => picked.Count > 0;

        public override void PreOpen()
        {
            base.PreOpen();
            var init = Find.GameInitData;
            maxPick = Mathf.Max(1, init.startingPawnCount);
            // Extra candidates default to plain colonists; give them the scenario's own pawn requests (xenotypes,
            // pawn kinds) in turn so all ten fit the starter.
            bool themed = Find.Scenario.AllParts.Any(p => p is ScenPart_ConfigPage_ConfigureStartingPawns_Xenotypes || p is ScenPart_ConfigPage_ConfigureStartingPawns_KindDefs);
            for (int i = maxPick; i < Candidates; i++)
            {
                if (themed) StartingPawnUtility.SetGenerationRequest(i, StartingPawnUtility.GetGenerationRequest(i % maxPick));
                if (i < init.startingAndOptionalPawns.Count)
                {
                    if (themed) StartingPawnUtility.RandomizeInPlace(init.startingAndOptionalPawns[i]);
                }
                else StartingPawnUtility.AddNewPawn(i);
            }
            hover = new float[init.startingAndOptionalPawns.Count];
        }

        public void Toggle(Pawn p)
        {
            if (picked.Remove(p)) { SoundDefOf.Tick_Low.PlayOneShotOnCamera(); return; }
            if (picked.Count >= maxPick)
            {
                Messages.Message($"This starter takes at most {maxPick}. Click a chosen colonist to drop them first.", MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            picked.Add(p);
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

        protected override void DrawContent(Rect area)
        {
            var pawns = Find.GameInitData.startingAndOptionalPawns;
            var rects = Grid(area, pawns.Count, 5, new Vector2(270f, 380f), 18f);
            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                var r = DealIn(rects[i], i, out float alpha);
                if (alpha <= 0f) continue;
                bool over = Mouse.IsOver(rects[i]);
                if (Event.current.type == EventType.Repaint) hover[i] = Mathf.MoveTowards(hover[i], over ? 1f : 0f, Time.unscaledDeltaTime * 8f);
                float h = Style.EaseOutCubic(hover[i]);
                int order = picked.IndexOf(p);
                bool chosen = order >= 0;
                r.y -= 8f * h + (chosen ? 6f : 0f);
                var kind = PawnColor(p);

                var old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, alpha * (picked.Count >= maxPick && !chosen ? 0.6f : 1f));
                string banner = ModsConfig.BiotechActive && p.genes?.Xenotype != null ? p.genes.XenotypeLabelCap : "Colonist";
                var inner = Style.CardFrame(r, kind, h, chosen, banner, i);
                DrawPawn(inner, p, kind);
                if (chosen)
                {
                    var badge = new Rect(r.xMax - 30f, r.y - 10f, 34f, 34f);
                    Style.GlowAround(badge, new Color(Style.Gold.r, Style.Gold.g, Style.Gold.b, 0.6f), 1.8f);
                    Style.Tex(badge, Style.Glow, Style.Gold);
                    Text.Font = GameFont.Medium;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Style.ShadowLabel(badge, (order + 1).ToString(), Color.white);
                    Text.Font = GameFont.Small;
                    Text.Anchor = TextAnchor.UpperLeft;
                }
                if (h > 0.05f) Style.SheenSweep(r, Mathf.Repeat(Style.Now * 0.7f, 1.6f), 0.15f * h);
                GUI.color = old;
                TooltipHandler.TipRegion(rects[i], () => Tooltip(p), p.thingIDNumber);
                if (Widgets.ButtonInvisible(rects[i])) Toggle(p);
            }
        }

        private static Color PawnColor(Pawn p)
        {
            if (ModsConfig.BiotechActive && p.genes?.Xenotype != null && p.genes.Xenotype != XenotypeDefOf.Baseliner)
                return new Color(0.7f, 0.5f, 0.95f);
            return new Color(0.45f, 0.62f, 0.9f);
        }

        internal static void DrawPawn(Rect inner, Pawn p, Color kind)
        {
            float a = GUI.color.a;
            var portrait = new Rect(inner.center.x - 44f, inner.y, 88f, 88f);
            Style.GlowAround(portrait, new Color(kind.r, kind.g, kind.b, 0.35f), 1.5f);
            GUI.DrawTexture(portrait, PortraitsCache.Get(p, portrait.size, Rot4.South, new Vector3(0f, 0f, 0.1f), 1.4f));
            if (ModsConfig.BiotechActive && p.genes?.XenotypeIcon != null && p.genes.Xenotype != XenotypeDefOf.Baseliner)
                Style.Tex(new Rect(inner.x, inner.y, 26f, 26f), p.genes.XenotypeIcon, new Color(1f, 1f, 1f, 0.9f));

            float y = portrait.yMax + 2f;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperCenter;
            Style.ShadowLabel(new Rect(inner.x, y, inner.width, 22f), p.Name.ToStringShort, Style.Parchment);
            Text.Font = GameFont.Tiny;
            string who = $"{p.gender.GetLabel().CapitalizeFirst()}, {p.ageTracker.AgeBiologicalYears}" + (p.story?.Adulthood != null ? " · " + p.story.Adulthood.TitleCapFor(p.gender) : p.story?.Childhood != null ? " · " + p.story.Childhood.TitleCapFor(p.gender) : "");
            Style.ShadowLabel(new Rect(inner.x, y + 20f, inner.width, 16f), who.Truncate(inner.width), new Color(kind.r, kind.g, kind.b, 0.95f));
            Text.Anchor = TextAnchor.UpperLeft;
            y += 40f;

            // Traits as chips.
            float x = inner.x;
            foreach (var trait in p.story?.traits?.allTraits ?? new List<Trait>())
            {
                string label = trait.LabelCap;
                float w = Text.CalcSize(label).x + 10f;
                if (x + w > inner.xMax) { x = inner.x; y += 18f; }
                var chip = new Rect(x, y, w, 16f);
                Style.Box(chip, new Color(kind.r, kind.g, kind.b, 0.18f));
                Style.Border(chip, new Color(kind.r, kind.g, kind.b, 0.45f), 1f);
                Text.Anchor = TextAnchor.MiddleCenter;
                Style.ShadowLabel(chip, label, Color.white);
                Text.Anchor = TextAnchor.UpperLeft;
                TooltipHandler.TipRegion(chip, trait.TipString(p));
                x += w + 4f;
            }
            y += 22f;

            // Every skill in the game's own order, so cards compare row by row; rows squeeze to fit the card.
            if (p.skills != null)
            {
                var skills = p.skills.skills.OrderBy(s => s.def.listOrder).ToList();
                float rowH = Mathf.Clamp((inner.yMax - y) / skills.Count, 10f, 15f);
                foreach (var s in skills)
                {
                    if (y + rowH > inner.yMax + 1f) break;
                    var row = new Rect(inner.x, y, inner.width, rowH);
                    bool off = s.TotallyDisabled;
                    Style.ShadowLabel(new Rect(row.x, row.y + rowH / 2f - 8f, 78f, 16f), s.def.skillLabel.CapitalizeFirst(), off ? new Color(0.6f, 0.45f, 0.42f) : new Color(0.88f, 0.86f, 0.8f));
                    var track = new Rect(row.x + 80f, row.y + rowH / 2f - 3.5f, row.width - 120f, 7f);
                    Style.Box(track, new Color(0f, 0f, 0f, 0.45f));
                    if (off)
                    {
                        Style.ShadowLabel(new Rect(track.xMax + 4f, row.y + rowH / 2f - 8f, 20f, 16f), "—", new Color(1f, 0.45f, 0.4f));
                    }
                    else
                    {
                        Style.Box(new Rect(track.x, track.y, track.width * s.Level / 20f, track.height), new Color(kind.r, kind.g, kind.b, 0.85f));
                        Style.ShadowLabel(new Rect(track.xMax + 4f, row.y + rowH / 2f - 8f, 20f, 16f), s.Level.ToString(), Color.white);
                        if (s.passion != Passion.None)
                            Style.Tex(new Rect(row.xMax - 13f, row.y + rowH / 2f - 6.5f, 13f, 13f), s.passion == Passion.Major ? SkillUI.PassionMajorIcon : SkillUI.PassionMinorIcon, Color.white);
                    }
                    y += rowH;
                }
            }
            Text.Font = GameFont.Small;
            GUI.color = new Color(1f, 1f, 1f, a);
        }

        internal static string Tooltip(Pawn p)
        {
            string text = Dialog_RewardPick.RecruitSummary(p);
            if (p.story?.Childhood != null) text += "\n\n" + p.story.Childhood.FullDescriptionFor(p).Resolve();
            if (p.story?.Adulthood != null) text += "\n\n" + p.story.Adulthood.FullDescriptionFor(p).Resolve();
            return text;
        }

        protected override bool CanDoNext()
        {
            if (!base.CanDoNext() || picked.Count == 0) return false;
            var init = Find.GameInitData;
            if (picked.Any(p => (p.DevelopmentalStage & init.allowedDevelopmentalStages) == 0))
            {
                Messages.Message("SelectedCharactersMustBeOfAllowedDevelopmentalStages".Translate(init.allowedDevelopmentalStages.ToCommaListOr()), MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }
            return true;
        }

        protected override void DoNext()
        {
            var init = Find.GameInitData;
            // The chosen colonists go first; the game takes the first startingPawnCount pawns.
            var rest = init.startingAndOptionalPawns.Where(p => !picked.Contains(p)).ToList();
            init.startingAndOptionalPawns.Clear();
            init.startingAndOptionalPawns.AddRange(picked);
            init.startingAndOptionalPawns.AddRange(rest);
            init.startingPawnCount = picked.Count;
            // The squad was chosen here, so the vanilla page's composition rules no longer apply.
            init.startingPawnsRequired = null;
            init.startingXenotypesRequired = null;
            init.startingMutantsRequired = null;
            foreach (var p in picked)
                if (p.Name is NameTriple n && string.IsNullOrEmpty(n.Nick)) p.Name = new NameTriple(n.First, n.First, n.Last);
            PortraitsCache.Clear();
            base.DoNext();
        }
    }
}
