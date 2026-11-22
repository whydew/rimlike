using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace Roguelike
{
    /// <summary>A passive run-wide effect.</summary>
    public class RelicDef : Def
    {
        public ThingDef iconThing;
        public RelicRarity rarity = RelicRarity.Common;
        /// <summary>Draw weight: common 3, uncommon 2, rare 1, legendary 0.35.</summary>
        public float Weight => rarity switch { RelicRarity.Legendary => 0.35f, RelicRarity.Rare => 1f, RelicRarity.Uncommon => 2f, _ => 3f };
        /// <summary>Stays in the pool after it's picked, up to this many copies; each copy raises its hediff's severity by 1.</summary>
        public int maxStacks = 1;
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
        /// <summary>Merchant prices are reduced by this fraction.</summary>
        public float shopDiscount;
        // DLC starter relics.
        public bool grantPsylink;          // Royalty: one colonist starts with a psylink.
        public bool psyfocusOnEnter;       // Royalty: psyfocus refills at the start of each node.
        public bool nobleTitle;            // Royalty: one colonist starts as an Imperial knight.
        public bool imperialWrit;          // Royalty: Imperial troopers drop in to help once per act on a combat node.
        public bool voidCodex;             // Anomaly: killing an entity grants a relic (once per node); creepy events at node start.
    }

    /// <summary>A gear or implant item reward.</summary>
    public class GearRewardDef : Def
    {
        public ThingDef thing;
        public ThingDef stuff;
        public int count = 1;
        /// <summary>Further items given with the main one, as a set (e.g. a matching helmet).</summary>
        public List<ThingDef> extraThings = new List<ThingDef>();
        /// <summary>Draw weight in the gear pool (1 is normal).</summary>
        public float weight = 1f;
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

    public enum RewardKind { Relic, Gear, Implant, Blueprint, Recruit, Path, Research }

    /// <summary>One card on the reward screen.</summary>
    public class RewardCard
    {
        public RewardKind kind;
        public RelicDef relic;
        public GearRewardDef gear;
        public BlueprintGroupDef blueprint;
        public Pawn recruit;
        /// <summary>For Path cards: the node this card leads to.</summary>
        public RunNode pathNode;
        /// <summary>For Research cards: the project the card completes.</summary>
        public ResearchProjectDef research;

        public string Title => kind switch
        {
            RewardKind.Relic => relic.LabelCap,
            RewardKind.Gear or RewardKind.Implant => gear.LabelCap,
            RewardKind.Blueprint => blueprint.LabelCap,
            RewardKind.Recruit => recruit.Name.ToStringFull,
            RewardKind.Path => (pathNode.IsBoss ? "Boss: " : "") + pathNode.preset.LabelCap,
            RewardKind.Research => research.LabelCap,
            _ => "?",
        };

        public string KindLabel => kind switch
        {
            RewardKind.Relic => relic.rarity == RelicRarity.Common ? "Relic" : $"{relic.rarity} relic",
            RewardKind.Gear => "Gear",
            RewardKind.Implant => "Implant",
            RewardKind.Blueprint => "Blueprint",
            RewardKind.Recruit => "Recruit",
            RewardKind.Path => pathNode.IsBoss ? $"Act {pathNode.act} boss" : GenText.SplitCamelCase(pathNode.preset.kind.ToString()),
            RewardKind.Research => "Research",
            _ => "",
        };

        public Color KindColor => kind switch
        {
            RewardKind.Relic => relic.rarity == RelicRarity.Legendary ? new Color(0.9f, 0.35f, 0.85f) : relic.rarity == RelicRarity.Rare ? new Color(0.95f, 0.55f, 0.15f) : relic.rarity == RelicRarity.Uncommon ? new Color(0.55f, 0.6f, 0.95f) : new Color(0.85f, 0.7f, 0.25f),
            RewardKind.Gear => new Color(0.45f, 0.6f, 0.8f),
            RewardKind.Implant => new Color(0.3f, 0.75f, 0.7f),
            RewardKind.Blueprint => new Color(0.85f, 0.5f, 0.25f),
            RewardKind.Recruit => new Color(0.45f, 0.8f, 0.4f),
            RewardKind.Path => pathNode.IsBoss ? new Color(0.85f, 0.3f, 0.25f) : pathNode.preset.HasMap ? new Color(0.55f, 0.55f, 0.6f) : new Color(0.4f, 0.65f, 0.85f),
            RewardKind.Research => new Color(0.7f, 0.5f, 0.95f),
            _ => Color.white,
        };
    }

    /// <summary>Builds reward card sets and applies picked cards.</summary>
    public static class RewardUtility
    {
        public const float RecruitChanceInStandardPool = 0.1f;

        public static IEnumerable<RelicDef> RelicPool(WorldComponent_RoguelikeRun run) =>
            DefDatabase<RelicDef>.AllDefs.Where(r => !r.starterOnly && run.relics.Count(o => o == r) < r.maxStacks && MetaProgress.IsAvailable(r));

        public static IEnumerable<BlueprintGroupDef> BlueprintPool(WorldComponent_RoguelikeRun run) =>
            DefDatabase<BlueprintGroupDef>.AllDefs.Where(b => !run.blueprints.Contains(b) && MetaProgress.IsAvailable(b));

        /// <summary>Research the squad could start now (prerequisites done), up to industrial tech.</summary>
        public static IEnumerable<ResearchProjectDef> ResearchPool() =>
            DefDatabase<ResearchProjectDef>.AllDefs.Where(r => !r.IsFinished && r.PrerequisitesCompleted && r.techLevel <= TechLevel.Industrial);

        /// <summary>Research that unlocks a gated objective or a blueprint building is offered more often.</summary>
        public static float ResearchWeight(ResearchProjectDef r)
        {
            bool objective = DefDatabase<NodePresetDef>.AllDefs.Any(p => p.requiresResearch.Contains(r));
            bool building = BlueprintGroupDef.Gated.Any(b => b.researchPrerequisites?.Contains(r) == true);
            return objective ? 4f : building ? 2f : 1f;
        }

        public static IEnumerable<GearRewardDef> GearPool(bool implant) =>
            DefDatabase<GearRewardDef>.AllDefs.Where(g => g.implant == implant && MetaProgress.IsAvailable(g));

        /// <summary>How many cards a reward screen shows: 3, plus relics and meta bonuses, minus Ascension 3+.</summary>
        public static int ChoiceCount(WorldComponent_RoguelikeRun run) =>
            Mathf.Max(2, 3 + run.relics.Sum(r => r.extraRewardChoices) + MetaProgress.ExtraRewardChoices + run.RewardChoiceOffset);

        public static List<RewardCard> Generate(WorldComponent_RoguelikeRun run, RunRewardKind nodeReward)
        {
            int count = ChoiceCount(run);
            var cards = new List<RewardCard>();
            for (int i = 0; i < count; i++)
            {
                if (nodeReward == RunRewardKind.Relics)
                {
                    var relic = RelicPool(run).Where(r => cards.All(c => c.relic != r)).RandomElementByWeightWithFallback(r => r.Weight);
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
            var research = ResearchPool().Where(r => taken.All(c => c.research != r)).ToList();
            var options = new List<(RewardKind kind, float weight)>();
            if (relics.Count > 0) options.Add((RewardKind.Relic, 30f));
            if (gear.Count > 0) options.Add((RewardKind.Gear, 30f));
            if (implants.Count > 0) options.Add((RewardKind.Implant, 15f));
            if (blueprints.Count > 0) options.Add((RewardKind.Blueprint, 25f));
            if (research.Count > 0) options.Add((RewardKind.Research, 18f));
            if (options.Count == 0) return null;
            var kind = options.RandomElementByWeight(o => o.weight).kind;
            return kind switch
            {
                RewardKind.Relic => new RewardCard { kind = kind, relic = relics.RandomElementByWeight(r => r.Weight) },
                RewardKind.Gear => new RewardCard { kind = kind, gear = gear.RandomElementByWeight(g => g.weight) },
                RewardKind.Implant => new RewardCard { kind = kind, gear = implants.RandomElementByWeight(g => g.weight) },
                RewardKind.Research => new RewardCard { kind = kind, research = research.RandomElementByWeight(ResearchWeight) },
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
                case RewardKind.Research:
                    if (!card.research.IsFinished) Find.ResearchManager.FinishProject(card.research, doCompletionDialog: false, doCompletionLetter: false);
                    Messages.Message($"Research complete: {card.research.LabelCap}.", MessageTypeDefOf.PositiveEvent);
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
                    foreach (var extra in card.gear.extraThings)
                        Deliver(ThingMaker.MakeThing(extra, extra.MadeFromStuff ? GenStuff.DefaultStuffFor(extra) : null), map, caravan);
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

    /// <summary>Applies relic effects that happen at run start and node start.</summary>
    public static class RelicEffects
    {
        /// <summary>One-time starter effects, right after landing.</summary>
        public static void OnRunStart(Map map)
        {
            var run = WorldComponent_RoguelikeRun.Get;
            var first = map.mapPawns.FreeColonists.FirstOrDefault(p => p.RaceProps.Humanlike);
            if (run == null || first == null) return;
            if (ModsConfig.RoyaltyActive && run.relics.Any(r => r.grantPsylink) && !first.HasPsylink) first.ChangePsylinkLevel(1, sendLetter: false);
            if (ModsConfig.RoyaltyActive && run.relics.Any(r => r.nobleTitle) && Faction.OfEmpire != null && first.royalty != null)
            {
                var knight = DefDatabase<RoyalTitleDef>.GetNamedSilentFail("Knight");
                if (knight != null) first.royalty.SetTitle(Faction.OfEmpire, knight, grantRewards: false, sendLetter: false);
            }
        }

        public static void OnNodeStart(Map map)
        {
            var run = WorldComponent_RoguelikeRun.Get;
            if (run is not { active: true }) return;
            if (ModsConfig.RoyaltyActive && run.relics.Any(r => r.psyfocusOnEnter))
                foreach (var p in map.mapPawns.FreeColonistsSpawned.Where(p => p.HasPsylink)) p.psychicEntropy?.OffsetPsyfocusDirectly(1f);
            var comp = map.GetComponent<MapComponent_RoguelikeNode>();
            if (ModsConfig.RoyaltyActive && run.relics.Any(r => r.imperialWrit) && run.writUsedAct < run.act && comp?.preset?.objective == ObjectiveKind.KillAll)
            {
                run.writUsedAct = run.act;
                SendImperialTroopers(map);
            }
            if (ModsConfig.AnomalyActive && run.relics.Any(r => r.voidCodex) && Rand.Chance(0.25f)) TryCreepyEvent(map);
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

        /// <summary>Imperial Writ: four Imperial troopers drop in near the squad and fight alongside it.</summary>
        public static void SendImperialTroopers(Map map)
        {
            var empire = Faction.OfEmpire;
            var kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Empire_Fighter_Trooper");
            if (empire == null || kind == null || empire.HostileTo(Faction.OfPlayer)) return;
            var near = map.mapPawns.FreeColonistsSpawned.FirstOrDefault()?.Position ?? map.Center;
            var troopers = new List<Pawn>();
            for (int i = 0; i < 4; i++) troopers.Add(PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, empire, mustBeCapableOfViolence: true)));
            DropPodUtility.DropThingsNear(near, map, troopers.Cast<Thing>(), forbid: false);
            LordMaker.MakeNewLord(empire, new LordJob_AssistColony(empire, near), map, troopers);
            Messages.Message("Imperial Writ: troopers are dropping in to help.", new TargetInfo(near, map), MessageTypeDefOf.PositiveEvent);
        }

        /// <summary>Bloodstained Codex: a random Anomaly threat that can fire on this map.</summary>
        public static bool TryCreepyEvent(Map map)
        {
            var options = DefDatabase<IncidentDef>.AllDefs.Where(d => d.modContentPack?.PackageId == "ludeon.rimworld.anomaly"
                && (d.category == IncidentCategoryDefOf.ThreatSmall || d.category == IncidentCategoryDefOf.ThreatBig) && d.TargetAllowed(map)).ToList();
            foreach (var def in options.InRandomOrder())
            {
                var parms = StorytellerUtility.DefaultParmsNow(def.category, map);
                parms.forced = true;
                if (def.Worker.CanFireNow(parms) && def.Worker.TryExecute(parms)) return true;
            }
            return false;
        }

        /// <summary>Adds the hediff if missing and sets its severity to the relic's stack count.</summary>
        public static void EnsureHediff(Pawn pawn, HediffDef def, int stacks)
        {
            if (pawn.health?.hediffSet == null) return;
            var h = pawn.health.hediffSet.GetFirstHediffOfDef(def) ?? pawn.health.AddHediff(def);
            if (h != null && h.Severity != stacks) h.Severity = stacks;
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
