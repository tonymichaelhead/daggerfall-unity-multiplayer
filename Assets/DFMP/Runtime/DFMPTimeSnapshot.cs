using DaggerfallWorkshop;

namespace DFMP.Runtime
{
    public struct DFMPTimeSnapshot
    {
        public uint ClassicMinutes;
        public float TimeScale;

        public static DFMPTimeSnapshot FromWorldTime(WorldTime worldTime)
        {
            if (worldTime == null)
                return default(DFMPTimeSnapshot);

            return new DFMPTimeSnapshot
            {
                ClassicMinutes = worldTime.DaggerfallDateTime.ToClassicDaggerfallTime(),
                TimeScale = worldTime.TimeScale
            };
        }

        public void ApplyToWorldTime(WorldTime worldTime)
        {
            if (worldTime == null)
                return;

            worldTime.DaggerfallDateTime.FromClassicDaggerfallTime(ClassicMinutes);
            worldTime.TimeScale = TimeScale;
        }
    }
}