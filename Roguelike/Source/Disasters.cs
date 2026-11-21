using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Roguelike
{
    /// <summary>Base for disaster modifiers whose intensity is "events per day": each 250-tick check rolls for one.</summary>
    public abstract class GameCondition_EscalatingStrikes : GameCondition_RoguelikeModifier
    {
        public override string IntensityText(float value) => $"{value:0.0} per day";

        public override void ApplyEffects(Map map)
        {
            if (intensity > 0f && Rand.Chance(intensity * 250f / GenDate.TicksPerDay)) Strike(map);
        }

        public abstract void Strike(Map map);

        /// <summary>Half the strikes land near the squad so a disaster can't be ignored.</summary>
        protected static IntVec3 TargetCell(Map map)
        {
            var colonists = map.mapPawns.FreeColonistsSpawned;
            if (colonists.Count > 0 && Rand.Bool &&
                CellFinder.TryFindRandomCellNear(colonists.RandomElement().Position, map, 20, c => !c.Roofed(map) && c.Standable(map), out var near))
                return near;
            return CellFinder.TryFindRandomCell(map, c => !c.Roofed(map) && c.Standable(map), out var any) ? any : map.Center;
        }
    }

    // ===================== Meteors =====================

    /// <summary>Meteors that get bigger and more frequent. The aftermath comes from <see cref="Skyfaller_RoguelikeMeteor"/>.</summary>
    public class GameCondition_EscalatingMeteors : GameCondition_EscalatingStrikes
    {
        public int Strength => Mathf.Clamp(1 + Mathf.FloorToInt(intensity / 1.5f), 1, 3);

        public override void Strike(Map map) => SpawnMeteor(map, TargetCell(map), Strength);

        public static Skyfaller_RoguelikeMeteor SpawnMeteor(Map map, IntVec3 cell, int strength)
        {
            var chunks = new List<Thing>();
            for (int i = 0; i < strength; i++) chunks.Add(ThingMaker.MakeThing(ThingDefOf.ChunkSlagSteel));
            var meteor = (Skyfaller_RoguelikeMeteor)SkyfallerMaker.SpawnSkyfaller(DefDatabase<ThingDef>.GetNamed("Roguelike_MeteorIncoming"), chunks, cell, map);
            meteor.strength = strength;
            Messages.Message("A meteor is falling!", new TargetInfo(cell, map), MessageTypeDefOf.ThreatSmall);
            return meteor;
        }
    }

    /// <summary>
    /// A meteor with an aftermath: the vanilla streak and landing shadow warn you, then on impact it leaves a
    /// rubble crater, starts fires, shakes the screen, stuns everyone nearby with a shockwave, and stronger
    /// ones sometimes set off a quake that damages buildings.
    /// </summary>
    public class Skyfaller_RoguelikeMeteor : Skyfaller
    {
        public int strength = 1;
        public static int Impacts;

        protected override void Impact()
        {
            var map = Map;
            var pos = Position;
            base.Impact();
            Aftermath(map, pos, strength);
        }

        public static void Aftermath(Map map, IntVec3 pos, int strength)
        {
            Impacts++;
            float radius = 3f + strength;
            foreach (var c in GenRadial.RadialCellsAround(pos, radius, true).Where(c => c.InBounds(map)))
            {
                FilthMaker.TryMakeFilth(c, map, ThingDefOf.Filth_RubbleRock);
                if (Rand.Chance(0.25f)) FireUtility.TryStartFireIn(c, map, 0.3f, null);
                if (Rand.Chance(0.3f)) FleckMaker.ThrowDustPuffThick(c.ToVector3Shifted(), map, 2f, new Color(0.5f, 0.45f, 0.4f));
            }

            // Shockwave: stun everyone in range.
            foreach (var pawn in map.mapPawns.AllPawnsSpawned.Where(p => p.Position.InHorDistOf(pos, radius * 2f)).ToList())
                pawn.stances?.stunner?.StunFor(120 + 60 * strength, null);

            if (map == Find.CurrentMap) Find.CameraDriver.shaker.DoShake(1.5f + strength);

            // Strong impacts sometimes cause a quake that damages nearby buildings.
            if (Rand.Chance(0.3f * strength))
            {
                foreach (var building in map.listerBuildings.allBuildingsColonist.Where(b => b.Position.InHorDistOf(pos, radius * 3f)).ToList())
                    building.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 15f * strength));
                if (map == Find.CurrentMap) Find.CameraDriver.shaker.DoShake(3f);
                Messages.Message("The impact shook the ground and damaged nearby buildings.", new TargetInfo(pos, map), MessageTypeDefOf.NegativeEvent);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref strength, "strength", 1);
        }
    }

    // ===================== Tornadoes =====================

    /// <summary>Vanilla tornadoes touching down more and more often.</summary>
    public class GameCondition_EscalatingTornadoes : GameCondition_EscalatingStrikes
    {
        public override void Strike(Map map)
        {
            var cell = TargetCell(map);
            GenSpawn.Spawn(DefDatabase<ThingDef>.GetNamed("Tornado"), cell, map);
            Messages.Message("A tornado has touched down!", new TargetInfo(cell, map), MessageTypeDefOf.ThreatBig);
        }
    }

    // ===================== Hurricane =====================

    /// <summary>A hurricane: a constant thunderstorm whose lightning strikes get more frequent.</summary>
    public class GameCondition_EscalatingStorm : GameCondition_EscalatingStrikes
    {
        private static WeatherDef storm;

        public override WeatherDef ForcedWeather() => storm ??= DefDatabase<WeatherDef>.GetNamed("RainyThunderstorm");

        public override float MinWindSpeed() => 1.5f;

        public override void Strike(Map map) =>
            map.weatherManager.eventHandler.AddEvent(new WeatherEvent_LightningStrike(map, TargetCell(map)));
    }

    // ===================== Flood =====================

    /// <summary>
    /// A surge that floods the map from its coast (or a random edge inland): intensity is how many cells deep the
    /// water has reached. The front turns ground into shallow water; well behind it, empty ground becomes deep water.
    /// </summary>
    public class GameCondition_RisingFlood : GameCondition_RoguelikeModifier
    {
        private const int DeepLag = 10;
        public Rot4 edge = Rot4.Invalid;
        private int shallowDone;
        private int deepDone;

        public override string IntensityText(float value) => $"{value:0} cells inland";

        public override void ApplyEffects(Map map)
        {
            if (!edge.IsValid)
            {
                var coast = Find.World.CoastDirectionAt(map.Tile);
                edge = coast.IsValid ? coast : Rot4.Random;
            }
            int shallow = Mathf.Min(Mathf.FloorToInt(intensity), DepthLimit(map));
            for (; shallowDone < shallow; shallowDone++) FloodRow(map, shallowDone, TerrainDefOf.WaterShallow);
            int deep = Mathf.Max(0, shallow - DeepLag);
            for (; deepDone < deep; deepDone++) FloodRow(map, deepDone, TerrainDefOf.WaterDeep);
        }

        private int DepthLimit(Map map) => edge == Rot4.North || edge == Rot4.South ? map.Size.z : map.Size.x;

        private void FloodRow(Map map, int depth, TerrainDef water)
        {
            int len = edge == Rot4.North || edge == Rot4.South ? map.Size.x : map.Size.z;
            for (int i = 0; i < len; i++)
            {
                IntVec3 c = edge == Rot4.North ? new IntVec3(i, 0, map.Size.z - 1 - depth)
                    : edge == Rot4.South ? new IntVec3(i, 0, depth)
                    : edge == Rot4.East ? new IntVec3(map.Size.x - 1 - depth, 0, i)
                    : new IntVec3(depth, 0, i);
                if (!c.InBounds(map) || c.GetEdifice(map) != null) continue;
                var terrain = c.GetTerrain(map);
                if (terrain == water || terrain == TerrainDefOf.WaterDeep || terrain.IsWater && water == TerrainDefOf.WaterShallow) continue;
                if (water == TerrainDefOf.WaterDeep)
                {
                    if (c.GetFirstPawn(map) != null) continue;
                    // Deep water swallows loose items.
                    foreach (var item in c.GetThingList(map).Where(t => t.def.category == ThingCategory.Item).ToList()) item.Destroy();
                }
                map.terrainGrid.SetTerrain(c, water);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref edge, "edge", Rot4.Invalid);
            Scribe_Values.Look(ref shallowDone, "shallowDone");
            Scribe_Values.Look(ref deepDone, "deepDone");
        }
    }
}
