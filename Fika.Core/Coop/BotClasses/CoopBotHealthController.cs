﻿// © 2025 Lacyway All Rights Reserved

using EFT;
using EFT.InventoryLogic;
using Fika.Core.Coop.Players;
using Fika.Core.Networking;
using Fika.Core.Networking.Packets;

namespace Fika.Core.Coop.ClientClasses
{
    public sealed class CoopBotHealthController(Profile.ProfileHealthClass healthInfo, Player player, InventoryController inventoryController, SkillManager skillManager, bool aiHealth)
        : GControl4(healthInfo, player, inventoryController, skillManager, aiHealth)
    {
        private readonly CoopBot coopBot = (CoopBot)player;
        public override bool _sendNetworkSyncPackets
        {
            get
            {
                return true;
            }
        }

        public override void SendNetworkSyncPacket(NetworkHealthSyncPacketStruct packet)
        {
            if (packet.SyncType == NetworkHealthSyncPacketStruct.ESyncType.IsAlive && !packet.Data.IsAlive.IsAlive)
            {
                coopBot.PacketSender.PacketQueue.Enqueue(coopBot.SetupCorpseSyncPacket(packet));
                return;
            }

            coopBot.PacketSender.PacketQueue.Enqueue(new HealthSyncPacket()
            {
                Packet = packet
            });
        }
    }
}
