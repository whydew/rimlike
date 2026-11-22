using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Roguelike
{
    /// <summary>
    /// A styled hover panel in place of a plain tooltip: a title, then short sections (a coloured heading and lines,
    /// some bulleted). It follows the mouse like a tooltip, never under the cursor, and is drawn above everything.
    /// </summary>
    public class InfoPanel
    {
        private const float Width = 340f, Pad = 14f, Bullet = 14f, HeaderH = 20f;
        private const int WindowId = 0x52_49_4E_46;

        private class Line
        {
            public string text;
            public Color color;
            public bool bullet;
        }

        private class Section
        {
            public string head;
            public Color color;
            public readonly List<Line> lines = new List<Line>();
        }

        public static readonly Color Text_ = new Color(0.92f, 0.9f, 0.85f);
        public static readonly Color Dim = new Color(0.72f, 0.7f, 0.65f);
        public static readonly Color Good = new Color(0.55f, 0.9f, 0.5f);
        public static readonly Color Bad = new Color(1f, 0.5f, 0.45f);
        public static readonly Color Terrain = new Color(0.62f, 0.82f, 0.62f);

        private readonly string title, subtitle;
        private readonly Color accent;
        private readonly List<Section> sections = new List<Section> { new Section() }; // the first has no heading

        public InfoPanel(string title, string subtitle, Color accent)
        {
            this.title = title;
            this.subtitle = subtitle;
            this.accent = accent;
        }

        /// <summary>Starts a new section with a heading.</summary>
        public InfoPanel Head(string head, Color color)
        {
            sections.Add(new Section { head = head, color = color });
            return this;
        }

        public InfoPanel Add(string text, Color? color = null, bool bullet = false)
        {
            if (!text.NullOrEmpty()) sections.Last().lines.Add(new Line { text = text, color = color ?? Text_, bullet = bullet });
            return this;
        }

        private float LineHeight(Line l) => Text.CalcHeight(l.text, Width - 2f * Pad - (l.bullet ? Bullet : 0f));

        private float Height()
        {
            Text.Font = GameFont.Small;
            float h = Pad + 24f + (subtitle.NullOrEmpty() ? 0f : 18f) + 6f;
            foreach (var s in sections.Where(s => s.lines.Count > 0))
            {
                if (s.head != null) h += HeaderH + 4f;
                h += s.lines.Sum(LineHeight) + 8f;
            }
            return h + Pad - 8f;
        }

        /// <summary>
        /// Shows the panel next to the mouse this frame (or with its top left at <paramref name="at"/>; <paramref name="slot"/> keeps
        /// several pinned panels apart). Call every frame while the anchor is hovered.
        /// </summary>
        public void Show(Vector2? at = null, int slot = 0)
        {
            float h = Height();
            var mouse = UI.MousePositionOnUIInverted;
            float x = mouse.x + 18f + Width <= UI.screenWidth - 4f ? mouse.x + 18f : mouse.x - 18f - Width;
            float y = mouse.y + 18f + h <= UI.screenHeight - 4f ? mouse.y + 18f : Mathf.Max(4f, mouse.y - 18f - h);
            var rect = at is { } p ? new Rect(p.x, p.y, Width, h) : new Rect(Mathf.Max(4f, x), y, Width, h);
            Find.WindowStack.ImmediateWindow(WindowId + slot, rect, WindowLayer.Super, () => Draw(rect.AtZero()), doBackground: false, shadowAlpha: 0f);
        }

        private void Draw(Rect r)
        {
            Style.Tex(r, Style.CardBody, Color.white);
            Style.Box(r, new Color(accent.r, accent.g, accent.b, 0.05f));
            Style.Border(r, new Color(accent.r * 0.85f, accent.g * 0.85f, accent.b * 0.85f, 1f), 1f);
            Style.Box(new Rect(r.x, r.y, 3f, r.height), accent);

            float x = r.x + Pad, w = r.width - 2f * Pad, y = r.y + Pad - 4f;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            Style.ShadowLabel(new Rect(x, y, w, 24f), title, Style.Parchment);
            y += 24f;
            if (!subtitle.NullOrEmpty())
            {
                Text.Font = GameFont.Tiny;
                Style.ShadowLabel(new Rect(x, y, w, 18f), subtitle.ToUpperInvariant(), new Color(accent.r, accent.g, accent.b, 0.95f));
                y += 18f;
            }
            y += 6f;
            foreach (var s in sections.Where(s => s.lines.Count > 0))
            {
                if (s.head != null)
                {
                    Text.Font = GameFont.Tiny;
                    Style.ShadowLabel(new Rect(x, y, w, HeaderH - 2f), s.head.ToUpperInvariant(), s.color);
                    Style.Box(new Rect(x, y + HeaderH - 2f, w, 1f), new Color(s.color.r, s.color.g, s.color.b, 0.35f));
                    y += HeaderH + 4f;
                }
                Text.Font = GameFont.Small;
                foreach (var l in s.lines)
                {
                    float lh = LineHeight(l);
                    if (l.bullet) Style.Tex(new Rect(x + 2f, y + 7f, 6f, 6f), Style.Disc, new Color(s.color.r, s.color.g, s.color.b, s.head == null ? 0f : 0.9f));
                    var old = GUI.color;
                    GUI.color = l.color;
                    Widgets.Label(new Rect(x + (l.bullet ? Bullet : 0f), y, w - (l.bullet ? Bullet : 0f), lh), l.text);
                    GUI.color = old;
                    y += lh;
                }
                y += 8f;
            }
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }

    /// <summary>The info panels for a node: its card (before you go) and its HUD (while you're there).</summary>
    public static class NodeInfo
    {
        private static readonly Color ObjectiveC = new Color(0.62f, 0.8f, 0.5f);
        private static readonly Color StretchC = new Color(0.35f, 0.8f, 0.85f);
        private static readonly Color EnemyC = new Color(0.92f, 0.45f, 0.38f);
        private static readonly Color WeatherC = new Color(0.55f, 0.72f, 0.95f);

        /// <summary>Everything about a node you might travel to.</summary>
        public static InfoPanel ForNode(RunNode n)
        {
            var p = n.preset;
            int squad = RunNode.SquadSize;
            var panel = new InfoPanel((n.IsBoss ? "Boss: " : "") + p.LabelCap, Dialog_RunMap.KindLabel(n), Dialog_RunMap.KindColor(n))
                .Add(n.TerrainLine, InfoPanel.Terrain)
                .Add(p.description, InfoPanel.Dim);
            if (!p.HasMap) return panel.Head("On arrival", ObjectiveC).Add("Resolves when you arrive: no map, no clock.")
                .Head("Reward", Style.Gold).Add(n.RewardShort, Style.Gold);

            panel.Head("Objective", ObjectiveC);
            if (p.objective == ObjectiveKind.Requirements)
            {
                foreach (var r in p.requirements.Where(r => r.kind != RequirementKind.NoDeaths)) panel.Add(r.Card(squad), bullet: true);
                if (p.minDays > 0f) panel.Add($"Stay at least {p.minDays:0.#} days", bullet: true);
                panel.Add($"Counts are for a squad of {squad}.", InfoPanel.Dim);
            }
            else panel.Add(p.ObjectiveTextFor(squad).CapitalizeFirst(), bullet: true);
            panel.Add(p.NeedsText, InfoPanel.Dim);

            if (p.stretchGoals.Count > 0)
            {
                panel.Head("Stretch goals", StretchC);
                foreach (var r in p.stretchGoals) panel.Add(r.Card(squad), bullet: true);
                panel.Add("Optional. Each one met levels up a pawn. After the objective you can stay for them, but the weather worsens at full speed.", InfoPanel.Dim);
            }

            var enemies = new List<string>();
            if (p.kind is NodeKind.CombatAlerted or NodeKind.Elite) enemies.Add("The enemy knows you're here and attacks at once.");
            if (p.kind == NodeKind.CombatUnaware) enemies.Add("The enemy camp hasn't seen you: scout, prepare, then strike.");
            if (p.patrolIntervalDays > 0f) enemies.Add($"Raids about every {p.patrolIntervalDays:0.#} days. The node panel shows each one ahead: when, from where, how many.");
            if (p.reinforcementDays > 0f) enemies.Add($"Reinforcements arrive on day {p.reinforcementDays:0.#}.");
            if (enemies.Count > 0)
            {
                panel.Head("Enemies", EnemyC);
                foreach (var e in enemies) panel.Add(e, bullet: true);
            }

            var weather = p.WeatherLine(n.Weather);
            if (weather != null) panel.Head("Weather", WeatherC).Add(weather).Add("Worsens slower while you're ahead of pace, faster while behind. No limit.", InfoPanel.Dim);

            panel.Head("Danger", InfoPanel.Bad);
            panel.Add(p.DangerLine is { } danger ? danger + "." : null, InfoPanel.Bad);
            panel.Add("Leaving before the objective is met ends the run.", InfoPanel.Dim);
            return panel.Head("Reward", Style.Gold).Add(n.RewardShort, new Color(1f, 0.92f, 0.75f));
        }

        /// <summary>The node you're on: progress on each requirement and stretch goal, the next raid and the weather.</summary>
        public static InfoPanel ForMap(MapComponent_RoguelikeNode c)
        {
            var p = c.preset;
            var run = WorldComponent_RoguelikeRun.Get;
            var node = run is { active: true } ? run.NodeAt(c.map.Tile) : null;
            var panel = new InfoPanel(p.LabelCap, node != null ? $"Act {run.act} · row {node.row + 1} · day {c.Days:0.0}" : $"Day {c.Days:0.0}",
                    node != null ? Dialog_RunMap.KindColor(node) : Style.Gold)
                .Add(p.description, InfoPanel.Dim);

            panel.Head(c.failed ? "Objective · failed" : c.completed ? "Objective · done" : $"Objective · {c.Progress:P0}", ObjectiveC);
            if (p.objective == ObjectiveKind.Requirements)
            {
                foreach (var r in p.requirements)
                    panel.Add($"{r.Card(c.squad)}: {c.ProgressOf(r)}", c.Met(r) ? InfoPanel.Good : InfoPanel.Text_, bullet: true);
                if (p.minDays > 0f) panel.Add($"Stay {p.minDays:0.#} days: day {Mathf.Min(c.Days, p.minDays):0.#}/{p.minDays:0.#}", c.StayMet ? InfoPanel.Good : InfoPanel.Text_, bullet: true);
            }
            else panel.Add(c.ProgressText.CapitalizeFirst(), c.RequirementsMet ? InfoPanel.Good : InfoPanel.Text_, bullet: true);

            if (p.stretchGoals.Count > 0)
            {
                panel.Head($"Stretch goals · {c.StretchMet}/{p.stretchGoals.Count}", StretchC);
                for (int i = 0; i < p.stretchGoals.Count; i++)
                {
                    var r = p.stretchGoals[i];
                    panel.Add($"{r.Card(c.squad)}: {(c.StretchDone(i) ? "met" : c.ProgressOf(r))}", c.StretchDone(i) ? InfoPanel.Good : InfoPanel.Text_, bullet: true);
                }
                panel.Add(c.StretchOpen ? "The objective is done. Stay for these while the weather worsens at full speed, or leave with Leave node." : "Each one met levels up a pawn.", InfoPanel.Dim);
            }

            if (c.RaidText is { } raid)
                panel.Head("Next raid", EnemyC).Add(raid + ".").Add("A red ring marks where it walks in once it's under two days away.", InfoPanel.Dim);

            var cond = c.Condition;
            if (cond != null)
            {
                panel.Head("Weather", WeatherC).Add($"{cond.def.LabelCap}: {cond.IntensityText(cond.intensity)} now.");
                if (c.completed && !c.StretchOpen) panel.Add("Stopped worsening.", InfoPanel.Dim);
                else if (c.InGrace) panel.Add($"Holding off for {c.GraceDays - c.Days:0.0} more days.", InfoPanel.Good);
                else
                {
                    float factor = c.PaceFactor;
                    string rate = cond is GameCondition_EscalatingTemperature ? c.CurrentRatePerDay.ToStringTemperatureOffset("F1") : c.CurrentRatePerDay.ToString("0.00");
                    string pace = c.completed ? "full speed while you stay" : factor < 0.95f ? "ahead of pace, slowed" : factor > 1.05f ? "behind pace, sped up" : "on pace";
                    panel.Add($"Worsening {rate} per day (x{factor:0.00}, {pace}).", factor > 1.05f ? InfoPanel.Bad : InfoPanel.Text_);
                }
            }
            if (p.DangerLine is { } danger && !c.completed) panel.Head("Danger", InfoPanel.Bad).Add(danger + ".", InfoPanel.Bad);
            return panel;
        }
    }
}
