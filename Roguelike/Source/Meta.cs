using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

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

        public override void ExposeData()
        {
            Scribe_Values.Look(ref points, "points");
            Scribe_Values.Look(ref runs, "runs");
            Scribe_Values.Look(ref wins, "wins");
            Scribe_Collections.Look(ref unlocked, "unlocked", LookMode.Value);
            unlocked ??= new List<string>();
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

        public static bool IsAvailable(RelicDef d) => !d.requiresUnlock || Unlocked(u => u.relic == d);
        public static bool IsAvailable(GearRewardDef d) => !d.requiresUnlock || Unlocked(u => u.gear == d);
        public static bool IsAvailable(BlueprintGroupDef d) => !d.requiresUnlock || Unlocked(u => u.blueprint == d);
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

        public static void RecordRunEnd(bool won)
        {
            S.runs++;
            if (won) S.wins++;
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

    /// <summary>The list of unlocks with buy buttons, used by the unlock dialog and the mod settings page.</summary>
    public static class UnlockList
    {
        private static Vector2 scroll;

        public static void Draw(Rect rect)
        {
            var s = MetaProgress.S;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 34f), $"Meta points: {s.points}");
            Text.Font = GameFont.Small;
            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            Widgets.Label(new Rect(rect.x, rect.y + 34f, rect.width, 24f), $"Runs: {s.runs}, wins: {s.wins}. Earn points by clearing nodes (+1), beating the boss (+5) and skipping rewards (+1).");
            GUI.color = Color.white;

            var defs = DefDatabase<UnlockDef>.AllDefs.OrderBy(u => u.KindLabel).ThenBy(u => u.cost).ToList();
            const float rowH = 54f;
            var outer = new Rect(rect.x, rect.y + 66f, rect.width, rect.height - 66f);
            var view = new Rect(0f, 0f, outer.width - 16f, defs.Count * rowH);
            Widgets.BeginScrollView(outer, ref scroll, view);
            for (int i = 0; i < defs.Count; i++)
            {
                var u = defs[i];
                var row = new Rect(0f, i * rowH, view.width, rowH - 4f);
                if (i % 2 == 0) Widgets.DrawLightHighlight(row);
                bool owned = MetaProgress.IsUnlocked(u);
                Widgets.Label(new Rect(row.x + 6f, row.y + 2f, row.width - 160f, 24f), $"<b>{u.LabelCap}</b>  ({u.KindLabel})");
                GUI.color = new Color(1f, 1f, 1f, 0.75f);
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(row.x + 6f, row.y + 24f, row.width - 160f, 26f), u.description);
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
                var button = new Rect(row.xMax - 140f, row.y + 10f, 130f, 30f);
                if (owned) Widgets.Label(button, "Unlocked");
                else if (Widgets.ButtonText(button, $"Buy ({u.cost})", active: MetaProgress.CanBuy(u)) && MetaProgress.CanBuy(u))
                    MetaProgress.TryBuy(u);
            }
            Widgets.EndScrollView();
        }
    }

    public class Dialog_Unlocks : Window
    {
        public Dialog_Unlocks()
        {
            doCloseButton = true;
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize => new Vector2(760f, 640f);

        public override void DoWindowContents(Rect inRect) => UnlockList.Draw(new Rect(inRect.x, inRect.y, inRect.width, inRect.height - 50f));
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
        }

        public override Vector2 InitialSize => new Vector2(680f, 500f);

        public override void DoWindowContents(Rect inRect)
        {
            var run = WorldComponent_RoguelikeRun.Get;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperCenter;
            GUI.color = won ? new Color(0.55f, 0.9f, 0.45f) : new Color(0.95f, 0.45f, 0.4f);
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 36f), won ? "Victory" : "Run over");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(inRect.x, inRect.y + 40f, inRect.width, 50f), reason);
            Text.Anchor = TextAnchor.UpperLeft;

            var lines = new List<string>
            {
                $"Nodes cleared: {run?.nodes.Count(n => n.state == RunNodeState.Completed) ?? 0} of {run?.nodes.Count ?? 0}",
                $"Squad: {PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists.Count()} alive",
                $"Relics: {(run == null || run.relics.Count == 0 ? "none" : string.Join(", ", run.relics.Select(r => r.label)))}",
                $"Blueprints: {(run == null || run.blueprints.Count == 0 ? "none" : string.Join(", ", run.blueprints.Select(b => b.label)))}",
                $"Meta points earned this run: {run?.pointsEarned ?? 0} (you now have {MetaProgress.S.points})",
            };
            Widgets.Label(new Rect(inRect.x + 20f, inRect.y + 100f, inRect.width - 40f, 240f), string.Join("\n\n", lines));

            float y = inRect.yMax - 40f, w = (inRect.width - 30f) / 4f;
            if (Widgets.ButtonText(new Rect(inRect.x, y, w, 38f), "Spend points")) Find.WindowStack.Add(new Dialog_Unlocks());
            if (Widgets.ButtonText(new Rect(inRect.x + (w + 10f), y, w, 38f), "New run")) NewRun.Start();
            if (Widgets.ButtonText(new Rect(inRect.x + 2 * (w + 10f), y, w, 38f), "Main menu")) GenScene.GoToMainMenu();
            if (Widgets.ButtonText(new Rect(inRect.x + 3 * (w + 10f), y, w, 38f), "Keep playing")) Close();
        }
    }
}
