﻿// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Game.Entities;
using System;
using System.Collections.Generic;

namespace Game.Networking.Packets
{
    public class AllAchievementData : ServerPacket
    {
        public AllAchievementData() : base(ServerOpcodes.AllAchievementData, ConnectionType.Instance) { }

        public override void Write()
        {
            Data.Write(_worldPacket);
        }

        public AllAchievements Data = new();
    }

    class AllAccountCriteria : ServerPacket
    {
        public AllAccountCriteria() : base(ServerOpcodes.AllAccountCriteria, ConnectionType.Instance) { }

        public override void Write()
        {
            _worldPacket.WriteInt32(Progress.Count);
            foreach (var progress in Progress)
                progress.Write(_worldPacket);
        }

        public List<CriteriaProgressPkt> Progress = new();
    }
    
    public class RespondInspectAchievements : ServerPacket
    {
        public RespondInspectAchievements() : base(ServerOpcodes.RespondInspectAchievements, ConnectionType.Instance) { }

        public override void Write()
        {
            _worldPacket.WritePackedGuid(Player);
            Data.Write(_worldPacket);
        }

        public ObjectGuid Player;
        public AllAchievements Data = new();
    }

    public class CriteriaUpdate : ServerPacket
    {
        public CriteriaUpdate() : base(ServerOpcodes.CriteriaUpdate, ConnectionType.Instance) { }

        public override void Write()
        {
            _worldPacket.WriteInt32(CriteriaID);
            _worldPacket.WriteInt64(Quantity);
            _worldPacket.WritePackedGuid(PlayerGUID);
            _worldPacket.WriteUInt32(Unused_10_1_5);
            _worldPacket.WriteUInt32(Flags);
            _worldPacket.WriteInt32((WowTime)CurrentTime);
            _worldPacket.WriteInt64(ElapsedTime);
            _worldPacket.WriteInt64(CreationTime);
            _worldPacket.WriteBit(RafAcceptanceID.HasValue);
            _worldPacket.FlushBits();

            if (RafAcceptanceID.HasValue)
                _worldPacket.WriteInt64(RafAcceptanceID.Value);
        }

        public int CriteriaID;
        public long Quantity;
        public ObjectGuid PlayerGUID;
        public uint Unused_10_1_5;
        public uint Flags;
        public RealmTime CurrentTime;
        public long ElapsedTime;
        public long CreationTime;
        public long? RafAcceptanceID;
    }

    class AccountCriteriaUpdate : ServerPacket
    {
        public AccountCriteriaUpdate() : base(ServerOpcodes.AccountCriteriaUpdate) { }

        public override void Write()
        {
            Progress.Write(_worldPacket);
        }

        public CriteriaProgressPkt Progress;
    }
    
    public class CriteriaDeleted : ServerPacket
    {
        public CriteriaDeleted() : base(ServerOpcodes.CriteriaDeleted, ConnectionType.Instance) { }

        public override void Write()
        {
            _worldPacket.WriteInt32(CriteriaID);
        }

        public int CriteriaID;
    }

    public class AchievementDeleted : ServerPacket
    {
        public AchievementDeleted() : base(ServerOpcodes.AchievementDeleted, ConnectionType.Instance) { }

        public override void Write()
        {
            _worldPacket.WriteInt32(AchievementID);
            _worldPacket.WriteInt32(Immunities);
        }

        public int AchievementID;
        public int Immunities; // this is just garbage, not used by client
    }

    public class AchievementEarned : ServerPacket
    {
        public AchievementEarned() : base(ServerOpcodes.AchievementEarned, ConnectionType.Instance) { }

        public override void Write()
        {
            _worldPacket.WritePackedGuid(Sender);
            _worldPacket.WritePackedGuid(Earner);
            _worldPacket.WriteInt32(AchievementID);
            _worldPacket.WriteInt32((WowTime)Time);
            _worldPacket.WriteUInt32(EarnerNativeRealm);
            _worldPacket.WriteUInt32(EarnerVirtualRealm);
            _worldPacket.WriteBit(Initial);
            _worldPacket.FlushBits();
        }

        public ObjectGuid Earner;
        public uint EarnerNativeRealm;
        public uint EarnerVirtualRealm;
        public int AchievementID;
        public RealmTime Time;
        public bool Initial;
        public ObjectGuid Sender;
    }

    public class BroadcastAchievement  : ServerPacket
    {
        public BroadcastAchievement() : base(ServerOpcodes.BroadcastAchievement) { }

        public override void Write()
        {
            _worldPacket.WriteBits(Name.GetByteCount(), 7);
            _worldPacket.WriteBit(GuildAchievement);
            _worldPacket.WritePackedGuid(PlayerGUID);
            _worldPacket.WriteInt32(AchievementID);
            _worldPacket.WriteString(Name);
        }

        public ObjectGuid PlayerGUID;
        public string Name = string.Empty;
        public int AchievementID;
        public bool GuildAchievement;
    }

    public class GuildCriteriaUpdate : ServerPacket
    {
        public GuildCriteriaUpdate() : base(ServerOpcodes.GuildCriteriaUpdate) { }

        public override void Write()
        {
            _worldPacket.WriteInt32(Progress.Count);

            foreach (GuildCriteriaProgress progress in Progress)
            {
                _worldPacket.WriteInt32(progress.CriteriaID);
                _worldPacket.WriteInt64(progress.DateCreated);
                _worldPacket.WriteInt64(progress.DateStarted);
                _worldPacket.WriteInt32((WowTime)progress.DateUpdated);
                _worldPacket.WriteUInt32(0); // this is a hack. this is a packed time written as int64 (progress.DateUpdated)
                _worldPacket.WriteInt64(progress.Quantity);
                _worldPacket.WritePackedGuid(progress.PlayerGUID);
                _worldPacket.WriteInt32(progress.Unused_10_1_5);
                _worldPacket.WriteInt32(progress.Flags);
            }
        }

