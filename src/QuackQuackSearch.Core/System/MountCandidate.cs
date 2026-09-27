namespace QuackQuackSearch.Core.System;

public enum MountType
{
    Local,
    Network,
    GVFS,
    Other
}

public sealed record MountCandidate(
    string Device,
    string MountPoint,
    string FsType,
    MountType Type,
    string SuggestedMonitoringMode,
    bool IsRemovableOrUserAccessible
);
