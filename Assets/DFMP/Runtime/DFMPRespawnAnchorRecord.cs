using System;

namespace DFMP.Runtime
{
    [Serializable]
    public class DFMPRespawnAnchorRecord
    {
        public string Kind = DFMPRespawnAnchorKind.None.ToString();
        public int WorldX;
        public float WorldY;
        public int WorldZ;
        public DFMPWorldContextRecord Context = new DFMPWorldContextRecord();

        public void Normalize()
        {
            DFMPRespawnAnchorKind parsedKind;
            if (!Enum.TryParse(Kind, true, out parsedKind))
                parsedKind = DFMPRespawnAnchorKind.None;

            Kind = parsedKind.ToString();
            if (Context == null)
                Context = new DFMPWorldContextRecord();
            Context.Normalize();
        }

        public static DFMPRespawnAnchorRecord FromKey(DFMPRespawnAnchorKind kind, DFMPWorldPosition position, DFMPWorldContextKey context)
        {
            var record = new DFMPRespawnAnchorRecord
            {
                Kind = kind.ToString(),
                WorldX = position.WorldX,
                WorldY = position.WorldY,
                WorldZ = position.WorldZ,
                Context = DFMPWorldContextRecord.FromKey(context)
            };
            record.Normalize();
            return record;
        }

        public bool IsInnAnchor()
        {
            return string.Equals(Kind, DFMPRespawnAnchorKind.Inn.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }
}