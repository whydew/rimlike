using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Roguelike
{
    /// <summary>
    /// One condition of a base-building objective. Counts can scale with the squad size frozen on entry.
    /// Produce: items of a kind made on this node (current amount on the map minus what was there on arrival).
    /// Keep: items of a kind on hand. Build: finished buildings (optionally of a stuff category, e.g. stone).
    /// Bedrooms / DiningRoom: enclosed, roofed rooms with that role and at least minCells cells.
    /// Shelter: human beds in enclosed, roofed rooms. NoDeaths: everyone who arrived is still alive.
    /// Any requirement can also ask for room quality (minImpressiveness, rooms only) or to be held for holdDays:
    /// the days it is met add up (pausing, not resetting, while it isn't), and once they reach holdDays it stays done.
    /// </summary>
    public enum RequirementKind { Produce, Keep, Build, Bedrooms, DiningRoom, Shelter, NoDeaths }

    public class NodeRequirement
    {
        public RequirementKind kind;
        public ThingDef thing;
        public int count = 1;
        public bool perColonist;
        public StuffCategoryDef stuffCategory;
        public int minCells = 12;
        /// <summary>Bedrooms and dining rooms: the room's impressiveness must reach this (40 decent, 50 slightly impressive, 65 somewhat impressive).</summary>
        public float minImpressiveness;
        public float holdDays;

        public bool IsRoom => kind is RequirementKind.Bedrooms or RequirementKind.DiningRoom;

        /// <summary>" (slightly impressive)" for a room with a quality bar.</summary>
        private string Quality => IsRoom && minImpressiveness > 0f ? $", {RoomStatDefOf.Impressiveness.GetScoreStage(minImpressiveness).label}" : "";

        private string HoldText => holdDays > 0f ? $" for {holdDays:0.#} days" : "";

        public int Target(int squad) => kind is RequirementKind.NoDeaths or RequirementKind.DiningRoom ? 1 : perColonist ? count * squad : count;

        public string Describe(int squad) => kind switch
        {
            RequirementKind.Produce => $"produce {Target(squad)} {thing.label}",
            RequirementKind.Keep => $"have {Target(squad)} {thing.label} on hand",
            RequirementKind.Build => $"build {Target(squad)} {(stuffCategory != null ? stuffCategory.label + " " : "")}{thing.label}",
            RequirementKind.Bedrooms => $"{Target(squad)} roofed bedrooms (at least {minCells} cells each{Quality})",
            RequirementKind.DiningRoom => $"a roofed dining room (at least {minCells} cells{Quality})",
            RequirementKind.Shelter => $"{Target(squad)} beds indoors",
            RequirementKind.NoDeaths => "nobody dies",
            _ => "",
        } + HoldText;

        /// <summary>The requirement in a few words, for a card.</summary>
        public string Card(int squad) => kind switch
        {
            RequirementKind.Produce => $"Produce {Target(squad)} {thing.label}",
            RequirementKind.Keep => $"Keep {Target(squad)} {thing.label}",
            RequirementKind.Build => $"Build {Target(squad)} {(stuffCategory != null ? stuffCategory.label + " " : "")}{thing.label}",
            RequirementKind.Bedrooms => $"{Target(squad)} bedrooms ({minCells}+ cells{Quality})",
            RequirementKind.DiningRoom => $"Dining room ({minCells}+ cells{Quality})",
            RequirementKind.Shelter => $"{Target(squad)} beds indoors",
            RequirementKind.NoDeaths => "Nobody dies",
            _ => "",
        } + HoldText;

        public string Short(int squad, int have, float held) => ShortCount(squad, have) + (holdDays > 0f ? $" (held {System.Math.Min(held, holdDays):0.#}/{holdDays:0.#} days)" : "");

        private string ShortCount(int squad, int have) => kind switch
        {
            RequirementKind.NoDeaths => have > 0 ? "no deaths" : "a squad member died",
            RequirementKind.DiningRoom => have > 0 ? "dining room" : "no dining room",
            RequirementKind.Bedrooms => $"{have}/{Target(squad)} bedrooms",
            RequirementKind.Shelter => $"{have}/{Target(squad)} beds indoors",
            _ => $"{have}/{Target(squad)} {(stuffCategory != null ? stuffCategory.label + " " : "")}{thing?.label}",
        };
    }

    /// <summary>Tuning for a weather modifier when nodes roll it at random: its daily rate, grace period and where it can appear.</summary>
    public class ModifierExtension : DefModExtension
    {
        public float ratePerDay = 1f;
        public float graceDays = 4f;
        public float weight = 1f;
        /// <summary>World-tile average temperatures (°C) where this modifier can appear.</summary>
        public FloatRange tileTemperature = new FloatRange(-999f, 999f);
        public bool coastalOnly;
    }

    public static class ModifierPool
    {
        public static IEnumerable<GameConditionDef> All =>
            DefDatabase<GameConditionDef>.AllDefs.Where(d => d.GetModExtension<ModifierExtension>() != null);

        /// <summary>A random modifier that fits the tile's climate and coast.</summary>
        public static GameConditionDef PickFor(PlanetTile tile)
        {
            float temp = Find.WorldGrid[tile].temperature;
            bool coastal = Find.World.CoastDirectionAt(tile).IsValid;
            var options = All.Where(d =>
            {
                var ext = d.GetModExtension<ModifierExtension>();
                return ext.tileTemperature.Includes(temp) && (!ext.coastalOnly || coastal);
            }).ToList();
            return options.RandomElementByWeightWithFallback(d => d.GetModExtension<ModifierExtension>().weight);
        }
    }

    /// <summary>Evaluates base-building requirements on a node map.</summary>
    public static class RequirementUtility
    {
        private static RoomRoleDef diningRoom;

        public static int CountOnMap(Map map, ThingDef def)
        {
            var list = new List<Thing>();
            ThingOwnerUtility.GetAllThingsRecursively(map, ThingRequest.ForDef(def), list);
            return list.Sum(t => t.stackCount);
        }

        private static IEnumerable<Room> EnclosedRooms(Map map) =>
            map.regionGrid.AllRooms.Where(r => r.ProperRoom && !r.PsychologicallyOutdoors && !r.UsesOutdoorTemperature);

        private static bool Impressive(Room room, NodeRequirement r) => r.minImpressiveness <= 0f || room.GetStat(RoomStatDefOf.Impressiveness) >= r.minImpressiveness;

        /// <summary>
        /// Makes sure the map holds at least <paramref name="count"/> of a mineable resource (e.g. steel, components):
        /// counts what's lying around and what its ore would yield, then scatters ore lumps into rock (or open ground) for the rest.
        /// </summary>
        public static int EnsureMineable(Map map, ThingDef resource, int count)
        {
            var ore = DefDatabase<ThingDef>.AllDefs.FirstOrDefault(d => d.building?.mineableThing == resource && d.building.mineableScatterCommonality > 0f);
            if (ore == null) return 0;
            int perCell = System.Math.Max(1, (int)(ore.building.EffectiveMineableYield * 0.75f)); // allow for low mining skill
            int Have() => CountOnMap(map, resource) + map.listerThings.ThingsOfDef(ore).Count * perCell;
            // Plain rock only (not other ore), else open ground.
            bool Rock(IntVec3 c) => c.GetEdifice(map) is { } e && e.def.building?.isNaturalRock == true && e.def.building.mineableScatterCommonality <= 0f
                                    && !c.Fogged(map) && !c.CloseToEdge(map, 10);
            bool Open(IntVec3 c) => c.Standable(map) && c.GetEdifice(map) == null && c.GetFirstItem(map) == null && c.GetFirstPawn(map) == null
                                    && !c.CloseToEdge(map, 10) && !c.Roofed(map);
            int spawned = 0;
            for (int lump = 0; lump < 30; lump++)
            {
                int cells = (count - Have() + perCell - 1) / perCell;
                if (cells <= 0) break;
                if (!CellFinder.TryFindRandomCell(map, Rock, out var center) && !CellFinder.TryFindRandomCell(map, Open, out center)) break;
                foreach (var c in GridShapeMaker.IrregularLump(center, map, System.Math.Min(cells, 20), c => Rock(c) || Open(c)))
                {
                    GenSpawn.Spawn(ore, c, map);
                    spawned++;
                }
            }
            return spawned;
        }

        public static int Have(MapComponent_RoguelikeNode c, NodeRequirement r)
        {
            var map = c.map;
            switch (r.kind)
            {
                case RequirementKind.Produce:
                    return System.Math.Max(0, CountOnMap(map, r.thing) - c.Baseline(r.thing));
                case RequirementKind.Keep:
                    return CountOnMap(map, r.thing);
                case RequirementKind.Build:
                    return map.listerBuildings.AllBuildingsColonistOfDef(r.thing)
                        .Count(b => r.stuffCategory == null || b.Stuff?.stuffProps?.categories?.Contains(r.stuffCategory) == true);
                case RequirementKind.Bedrooms:
                    return EnclosedRooms(map).Count(room => room.Role == RoomRoleDefOf.Bedroom && room.CellCount >= r.minCells && Impressive(room, r));
                case RequirementKind.DiningRoom:
                    diningRoom ??= DefDatabase<RoomRoleDef>.GetNamedSilentFail("DiningRoom");
                    return EnclosedRooms(map).Any(room => room.Role == diningRoom && room.CellCount >= r.minCells && Impressive(room, r)) ? 1 : 0;
                case RequirementKind.Shelter:
                    return map.listerBuildings.allBuildingsColonist.OfType<Building_Bed>()
                        .Count(b => b.def.building.bed_humanlike && b.GetRoom() is { ProperRoom: true, PsychologicallyOutdoors: false });
                case RequirementKind.NoDeaths:
                    return c.entrySquad.All(p => p != null && !p.Dead) ? 1 : 0;
            }
            return 0;
        }
    }
}
