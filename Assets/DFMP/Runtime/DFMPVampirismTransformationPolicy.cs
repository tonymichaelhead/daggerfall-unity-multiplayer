namespace DFMP.Runtime
{
    public enum DFMPVampirismTransformationAction
    {
        AllowVanilla,
        DeferToServerTransition
    }

    public static class DFMPVampirismTransformationPolicy
    {
        public static DFMPVampirismTransformationAction GetAction(bool isMultiplayerClientConnected)
        {
            return isMultiplayerClientConnected
                ? DFMPVampirismTransformationAction.DeferToServerTransition
                : DFMPVampirismTransformationAction.AllowVanilla;
        }
    }
}