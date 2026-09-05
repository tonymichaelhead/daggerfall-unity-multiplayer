using DaggerfallWorkshop;

namespace DFCoop.Runtime
{
    public struct DFCoopTimeSnapshot
    {
        public uint ClassicMinutes;
        public float TimeScale;

        public static DFCoopTimeSnapshot FromWorldTime(WorldTime worldTime)
        {
            if (worldTime == null)
                return default(DFCoopTimeSnapshot);

            return new DFCoopTimeSnapshot
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