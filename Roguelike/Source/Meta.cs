using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Roguelike
{
    /// <summary>Something meta points can buy: a starter, a pool addition (variety), or a permanent bonus (power).</summary>
    public class UnlockDef : Def
    {
        public int cost = 3;
        /// <summary>Starter loadout id (matches ScenPart_RoguelikeRun.starterId).</summary>
        public string starterId;
        public RelicDef relic;
        public BlueprintGroupDef blueprint;
        public GearRewardDef gear;
        // Power unlocks.
        public float graceDaysOffset;
        public int extraRewardChoices;
        public List<ThingDefCountClass> startThings = new List<ThingDefCountClass>();

        public bool IsPower => graceDaysOffset != 0f || extraRewardChoices != 0 || startThings.Count > 0;

        public string KindLabel => starterId != null ? "Starter" : IsPower ? "Power" : "Variety";
    }

    /// <summary>Meta-progression that survives between runs, stored in the mod's settings file (not in save games).</summary>
    public class RoguelikeSettings : ModSettings
    {
        public int points;
        public int runs;
        public int wins;
        public List<string> unlocked = new List<string>();
        /// <summary>Owned variety unlocks the player has switched off: their item stays out of the pools.</summary>
        public List<string> disabled = new List<string>();
        /// <summary>Highest Ascension level unlocked (win at your highest level to unlock the next) and the one chosen for new runs.</summary>
        public int maxAscension;
        public int selectedAscension;
        /// <summary>Game mode for new runs: false = classic acts, true = hub mode (one colony, an outward frontier).</summary>
        public bool hubMode;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref points, "points");
            Scribe_Values.Look(ref runs, "runs");
            Scribe_Values.Look(ref wins, "wins");
            Scribe_Collections.Look(ref unlocked, "unlocked", LookMode.Value);
            Scribe_Values.Look(ref maxAscension, "maxAscension");
            Scribe_Values.Look(ref selectedAscension, "selectedAscension");
            Scribe_Collections.Look(ref disabled, "disabled", LookMode.Value);
            Scribe_Values.Look(ref hubMode, "hubMode");
            unlocked ??= new List<string>();
            disabled ??= new List<string>();
        }
    }

    public class RoguelikeMod : Mod
    {
        public static RoguelikeMod Instance;
        public static RoguelikeSettings Settings;

        public RoguelikeMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<RoguelikeSettings>();
        }

        public override string SettingsCategory() => "Roguelike";

        public override void DoSettingsWindowContents(Rect inRect) => UnlockList.Draw(inRect);
    }

    public static class MetaProgress
    {
        private static readonly RoguelikeSettings Fallback = new RoguelikeSettings();

        public static RoguelikeSettings S => RoguelikeMod.Settings ?? Fallback;

        public static IEnumerable<UnlockDef> Owned => DefDatabase<UnlockDef>.AllDefs.Where(IsUnlocked);

        public static bool IsUnlocked(UnlockDef u) => S.unlocked.Contains(u.defName);

        private static bool Unlocked(System.Func<UnlockDef, bool> match) => DefDatabase<UnlockDef>.AllDefs.Any(u => match(u) && IsUnlocked(u));

        public static bool IsEnabled(UnlockDef u) => !S.disabled.Contains(u.defName);
        public static bool CanToggle(UnlockDef u) => u.KindLabel == "Variety" && IsUnlocked(u);

        /// <summary>Switches an owned variety unlock in or out of the pools.</summary>
        public static void Toggle(UnlockDef u)
        {
            if (!CanToggle(u)) return;
            if (!S.disabled.Remove(u.defName)) S.disabled.Add(u.defName);
            Save();
        }

        private static bool InPool(System.Func<UnlockDef, bool> match) => Unlocked(u => match(u) && IsEnabled(u));

        public static bool IsAvailable(RelicDef d) => !d.requiresUnlock || InPool(u => u.relic == d);
        public static bool IsAvailable(GearRewardDef d) => !d.requiresUnlock || InPool(u => u.gear == d);
        public static bool IsAvailable(BlueprintGroupDef d) => !d.requiresUnlock || InPool(u => u.blueprint == d);
        public static bool IsStarterAvailable(ScenPart_RoguelikeRun part) => !part.requiresUnlock || Unlocked(u => u.starterId == part.starterId);

        public static float GraceDaysOffset => Owned.Sum(u => u.graceDaysOffset);
        public static int ExtraRewardChoices => Owned.Sum(u => u.extraRewardChoices);
        public static IEnumerable<ThingDefCountClass> StartThings => Owned.SelectMany(u => u.startThings);

        public static void Award(int points)
        {
            if (points <= 0) return;
            S.points += points;
            Save();
        }

        public static bool CanBuy(UnlockDef u) => !IsUnlocked(u) && S.points >= u.cost;

        public static bool TryBuy(UnlockDef u)
        {
            if (!CanBuy(u)) return false;
            S.points -= u.cost;
            S.unlocked.Add(u.defName);
            Save();
            ScenarioLister.MarkDirty();
            return true;
        }

        public const int MaxAscension = 5;

        public static readonly string[] AscensionEffects =
        {
            "No handicaps.",
            "Weather worsens 25% faster.",
            "Raids are 30% stronger.",
            "One fewer card at every reward screen.",
            "Weather holds off one day less.",
            "Bosses are 50% stronger.",
        };

        public static int SelectedAscension => Mathf.Clamp(S.selectedAscension, 0, S.maxAscension);

        /// <summary>The mode chosen for new runs (read when the world is generated).</summary>
        public static bool HubMode => S.hubMode;

        public static void RecordRunEnd(bool won, int ascension = 0)
        {
            S.runs++;
            if (won) S.wins++;
            if (won && ascension >= S.maxAscension && S.maxAscension < MaxAscension) S.maxAscension = ascension + 1;
            Save();
        }

        /// <summary>The self-test works on an in-memory copy and never touches the player's real progress.</summary>
        public static void Save()
        {
            if (!SelfTest.Enabled) RoguelikeMod.Instance?.WriteSettings();
        }

        public static void ResetForSelfTest()
        {
            S.points = 0;
            S.runs = S.wins = 0;
            S.unlocked.Clear();
            S.disabled.Clear();
            S.maxAscension = S.selectedAscension = 0;
            S.hubMode = false;
            ScenarioLister.MarkDirty();
        }
    }

    /// <summary>Only roguelike starters appear in the scenario list, and locked ones only once bought.</summary>
    [HarmonyPatch(typeof(ScenarioLister), nameof(ScenarioLister.ScenariosInCategory))]
    public static class Patch_ScenarioLister_HideLocked
    {
        public static void Postfix(ref IEnumerable<Scenario> __result)
        {
            // With the mod enabled, only roguelike starts are offered; disable the mod to play standard RimWorld.
            __result = __result.Where(s =>
            {
                var parts = s.AllParts.OfType<ScenPart_RoguelikeRun>().ToList();
                return parts.Count > 0 && parts.All(MetaProgress.IsStarterAvailable);
            }).ToList();
        }
    }

    /// <summary>
    /// The meta-progression screen, used by the unlock dialog and the mod settings page: an animated points counter,
    /// the Ascension track, and unlocks as tiles grouped by kind. Clicking an affordable tile buys it.
    /// </summary>
    public static class UnlockList
    {
        private static readonly Color StarterC = new Color(0.45f, 0.8f, 0.42f);
        private static readonly Color VarietyC = new Color(0.45f, 0.62f, 0.9f);
        private static readonly Color AscensionC = new Color(0.92f, 0.38f, 0.3f);
        private const float HeaderH = 96f;
        private const float TileH = 138f;
        private const float Gap = 12f;

        internal static Vector2 scroll;
        private static float openedAt, lastDrawn = -10f, shownPoints;
        private static readonly Dictionary<string, float> bought = new Dictionary<string, float>();
        private static readonly Dictionary<string, float> hover = new Dictionary<string, float>();
        private static readonly List<Style.Particle> particles = new List<Style.Particle>();
        private static readonly List<(string text, float at)> floaters = new List<(string, float)>();

        private static readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();

        /// <summary>Each unlock's own badge (Textures/Roguelike/Unlocks, from Tools/make-unlock-icons.ps1).</summary>
        private static Texture2D Icon(UnlockDef u)
        {
            if (!icons.TryGetValue(u.defName, out var tex))
                icons[u.defName] = tex = ContentFinder<Texture2D>.Get("Roguelike/Unlocks/" + u.defName.Replace("Roguelike_Unlock_", ""), reportFailure: false);
            return tex;
        }

        private static Color KindColor(UnlockDef u) => u.starterId != null ? StarterC : u.IsPower ? Style.Gold : VarietyC;

        public static void Draw(Rect rect)
        {
            var s = MetaProgress.S;
            float now = Style.Now;
            if (now - lastDrawn > 0.5f)
            {
                // Freshly opened: replay the entrance and start the counter from the real value.
                openedAt = now;
                shownPoints = s.points;
                floaters.Clear();
                particles.Clear();
            }
            lastDrawn = now;
            float t = now - openedAt;
            if (Event.current.type == EventType.Repaint)
                shownPoints = Mathf.Lerp(shownPoints, s.points, 1f - Mathf.Exp(-8f * Mathf.Min(Time.unscaledDeltaTime, 0.05f)));

            DrawHeader(new Rect(rect.x, rect.y, rect.width, HeaderH), s, t);

            // Tiles, grouped by kind.
            var groups = DefDatabase<UnlockDef>.AllDefs.GroupBy(u => u.KindLabel).OrderBy(g => g.Key == "Starter" ? 0 : g.Key == "Variety" ? 1 : 2).ToList();
            var outer = new Rect(rect.x, rect.y + HeaderH + 8f, rect.width, rect.height - HeaderH - 8f);
            float viewW = outer.width - 16f;
            int cols = Mathf.Max(1, Mathf.FloorToInt((viewW + Gap) / (210f + Gap)));
            float tileW = (viewW - (cols - 1) * Gap) / cols;
            float viewH = groups.Sum(g => 34f + Mathf.CeilToInt(g.Count() / (float)cols) * (TileH + Gap));
            var view = new Rect(0f, 0f, viewW, viewH);
            Widgets.BeginScrollView(outer, ref scroll, view);
            float y = 0f;
            int index = 0;
            foreach (var g in groups)
            {
                var color = g.Key == "Starter" ? StarterC : g.Key == "Power" ? Style.Gold : VarietyC;
                Text.Font = GameFont.Small;
                Style.ShadowLabel(new Rect(0f, y + 4f, 200f, 24f), g.Key == "Starter" ? "Starters" : g.Key == "Power" ? "Permanent bonuses" : "More in the pools", color);
                var size = Text.CalcSize(g.Key == "Starter" ? "Starters" : g.Key == "Power" ? "Permanent bonuses" : "More in the pools");
                Style.Box(new Rect(size.x + 12f, y + 16f, viewW - size.x - 12f, 1f), new Color(color.r, color.g, color.b, 0.35f));
                y += 34f;
                int i = 0;
                foreach (var u in g.OrderBy(u => MetaProgress.IsUnlocked(u)).ThenBy(u => u.cost).ThenBy(u => u.label))
                {
                    var tile = new Rect((i % cols) * (tileW + Gap), y + (i / cols) * (TileH + Gap), tileW, TileH);
                    bool visible = tile.yMax >= scroll.y && tile.y <= scroll.y + outer.height;
                    if (visible) DrawTile(tile, u, s, t - 0.15f - index * 0.025f);
                    i++;
                    index++;
                }
                y += Mathf.CeilToInt(i / (float)cols) * (TileH + Gap);
            }
            Style.UpdateAndDraw(particles);
            Widgets.EndScrollView();
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static void DrawHeader(Rect r, RoguelikeSettings s, float t)
        {
            Style.Tex(r, Style.CardBody, Color.white);
            Style.Tex(new Rect(r.x, r.y, r.width * 0.6f, r.height), Style.BannerFade, new Color(Style.Gold.r, Style.Gold.g, Style.Gold.b, 0.12f));
            Style.Border(r, new Color(Style.Gold.r, Style.Gold.g, Style.Gold.b, 0.45f), 1f);
            Style.Box(new Rect(r.x, r.y, 3f, r.height), Style.Gold);

            // Points: a glowing emblem and a counter that rolls toward the real value.
            var emblem = new Rect(r.x + 18f, r.center.y - 24f, 48f, 48f);
            Style.GlowAround(emblem, new Color(Style.Gold.r, Style.Gold.g, Style.Gold.b, 0.45f + 0.15f * Style.Pulse(2f)), 1.9f);
            Style.Tex(emblem.ContractedBy(10f), Style.Glow, Color.white);
            Style.Tex(emblem.ContractedBy(4f), Style.SoftGlow, new Color(Style.Gold.r, Style.Gold.g, Style.Gold.b, 0.9f));
            Text.Font = GameFont.Tiny;
            Style.ShadowLabel(new Rect(emblem.xMax + 14f, r.y + 12f, 200f, 18f), "META POINTS", new Color(Style.Gold.r, Style.Gold.g, Style.Gold.b, 0.85f));
            Text.Font = GameFont.Medium;
            var pointsRect = new Rect(emblem.xMax + 14f, r.y + 28f, 200f, 36f);
            float swell = Mathf.Clamp01(Mathf.Abs(shownPoints - s.points));
            var m = GUI.matrix;
            GUIUtility.ScaleAroundPivot(Vector2.one * (1.35f + 0.15f * swell), new Vector2(pointsRect.x, pointsRect.center.y));
            Style.ShadowLabel(pointsRect, Mathf.RoundToInt(shownPoints).ToString(), Color.Lerp(Color.white, Style.Gold, 0.5f + 0.5f * swell));
            GUI.matrix = m;
            Text.Font = GameFont.Tiny;
            Style.ShadowLabel(new Rect(emblem.xMax + 14f, r.y + 66f, 420f, 18f),
                $"{s.runs} runs · {s.wins} wins  ·  +1 per node, +5 per boss, +1 per skipped reward", new Color(1f, 1f, 1f, 0.6f));

            // Points spent float up from the counter.
            for (int i = floaters.Count - 1; i >= 0; i--)
            {
                float age = Style.Now - floaters[i].at;
                if (age > 1.2f) { floaters.RemoveAt(i); continue; }
                Text.Font = GameFont.Small;
                Style.ShadowLabel(new Rect(pointsRect.x + 70f, pointsRect.y - age * 30f, 80f, 24f), floaters[i].text, new Color(1f, 0.45f, 0.35f, 1f - age / 1.2f));
            }

            // Ascension track: one pip per level; click an unlocked level to choose it for new runs.
            Text.Font = GameFont.Tiny;
            var track = new Rect(r.xMax - 330f, r.y + 12f, 310f, r.height - 24f);
            Text.Anchor = TextAnchor.UpperRight;
            Style.ShadowLabel(new Rect(track.x, track.y, track.width, 18f), s.maxAscension > 0 ? $"ASCENSION {MetaProgress.SelectedAscension}" : "ASCENSION: WIN A RUN TO UNLOCK", new Color(AscensionC.r, AscensionC.g, AscensionC.b, 0.95f));
            Text.Anchor = TextAnchor.UpperLeft;
            int sel = MetaProgress.SelectedAscension;
            float pip = 34f, step = (track.width - pip) / MetaProgress.MaxAscension;
            float py = track.y + 30f;
            Style.Box(new Rect(track.x + pip / 2f, py + pip / 2f - 1f, track.width - pip, 2f), new Color(1f, 1f, 1f, 0.12f));
            Style.Box(new Rect(track.x + pip / 2f, py + pip / 2f - 1f, step * sel, 2f), AscensionC);
            for (int l = 0; l <= MetaProgress.MaxAscension; l++)
            {
                var pr = new Rect(track.x + l * step, py, pip, pip);
                bool open = l <= s.maxAscension, chosen = l == sel, over = open && Mouse.IsOver(pr);
                float appear = Style.EaseOutBack((t - 0.2f - l * 0.06f) / 0.4f);
                pr = new Rect(pr.center.x - pip * appear / 2f, pr.center.y - pip * appear / 2f, pip * appear, pip * appear);
                if (chosen) Style.GlowAround(pr, new Color(AscensionC.r, AscensionC.g, AscensionC.b, 0.5f + 0.25f * Style.Pulse(3f)), 2.2f);
                else if (over) Style.GlowAround(pr, new Color(AscensionC.r, AscensionC.g, AscensionC.b, 0.35f), 1.8f);
                Style.Tex(pr, Style.CardBody, Color.white);
                Style.Box(pr, l <= sel ? new Color(AscensionC.r, AscensionC.g, AscensionC.b, 0.55f) : new Color(1f, 1f, 1f, open ? 0.06f : 0.02f));
                Style.Border(pr, open ? new Color(AscensionC.r, AscensionC.g, AscensionC.b, chosen ? 1f : 0.6f) : new Color(1f, 1f, 1f, 0.15f), chosen ? 2f : 1f);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                Style.ShadowLabel(pr, l.ToString(), open ? Color.white : new Color(1f, 1f, 1f, 0.3f));
                Text.Anchor = TextAnchor.UpperLeft;
                TooltipHandler.TipRegion(pr, l == 0 ? "Ascension 0: no handicaps."
                    : $"Ascension {l}: {MetaProgress.AscensionEffects[l]}\nLevels stack: " + string.Join(" ", Enumerable.Range(1, l).Select(k => MetaProgress.AscensionEffects[k])) +
                      (open ? "" : "\n\nLocked. Win at your highest level to unlock the next."));
                if (open && !chosen && Widgets.ButtonInvisible(pr))
                {
                    s.selectedAscension = l;
                    MetaProgress.Save();
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                }
            }
            Text.Font = GameFont.Small;
        }

        private static void DrawTile(Rect tile, UnlockDef u, RoguelikeSettings s, float t)
        {
            float appear = Style.EaseOutCubic(t / 0.35f);
            if (appear <= 0f) return;
            bool owned = MetaProgress.IsUnlocked(u), affordable = MetaProgress.CanBuy(u);
            bool toggle = MetaProgress.CanToggle(u), off = toggle && !MetaProgress.IsEnabled(u);
            var c = KindColor(u);
            bool over = (!owned || toggle) && Mouse.IsOver(tile);
            float h = hover.TryGetValue(u.defName, out var hv) ? hv : 0f;
            if (Event.current.type == EventType.Repaint) hover[u.defName] = h = Mathf.MoveTowards(h, over ? 1f : 0f, Time.unscaledDeltaTime * 8f);
            float flash = bought.TryGetValue(u.defName, out var at) ? Mathf.Clamp01(1f - (Style.Now - at) / 0.7f) : 0f;

            var r = tile;
            r.y += (1f - appear) * 20f - 4f * h * (affordable ? 1f : 0.3f);
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, appear * (off ? 0.45f : owned ? 0.8f : affordable ? 1f : 0.7f));

            float aura = (affordable ? 0.12f + 0.1f * Style.Pulse(2.2f, tile.x * 0.01f) : 0f) + 0.3f * h + 0.6f * flash;
            if (aura > 0f) Style.GlowAround(r, new Color(c.r, c.g, c.b, aura), 1.15f, Style.SoftGlow);
            Style.Tex(r, Style.CardBody, Color.white);
            Style.Box(r, new Color(c.r, c.g, c.b, owned ? 0.08f : 0.04f + 0.06f * h));
            Style.Border(r, off ? new Color(0.5f, 0.5f, 0.5f, 0.4f + 0.3f * h) : owned ? new Color(StarterC.r, StarterC.g, StarterC.b, 0.6f + 0.3f * h) : new Color(c.r, c.g, c.b, affordable ? 0.75f + 0.25f * h : 0.3f), affordable || owned ? 1.5f : 1f);
            Style.Box(new Rect(r.x, r.y, r.width, 2f), new Color(c.r, c.g, c.b, 0.9f));

            // Icon.
            var icon = new Rect(r.x + 10f, r.y + 12f, 40f, 40f);
            Style.GlowAround(icon, new Color(c.r, c.g, c.b, 0.35f), 1.6f);
            var iconTex = Icon(u);
            var thing = u.relic?.iconThing ?? u.gear?.thing ?? (u.blueprint != null && u.blueprint.buildings.Count > 0 ? u.blueprint.buildings[0] : null);
            if (iconTex != null) Style.Tex(icon.ExpandedBy(4f), iconTex, Color.white);
            else if (thing != null) Widgets.DefIcon(icon, thing, GenStuff.DefaultStuffFor(thing), color: GUI.color);
            else
            {
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;
                Style.ShadowLabel(icon, u.KindLabel.Substring(0, 1), c);
            }

            // Title, kind and description.
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            Style.ShadowLabel(new Rect(icon.xMax + 8f, r.y + 10f, r.width - icon.width - 26f, 22f), u.LabelCap.Truncate(r.width - icon.width - 26f), Style.Parchment);
            Text.Font = GameFont.Tiny;
            Style.ShadowLabel(new Rect(icon.xMax + 8f, r.y + 32f, r.width - icon.width - 26f, 18f), u.KindLabel.ToUpperInvariant(), new Color(c.r, c.g, c.b, 0.9f));
            var oldColor = GUI.color;
            GUI.color = new Color(0.86f, 0.84f, 0.78f, GUI.color.a);
            Widgets.Label(new Rect(r.x + 10f, icon.yMax + 8f, r.width - 20f, r.yMax - icon.yMax - 40f), u.description);
            GUI.color = oldColor;

            // Footer: cost badge or owned mark.
            var foot = new Rect(r.x + 10f, r.yMax - 28f, r.width - 20f, 20f);
            Text.Anchor = TextAnchor.MiddleRight;
            if (toggle)
            {
                Style.ShadowLabel(foot, off ? "OFF" : "✓ IN POOL", off ? new Color(0.65f, 0.65f, 0.65f) : StarterC);
                Text.Anchor = TextAnchor.MiddleLeft;
                if (over) Style.ShadowLabel(foot, off ? "Click to add back" : "Click to remove", new Color(1f, 1f, 1f, 0.7f));
            }
            else if (owned) Style.ShadowLabel(foot, "✓ OWNED", StarterC);
            else
            {
                var badge = new Rect(foot.xMax - 64f, foot.y - 2f, 64f, 24f);
                Style.Box(badge, new Color(0f, 0f, 0f, 0.45f));
                Style.Border(badge, new Color(Style.Gold.r, Style.Gold.g, Style.Gold.b, affordable ? 0.9f : 0.3f), 1f);
                Style.Tex(new Rect(badge.x + 4f, badge.center.y - 7f, 14f, 14f), Style.Glow, Style.Gold);
                Text.Font = GameFont.Small;
                Style.ShadowLabel(new Rect(badge.x, badge.y, badge.width - 8f, badge.height), u.cost.ToString(), affordable ? Style.Gold : new Color(0.7f, 0.5f, 0.45f));
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                if (over) Style.ShadowLabel(foot, affordable ? "Click to unlock" : $"Need {u.cost - s.points} more", new Color(1f, 1f, 1f, 0.7f));
            }
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            if (h > 0.05f && affordable) Style.SheenSweep(r, Mathf.Repeat(Style.Now * 0.7f, 1.6f), 0.15f * h);
            if (flash > 0f) Style.Box(r, new Color(1f, 1f, 1f, 0.6f * flash));
            GUI.color = old;

            if (toggle && Widgets.ButtonInvisible(tile))
            {
                MetaProgress.Toggle(u);
                (off ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();
            }
            if (affordable && Widgets.ButtonInvisible(tile) && MetaProgress.TryBuy(u))
            {
                bought[u.defName] = Style.Now;
                Style.Burst(particles, r.center, c, 40, 260f);
                floaters.Add(($"-{u.cost}", Style.Now));
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
        }
    }

    public class Dialog_Unlocks : Window
    {
        private float openedAt = -1f;

        public Dialog_Unlocks()
        {
            doCloseX = false;
            forcePause = true;
            absorbInputAroundWindow = true;
            doWindowBackground = false;
            drawShadow = false;
        }

        public override Vector2 InitialSize => new Vector2(900f, 700f);

        protected override float Margin => 0f;

        public override void DoWindowContents(Rect inRect)
        {
            if (openedAt < 0f) openedAt = Style.Now;
            float a = Style.EaseOutCubic((Style.Now - openedAt) / 0.3f);
            GUI.color = new Color(1f, 1f, 1f, a);
            Style.Tex(inRect.ContractedBy(6f), Style.CardBody, Color.white);
            Style.Border(inRect.ContractedBy(6f), new Color(Style.Gold.r, Style.Gold.g, Style.Gold.b, 0.6f), 1f);
            var body = inRect.ContractedBy(22f);
            Text.Font = GameFont.Medium;
            Style.ShadowLabel(new Rect(body.x, body.y, body.width, 34f), "Unlocks", Style.Gold);
            Text.Font = GameFont.Small;
            UnlockList.Draw(new Rect(body.x, body.y + 42f, body.width, body.height - 42f - 52f));
            if (Style.Button(new Rect(body.center.x - 90f, body.yMax - 40f, 180f, 38f), "Done", Style.Gold)) Close();
            GUI.color = Color.white;
        }
    }

    /// <summary>Shown when a run is won or lost: what happened, what you earned, and where to go next.</summary>
    public class Dialog_RunEnd : Window
    {
        private readonly bool won;
        private readonly string reason;

        public Dialog_RunEnd(bool won, string reason)
        {
            this.won = won;
            this.reason = reason;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            doCloseX = false;
            doWindowBackground = false;
            drawShadow = false;
        }

        public override Vector2 InitialSize => new Vector2(720f, 470f);

        public override void WindowOnGUI()
        {
            // Dim the game behind the panel.
            Style.Box(new Rect(0f, 0f, UI.screenWidth, UI.screenHeight), new Color(0f, 0f, 0f, 0.55f * Style.EaseOutCubic(openedAt < 0f ? 0f : (Style.Now - openedAt) / 0.4f)));
            base.WindowOnGUI();
        }

        private float openedAt = -1f;
        private readonly List<Style.Particle> particles = new List<Style.Particle>();

        public override void DoWindowContents(Rect inRect)
        {
            var run = WorldComponent_RoguelikeRun.Get;
            if (openedAt < 0f)
            {
                openedAt = Style.Now;
                if (won) Style.Burst(particles, new Vector2(inRect.center.x, inRect.y + 28f), Style.Gold, 60, 300f);
            }
            float t = Style.Now - openedAt;
            var tone = won ? Style.Gold : new Color(0.92f, 0.38f, 0.32f);
            var panel = inRect.ExpandedBy(Margin - 6f);
            Style.Tex(panel.ExpandedBy(16f), Style.SoftGlow, new Color(tone.r, tone.g, tone.b, 0.18f));
            Style.Tex(panel, Style.CardBody, Color.white);
            Style.Border(panel, new Color(tone.r, tone.g, tone.b, 0.7f), 1f);
            Style.Box(new Rect(panel.x, panel.y, panel.width, 3f), tone);

            // Title scales in with a glow behind it.
            float s = Style.EaseOutBack(t / 0.6f);
            var titleRect = new Rect(inRect.x, inRect.y, inRect.width, 44f);
            Style.GlowAround(new Rect(inRect.center.x - 150f, titleRect.y + 4f, 300f, 36f), new Color(tone.r, tone.g, tone.b, 0.3f * Mathf.Clamp01(s) + 0.08f * Style.Pulse(2f)), 1.7f, Style.SoftGlow);
            var m = GUI.matrix;
            GUIUtility.ScaleAroundPivot(new Vector2(Mathf.Max(0.01f, s), Mathf.Max(0.01f, s)) * 1.25f, titleRect.center);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Style.ShadowLabel(titleRect, won ? "Victory" : "Run over", tone);
            GUI.matrix = m;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperCenter;
            GUI.color = new Color(1f, 1f, 1f, Style.EaseOutCubic((t - 0.3f) / 0.4f));
            Widgets.Label(new Rect(inRect.x, inRect.y + 48f, inRect.width, 46f), reason);
            GUI.color = Color.white;
            float rule = Style.EaseOutCubic((t - 0.4f) / 0.5f);
            Style.Box(new Rect(inRect.center.x - 200f * rule, inRect.y + 94f, 400f * rule, 1f), new Color(tone.r, tone.g, tone.b, 0.6f));
            Text.Anchor = TextAnchor.UpperLeft;

            // Stats appear one after another; the points count up.
            int earned = run?.pointsEarned ?? 0;
            int shown = Mathf.RoundToInt(earned * Style.EaseOutCubic((t - 1.3f) / 1.2f));
            bool hub = run?.hubMode == true;
            string asc = run != null && run.ascension > 0 ? $" at Ascension {run.ascension}" : "";
            var lines = new List<string>
            {
                hub ? $"Hub run: deepest node cleared {run.deepestCleared} (final boss at {Hub.FinalDepth}){(run.endless ? ", endless" : "")}{asc}"
                    : $"Reached act {run?.act ?? 1} of {WorldComponent_RoguelikeRun.ActsPerRun}{asc}",
                hub ? $"Frontier nodes cleared: {run.nodes.Count(n => n.depth > 0 && n.state == RunNodeState.Completed)} of {run.nodes.Count(n => n.depth > 0)} revealed"
                    : $"Nodes cleared: {run?.nodes.Count(n => n.state == RunNodeState.Completed) ?? 0} of {run?.nodes.Count ?? 0}",
                $"Squad: {PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists.Count()} alive",
                $"Relics: {(run == null || run.relics.Count == 0 ? "none" : string.Join(", ", run.relics.GroupBy(r => r).Select(g => g.Key.label + (g.Count() > 1 ? $" ×{g.Count()}" : ""))))}",
                $"Blueprints: {(run == null || run.blueprints.Count == 0 ? "none" : string.Join(", ", run.blueprints.Select(b => b.label)))}",
                $"Meta points earned this run: {shown} (you now have {MetaProgress.S.points - earned + shown})",
            };
            float ly = inRect.y + 108f;
            for (int i = 0; i < lines.Count; i++)
            {
                float a = Style.EaseOutCubic((t - 0.6f - i * 0.12f) / 0.35f);
                if (a <= 0f) break;
                float h = Text.CalcHeight(lines[i], inRect.width - 60f);
                var lr = new Rect(inRect.x + 40f - (1f - a) * 24f, ly, inRect.width - 60f, h);
                Style.Tex(new Rect(inRect.x + 22f, ly + h / 2f - 4f, 8f, 8f), Style.Glow, new Color(tone.r, tone.g, tone.b, a));
                GUI.color = new Color(1f, 1f, 1f, a);
                Widgets.Label(lr, lines[i]);
                GUI.color = Color.white;
                ly += h + 12f;
            }
            Style.UpdateAndDraw(particles);

            float y = inRect.yMax - 40f, w = (inRect.width - 30f) / 4f;
            if (Style.Button(new Rect(inRect.x, y, w, 38f), "Spend points", Style.Gold)) Find.WindowStack.Add(new Dialog_Unlocks());
            if (Style.Button(new Rect(inRect.x + (w + 10f), y, w, 38f), "New run", tone)) NewRun.Start();
            if (Style.Button(new Rect(inRect.x + 2 * (w + 10f), y, w, 38f), "Main menu", Style.Parchment)) GenScene.GoToMainMenu();
            // Hub mode: after the final boss the frontier goes on, endless.
            bool keepGoing = won && run is { hubMode: true, endless: false };
            if (Style.Button(new Rect(inRect.x + 3 * (w + 10f), y, w, 38f), keepGoing ? "Keep going" : "Keep playing", keepGoing ? Style.Gold : Style.Parchment)) KeepGoing();
        }

        /// <summary>Closes the screen; after a hub-mode win the run carries on as an endless run.</summary>
        public void KeepGoing()
        {
            var run = WorldComponent_RoguelikeRun.Get;
            if (won && run is { hubMode: true }) run.endless = true;
            Close();
        }
    }
}
