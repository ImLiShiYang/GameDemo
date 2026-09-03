using System.Collections.Generic;

public sealed class WorldSnapshotMessage
{
    public uint ServerTick;
    public readonly List<PlayerNetworkState> Players = new List<PlayerNetworkState>(2);
    public readonly List<EntityNetworkState> Entities = new List<EntityNetworkState>(32);
}
