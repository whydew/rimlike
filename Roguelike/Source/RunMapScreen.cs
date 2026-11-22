using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Roguelike
{
    /// <summary>
    /// The act map: a flat, full-screen view of the run's node graph, rows rising from the starting nodes to the boss,
    /// on a framed map sheet. A legend and run summary sit on the left; the right shows the hovered node's card (or the
    /// current node's). It replaces the world map during a run: the World button opens it, and whenever the world would
    /// show (between nodes, or on a map-less node) it covers the planet. Opened on purpose it can be closed; covering
    /// the world with no map to return to, it stays.
    /// </summary>
    public class Dialog_RunMap : Window
    {
        private const float NodeSize = 50f, BossSize = 84f, ColSpacing = 200f, SheetMaxWidth = 760f, SidePanel = 250f;

        private static readonly Color SheetC = new Color(0.16f, 0.145f, 0.12f);
        private static readonly Color InkC = new Color(0.93f, 0.86f, 0.7f);

        private readonly bool coversWorld;
        private float openedAt = -1f;
        private int shownAct;
        private int hoveredId = -1;
        private bool sidesShown;
        private float hoverAmount;
        /// <summary>Hub mode: pixels between rings (scroll to zoom) and how far the view is dragged from the hub.</summary>
        private float ringPx = -1f;
        private Vector2 pan;

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

        // ---------- Node kinds ----------

        public static string KindLabel(RunNode n) => n.IsBoss ? "Boss" : n.preset.kind == NodeKind.Hub ? "Your hub" : KindLabel(n.preset.kind);

        public static string KindLabel(NodeKind k) => k switch
        {
            NodeKind.Expedition => "Expedition",
            NodeKind.CombatAlerted => "Ambush",
            NodeKind.CombatUnaware => "Raider camp",
            NodeKind.Escort => "Escort",
            NodeKind.Event => "Event",
            NodeKind.Elite => "Elite",
            NodeKind.Rest => "Rest site",
            NodeKind.Merchant => "Merchant",
            NodeKind.Treasure => "Treasure",
            _ => k.ToString(),
        };

        private static readonly Color BossC = new Color(0.9f, 0.3f, 0.25f);

        /// <summary>Each kind's colour: the ring around its node and its legend swatch.</summary>
        public static Color KindColor(RunNode n) => n.IsBoss ? BossC : KindColor(n.preset.kind);

        public static Color KindColor(NodeKind k) => k switch
        {
            NodeKind.Expedition => new Color(0.62f, 0.8f, 0.5f),
            NodeKind.CombatAlerted or NodeKind.CombatUnaware => new Color(0.92f, 0.45f, 0.38f),
            NodeKind.Escort => new Color(0.95f, 0.65f, 0.3f),
            NodeKind.Elite => new Color(0.78f, 0.5f, 0.95f),
            NodeKind.Event => new Color(0.5f, 0.72f, 0.98f),
            NodeKind.Rest => new Color(0.45f, 0.88f, 0.7f),
            NodeKind.Merchant => new Color(0.95f, 0.82f, 0.38f),
            NodeKind.Treasure => new Color(0.98f, 0.9f, 0.5f),
            NodeKind.Hub => Style.Gold,
            _ => Style.Parchment,
        };

        // ---------- Drawing ----------

        public override void DoWindowContents(Rect inRect)
        {
            var run = WorldComponent_RoguelikeRun.Get;
            if (run == null) return;
            if (openedAt < 0f) openedAt = Style.Now;
            float t = Style.Now - openedAt;

            // Opaque when covering the planet; otherwise the node map shows faintly through.
            if (coversWorld) Style.Box(inRect, new Color(0.04f, 0.04f, 0.05f, 1f));
            Style.Backdrop(inRect, Style.EaseOutCubic(t / 0.25f));

            DrawHeader(inRect, run, t);
            // Hub mode has no act tabs and a square sheet for the rings.
            float top = run.hubMode ? 100f : 132f, bottom = inRect.height - 78f;
            bool sides = inRect.width >= SheetMaxWidth * 0.8f + 2f * (SidePanel + 60f);
            sidesShown = sides;
            float sheetW = Mathf.Min(run.hubMode ? bottom - top : SheetMaxWidth, inRect.width - (sides ? 2f * (SidePanel + 60f) : 40f));
            var sheet = new Rect(inRect.center.x - sheetW / 2f, top + (1f - Style.EaseOutCubic(t / 0.4f)) * 30f, sheetW, bottom - top);

            DrawSheet(sheet, t);
            if (run.hubMode) DrawFrontier(sheet.ContractedBy(24f), run, t);
            else DrawGraph(sheet.ContractedBy(24f), run, t);
            if (sides)
            {
                DrawLegend(new Rect(sheet.x - SidePanel - 40f, top + 8f, SidePanel, sheet.height - 16f), run, t);
                DrawDetail(new Rect(sheet.xMax + 40f, top + 8f, SidePanel, sheet.height - 16f), run, t);
            }
            DrawFooter(inRect);
        }

        private void DrawHeader(Rect inRect, WorldComponent_RoguelikeRun run, float t)
        {
            float titleIn = Style.EaseOutCubic((t - 0.05f) / 0.4f);
            var titleRect = new Rect(inRect.x, 24f - (1f - titleIn) * 12f, inRect.width, 40f);
            Style.GlowAround(new Rect(inRect.center.x - 220f, titleRect.y + 4f, 440f, 32f), new Color(Style.Gold.r, Style.Gold.g, Style.Gold.b, 0.14f * titleIn), 1.6f, Style.SoftGlow);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            var current = run.CurrentNode;
            if (run.hubMode)
            {
                Style.ShadowLabel(titleRect, "The frontier", new Color(Style.Gold.r, Style.Gold.g, Style.Gold.b, titleIn));
                Text.Font = GameFont.Small;
                string hubSub = run.RunOver ? (run.runWon ? "Run won." : "Run over.")
                    : current != null ? $"A team is out at {(current.IsBoss ? "the boss: " : "")}{current.preset.label} (depth {current.depth})."
                    : run.endless ? $"Endless: the deepest node you've cleared is {run.deepestCleared} out."
                    : $"Deepest node cleared: {run.deepestCleared} of {Hub.FinalDepth}. Send a team from the hub.";
                Style.ShadowLabel(new Rect(inRect.x, titleRect.yMax, inRect.width, 22f), hubSub, new Color(Style.Parchment.r, Style.Parchment.g, Style.Parchment.b, 0.8f * titleIn));
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }
            Style.ShadowLabel(titleRect, $"Act {shownAct} of {WorldComponent_RoguelikeRun.ActsPerRun}", new Color(Style.Gold.r, Style.Gold.g, Style.Gold.b, titleIn));
            Text.Font = GameFont.Small;
            string sub = run.RunOver ? (run.runWon ? "Run won." : "Run over.")
                : shownAct < run.act ? "Behind you."
                : shownAct > run.act ? "Ahead: you reach this act by beating the boss before it."
                : current != null ? $"You are at {(current.IsBoss ? "the boss: " : "")}{current.preset.label}."
                : "Choose your next node.";
            Style.ShadowLabel(new Rect(inRect.x, titleRect.yMax, inRect.width, 22f), sub, new Color(Style.Parchment.r, Style.Parchment.g, Style.Parchment.b, 0.8f * titleIn));

            // Act tabs.
            int acts = WorldComponent_RoguelikeRun.ActsPerRun;
            float w = 110f, gap = 8f, x0 = inRect.center.x - (acts * w + (acts - 1) * gap) / 2f;
            for (int a = 1; a <= acts; a++)
            {
                var r = new Rect(x0 + (a - 1) * (w + gap), titleRect.yMax + 28f, w, 28f);
                var accent = a == shownAct ? Style.Gold : a == run.act ? new Color(0.5f, 0.9f, 0.5f) : Style.Parchment;
                if (Style.Button(r, a == run.act ? $"Act {a} · now" : $"Act {a}", accent) && shownAct != a)
                {
                    shownAct = a;
                    openedAt = Style.Now - 0.1f; // replay the nodes' entrance for the new act
                }
                if (a == shownAct) Style.Box(new Rect(r.x + 8f, r.yMax + 2f, r.width - 16f, 2f), Style.Gold);
            }
            Text.Anchor = TextAnchor.UpperLeft;
        }

        /// <summary>The map sheet: a dark, warm panel with a double gold frame and corner studs.</summary>
        private static void DrawSheet(Rect r, float t)
        {
            float a = Style.EaseOutCubic(t / 0.35f);
            Style.Tex(new Rect(r.x + 10f, r.y + 18f, r.width, r.height).ExpandedBy(24f), Style.SoftGlow, new Color(0f, 0f, 0f, 0.75f * a));
            Style.Tex(r, Style.CardBody, new Color(1f, 1f, 1f, a));
            Style.Box(r, new Color(SheetC.r, SheetC.g, SheetC.b, 0.55f * a));
            Style.Tex(r.ExpandedBy(r.width * 0.1f), Style.Vignette, new Color(0f, 0f, 0f, 0.45f * a));
            Style.Border(r, new Color(Style.Gold.r, Style.Gold.g, Style.Gold.b, 0.85f * a), 2f);
            Style.Border(r.ContractedBy(6f), new Color(Style.Gold.r, Style.Gold.g, Style.Gold.b, 0.25f * a), 1f);
            foreach (var c in new[] { new Vector2(r.x, r.y), new Vector2(r.xMax, r.y), new Vector2(r.x, r.yMax), new Vector2(r.xMax, r.yMax) })
            {
                Style.Tex(new Rect(c.x - 10f, c.y - 10f, 20f, 20f), Style.Glow, new Color(Style.Gold.r, Style.Gold.g, Style.Gold.b, 0.8f * a));
                Style.Tex(new Rect(c.x - 4f, c.y - 4f, 8f, 8f), Style.Disc, new Color(1f, 0.95f, 0.8f, a));
            }
        }

        private void DrawFooter(Rect inRect)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            var hint = new Rect(inRect.x + 40f, inRect.yMax - 58f, inRect.width - 320f, 36f);
            Style.ShadowLabel(hint, WorldComponent_RoguelikeRun.Get?.hubMode == true
                ? "Hover a node for its briefing. Scroll to zoom, drag to pan. Send teams out from the hub; clearing a node opens the next ring beyond it."
                : "Hover a node for its briefing. After you clear a node, you pick the next one from the nodes it links to.", new Color(1f, 1f, 1f, 0.5f));
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            if (CanClose && Style.Button(new Rect(inRect.xMax - 220f, inRect.yMax - 64f, 180f, 42f), "Close", Style.Parchment)) Close();
        }

        // ---------- Graph ----------

        private void DrawGraph(Rect area, WorldComponent_RoguelikeRun run, float t)
        {
            var actNodes = run.nodes.Where(n => n.act == shownAct).ToList();
            float dt = Event.current.type == EventType.Repaint ? Mathf.Min(Time.unscaledDeltaTime, 0.05f) : 0f;
            int rows = WorldComponent_RoguelikeRun.RowCount;

            // Faint dotted row guides with floor numbers.
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            for (int row = 0; row < rows; row++)
            {
                float y = RowY(row, area);
                for (float x = area.x + 26f; x < area.xMax - 6f; x += 14f)
                    Style.Box(new Rect(x, y, 6f, 1f), new Color(InkC.r, InkC.g, InkC.b, 0.07f));
                if (row < rows - 1) Style.ShadowLabel(new Rect(area.x, y - 9f, 30f, 18f), (row + 1).ToString(), new Color(InkC.r, InkC.g, InkC.b, 0.3f));
            }
            Text.Anchor = TextAnchor.UpperLeft;

            // Edges first, under the nodes. The boss's link to the next act is shown under the boss, not as a line.
            foreach (var n in actNodes)
                foreach (var to in n.next.Select(run.NodeById).Where(o => o != null && o.act == shownAct))
                    DrawEdge(Pos(n, area, run), Pos(to, area, run), n, to, t);

            int hovered = -1;
            foreach (var n in actNodes)
            {
                float appear = Mathf.Clamp01((t - 0.12f - n.row * 0.06f) / 0.4f);
                if (appear <= 0f) continue;
                var c = Pos(n, area, run);
                float size = n.IsBoss ? BossSize : NodeSize;
                var hit = new Rect(c.x - size / 2f - 6f, c.y - size / 2f - 6f, size + 12f, size + 12f);
                if (Mouse.IsOver(hit)) hovered = n.id;
                // Too narrow for the side card: the briefing comes as a tooltip instead.
                if (!sidesShown && Mouse.IsOver(hit))
                    (Revealed(n) ? NodeInfo.ForNode(n) : new InfoPanel("Expedition", "Base building", KindColor(n)).Add(n.TerrainLine, InfoPanel.Terrain)
                        .Add("Its objective is set when you can choose it, to suit your squad.", InfoPanel.Dim)).Show();
                float grow = n.id == hoveredId ? Style.EaseOutCubic(hoverAmount) : 0f;
                if (n.state == RunNodeState.Available) c.y -= 2f * Mathf.Sin(Style.Now * 2.4f + n.id);
                float s = size * Style.EaseOutBack(appear) * (1f + 0.14f * grow);
                DrawNode(new Rect(c.x - s / 2f, c.y - s / 2f, s, s), n, Style.EaseOutCubic(appear), grow);
            }
            if (hovered != hoveredId)
            {
                hoveredId = hovered;
                hoverAmount = 0f;
                if (hovered >= 0) SoundDefOf.Mouseover_Standard.PlayOneShotOnCamera();
            }
            hoverAmount = Mathf.MoveTowards(hoverAmount, 1f, dt * 7f);
        }

        /// <summary>
        /// Hub mode: the hub in the centre and one ring per depth. Each node owns a slice of its parent's angle, sized by
        /// how many open branches lie beyond it, so branches fan outward without crossing however far the frontier spreads.
        /// </summary>
        private void DrawFrontier(Rect area, WorldComponent_RoguelikeRun run, float t)
        {
            var hub = run.HubNode;
            if (hub == null) return;
            float dt = Event.current.type == EventType.Repaint ? Mathf.Min(Time.unscaledDeltaTime, 0.05f) : 0f;
            var shown = run.nodes.Where(n => n.state != RunNodeState.Skipped).ToList();
            int maxDepth = Mathf.Max(3, shown.Max(n => n.depth));

            // The frontier never ends, so the sheet is a window onto it: scroll to zoom, drag to pan.
            GUI.BeginGroup(area);
            var local = new Rect(0f, 0f, area.width, area.height);
            if (ringPx < 0f) ringPx = Mathf.Clamp((Mathf.Min(local.width, local.height) / 2f - 34f) / maxDepth, 46f, 140f);
            if (Mouse.IsOver(local))
            {
                if (Event.current.type == EventType.ScrollWheel)
                {
                    ringPx = Mathf.Clamp(ringPx * (1f - Event.current.delta.y * 0.04f), 20f, 140f);
                    Event.current.Use();
                }
                else if (Event.current.type == EventType.MouseDrag && Event.current.button == 0)
                {
                    pan += Event.current.delta;
                    Event.current.Use();
                }
            }
            float ring = ringPx;
            var center = local.center + pan;
            float scale = Mathf.Clamp(ring / 90f, 0.45f, 1f); // nodes shrink as you zoom out

            var pos = new Dictionary<int, Vector2>();
            // Each open branch weighs 1/depth: its angle times its ring's radius is then the same for every branch, so
            // nodes sit evenly spaced along their rings however deep the frontier goes.
            var leaves = new Dictionary<int, float>();
            float Leaves(RunNode n) => leaves.TryGetValue(n.id, out float l) ? l : leaves[n.id] = n.next.Count == 0 ? 1f / Mathf.Max(1, n.depth) : n.next.Sum(id => Leaves(run.NodeById(id)));
            void Place(RunNode n, float a0, float a1)
            {
                float mid = (a0 + a1) / 2f * Mathf.Deg2Rad;
                pos[n.id] = center + new Vector2(Mathf.Cos(mid), Mathf.Sin(mid)) * ring * n.depth;
                float total = Leaves(n), a = a0;
                foreach (var child in n.next.Select(run.NodeById))
                {
                    float span = (a1 - a0) * Leaves(child) / total;
                    Place(child, a, a + span);
                    a += span;
                }
            }
            // Start at the top, centred on the first branch.
            float first = hub.next.Count > 0 ? 360f * Leaves(run.NodeById(hub.next[0])) / Leaves(hub) : 0f;
            Place(hub, -90f - first / 2f, 270f - first / 2f);

            // Dotted rings with their depth; boss depths in red.
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            for (int d = 1; d <= maxDepth; d++)
            {
                float rad = ring * d;
                bool bossRing = d % Hub.BossEvery == 0;
                var c = bossRing ? new Color(BossC.r, BossC.g, BossC.b, 0.35f) : new Color(InkC.r, InkC.g, InkC.b, 0.16f);
                int dots = Mathf.Clamp(Mathf.RoundToInt(rad * 2f * Mathf.PI / 12f), 24, 360);
                for (int i = 0; i < dots; i++)
                {
                    float ang = i * Mathf.PI * 2f / dots;
                    var p = center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad;
                    if (local.Contains(p)) Style.Box(new Rect(p.x - 2f, p.y - 0.75f, 4f, 1.5f), c);
                }
                if (ring >= 30f || bossRing) Style.ShadowLabel(new Rect(center.x - 15f, center.y - rad - 9f, 30f, 18f), d.ToString(), new Color(c.r, c.g, c.b, 0.9f));
            }
            Text.Anchor = TextAnchor.UpperLeft;

            foreach (var n in shown)
                foreach (var to in n.next.Select(run.NodeById).Where(o => o != null && pos.ContainsKey(o.id)))
                    DrawEdge(pos[n.id], pos[to.id], n, to, t, scale);

            int hovered = -1;
            foreach (var n in shown.Where(n => pos.ContainsKey(n.id)).OrderBy(n => n.depth))
            {
                float appear = Mathf.Clamp01((t - 0.12f - Mathf.Min(n.depth, 8) * 0.05f) / 0.4f);
                if (appear <= 0f) continue;
                var c = pos[n.id];
                float size = (n.IsBoss || n == hub ? BossSize : NodeSize) * scale;
                var hit = new Rect(c.x - size / 2f - 6f, c.y - size / 2f - 6f, size + 12f, size + 12f);
                if (!hit.Overlaps(local)) continue;
                if (Mouse.IsOver(hit)) hovered = n.id;
                if (!sidesShown && Mouse.IsOver(hit)) NodeInfo.ForNode(n).Show();
                float grow = n.id == hoveredId ? Style.EaseOutCubic(hoverAmount) : 0f;
                if (n.state == RunNodeState.Available) c.y -= 2f * Mathf.Sin(Style.Now * 2.4f + n.id);
                float s = size * Style.EaseOutBack(appear) * (1f + 0.14f * grow);
                DrawNode(new Rect(c.x - s / 2f, c.y - s / 2f, s, s), n, Style.EaseOutCubic(appear), grow, labels: scale > 0.7f || n.id == hoveredId);
            }
            GUI.EndGroup();
            if (hovered != hoveredId)
            {
                hoveredId = hovered;
                hoverAmount = 0f;
                if (hovered >= 0) SoundDefOf.Mouseover_Standard.PlayOneShotOnCamera();
            }
            hoverAmount = Mathf.MoveTowards(hoverAmount, 1f, dt * 7f);
        }

        private static float RowY(int row, Rect area) => area.yMax - area.height * (row + 0.5f) / WorldComponent_RoguelikeRun.RowCount;

        /// <summary>Rows rise from the bottom; each row is centred, with a small fixed jitter so the graph looks hand-drawn.</summary>
        private static Vector2 Pos(RunNode n, Rect area, WorldComponent_RoguelikeRun run)
        {
            int count = run.Row(n.act, n.row).Count();
            float y = RowY(n.row, area);
            float span = Mathf.Min(area.width - 2f * NodeSize - 40f, ColSpacing * (count - 1));
            float x = area.center.x + 12f + (count <= 1 ? 0f : (n.col / (float)(count - 1) - 0.5f) * span);
            if (!n.IsBoss)
            {
                x += (Rand.ValueSeeded(n.id * 31 + 7) - 0.5f) * 44f;
                y += (Rand.ValueSeeded(n.id * 57 + 3) - 0.5f) * 18f;
            }
            return new Vector2(x, y);
        }

        private static void DrawEdge(Vector2 a, Vector2 b, RunNode from, RunNode to, float t, float scale = 1f)
        {
            bool fromTaken = from.state == RunNodeState.Current || from.state == RunNodeState.Completed;
            bool open = fromTaken && to.state == RunNodeState.Available;
            bool taken = fromTaken && (to.state == RunNodeState.Current || to.state == RunNodeState.Completed);
            bool dead = to.state == RunNodeState.Skipped || from.state == RunNodeState.Skipped;
            var color = open ? Style.Gold : taken ? new Color(InkC.r, InkC.g, InkC.b, 0.95f)
                : dead ? new Color(InkC.r, InkC.g, InkC.b, 0.1f) : new Color(InkC.r, InkC.g, InkC.b, 0.38f);
            float width = open ? 3f : taken ? 3.5f : 2f;
            float reveal = Style.EaseOutCubic((t - 0.2f) / 0.55f);
            // Stop short of the nodes, so lines meet their rims rather than their centres.
            var dir = (b - a).normalized;
            a += dir * (from.IsBoss || from.preset.kind == NodeKind.Hub ? BossSize : NodeSize) * 0.6f * scale;
            b -= dir * (to.IsBoss ? BossSize : NodeSize) * 0.6f * scale;
            b = Vector2.Lerp(a, b, reveal);
            if (taken)
            {
                Widgets.DrawLine(a, b, new Color(0f, 0f, 0f, 0.5f), width + 3f); // ink shadow
                Widgets.DrawLine(a, b, color, width);
                return;
            }
            // Dashed; on open paths the dashes flow toward the choices and glow.
            float len = (b - a).magnitude, dash = open ? 10f : 7f, gap = open ? 6f : 8f;
            float offset = open ? Mathf.Repeat(Style.Now * 28f, dash + gap) : 0f;
            for (float s = offset - (dash + gap); s < len; s += dash + gap)
            {
                float s0 = Mathf.Max(0f, s), s1 = Mathf.Min(len, s + dash);
                if (s1 <= s0) continue;
                if (open) Widgets.DrawLine(a + dir * s0, a + dir * s1, new Color(Style.Gold.r, Style.Gold.g, Style.Gold.b, 0.25f), width + 4f);
                Widgets.DrawLine(a + dir * s0, a + dir * s1, color, width);
            }
        }

        private static void DrawNode(Rect r, RunNode n, float appear, float hover, bool labels = true)
        {
            var kind = KindColor(n);
            bool available = n.state == RunNodeState.Available, current = n.state == RunNodeState.Current;
            bool done = n.state == RunNodeState.Completed, skipped = n.state == RunNodeState.Skipped;
            float fade = skipped ? 0.35f : 1f;

            // Halo for the nodes that matter now, then a shadow, the disc, the kind-coloured rim and the icon.
            if (available) Style.GlowAround(r, new Color(Style.Gold.r, Style.Gold.g, Style.Gold.b, (0.35f + 0.35f * Style.Pulse(3f, n.id)) * appear), 1.9f);
            if (current) Style.GlowAround(r, new Color(0.45f, 1f, 0.45f, (0.4f + 0.25f * Style.Pulse(2f)) * appear), 2f);
            if (n.IsBoss) Style.GlowAround(r, new Color(BossC.r, BossC.g, BossC.b, (0.3f + 0.15f * Style.Pulse(1.2f)) * appear), 1.7f);
            if (hover > 0f) Style.GlowAround(r, new Color(kind.r, kind.g, kind.b, 0.4f * hover * appear), 1.8f);
            Style.Tex(new Rect(r.x + 3f, r.y + 5f, r.width, r.height), Style.Disc, new Color(0f, 0f, 0f, 0.6f * appear));
            var body = done ? new Color(0.1f, 0.1f, 0.1f) : Color.Lerp(new Color(0.12f, 0.11f, 0.1f), kind, 0.18f);
            Style.Tex(r, Style.Disc, new Color(body.r, body.g, body.b, appear * (skipped ? 0.6f : 1f)));
            var rim = available ? Color.Lerp(kind, Style.Gold, 0.5f * Style.Pulse(3f, n.id)) : current ? new Color(0.55f, 1f, 0.55f) : kind;
            Style.Tex(r, Style.Ring, new Color(rim.r, rim.g, rim.b, appear * fade * (done ? 0.55f : 1f)));
            var icon = NodeIcons.For(n);
            var iconTint = done ? new Color(0.62f, 0.62f, 0.62f) : skipped ? new Color(0.4f, 0.4f, 0.4f) : Color.Lerp(Color.white, kind, 0.25f);
            if (icon != null) Style.Tex(r.ContractedBy(r.width * 0.2f), icon, new Color(iconTint.r, iconTint.g, iconTint.b, appear * fade));
            if (done && n.preset.kind != NodeKind.Hub)
                Style.Tex(new Rect(r.xMax - r.width * 0.36f, r.yMax - r.width * 0.36f, r.width * 0.38f, r.width * 0.38f), Widgets.CheckboxOnTex, new Color(1f, 1f, 1f, appear));

            Text.Font = GameFont.Tiny;
            if (current)
            {
                // A "you are here" tag above.
                var tag = new Rect(r.center.x - 22f, r.y - 20f, 44f, 16f);
                Style.Box(tag, new Color(0.2f, 0.45f, 0.2f, 0.9f * appear));
                Text.Anchor = TextAnchor.MiddleCenter;
                Style.ShadowLabel(tag, "YOU", new Color(0.85f, 1f, 0.85f, appear));
            }
            if (!labels)
            {
                Text.Font = GameFont.Small;
                return;
            }
            // Under the node: its kind, and the biome for map nodes (or where the boss leads).
            Text.Anchor = TextAnchor.UpperCenter;
            bool hub = WorldComponent_RoguelikeRun.Get?.hubMode == true;
            string line2 = n.IsBoss && hub ? (n.depth >= Hub.FinalDepth && n.depth < Hub.FinalDepth + Hub.BossEvery ? "final boss" : $"depth {n.depth}")
                : n.IsBoss ? (n.act < WorldComponent_RoguelikeRun.ActsPerRun ? $"then act {n.act + 1}" : "final boss")
                : n.preset.HasMap && n.tile.Valid ? Find.WorldGrid[n.tile].PrimaryBiome.LabelCap.ToString() : null;
            float a = (skipped ? 0.3f : done ? 0.55f : 0.9f) * appear;
            Style.ShadowLabel(new Rect(r.center.x - 80f, r.yMax + 2f, 160f, 16f), KindLabel(n), new Color(kind.r, kind.g, kind.b, a));
            if (line2 != null) Style.ShadowLabel(new Rect(r.center.x - 80f, r.yMax + 15f, 160f, 16f), line2, new Color(InkC.r, InkC.g, InkC.b, a * 0.75f));
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        // ---------- Side panels ----------

        private static void Panel(Rect r, string title, float a)
        {
            Style.Tex(r, Style.CardBody, new Color(1f, 1f, 1f, 0.92f * a));
            Style.Border(r, new Color(Style.Gold.r, Style.Gold.g, Style.Gold.b, 0.45f * a), 1f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Style.ShadowLabel(new Rect(r.x + 12f, r.y + 6f, r.width - 24f, 18f), title.ToUpperInvariant(), new Color(Style.Gold.r, Style.Gold.g, Style.Gold.b, a));
            Style.Box(new Rect(r.x + 12f, r.y + 26f, r.width - 24f, 1f), new Color(Style.Gold.r, Style.Gold.g, Style.Gold.b, 0.3f * a));
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static readonly NodeKind[] LegendKinds =
            { NodeKind.Expedition, NodeKind.CombatAlerted, NodeKind.CombatUnaware, NodeKind.Escort, NodeKind.Elite, NodeKind.Event, NodeKind.Rest, NodeKind.Merchant, NodeKind.Treasure };

        /// <summary>Left: what each icon means, and the run so far.</summary>
        private static void DrawLegend(Rect r, WorldComponent_RoguelikeRun run, float t)
        {
            float a = Style.EaseOutCubic((t - 0.2f) / 0.4f);
            if (a <= 0f) return;
            var kinds = LegendKinds.Cast<NodeKind?>().Append(null).ToList();
            if (run.hubMode) kinds.Insert(0, NodeKind.Hub);
            var legend = new Rect(r.x, r.y, r.width, 40f + kinds.Count * 26f);
            Panel(legend, "Legend", a);
            float y = legend.y + 34f;
            foreach (var k in kinds)
            {
                var c = k == null ? BossC : KindColor(k.Value);
                var dot = new Rect(legend.x + 14f, y + 2f, 20f, 20f);
                Style.Tex(dot, Style.Disc, new Color(0.12f, 0.11f, 0.1f, a));
                Style.Tex(dot, Style.Ring, new Color(c.r, c.g, c.b, a));
                var icon = NodeIcons.ForKind(k);
                if (icon != null) Style.Tex(dot.ContractedBy(4f), icon, new Color(1f, 1f, 1f, a));
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Style.ShadowLabel(new Rect(dot.xMax + 10f, y, legend.width - 60f, 24f), k == null ? "Boss" : k == NodeKind.Hub ? "Your hub" : KindLabel(k.Value), new Color(Style.Parchment.r, Style.Parchment.g, Style.Parchment.b, a));
                y += 26f;
            }

            // The run so far.
            string asc = run.ascension > 0 ? $" · Ascension {run.ascension}" : "";
            var lines = new List<string>
            {
                run.hubMode ? $"Depth {run.deepestCleared} of {Hub.FinalDepth}{(run.endless ? " · endless" : "")}{asc}" : $"Act {run.act} of {WorldComponent_RoguelikeRun.ActsPerRun}{asc}",
                run.hubMode ? $"Cleared: {run.nodes.Count(n => n.depth > 0 && n.state == RunNodeState.Completed)} · open: {Hub.Frontier(run).Count()}"
                    : $"Nodes cleared: {run.nodes.Count(n => n.state == RunNodeState.Completed)}",
                $"Relics: {run.relics.Count} · Blueprints: {run.blueprints.Count}",
                $"Points earned: {run.pointsEarned}",
            };
            var squad = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists.Where(p => p.RaceProps.Humanlike).ToList();
            if (squad.Count > 0)
            {
                float worst = squad.Max(Wear.Of);
                lines.Add($"Squad: {squad.Count}" + (worst > 0f ? $" · road wear up to {worst:P0}" : ""));
            }
            var summary = new Rect(r.x, legend.yMax + 16f, r.width, 40f + lines.Count * 22f);
            Panel(summary, "This run", a);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            for (int i = 0; i < lines.Count; i++)
                Style.ShadowLabel(new Rect(summary.x + 14f, summary.y + 34f + i * 22f, summary.width - 24f, 22f), lines[i], new Color(0.88f, 0.86f, 0.8f, a));
        }

        /// <summary>Right: the hovered node's card, or the current one's, or the first you can choose.</summary>
        private void DrawDetail(Rect r, WorldComponent_RoguelikeRun run, float t)
        {
            float a = Style.EaseOutCubic((t - 0.25f) / 0.4f);
            if (a <= 0f) return;
            var node = run.NodeById(hoveredId);
            if (node == null)
            {
                node = run.CurrentNode ?? run.nodes.FirstOrDefault(n => n.state == RunNodeState.Available && (!run.hubMode || n.depth > 0));
                if (run.hubMode) node ??= run.HubNode;
                else if (node == null || node.act != shownAct) node = run.nodes.FirstOrDefault(n => n.act == shownAct && n.IsBoss);
            }
            if (node == null) return;
            float w = Mathf.Min(r.width, Dialog_RewardPick.CardWidth), h = Mathf.Min(r.height - 30f, Dialog_RewardPick.CardHeight);
            var card = new Rect(r.center.x - w / 2f, r.y, w, h);
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, a);
            if (Revealed(node)) Dialog_RewardPick.DrawCard(card, new RewardCard { kind = RewardKind.Path, pathNode = node }, node.id == hoveredId ? hoverAmount : 0f, 0f, 0f, node.id);
            else DrawUnrevealed(card, node, a);
            GUI.color = old;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperCenter;
            string state = run.hubMode && node.id == run.hubNodeId ? "Your hub."
                : run.hubMode && node.state == RunNodeState.Available ? "Open: send a team from the hub."
                : run.hubMode && node.state == RunNodeState.Current ? "Your team is here."
                : run.hubMode && node.state == RunNodeState.Completed && Hub.HarvestSites(run).Contains(node) ? "Cleared: open for harvest trips."
                : node.state switch
            {
                RunNodeState.Available => "You can choose this next.",
                RunNodeState.Current => "You are here.",
                RunNodeState.Completed => "Cleared.",
                RunNodeState.Skipped => "Skipped.",
                _ => "Not reachable yet.",
            };
            Style.ShadowLabel(new Rect(r.x, card.yMax + 8f, r.width, 18f), state, new Color(Style.Parchment.r, Style.Parchment.g, Style.Parchment.b, 0.7f * a));
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        /// <summary>An expedition whose objective isn't set yet: kind, terrain and a note.</summary>
        private static void DrawUnrevealed(Rect r, RunNode n, float a)
        {
            var kind = KindColor(n);
            var inner = Style.CardFrame(r, kind, 0f, false, "Expedition");
            var icon = NodeIcons.For(n);
            var art = new Rect(r.center.x - 34f, inner.y + 10f, 68f, 68f);
            Style.GlowAround(art, new Color(kind.r, kind.g, kind.b, 0.3f * a), 1.8f);
            if (icon != null) Style.Tex(art, icon, new Color(1f, 1f, 1f, a));
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperCenter;
            Style.ShadowLabel(new Rect(inner.x, art.yMax + 10f, inner.width, 24f), "Base building", Style.Parchment);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            float y = art.yMax + 40f;
            if (n.TerrainLine is { } terrain)
            {
                GUI.color = new Color(0.62f, 0.82f, 0.62f, a);
                float th = Text.CalcHeight(terrain, inner.width);
                Widgets.Label(new Rect(inner.x, y, inner.width, th), terrain);
                y += th + 8f;
            }
            GUI.color = new Color(0.88f, 0.86f, 0.8f, a);
            Widgets.Label(new Rect(inner.x, y, inner.width, inner.yMax - y),
                "Its objective is set when you can choose it, to suit your squad and what you've unlocked. Every expedition has a short objective plus stretch goals that level up your pawns.");
            GUI.color = Color.white;
        }

        /// <summary>
        /// Whether the node's objective is final. Expeditions not yet offered are re-rolled to fit the squad when they
        /// come up, so until then the map only says what kind of node it is.
        /// </summary>
        public static bool Revealed(RunNode n) =>
            n.offered || n.IsBoss || n.preset.kind != NodeKind.Expedition || n.act == 1 && n.row == 0
            || n.state == RunNodeState.Current || n.state == RunNodeState.Completed;
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
