﻿using LiteNetLib.Utils;

namespace Fika.Core.Networking.Packets.World
{
    public struct SyncTransitControllersPacket : INetSerializable
    {
        public string ProfileId;
        public string RaidId;
        public int Count;
        public string[] Maps;

        public void Deserialize(NetDataReader reader)
        {
            ProfileId = reader.GetString();
            RaidId = reader.GetString();
            Count = reader.GetInt();
            Maps = reader.GetStringArray();
        }

        public readonly void Serialize(NetDataWriter writer)
        {
            writer.Put(ProfileId);
            writer.Put(RaidId);
            writer.Put(Count);
            writer.PutArray(Maps);
        }
    }
}
