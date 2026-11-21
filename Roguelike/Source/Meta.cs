using Verse;

namespace Roguelike
{
    /// <summary>Meta-progression: which locked content has been unlocked across runs. Filled in by stage 4.</summary>
    public static class MetaProgress
    {
        public static bool IsUnlocked(Def def) => false;

        public static bool IsAvailable(RelicDef d) => !d.requiresUnlock || IsUnlocked(d);
        public static bool IsAvailable(GearRewardDef d) => !d.requiresUnlock || IsUnlocked(d);
        public static bool IsAvailable(BlueprintGroupDef d) => !d.requiresUnlock || IsUnlocked(d);
    }
}
