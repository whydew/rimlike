using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Roguelike
{
    /// <summary>A passive run-wide effect.</summary>
    public class RelicDef : Def
    {
        public ThingDef iconThing;
        public bool starterOnly;
        public bool requiresUnlock;
        public HediffDef hediff;
        public int extraRewardChoices;
        public float weatherRateFactor = 1f;
        public float graceDaysOffset;
        public float maxPaceFactorFactor = 1f;
        public float colonistCapacityFactor = 1f;
        public float packAnimalCapacityFactor = 1f;
        public bool healAnimalsOnEnter;
        public bool fixWorstConditionOnEnter;
    }

    /// <summary>A gear or implant item reward.</summary>
    public class GearRewardDef : Def
    {
        public ThingDef thing;
        public ThingDef stuff;
        public int count = 1;
        public bool implant;
        public bool requiresUnlock;

        public override void ResolveReferences()
        {
            base.ResolveReferences();
            if (label.NullOrEmpty() && thing != null)
                label = (count > 1 ? $"{count}x " : "") + (stuff != null ? GenLabel.ThingLabel(thing, stuff) : thing.label);
        }
    }

    /// <summary>A group of buildings hidden from the build menu during a run until drafted.</summary>
    public class BlueprintGroupDef : Def
    {
        public List<ThingDef> buildings = new List<ThingDef>();
        public bool requiresUnlock;

        private static HashSet<BuildableDef> gated;

        /// <summary>Every building that belongs to some group.</summary>
        public static HashSet<BuildableDef> Gated =>
            gated ??= new HashSet<BuildableDef>(DefDatabase<BlueprintGroupDef>.AllDefs.SelectMany(g => g.buildings));
    }

    public enum RewardKind { Relic, Gear, Implant, Blueprint, Recruit }

    /// <summary>One card on the reward screen.</summary>
    public class RewardCard
    {
        public RewardKind kind;
        public RelicDef relic;
        public GearRewardDef gear;
        public BlueprintGroupDef blueprint;
        public Pawn recruit;

        public string Title => kind switch
        {
            RewardKind.Relic => relic.LabelCap,
            RewardKind.Gear or RewardKind.Implant => gear.LabelCap,
            RewardKind.Blueprint => blueprint.LabelCap,
            RewardKind.Recruit => recruit.Name.ToStringFull,
            _ => "?",
        };

        public string KindLabel => kind switch
        {
            RewardKind.Relic => "Relic",
            RewardKind.Gear => "Gear",
            RewardKind.Implant => "Implant",
            RewardKind.Blueprint => "Blueprint",
            RewardKind.Recruit => "Recruit",
            _ => "",
        };

        public Color KindColor => kind switch
        {
            RewardKind.Relic => new Color(0.85f, 0.7f, 0.25f),
            RewardKind.Gear => new Color(0.45f, 0.6f, 0.8f),
            RewardKind.Implant => new Color(0.3f, 0.75f, 0.7f),
            RewardKind.Blueprint => new Color(0.85f, 0.5f, 0.25f),
            RewardKind.Recruit => new Color(0.45f, 0.8f, 0.4f),
            _ => Color.white,
        };
    }

    /// <summary>Builds reward card sets and applies picked cards.</summary>
    public static class RewardUtility
    {
        public const float RecruitChanceInStandardPool = 0.1f;

        public static IEnumerable<RelicDef> RelicPool(WorldComponent_RoguelikeRun run) =>
            DefDatabase<RelicDef>.AllDefs.Where(r => !r.starterOnly && !run.relics.Contains(r) && MetaProgress.IsAvailable(r));

        public static IEnumerable<BlueprintGroupDef> BlueprintPool(WorldComponent_RoguelikeRun run) =>
            DefDatabase<BlueprintGroupDef>.AllDefs.Where(b => !run.blueprints.Contains(b) && MetaProgress.IsAvailable(b));

        public static IEnumerable<GearRewardDef> GearPool(bool implant) =>
            DefDatabase<GearRewardDef>.AllDefs.Where(g => g.implant == implant && MetaProgress.IsAvailable(g));

        public static List<RewardCard> Generate(WorldComponent_RoguelikeRun run, RunRewardKind nodeReward)
        {
            int count = Mathf.Max(2, 3 + run.relics.Sum(r => r.extraRewardChoices) + MetaProgress.ExtraRewardChoices + run.RewardChoiceOffset);
            var cards = new List<RewardCard>();
            for (int i = 0; i < count; i++)
            {
                if (nodeReward == RunRewardKind.Relics)
                {
                    var relic = RelicPool(run).Where(r => cards.All(c => c.relic != r)).RandomElementWithFallback();
                    if (relic != null) { cards.Add(new RewardCard { kind = RewardKind.Relic, relic = relic }); continue; }
                }
                var card = nodeReward == RunRewardKind.Recruits || Rand.Chance(RecruitChanceInStandardPool)
                    ? new RewardCard { kind = RewardKind.Recruit, recruit = GenerateRecruit() }
                    : RandomNonRecruit(run, cards);
                if (card != null) cards.Add(card);
            }
            return cards;
        }

        private static RewardCard RandomNonRecruit(WorldComponent_RoguelikeRun run, List<RewardCard> taken)
        {
            var relics = RelicPool(run).Where(r => taken.All(c => c.relic != r)).ToList();
            var blueprints = BlueprintPool(run).Where(b => taken.All(c => c.blueprint != b)).ToList();
            var gear = GearPool(false).Where(g => taken.All(c => c.gear != g)).ToList();
            var implants = GearPool(true).Where(g => taken.All(c => c.gear != g)).ToList();
            var options = new List<(RewardKind kind, float weight)>();
            if (relics.Count > 0) options.Add((RewardKind.Relic, 30f));
            if (gear.Count > 0) options.Add((RewardKind.Gear, 30f));
            if (implants.Count > 0) options.Add((RewardKind.Implant, 15f));
            if (blueprints.Count > 0) options.Add((RewardKind.Blueprint, 25f));
            if (options.Count == 0) return null;
            var kind = options.RandomElementByWeight(o => o.weight).kind;
            return kind switch
            {
                RewardKind.Relic => new RewardCard { kind = kind, relic = relics.RandomElement() },
                RewardKind.Gear => new RewardCard { kind = kind, gear = gear.RandomElement() },
                RewardKind.Implant => new RewardCard { kind = kind, gear = implants.RandomElement() },
                _ => new RewardCard { kind = kind, blueprint = blueprints.RandomElement() },
            };
        }

        public static Pawn GenerateRecruit()
        {
            var request = new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false, colonistRelationChanceFactor: 0f, forceAddFreeWarmLayerIfNeeded: true);
            return PawnGenerator.GeneratePawn(request);
        }

        /// <summary>Gives the player the card's reward. Items and recruits appear next to the squad (or join the caravan if there's no map).</summary>
        public static void Apply(RewardCard card, Map map, Caravan caravan = null)
        {
            var run = WorldComponent_RoguelikeRun.Get;
            switch (card.kind)
            {
                case RewardKind.Relic:
                    run.AddRelic(card.relic);
                    break;
                case RewardKind.Blueprint:
                    if (!run.blueprints.Contains(card.blueprint)) run.blueprints.Add(card.blueprint);
                    break;
                case RewardKind.Gear:
                case RewardKind.Implant:
                    int left = card.gear.count;
                    while (left > 0)
                    {
                        var thing = ThingMaker.MakeThing(card.gear.thing, card.gear.stuff ?? (card.gear.thing.MadeFromStuff ? GenStuff.DefaultStuffFor(card.gear.thing) : null));
                        thing.stackCount = Mathf.Min(left, card.gear.thing.stackLimit);
                        left -= thing.stackCount;
                        Deliver(thing, map, caravan);
                    }
                    break;
                case RewardKind.Recruit:
                    if (card.recruit.Faction != Faction.OfPlayer) card.recruit.SetFaction(Faction.OfPlayer);
                    Deliver(card.recruit, map, caravan);
                    break;
            }
        }

        private static void Deliver(Thing thing, Map map, Caravan caravan)
        {
            if (map != null && Find.Maps.Contains(map))
            {
                var near = map.mapPawns.FreeColonistsSpawned.FirstOrDefault()?.Position ?? map.Center;
                if (thing is Pawn pawn)
                {
                    var cell = CellFinder.TryFindRandomCellNear(near, map, 4, c => c.Standable(map), out var found) ? found : near;
                    GenSpawn.Spawn(pawn, cell, map);
                }
                else GenPlace.TryPlaceThing(thing, near, map, ThingPlaceMode.Near);
                return;
            }
            if (caravan != null)
            {
                if (thing is Pawn pawn)
                {
                    // Caravan members must be world pawns.
                    if (!pawn.IsWorldPawn()) Find.WorldPawns.PassToWorld(pawn);
                    caravan.AddPawn(pawn, true);
                }
                else CaravanInventoryUtility.GiveThing(caravan, thing);
            }
        }

        /// <summary>Unpicked recruits go back to the world so the game can clean them up.</summary>
        public static void DiscardUnpicked(IEnumerable<RewardCard> cards, RewardCard picked)
        {
            foreach (var c in cards)
                if (c != picked && c.recruit != null && !c.recruit.Spawned && !c.recruit.Destroyed)
                    Find.WorldPawns.PassToWorld(c.recruit, PawnDiscardDecideMode.Discard);
        }
    }

    /// <summary>Applies relic effects that happen at node start.</summary>
    public static class RelicEffects
    {
        public static void OnNodeStart(Map map)
        {
            var run = WorldComponent_RoguelikeRun.Get;
            if (run is not { active: true }) return;
            if (run.relics.Any(r => r.healAnimalsOnEnter))
                foreach (var animal in map.mapPawns.SpawnedColonyAnimals)
                    HealthUtility.HealNonPermanentInjuriesAndRestoreLegs(animal);
            if (run.relics.Any(r => r.fixWorstConditionOnEnter))
            {
                var hurt = map.mapPawns.FreeColonistsSpawned.Where(p => HealthUtility.TryGetWorstHealthCondition(p, out _, out _)).ToList();
                if (hurt.Count > 0)
                {
                    var pawn = hurt.RandomElement();
                    var text = HealthUtility.FixWorstHealthCondition(pawn);
                    if (!text.NullOrEmpty()) Messages.Message(text, pawn, MessageTypeDefOf.PositiveEvent);
                }
            }
            run.ApplyRelicHediffs();
        }

        public static void EnsureHediff(Pawn pawn, HediffDef def)
        {
            if (pawn.health?.hediffSet != null && !pawn.health.hediffSet.HasHediff(def)) pawn.health.AddHediff(def);
        }
    }

    /// <summary>During a run, buildings in un-drafted blueprint groups are hidden from the build menu.</summary>
    [HarmonyPatch(typeof(Designator_Build), nameof(Designator_Build.Visible), MethodType.Getter)]
    public static class Patch_DesignatorBuild_Visible
    {
        public static void Postfix(Designator_Build __instance, ref bool __result)
        {
            if (!__result) return;
            var run = WorldComponent_RoguelikeRun.Get;
            if (run is not { active: true }) return;
            if (run.IsBuildingLocked(__instance.PlacingDef)) __result = false;
        }
    }

    /// <summary>Relics that change how much colonists and pack animals can carry in caravans.</summary>
    [HarmonyPatch(typeof(MassUtility), nameof(MassUtility.Capacity))]
    public static class Patch_MassUtility_Capacity
    {
        public static void Postfix(Pawn p, ref float __result)
        {
            var run = WorldComponent_RoguelikeRun.Get;
            if (run is not { active: true } || run.relics.Count == 0 || p.Faction != Faction.OfPlayer) return;
            float factor = 1f;
            foreach (var r in run.relics) factor *= p.RaceProps.Humanlike ? r.colonistCapacityFactor : r.packAnimalCapacityFactor;
            __result *= factor;
        }
    }
}
