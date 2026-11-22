using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Roguelike
{
    /// <summary>
    /// The act map: a flat, full-screen view of the run's node graph, rows rising from the starting nodes to the boss.
    /// It replaces the world map during a run: the World button opens it, and whenever the world would show (between
    /// nodes, or on a map-less node) it covers the planet. Opened from a node it can be closed; covering the world with
    /// no map to return to, it stays.
    /// </summary>
    public class Dialog_RunMap : Window
    {
        private const float NodeSize = 46f, BossSize = 78f, ColSpacing = 290f;

        private readonly bool coversWorld;
        private float openedAt = -1f;
        private int shownAct;
        private int hoveredId = -1;
        private float hoverAmount;

        public Dialog_RunMap(bool coversWorld = false)
        {
            this.coversWorld = coversWorld;
            // Covering the world it sits under every dialog (events, shops, reward cards); opened on purpose, on top.
            layer = coversWorld ? WindowLayer.GameUI : WindowLayer.Dialog;
            doWindowBackground = false;
            drawShadow = false;
            absorbInputAroundWindow = true;
            preventCameraMotion = true;
            closeOnClickedOutside = false;
            shownAct = WorldComponent_RoguelikeRun.Get?.act ?? 1;
        }

        public override Vector2 InitialSize => new Vector2(UI.screenWidth, UI.screenHeight);

        protected override float Margin => 0f;

        /// <summary>It can be closed unless it's covering the world with no map to go back to.</summary>
        public bool CanClose => !coversWorld || Find.CurrentMap != null;

        /// <summary>Opens the act map, or closes it if it's open (World button, HUD button, node-choice screen).</summary>
        public static void Toggle()
        {
            // A map covering the world sits under other dialogs, so opening on purpose always adds one on top.
            var open = Find.WindowStack.Windows.OfType<Dialog_RunMap>().FirstOrDefault(w => !w.coversWorld);
            if (open == null) Find.WindowStack.Add(new Dialog_RunMap());
            else open.Close();
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            closeOnCancel = CanClose;
            var run = WorldComponent_RoguelikeRun.Get;
            if (run is not { active: true } || coversWorld && !WorldRendererUtility.WorldSelected) Close(false);
        }

        public override void PostClose()
        {
            base.PostClose();
            // Back to the node map, not the planet underneath.
            if (WorldRendererUtility.WorldSelected && Find.CurrentMap != null) CameraJumper.TryHideWorld();
        }

        public override void DoWindowContents(Rect inRect)
        {
            var run = WorldComponent_RoguelikeRun.Get;
            if (run == null) return;
            if (openedAt < 0f) openedAt = Style.Now;
            float t = Style.Now - openedAt;

            // Opaque when covering the planet; otherwise the node map shows faintly through.
            if (coversWorld) Style.Box(inRect, new Color(0.05f, 0.05f, 0.06f, 1f));
            Style.Backdrop(inRect, Style.EaseOutCubic(t / 0.25f));

            DrawHeader(inRect, run, t);
            var area = new Rect(inRect.x + 60f, 150f, inRect.width - 120f, inRect.height - 150f - 90f);
            DrawGraph(area, run, t);
            DrawFooter(inRect, run);
        }

        private void DrawHeader(Rect inRect, WorldComponent_RoguelikeRun run, float t)
        {
            float titleIn = Style.EaseOutCubic((t - 0.05f) / 0.4f);
            var titleRect = new Rect(inRect.x, 30f - (1f - titleIn) * 12f, inRect.width, 40f);
            Style.GlowAround(new Rect(inRect.center.x - 220f, titleRect.y + 4f, 440f, 32f), new Color(Style.Gold.r, Style.Gold.g, Style.Gold.b, 0.14f * titleIn), 1.6f, Style.SoftGlow);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Style.ShadowLabel(titleRect, $"Act {shownAct} of {WorldComponent_RoguelikeRun.ActsPerRun}", new Color(Style.Gold.r, Style.Gold.g, Style.Gold.b, titleIn));
            Text.Font = GameFont.Small;
            var current = run.CurrentNode;
            string sub = run.RunOver ? (run.runWon ? "Run won." : "Run over.")
                : current != null ? $"You are at {(current.IsBoss ? "the boss: " : "")}{current.preset.label}."
                : "Choose your next node.";
            if (shownAct != run.act) sub = shownAct < run.act ? "Behind you." : "Ahead: you reach this act by beating the boss before it.";
            Style.ShadowLabel(new Rect(inRect.x, titleRect.yMax, inRect.width, 22f), sub, new Color(Style.Parchment.r, Style.Parchment.g, Style.Parchment.b, 0.8f * titleIn));

            // Act tabs.
            int acts = WorldComponent_RoguelikeRun.ActsPerRun;
            float w = 100f, gap = 10f, x0 = inRect.center.x - (acts * w + (acts - 1) * gap) / 2f;
            for (int a = 1; a <= acts; a++)
            {
                var r = new Rect(x0 + (a - 1) * (w + gap), titleRect.yMax + 30f, w, 28f);
                var accent = a == shownAct ? Style.Gold : a == run.act ? new Color(0.5f, 0.9f, 0.5f) : Style.Parchment;
                if (Style.Button(r, a == run.act ? $"Act {a} (now)" : $"Act {a}", accent)) shownAct = a;
                if (a == shownAct) Style.Box(new Rect(r.x + 8f, r.yMax + 2f, r.width - 16f, 2f), Style.Gold);
            }
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawFooter(Rect inRect, WorldComponent_RoguelikeRun run)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            var hint = new Rect(inRect.x + 40f, inRect.yMax - 58f, inRect.width - 320f, 36f);
            Style.ShadowLabel(hint, "Hover a node for its briefing. After you clear a node, you pick the next one from the nodes it links to.", new Color(1f, 1f, 1f, 0.55f));
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            if (CanClose && Style.Button(new Rect(inRect.xMax - 220f, inRect.yMax - 64f, 180f, 42f), "Close", Style.Parchment)) Close();
        }

        private void DrawGraph(Rect area, WorldComponent_RoguelikeRun run, float t)
        {
            var actNodes = run.nodes.Where(n => n.act == shownAct).ToList();
            float dt = Event.current.type == EventType.Repaint ? Mathf.Min(Time.unscaledDeltaTime, 0.05f) : 0f;

            // Edges first, under the nodes. The boss's link to the next act is shown as a tag, not a line.
            foreach (var n in actNodes)
                foreach (var to in n.next.Select(run.NodeById).Where(o => o != null && o.act == shownAct))
                    DrawEdge(Pos(n, area, run), Pos(to, area, run), n, to, t);

            int hovered = -1;
            foreach (var n in actNodes)
            {
                float appear = Style.EaseOutCubic((t - 0.1f - n.row * 0.05f) / 0.35f);
                if (appear <= 0f) continue;
                var c = Pos(n, area, run);
                float size = n.IsBoss ? BossSize : NodeSize;
                var hit = new Rect(c.x - size / 2f - 6f, c.y - size / 2f - 6f, size + 12f, size + 12f);
                bool over = Mouse.IsOver(hit);
                if (over) hovered = n.id;
                float grow = n.id == hoveredId ? Style.EaseOutCubic(hoverAmount) : 0f;
                DrawNode(new Rect(c.x - size / 2f, c.y - size / 2f, size, size).ExpandedBy(size * 0.12f * grow), n, appear, area.width);
                if (over) TooltipHandler.TipRegion(hit, () => Tip(n), n.id * 7 + 0x524D);
            }
            if (hovered != hoveredId)
            {
                hoveredId = hovered;
                hoverAmount = 0f;
                if (hovered >= 0) SoundDefOf.Mouseover_Standard.PlayOneShotOnCamera();
            }
            hoverAmount = Mathf.MoveTowards(hoverAmount, 1f, dt * 7f);
        }

        /// <summary>Rows rise from the bottom; each row is centred, with a small fixed jitter so the graph looks hand-drawn.</summary>
        private static Vector2 Pos(RunNode n, Rect area, WorldComponent_RoguelikeRun run)
        {
            int rows = WorldComponent_RoguelikeRun.RowCount;
            int count = run.Row(n.act, n.row).Count();
            float y = area.yMax - area.height * (n.row + 0.5f) / rows;
            float span = Mathf.Min(area.width * 0.75f, ColSpacing * (count - 1));
            float x = area.center.x + (count <= 1 ? 0f : (n.col / (float)(count - 1) - 0.5f) * span);
            if (!n.IsBoss)
            {
                x += (Rand.ValueSeeded(n.id * 31 + 7) - 0.5f) * 60f;
                y += (Rand.ValueSeeded(n.id * 57 + 3) - 0.5f) * 24f;
            }
            return new Vector2(x, y);
        }

        private static void DrawEdge(Vector2 a, Vector2 b, RunNode from, RunNode to, float t)
        {
            bool fromTaken = from.state == RunNodeState.Current || from.state == RunNodeState.Completed;
            bool open = fromTaken && to.state == RunNodeState.Available;
            bool taken = fromTaken && (to.state == RunNodeState.Current || to.state == RunNodeState.Completed);
            bool dead = to.state == RunNodeState.Skipped || from.state == RunNodeState.Skipped;
            var color = open ? Style.Gold : taken ? new Color(Style.Parchment.r, Style.Parchment.g, Style.Parchment.b, 0.9f)
                : dead ? new Color(0.4f, 0.4f, 0.4f, 0.25f) : new Color(0.75f, 0.75f, 0.75f, 0.45f);
            float width = open ? 3f : taken ? 2.5f : 1.6f;
            float reveal = Style.EaseOutCubic((t - 0.15f) / 0.5f);
            // Stop short of the icons, so lines meet the nodes' edges rather than their centres.
            var dir = (b - a).normalized;
            a += dir * (from.IsBoss ? BossSize : NodeSize) * 0.55f;
            b -= dir * (to.IsBoss ? BossSize : NodeSize) * 0.55f;
            b = Vector2.Lerp(a, b, reveal);
            if (taken)
            {
                Widgets.DrawLine(a, b, color, width);
                return;
            }
            // Dashed, and on open paths the dashes flow toward the choices.
            float len = (b - a).magnitude, dash = 9f, gap = 7f;
            float offset = open ? Mathf.Repeat(Style.Now * 24f, dash + gap) : 0f;
            for (float s = offset - (dash + gap); s < len; s += dash + gap)
            {
                float s0 = Mathf.Max(0f, s), s1 = Mathf.Min(len, s + dash);
                if (s1 > s0) Widgets.DrawLine(a + dir * s0, a + dir * s1, color, width);
            }
        }

        private static void DrawNode(Rect r, RunNode n, float appear, float areaWidth)
        {
            var tint = StateColor(n);
            tint.a *= appear;
            bool available = n.state == RunNodeState.Available, current = n.state == RunNodeState.Current;
            // Dark disc for contrast, then an aura on the nodes that matter right now.
            Style.Tex(r.ExpandedBy(r.width * 0.35f), Style.SoftGlow, new Color(0f, 0f, 0f, 0.75f * appear));
            if (available) Style.GlowAround(r, new Color(Style.Gold.r, Style.Gold.g, Style.Gold.b, (0.3f + 0.35f * Style.Pulse(3f, n.id)) * appear), 1.7f);
            if (current) Style.GlowAround(r, new Color(0.45f, 1f, 0.45f, (0.35f + 0.25f * Style.Pulse(2f)) * appear), 1.8f);
            if (n.IsBoss) Style.GlowAround(r, new Color(0.85f, 0.25f, 0.2f, 0.3f * appear), 1.5f);
            var icon = NodeIcons.For(n);
            if (icon != null) Style.Tex(r, icon, tint);
            else Style.Box(r.ContractedBy(r.width * 0.3f), tint);
            if (n.state == RunNodeState.Completed)
            {
                Style.Tex(new Rect(r.xMax - 14f, r.yMax - 16f, 18f, 18f), Widgets.CheckboxOnTex, new Color(1f, 1f, 1f, appear));
            }

            // Under the icon: the biome (map nodes) and, for the boss, where it leads.
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperCenter;
            string under = n.IsBoss ? (n.act < WorldComponent_RoguelikeRun.ActsPerRun ? $"Boss · then act {n.act + 1}" : "Final boss")
                : n.preset?.HasMap == true && n.tile.Valid ? Find.WorldGrid[n.tile].PrimaryBiome.LabelCap.ToString() : n.preset?.LabelCap.ToString();
            if (!under.NullOrEmpty())
                Style.ShadowLabel(new Rect(r.center.x - 80f, r.yMax + 2f, 160f, 18f), under, new Color(Style.Parchment.r, Style.Parchment.g, Style.Parchment.b, (n.state == RunNodeState.Skipped ? 0.3f : 0.75f) * appear));
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private static Color StateColor(RunNode n) => n.state switch
        {
            RunNodeState.Available => Color.Lerp(new Color(0.85f, 0.78f, 0.55f), new Color(1f, 0.97f, 0.82f), Style.Pulse(3f, n.id)),
            RunNodeState.Current => new Color(0.55f, 1f, 0.55f),
            RunNodeState.Completed => new Color(0.6f, 0.6f, 0.6f),
            RunNodeState.Skipped => new Color(0.32f, 0.32f, 0.32f, 0.6f),
            _ => n.IsBoss ? new Color(0.85f, 0.35f, 0.3f) : new Color(0.72f, 0.72f, 0.72f, 0.9f),
        };

        /// <summary>
        /// Whether the node's objective is final. Expeditions not yet offered are re-rolled to fit the squad when they
        /// come up, so until then the map only says what kind of node it is.
        /// </summary>
        private static bool Revealed(RunNode n) =>
            n.offered || n.IsBoss || n.preset.kind != NodeKind.Expedition || n.act == 1 && n.row == 0
            || n.state == RunNodeState.Current || n.state == RunNodeState.Completed;

        private static string Tip(RunNode n)
        {
            var sb = new StringBuilder();
            bool revealed = Revealed(n);
            sb.AppendLine(((n.IsBoss ? "Boss: " : "") + (revealed ? n.preset.LabelCap.ToString() : "Expedition")).Colorize(ColoredText.TipSectionTitleColor));
            sb.AppendLine(n.state switch
            {
                RunNodeState.Available => "Available: you can choose it next.",
                RunNodeState.Current => "You are here.",
                RunNodeState.Completed => "Cleared.",
                RunNodeState.Skipped => "Skipped.",
                _ => "Not reachable yet.",
            });
            if (n.TerrainLine is { } terrain) sb.AppendLine(terrain);
            sb.AppendLine();
            if (!revealed) sb.Append("Base building. Its objective is set when you can choose it, to suit your squad.");
            else sb.Append(n.RewardText + "\n\n" + n.Briefing);
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>During a run the World button opens the act map instead of the planet.</summary>
    [HarmonyPatch(typeof(MainButtonWorker_ToggleWorld), nameof(MainButtonWorker_ToggleWorld.Activate))]
    public static class Patch_ToggleWorld_OpenRunMap
    {
        public static bool Prefix()
        {
            if (WorldComponent_RoguelikeRun.Get is not { active: true }) return true;
            Dialog_RunMap.Toggle();
            return false;
        }
    }
}
