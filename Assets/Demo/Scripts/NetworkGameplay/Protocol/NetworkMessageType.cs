public enum NetworkMessageType : ushort
{
    ConnectRequest = 1,
    Welcome = 2,
    ConnectionRejected = 3,
    ClientInput = 10,
    WorldSnapshot = 20,
    EntitySpawn = 21,
    EntityDespawn = 22,
    BattleEvent = 30
}