        public List<GuildCriteriaProgress> Progress = new();
    }

    public class GuildCriteriaDeleted : ServerPacket
    {
        public GuildCriteriaDeleted() : base(ServerOpcodes.GuildCriteriaDeleted) { }

        public override void Write()
        {
            _worldPacket.WritePackedGuid(GuildGUID);
            _worldPacket.WriteInt32(CriteriaID);
        }

        public ObjectGuid GuildGUID;
        public int CriteriaID;
    }

    public class GuildSetFocusedAchievement : ClientPacket
    {
        public GuildSetFocusedAchievement(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            AchievementID = _worldPacket.ReadInt32();
        }

        public int AchievementID;
    }

    public class GuildAchievementDeleted : ServerPacket
    {
        public GuildAchievementDeleted() : base(ServerOpcodes.GuildAchievementDeleted) { }

        public override void Write()
        {
            _worldPacket.WritePackedGuid(GuildGUID);
            _worldPacket.WriteInt32(AchievementID);
            _worldPacket.WriteInt32((WowTime)TimeDeleted);
        }

        public ObjectGuid GuildGUID;
        public int AchievementID;
        public RealmTime TimeDeleted;
    }

    public class GuildAchievementEarned : ServerPacket
    {
        public GuildAchievementEarned() : base(ServerOpcodes.GuildAchievementEarned) { }

        public override void Write()
        {
            _worldPacket.WritePackedGuid(GuildGUID);
            _worldPacket.WriteInt32(AchievementID);
            _worldPacket.WriteInt32((WowTime)TimeEarned);
        }

        public int AchievementID;
        public ObjectGuid GuildGUID;
        public RealmTime TimeEarned;
    }

    public class AllGuildAchievements : ServerPacket
    {
        public AllGuildAchievements() : base(ServerOpcodes.AllGuildAchievements) { }

        public override void Write()
        {
            _worldPacket.WriteInt32(Earned.Count);

            foreach (EarnedAchievement earned in Earned)
                earned.Write(_worldPacket);
        }

        public List<EarnedAchievement> Earned = new();
    }

    class GuildGetAchievementMembers : ClientPacket
    {
        public GuildGetAchievementMembers(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            PlayerGUID = _worldPacket.ReadPackedGuid();
            GuildGUID = _worldPacket.ReadPackedGuid();
            AchievementID = _worldPacket.ReadInt32();
        }

        public ObjectGuid PlayerGUID;
        public ObjectGuid GuildGUID;
        public int AchievementID;
    }

    class GuildAchievementMembers : ServerPacket
    {
        public GuildAchievementMembers() : base(ServerOpcodes.GuildAchievementMembers) { }

        public override void Write()
        {
            _worldPacket.WritePackedGuid(GuildGUID);
            _worldPacket.WriteInt32(AchievementID);
            _worldPacket.WriteInt32(Member.Count);
            foreach (ObjectGuid guid in Member)
                _worldPacket.WritePackedGuid(guid);
        }

        public ObjectGuid GuildGUID;
        public int AchievementID;
        public List<ObjectGuid> Member = new();
    }

    //Structs
    public struct EarnedAchievement
    {
        public void Write(WorldPacket data)
        {
            data.WriteInt32(Id);
            data.WriteInt32((WowTime)Date);
            data.WritePackedGuid(Owner);
            data.WriteUInt32(VirtualRealmAddress);
            data.WriteUInt32(NativeRealmAddress);
        }

        public int Id;
        public RealmTime Date;
        public ObjectGuid Owner;
        public uint VirtualRealmAddress;
        public uint NativeRealmAddress;
    }

    public struct CriteriaProgressPkt
    {
        public void Write(WorldPacket data)
        {
            data.WriteInt32(Id);
            data.WriteInt64(Quantity);
            data.WritePackedGuid(Player);
            data.WriteUInt32(Unused_10_1_5);
            data.WriteUInt32(Flags);
            data.WriteInt32((WowTime)Date);
            data.WriteInt64(TimeFromStart);
            data.WriteInt64(TimeFromCreate);
            data.WriteBit(RafAcceptanceID.HasValue);
            data.FlushBits();

            if (RafAcceptanceID.HasValue)
                data.WriteInt64(RafAcceptanceID.Value);
        }

        public int Id;
        public long Quantity;
        public ObjectGuid Player;
        public uint Unused_10_1_5;
        public uint Flags;
        public RealmTime Date;
        public long TimeFromStart;
        public long TimeFromCreate;
        public long? RafAcceptanceID;
    }

    public struct GuildCriteriaProgress
    {
        public int CriteriaID;
        public long DateCreated;
        public long DateStarted;
        public RealmTime DateUpdated;
        public long Quantity;
        public ObjectGuid PlayerGUID;
        public int Unused_10_1_5;
        public int Flags;
    }

    public class AllAchievements
    {
        public void Write(WorldPacket data)
        {
            data.WriteInt32(Earned.Count);
            data.WriteInt32(Progress.Count);

            foreach (EarnedAchievement earned in Earned)
                earned.Write(data);

            foreach (CriteriaProgressPkt progress in Progress)
                progress.Write(data);
        }

        public List<EarnedAchievement> Earned = new();
        public List<CriteriaProgressPkt> Progress = new();
    }
}
