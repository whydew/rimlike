using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Roguelike
{
    /// <summary>
    /// Packing for the next node: the whole squad leaves, and you choose what they carry within their carry limit.
    /// Items are tiles grouped by category: click to add, right-click to remove, shift for all or none, plus one-click
    /// presets. Replaces vanilla's caravan screen for node-to-node travel.
    /// </summary>
    public class Dialog_Packing : Window
    {
        public enum Category { Food, Medicine, Weapons, Apparel, Materials, Other }

        /// <summary>Things of one kind on the map or in the squad's packs, and how many of them to take.</summary>
        public class Group
        {
            public readonly List<Thing> things = new List<Thing>();
            public Category category;
            public int available, chosen, carried;
            public float unitMass, unitValue;
            public float flashAt = -10f;

            public Thing Any => things[0];
            public string Label => Any.LabelCapNoCount;
        }

        private static readonly Color Accent = new Color(0.45f, 0.62f, 0.9f);
        private static readonly Dictionary<Category, Color> CategoryColors = new Dictionary<Category, Color>
        {
            [Category.Food] = new Color(0.55f, 0.8f, 0.4f),
            [Category.Medicine] = new Color(0.4f, 0.75f, 0.85f),
            [Category.Weapons] = new Color(0.9f, 0.45f, 0.35f),
            [Category.Apparel] = new Color(0.7f, 0.55f, 0.9f),
            [Category.Materials] = new Color(0.85f, 0.65f, 0.35f),
            [Category.Other] = new Color(0.7f, 0.7f, 0.72f),
        };

        private readonly Map map;
        private readonly string destination;
        private readonly Texture2D destinationIcon;
        private readonly Action<Caravan> onDeparted;
        private readonly Action onCancelled;
        /// <summary>Who leaves. With <see cref="chooseTeam"/> (hub mode) it's picked from <see cref="candidates"/>; otherwise everyone.</summary>
        private readonly List<Pawn> pawns;
        private readonly List<Pawn> candidates;
        private readonly bool chooseTeam;
        private readonly string backLabel;
        private readonly List<Group> groups = new List<Group>();
        private float gearMass, capacity;
        private Category? filter;
        private Vector2 scroll, squadScroll;
        private float openedAt = -1f, shownMass, blockedAt = -10f;
        private bool departed;

        public Dialog_Packing(Map map, string destination, Texture2D destinationIcon, Action<Caravan> onDeparted, Action onCancelled,
            bool chooseTeam = false, string backLabel = null)
        {
            this.chooseTeam = chooseTeam;
            this.backLabel = backLabel ?? "Back to the node choice";
            this.map = map;
            this.destination = destination;
            this.destinationIcon = destinationIcon;
            this.onDeparted = onDeparted;
            this.onCancelled = onCancelled;
            forcePause = true;
            absorbInputAroundWindow = true;
            doWindowBackground = false;
            drawShadow = false;
            closeOnCancel = true;
            closeOnAccept = false;

            candidates = CaravanFormingUtility.AllSendablePawns(map, allowEvenIfDowned: !chooseTeam, allowEvenIfInMentalState: !chooseTeam, allowEvenIfPrisonerNotSecure: true)
                .Where(p => p.Faction == Faction.OfPlayer || !chooseTeam && p.IsPrisonerOfColony).ToList();
            if (chooseTeam)
            {
                // By default every able colonist but one goes; someone stays to keep the hub running. Animals stay home.
                var colonists = candidates.Where(p => p.IsColonist).ToList();
                pawns = colonists.Take(Mathf.Max(1, colonists.Count - 1)).ToList();
            }
            else pawns = candidates.ToList();
            Rebuild();
        }

        /// <summary>Recounts carry capacity and the things on offer for the current team. Picks so far are reset.</summary>
        private void Rebuild()
        {
            capacity = CollectionsMassCalculator.Capacity(pawns);
            gearMass = pawns.Sum(p => MassUtility.GearMass(p));
            groups.Clear();
            foreach (var t in CaravanFormingUtility.AllReachableColonyItems(map, allowEvenIfOutsideHomeArea: true, allowEvenIfReserved: true, canMinify: true))
                if (t is not Corpse && t.Spawned) Add(t, carried: false); // depart moves spawned items only
            foreach (var p in pawns)
                foreach (var t in p.inventory.innerContainer.ToList())
                    Add(t, carried: true);
            groups.SortBy(g => (int)g.category, g => -g.unitValue);
            shownMass = MassUsage;
        }

        public IReadOnlyList<Pawn> Candidates => candidates;

        /// <summary>Hub mode: chooses who goes (from <see cref="Candidates"/>).</summary>
        public void SetTeam(IEnumerable<Pawn> team)
        {
            if (!chooseTeam) return;
            pawns.Clear();
            pawns.AddRange(team.Where(candidates.Contains).Distinct());
            Rebuild();
        }

        public void Toggle(Pawn p)
        {
            if (!chooseTeam || !candidates.Contains(p)) return;
            if (!pawns.Remove(p)) pawns.Add(p);
            Rebuild();
        }

        private void Add(Thing t, bool carried)
        {
            var g = groups.FirstOrDefault(x => TransferableUtility.TransferAsOne(x.Any, t, TransferAsOneMode.Normal));
            if (g == null)
            {
                g = new Group { category = Categorize(t), unitMass = t.GetStatValue(StatDefOf.Mass), unitValue = t.MarketValue };
                groups.Add(g);
            }
            g.things.Add(t);
            g.available += t.stackCount;
            if (carried)
            {
                // What the squad already carries stays packed unless removed.
                g.carried += t.stackCount;
                g.chosen += t.stackCount;
            }
        }

        private static Category Categorize(Thing t)
        {
            var d = t.def;
            if (d.IsMedicine) return Category.Medicine;
            if (d.IsNutritionGivingIngestible && !d.IsDrug) return Category.Food;
            if (d.IsWeapon) return Category.Weapons;
            if (d.IsApparel) return Category.Apparel;
            if (d.IsStuff || d.CountAsResource) return Category.Materials;
            return Category.Other;
        }

        public IReadOnlyList<Group> Groups => groups;
        public IReadOnlyList<Pawn> Pawns => pawns;
        public float MassCapacity => capacity;
        public float MassUsage => gearMass + groups.Sum(g => g.chosen * g.unitMass);
        private float FreeMass => capacity - MassUsage;

        public override Vector2 InitialSize => new Vector2(UI.screenWidth, UI.screenHeight);

        protected override float Margin => 0f;

        // ---------- choosing ----------

        /// <summary>Sets how many of a group to take, clamped to what's there and what the squad can still carry.</summary>
        public int Set(Group g, int count)
        {
            count = Mathf.Clamp(count, 0, g.available);
            if (count > g.chosen && g.unitMass > 0f)
            {
                int fits = Mathf.FloorToInt((FreeMass + 0.0001f) / g.unitMass);
                if (count - g.chosen > fits)
                {
                    count = g.chosen + Mathf.Max(0, fits);
                    blockedAt = Style.Now;
                }
            }
            if (count != g.chosen) g.flashAt = Style.Now;
            g.chosen = count;
            return count;
        }

        public void Clear()
        {
            foreach (var g in groups) g.chosen = 0;
        }

        /// <summary>A sensible kit: food for about a week, all the medicine, and whatever the squad already carries.</summary>
        public void Essentials()
        {
            foreach (var g in groups) g.chosen = g.carried;
            float nutritionNeeded = pawns.Count(p => p.RaceProps.Humanlike) * 1.6f * 7f;
            foreach (var g in groups.Where(g => g.category == Category.Food && g.Any.def.ingestible != null)
                         .OrderByDescending(g => g.Any.GetStatValue(StatDefOf.Nutrition) / Mathf.Max(0.01f, g.unitMass)))
            {
                float per = g.Any.GetStatValue(StatDefOf.Nutrition);
                if (per <= 0f || nutritionNeeded <= 0f) continue;
                int want = Mathf.CeilToInt(nutritionNeeded / per);
                int got = Set(g, g.chosen + want) - g.carried;
                nutritionNeeded -= Mathf.Max(0, got) * per;
            }
            foreach (var g in groups.Where(g => g.category == Category.Medicine)) Set(g, g.available);
        }

        /// <summary>Fills the packs with the most valuable things per kilogram.</summary>
        public void MostValuable()
        {
            foreach (var g in groups.OrderByDescending(g => g.unitMass > 0f ? g.unitValue / g.unitMass : float.MaxValue))
                Set(g, g.available);
        }

        // ---------- leaving ----------

        /// <summary>Packs the chosen things into the squad's inventories and leaves the map as a caravan.</summary>
        public bool Depart()
        {
            if (departed) return false;
            if (MassUsage > capacity + 0.01f)
            {
                Messages.Message("Too heavy: the squad can't carry all of this.", MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }
            if (!pawns.Any(p => p.IsColonist && !p.Downed))
            {
                Messages.Message(chooseTeam ? "Choose at least one colonist for the team." : "Someone has to be able to walk.", MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }
            foreach (var g in groups)
            {
                // Carried things that weren't kept are dropped here; map things that were chosen are packed.
                int keepCarried = Mathf.Min(g.carried, g.chosen), fromMap = g.chosen - keepCarried, toDrop = g.carried - keepCarried;
                foreach (var t in g.things.Where(t => !t.Spawned && t.ParentHolder is Pawn_InventoryTracker).ToList())
                {
                    if (toDrop <= 0) break;
                    var owner = ((Pawn_InventoryTracker)t.ParentHolder).pawn;
                    int n = Mathf.Min(toDrop, t.stackCount);
                    owner.inventory.innerContainer.TryDrop(t, owner.Position, map, ThingPlaceMode.Near, n, out _);
                    toDrop -= n;
                }
                foreach (var t in g.things.Where(t => t.Spawned).ToList())
                {
                    if (fromMap <= 0) break;
                    int n = Mathf.Min(fromMap, t.stackCount);
                    var piece = t.SplitOff(n);
                    if (piece.Faction != null && piece.Faction != Faction.OfPlayer) piece.SetFactionDirect(Faction.OfPlayer);
                    var item = piece.TryMakeMinified();
                    CaravanInventoryUtility.FindPawnToMoveInventoryTo(item, pawns, null).inventory.TryAddAndUnforbid(item);
                    fromMap -= n;
                }
            }
            var caravan = CaravanExitMapUtility.ExitMapAndCreateCaravan(pawns, Faction.OfPlayer, map.Tile, map.Tile, PlanetTile.Invalid, sendMessage: false);
            departed = true;
            Close();
            onDeparted?.Invoke(caravan);
            return true;
        }

        public override void PostClose()
        {
            base.PostClose();
            if (!departed) onCancelled?.Invoke();
        }

        // ---------- drawing ----------

        public override void DoWindowContents(Rect inRect)
        {
            if (openedAt < 0f) openedAt = Style.Now;
            float t = Style.Now - openedAt;
            Style.Backdrop(inRect, Style.EaseOutCubic(t / 0.3f));
            if (Event.current.type == EventType.Repaint)
                shownMass = Mathf.Lerp(shownMass, MassUsage, 1f - Mathf.Exp(-10f * Mathf.Min(Time.unscaledDeltaTime, 0.05f)));

            var body = inRect.ContractedBy(40f);
            body.yMin += 10f;
            DrawHeader(new Rect(body.x, body.y, body.width, 64f), t);
            DrawMeter(new Rect(body.x, body.y + 76f, body.width, 40f));

            float top = body.y + 128f;
            DrawSquad(new Rect(body.x, top, 230f, body.yMax - top - 64f));
            var right = new Rect(body.x + 250f, top, body.width - 250f, body.yMax - top - 64f);
            DrawToolbar(new Rect(right.x, right.y, right.width, 34f));
            DrawTiles(new Rect(right.x, right.y + 46f, right.width, right.height - 46f), t);

            var bar = new Rect(body.x, body.yMax - 46f, body.width, 44f);
            if (Style.Button(new Rect(bar.x, bar.y, 220f, 42f), backLabel, Style.Parchment)) Close();
            if (Style.Button(new Rect(bar.xMax - 220f, bar.y, 220f, 42f), "Depart", Style.Gold, MassUsage <= capacity + 0.01f && pawns.Any(p => p.IsColonist && !p.Downed))) Depart();
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawHeader(Rect r, float t)
        {
            float a = Style.EaseOutCubic(t / 0.4f);
            var icon = new Rect(r.x, r.y, 64f, 64f);
            Style.GlowAround(icon, new Color(Style.Gold.r, Style.Gold.g, Style.Gold.b, 0.35f * a), 1.6f);
            if (destinationIcon != null) Style.Tex(icon, destinationIcon, new Color(1f, 1f, 1f, a));
            Text.Font = GameFont.Tiny;
            Style.ShadowLabel(new Rect(icon.xMax + 14f, r.y + 6f, 400f, 18f), "PACK FOR", new Color(Style.Gold.r, Style.Gold.g, Style.Gold.b, 0.85f * a));
            Text.Font = GameFont.Medium;
            Style.ShadowLabel(new Rect(icon.xMax + 14f, r.y + 22f, r.width - 400f, 36f), destination, new Color(Style.Parchment.r, Style.Parchment.g, Style.Parchment.b, a));
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperRight;
            Style.ShadowLabel(new Rect(r.xMax - 520f, r.y + 10f, 520f, 54f),
                "Click a tile to add, right-click to remove. Shift-click for all or none, ctrl-click for an exact amount.\n" +
                (chooseTeam ? "Click a portrait to choose who goes. Whoever stays keeps the hub running." : "The whole squad leaves; what isn't packed stays behind."), new Color(1f, 1f, 1f, 0.6f * a));
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private void DrawMeter(Rect r)
        {
            float frac = capacity <= 0f ? 1f : shownMass / capacity;
            var c = frac > 0.98f ? new Color(0.92f, 0.32f, 0.27f) : frac > 0.85f ? new Color(0.92f, 0.72f, 0.28f) : new Color(0.4f, 0.78f, 0.38f);
            float blocked = Mathf.Clamp01(1f - (Style.Now - blockedAt) / 0.6f);
            if (blocked > 0f)
            {
                r.x += Mathf.Sin(Style.Now * 60f) * 4f * blocked;
                Style.GlowAround(r, new Color(0.92f, 0.32f, 0.27f, 0.5f * blocked), 1.15f, Style.SoftGlow);
            }
            Style.Tex(r, Style.CardBody, Color.white);
            Style.Border(r, new Color(c.r, c.g, c.b, 0.7f), 1f);
            var fill = new Rect(r.x + 2f, r.y + 2f, (r.width - 4f) * Mathf.Clamp01(frac), r.height - 4f);
            Style.Box(fill, new Color(c.r * 0.55f, c.g * 0.55f, c.b * 0.55f, 0.95f));
            Style.Box(new Rect(fill.x, fill.y, fill.width, fill.height * 0.45f), new Color(c.r, c.g, c.b, 0.5f));
            Style.Tex(new Rect(fill.xMax - 8f, fill.y - 6f, 16f, fill.height + 12f), Style.Glow, c);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Style.ShadowLabel(r, $"Carrying {shownMass:0} / {capacity:0} kg   ·   {Mathf.Max(0f, capacity - shownMass):0} kg free", Color.white);
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawSquad(Rect r)
        {
            Style.Tex(r, Style.CardBody, new Color(1f, 1f, 1f, 0.9f));
            Style.Border(r, new Color(Accent.r, Accent.g, Accent.b, 0.4f), 1f);
            Text.Font = GameFont.Tiny;
            Style.ShadowLabel(new Rect(r.x + 10f, r.y + 8f, r.width, 18f), chooseTeam ? $"TEAM · {pawns.Count} of {candidates.Count}" : $"SQUAD · {pawns.Count}", new Color(Accent.r, Accent.g, Accent.b, 0.95f));
            // The list scrolls when the squad (or the hub's colonists and animals) don't fit.
            var shown = chooseTeam ? candidates : pawns;
            var outer = new Rect(r.x, r.y + 30f, r.width, r.height - 34f);
            var view = new Rect(0f, 0f, outer.width - 16f, shown.Count * 54f);
            Widgets.BeginScrollView(outer, ref squadScroll, view);
            float y = 0f;
            foreach (var p in shown)
            {
                var row = new Rect(8f, y, view.width - 12f, 48f);
                bool going = pawns.Contains(p);
                if (chooseTeam)
                {
                    Style.Box(row, new Color(Accent.r, Accent.g, Accent.b, going ? 0.22f : Mouse.IsOver(row) ? 0.08f : 0f));
                    if (going) Style.Border(row, new Color(Accent.r, Accent.g, Accent.b, 0.8f), 1f);
                    if (Widgets.ButtonInvisible(row))
                    {
                        Toggle(p);
                        SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    }
                }
                var oldC = GUI.color;
                if (!going) GUI.color = new Color(1f, 1f, 1f, 0.45f);
                GUI.DrawTexture(new Rect(row.x, row.y, 48f, 48f), PortraitsCache.Get(p, new Vector2(48f, 48f), Rot4.South, new Vector3(0f, 0f, 0.1f), 1.4f));
                Text.Font = GameFont.Small;
                Style.ShadowLabel(new Rect(row.x + 54f, row.y + 4f, row.width - 54f, 22f), p.LabelShortCap, Style.Parchment);
                Text.Font = GameFont.Tiny;
                string state = p.Downed ? "downed, carried" : p.RaceProps.Animal ? "pack animal" : p.IsPrisonerOfColony ? "prisoner" : "colonist";
                if (chooseTeam && !going) state = "stays home";
                Style.ShadowLabel(new Rect(row.x + 54f, row.y + 26f, row.width - 54f, 18f), $"{MassUtility.Capacity(p):0} kg · {state}", new Color(1f, 1f, 1f, 0.6f));
                GUI.color = oldC;
                y += 54f;
            }
            Widgets.EndScrollView();
            Text.Font = GameFont.Small;
        }

        private void DrawToolbar(Rect r)
        {
            float x = r.x;
            if (Chip(new Rect(x, r.y, 70f, r.height), "All", filter == null, Accent)) filter = null;
            x += 76f;
            foreach (Category c in Enum.GetValues(typeof(Category)))
            {
                int n = groups.Count(g => g.category == c);
                if (n == 0) continue;
                float w = Text.CalcSize(c.ToString()).x + 40f;
                if (Chip(new Rect(x, r.y, w, r.height), $"{c} {n}", filter == c, CategoryColors[c])) filter = filter == c ? null : c;
                x += w + 6f;
            }
            float bx = r.xMax;
            bx -= 90f;
            if (Style.Button(new Rect(bx, r.y, 90f, r.height), "Clear", Style.Parchment)) { Clear(); SoundDefOf.Tick_Low.PlayOneShotOnCamera(); }
            bx -= 136f;
            if (Style.Button(new Rect(bx, r.y, 130f, r.height), "Most valuable", Style.Gold)) MostValuable();
            bx -= 116f;
            if (Style.Button(new Rect(bx, r.y, 110f, r.height), "Essentials", new Color(0.55f, 0.8f, 0.4f))) Essentials();
            TooltipHandler.TipRegion(new Rect(bx, r.y, 110f, r.height), "Food for about a week, all the medicine, and what the squad already carries.");
        }

        private static bool Chip(Rect r, string label, bool on, Color c)
        {
            bool over = Mouse.IsOver(r);
            Style.Box(r, new Color(c.r, c.g, c.b, on ? 0.35f : over ? 0.18f : 0.08f));
            Style.Border(r, new Color(c.r, c.g, c.b, on ? 1f : 0.45f), 1f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Style.ShadowLabel(r, label, on ? Color.white : Style.Parchment);
            Text.Anchor = TextAnchor.UpperLeft;
            return Widgets.ButtonInvisible(r);
        }

        private void DrawTiles(Rect r, float t)
        {
            var shown = groups.Where(g => filter == null || g.category == filter).ToList();
            const float w = 150f, h = 128f, gap = 10f;
            int cols = Mathf.Max(1, Mathf.FloorToInt((r.width - 16f + gap) / (w + gap)));
            var view = new Rect(0f, 0f, r.width - 16f, Mathf.CeilToInt(shown.Count / (float)cols) * (h + gap) + 8f);
            Widgets.BeginScrollView(r, ref scroll, view);
            for (int i = 0; i < shown.Count; i++)
            {
                var g = shown[i];
                var tile = new Rect((i % cols) * (w + gap), 4f + (i / cols) * (h + gap), w, h);
                if (tile.yMax < scroll.y || tile.y > scroll.y + r.height) continue;
                float appear = Style.EaseOutCubic((t - 0.15f - Mathf.Min(i, 30) * 0.015f) / 0.3f);
                if (appear <= 0f) continue;
                DrawTile(new Rect(tile.x, tile.y + (1f - appear) * 16f, w, h), g, appear);
                if (Mouse.IsOver(tile) && Event.current.type == EventType.MouseDown)
                {
                    bool shift = Event.current.shift;
                    int step = g.available <= 10 ? 1 : Mathf.CeilToInt(g.available / 10f);
                    if (Event.current.button == 0 && Event.current.control)
                    {
                        var group = g;
                        Find.WindowStack.Add(new Dialog_Slider(n => $"Take {n} {group.Label}", 0, group.available, n => Set(group, n), group.chosen));
                    }
                    else if (Event.current.button == 0) { Set(g, shift ? g.available : g.chosen + step); SoundDefOf.Tick_High.PlayOneShotOnCamera(); }
                    else if (Event.current.button == 1) { Set(g, shift ? 0 : g.chosen - step); SoundDefOf.Tick_Low.PlayOneShotOnCamera(); }
                    Event.current.Use();
                }
            }
            Widgets.EndScrollView();
        }

        private static void DrawTile(Rect r, Group g, float alpha)
        {
            var c = CategoryColors[g.category];
            bool over = Mouse.IsOver(r);
            float frac = g.available == 0 ? 0f : g.chosen / (float)g.available;
            float flash = Mathf.Clamp01(1f - (Style.Now - g.flashAt) / 0.35f);
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            if (g.chosen > 0) Style.GlowAround(r, new Color(c.r, c.g, c.b, 0.18f + 0.3f * flash), 1.12f, Style.SoftGlow);
            Style.Tex(r, Style.CardBody, Color.white);
            Style.Box(r, new Color(c.r, c.g, c.b, (g.chosen > 0 ? 0.1f : 0.03f) + (over ? 0.06f : 0f)));
            Style.Border(r, new Color(c.r, c.g, c.b, g.chosen > 0 ? 0.9f : over ? 0.6f : 0.3f), g.chosen > 0 ? 1.5f : 1f);

            var icon = new Rect(r.center.x - 24f, r.y + 8f, 48f, 48f);
            Widgets.ThingIcon(icon, g.Any);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperCenter;
            Style.ShadowLabel(new Rect(r.x + 4f, icon.yMax + 2f, r.width - 8f, 30f), g.Label.Truncate(r.width - 8f), Style.Parchment);
            Style.ShadowLabel(new Rect(r.x + 4f, r.yMax - 38f, r.width - 8f, 16f), $"{g.unitMass * g.chosen:0.#} kg", new Color(1f, 1f, 1f, 0.55f));
            Text.Anchor = TextAnchor.UpperLeft;

            // Taken / available with a fill bar.
            var bar = new Rect(r.x + 8f, r.yMax - 18f, r.width - 16f, 10f);
            Style.Box(bar, new Color(0f, 0f, 0f, 0.5f));
            Style.Box(new Rect(bar.x, bar.y, bar.width * frac, bar.height), new Color(c.r, c.g, c.b, 0.9f));
            Text.Anchor = TextAnchor.MiddleCenter;
            Style.ShadowLabel(new Rect(bar.x, bar.y - 3f, bar.width, 16f), $"{g.chosen} / {g.available}", Color.white);
            Text.Anchor = TextAnchor.UpperLeft;
            if (g.carried > 0)
            {
                Text.Anchor = TextAnchor.UpperRight;
                Style.ShadowLabel(new Rect(r.x, r.y + 4f, r.width - 6f, 16f), "packed", new Color(c.r, c.g, c.b, 0.9f));
                Text.Anchor = TextAnchor.UpperLeft;
            }
            if (flash > 0f) Style.Box(r, new Color(1f, 1f, 1f, 0.2f * flash));
            Text.Font = GameFont.Small;
            GUI.color = old;
            TooltipHandler.TipRegion(r, () => $"{g.Label}\n{g.unitMass:0.##} kg each, {g.unitValue:0} silver each\n\n{g.Any.DescriptionDetailed}", g.Any.thingIDNumber);
        }
    }
}
