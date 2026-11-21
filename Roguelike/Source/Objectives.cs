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

        public int Target(int squad) => kind is RequirementKind.NoDeaths or RequirementKind.DiningRoom ? 1 : perColonist ? count * squad : count;

        public string Describe(int squad) => kind switch
        {
            RequirementKind.Produce => $"produce {Target(squad)} {thing.label}",
            RequirementKind.Keep => $"have {Target(squad)} {thing.label} on hand",
            RequirementKind.Build => $"build {Target(squad)} {(stuffCategory != null ? stuffCategory.label + " " : "")}{thing.label}",
            RequirementKind.Bedrooms => $"{Target(squad)} roofed bedrooms (at least {minCells} cells each)",
            RequirementKind.DiningRoom => $"a roofed dining room (at least {minCells} cells)",
            RequirementKind.Shelter => $"{Target(squad)} beds indoors",
            RequirementKind.NoDeaths => "nobody dies",
            _ => "",
        };

        public string Short(int squad, int have) => kind switch
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
                    return EnclosedRooms(map).Count(room => room.Role == RoomRoleDefOf.Bedroom && room.CellCount >= r.minCells);
                case RequirementKind.DiningRoom:
                    diningRoom ??= DefDatabase<RoomRoleDef>.GetNamedSilentFail("DiningRoom");
                    return EnclosedRooms(map).Any(room => room.Role == diningRoom && room.CellCount >= r.minCells) ? 1 : 0;
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
