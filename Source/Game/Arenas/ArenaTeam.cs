﻿// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Framework.Database;
using Game.Entities;
using Game.Groups;
using Game.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using Game.Cache;

namespace Game.Arenas
{
    public class ArenaTeam
    {
        public ArenaTeam()
        {
            stats.Rating = (ushort)WorldConfig.Values[WorldCfg.ArenaStartRating].Int32;
        }

        public bool Create(ObjectGuid captainGuid, byte _type, string arenaTeamName, uint backgroundColor, byte emblemStyle, uint emblemColor, byte borderStyle, uint borderColor)
        {
            // Check if captain exists
            if (Global.CharacterCacheStorage.GetCharacterCacheByGuid(captainGuid) == null)
                return false;

            // Check if arena team name is already taken
            if (Global.ArenaTeamMgr.GetArenaTeamByName(arenaTeamName) != null)
                return false;

            // Generate new arena team id
            teamId = Global.ArenaTeamMgr.GenerateArenaTeamId();

            // Assign member variables
            CaptainGuid = captainGuid;
            type = _type;
            TeamName = arenaTeamName;
            BackgroundColor = backgroundColor;
            EmblemStyle = emblemStyle;
            EmblemColor = emblemColor;
            BorderStyle = borderStyle;
            BorderColor = borderColor;
            long captainLowGuid = captainGuid.GetCounter();

            // Save arena team to db
            PreparedStatement stmt = CharacterDatabase.GetPreparedStatement(CharStatements.INS_ARENA_TEAM);
            stmt.SetInt32(0, teamId);
            stmt.SetString(1, TeamName);
            stmt.SetInt64(2, captainLowGuid);
            stmt.SetUInt8(3, type);
            stmt.SetUInt16(4, stats.Rating);
            stmt.SetUInt32(5, BackgroundColor);
            stmt.SetUInt8(6, EmblemStyle);
            stmt.SetUInt32(7, EmblemColor);
            stmt.SetUInt8(8, BorderStyle);
            stmt.SetUInt32(9, BorderColor);
            DB.Characters.Execute(stmt);

            // Add captain as member
            AddMember(CaptainGuid);

            Log.outDebug(LogFilter.Arena, 
                $"New ArenaTeam created Id: {GetId()}, Name: {GetName()} " +
                $"Type: {GetArenaType()} Captain low GUID: {captainLowGuid}");
            return true;
        }

        public bool AddMember(ObjectGuid playerGuid)
        {
            string playerName;
            Class playerClass;

            // Check if arena team is full (Can't have more than Type * 2 players)
            if (GetMembersSize() >= GetArenaType() * 2)
                return false;

            // Get player name and class either from db or character cache
            CharacterCacheEntry characterInfo;
            Player player = Global.ObjAccessor.FindPlayer(playerGuid);
            if (player != null)
            {
                playerClass = player.GetClass();
                playerName = player.GetName();
            }
            else if ((characterInfo = Global.CharacterCacheStorage.GetCharacterCacheByGuid(playerGuid)) != null)
            {
                playerName = characterInfo.Name;
                playerClass = characterInfo.ClassId;
            }
            else
                return false;

            // Check if player is already in a similar arena team
            if ((player != null && player.GetArenaTeamId(GetSlot()) != 0) || Global.CharacterCacheStorage.GetCharacterArenaTeamIdByGuid(playerGuid, GetArenaType()) != 0)
            {
                Log.outDebug(LogFilter.Arena, 
                    $"Arena: {playerGuid} {playerName} " +
                    $"already has an arena team of Type {GetArenaType()}");
                return false;
            }

            // Set player's personal rating
            int personalRating = 0;

            if (WorldConfig.Values[WorldCfg.ArenaStartPersonalRating].Int32 > 0)
                personalRating = WorldConfig.Values[WorldCfg.ArenaStartPersonalRating].Int32;
            else if (GetRating() >= 1000)
                personalRating = 1000;

            // Try to get player's match maker rating from db and fall back to config setting if not found
            PreparedStatement stmt = CharacterDatabase.GetPreparedStatement(CharStatements.SEL_MATCH_MAKER_RATING);
            stmt.SetInt64(0, playerGuid.GetCounter());
            stmt.SetUInt8(1, GetSlot());
            SQLResult result = DB.Characters.Query(stmt);

            int matchMakerRating;
            if (!result.IsEmpty())
                matchMakerRating = result.Read<ushort>(0);
            else
                matchMakerRating = WorldConfig.Values[WorldCfg.ArenaStartMatchmakerRating].Int32;

            // Remove all player signatures from other petitions
            // This will prevent player from joining too many arena teams and corrupt arena team data integrity
            //Player.RemovePetitionsAndSigns(playerGuid, GetArenaType());

            // Feed data to the struct
            ArenaTeamMember newMember = new();
            newMember.Name = playerName;
            newMember.Guid = playerGuid;
            newMember.Class = (byte)playerClass;
            newMember.SeasonGames = 0;
            newMember.WeekGames = 0;
            newMember.SeasonWins = 0;
            newMember.WeekWins = 0;
            newMember.PersonalRating = (ushort)personalRating;
            newMember.MatchMakerRating = (ushort)matchMakerRating;

            Members.Add(newMember);
            Global.CharacterCacheStorage.UpdateCharacterArenaTeamId(playerGuid, GetSlot(), GetId());

            // Save player's arena team membership to db
            stmt = CharacterDatabase.GetPreparedStatement(CharStatements.INS_ARENA_TEAM_MEMBER);
            stmt.SetInt32(0, teamId);
            stmt.SetInt64(1, playerGuid.GetCounter());
            stmt.SetUInt16(2, (ushort)personalRating);
            DB.Characters.Execute(stmt);

            // Inform player if online
            if (player != null)
            {
                player.SetInArenaTeam(teamId, GetSlot(), GetArenaType());
                player.SetArenaTeamIdInvited(0);

                // Hide promote/remove buttons
                if (CaptainGuid != playerGuid)
                    player.SetArenaTeamInfoField(GetSlot(), ArenaTeamInfoType.Member, 1);
            }

            Log.outDebug(LogFilter.Arena, 
                $"Player: {playerName} [{playerGuid}] joined arena team " +
                $"Type: {GetArenaType()} [Id: {GetId()}, Name: {GetName()}].");

            return true;
        }

        public bool LoadArenaTeamFromDB(SQLResult result)
        {
            if (result.IsEmpty())
                return false;

            teamId = result.Read<int>(0);
            TeamName = result.Read<string>(1);
            CaptainGuid = ObjectGuid.Create(HighGuid.Player, result.Read<long>(2));
            type = result.Read<byte>(3);
            BackgroundColor = result.Read<uint>(4);
            EmblemStyle = result.Read<byte>(5);
            EmblemColor = result.Read<uint>(6);
            BorderStyle = result.Read<byte>(7);
            BorderColor = result.Read<uint>(8);
            stats.Rating = result.Read<ushort>(9);
            stats.WeekGames = result.Read<ushort>(10);
            stats.WeekWins = result.Read<ushort>(11);
            stats.SeasonGames = result.Read<ushort>(12);
            stats.SeasonWins = result.Read<ushort>(13);
            stats.Rank = result.Read<uint>(14);

            return true;
        }

        public bool LoadMembersFromDB(SQLResult result)
        {
            if (result.IsEmpty())
                return false;

            bool captainPresentInTeam = false;

            do
            {
                uint arenaTeamId = result.Read<uint>(0);

                // We loaded all members for this arena_team already, break cycle
                if (arenaTeamId > teamId)
                    break;

                ArenaTeamMember newMember = new();
                newMember.Guid = ObjectGuid.Create(HighGuid.Player, result.Read<long>(1));
                newMember.WeekGames = result.Read<ushort>(2);
                newMember.WeekWins = result.Read<ushort>(3);
                newMember.SeasonGames = result.Read<ushort>(4);
                newMember.SeasonWins = result.Read<ushort>(5);
                newMember.Name = result.Read<string>(6);
                newMember.Class = result.Read<byte>(7);
                newMember.PersonalRating = result.Read<ushort>(8);
                newMember.MatchMakerRating = (ushort)(result.Read<ushort>(9) > 0 ? result.Read<ushort>(9) : 1500);

                // Delete member if character information is missing
                if (string.IsNullOrEmpty(newMember.Name))
                {
                    Log.outError(LogFilter.Sql,
                        $"ArenaTeam {arenaTeamId} has member with empty name - " +
                        $"probably {newMember.Guid} doesn't exist, deleting him from memberlist!");
                    DelMember(newMember.Guid, true);
                    continue;
                }

                // Check if team team has a valid captain
                if (newMember.Guid == GetCaptain())
                    captainPresentInTeam = true;

                // Put the player in the team
                Members.Add(newMember);
                Global.CharacterCacheStorage.UpdateCharacterArenaTeamId(newMember.Guid, GetSlot(), GetId());
            }
            while (result.NextRow());

            if (Empty() || !captainPresentInTeam)
            {
                // Arena team is empty or captain is not in team, delete from db
                Log.outDebug(LogFilter.Arena, 
                    $"ArenaTeam {teamId} does not have any members " +
                    $"or its captain is not in team, disbanding it...");
                return false;
            }

            return true;
        }

        public bool SetName(string name)
        {
            if (TeamName == name || string.IsNullOrEmpty(name) || name.Length > 24 || Global.ObjectMgr.IsReservedName(name) || !ObjectManager.IsValidCharterName(name))
                return false;

            TeamName = name;
            PreparedStatement stmt = CharacterDatabase.GetPreparedStatement(CharStatements.UPD_ARENA_TEAM_NAME);
            stmt.SetString(0, TeamName);
            stmt.SetInt32(1, GetId());
            DB.Characters.Execute(stmt);
            return true;
        }

        public void SetCaptain(ObjectGuid guid)
        {
            // Disable remove/promote buttons
            Player oldCaptain = Global.ObjAccessor.FindPlayer(GetCaptain());
            if (oldCaptain != null)
                oldCaptain.SetArenaTeamInfoField(GetSlot(), ArenaTeamInfoType.Member, 1);

            // Set new captain
            CaptainGuid = guid;

            // Update database
            PreparedStatement stmt = CharacterDatabase.GetPreparedStatement(CharStatements.UPD_ARENA_TEAM_CAPTAIN);
            stmt.SetInt64(0, guid.GetCounter());
            stmt.SetInt32(1, GetId());
            DB.Characters.Execute(stmt);

            // Enable remove/promote buttons
            Player newCaptain = Global.ObjAccessor.FindPlayer(guid);
            if (newCaptain != null)
            {
                newCaptain.SetArenaTeamInfoField(GetSlot(), ArenaTeamInfoType.Member, 0);
                if (oldCaptain != null)
                {
                    Log.outDebug(LogFilter.Arena, 
                        $"Player: {oldCaptain.GetName()} [GUID: {oldCaptain.GetGUID()}] promoted " +
                        $"player: {newCaptain.GetName()} [GUID: {newCaptain.GetGUID()}] to leader of " +
                        $"arena team [Id: {GetId()}, Name: {GetName()}] [Type: {GetArenaType()}].");
                }
            }
        }

        public void DelMember(ObjectGuid guid, bool cleanDb)
        {
            // Remove member from team
            foreach (var member in Members)
            {
                if (member.Guid == guid)
                {
                    Members.Remove(member);
                    Global.CharacterCacheStorage.UpdateCharacterArenaTeamId(guid, GetSlot(), 0);
                    break;
                }
            }

            // Remove arena team info from player data
            Player player = Global.ObjAccessor.FindPlayer(guid);
            if (player != null)
            {
                // delete all info regarding this team
                for (uint i = 0; i < (int)ArenaTeamInfoType.End; ++i)
                    player.SetArenaTeamInfoField(GetSlot(), (ArenaTeamInfoType)i, 0);
                Log.outDebug(LogFilter.Arena, 
                    $"Player: {player.GetName()} [GUID: {player.GetGUID()}] left arena team " +
                    $"Type: {GetArenaType()} [Id: {GetId()}, Name: {GetName()}].");
            }

            // Only used for single member deletion, for arena team disband we use a single query for more efficiency
            if (cleanDb)
            {
                PreparedStatement stmt = CharacterDatabase.GetPreparedStatement(CharStatements.DEL_ARENA_TEAM_MEMBER);
                stmt.SetInt32(0, GetId());
                stmt.SetInt64(1, guid.GetCounter());
                DB.Characters.Execute(stmt);
            }
        }

        public void Disband(WorldSession session)
        {
            // Broadcast update
            if (session != null)
            {
                Player player = session.GetPlayer();
                if (player != null)
                    Log.outDebug(LogFilter.Arena,
                        $"Player: {player.GetName()} [GUID: {player.GetGUID()}] disbanded arena team " +
                        $"Type: {GetArenaType()} [Id: {GetId()}, Name: {GetName()}].");
            }

            // Remove all members from arena team
            while (!Members.Empty())
                DelMember(Members.FirstOrDefault().Guid, false);

            // Update database
            SQLTransaction trans = new();

            PreparedStatement stmt = CharacterDatabase.GetPreparedStatement(CharStatements.DEL_ARENA_TEAM);
            stmt.SetInt32(0, teamId);
            trans.Append(stmt);

            stmt = CharacterDatabase.GetPreparedStatement(CharStatements.DEL_ARENA_TEAM_MEMBERS);
            stmt.SetInt32(0, teamId);
            trans.Append(stmt);

            DB.Characters.CommitTransaction(trans);

            // Remove arena team from ArenaTeamMgr
            Global.ArenaTeamMgr.RemoveArenaTeam(teamId);
        }

        public void Disband()
        {
            // Remove all members from arena team
            while (!Members.Empty())
                DelMember(Members.First().Guid, false);

            // Update database
            SQLTransaction trans = new();

            PreparedStatement stmt = CharacterDatabase.GetPreparedStatement(CharStatements.DEL_ARENA_TEAM);
            stmt.SetInt32(0, teamId);
            trans.Append(stmt);

            stmt = CharacterDatabase.GetPreparedStatement(CharStatements.DEL_ARENA_TEAM_MEMBERS);
            stmt.SetInt32(0, teamId);
            trans.Append(stmt);

            DB.Characters.CommitTransaction(trans);

            // Remove arena team from ArenaTeamMgr
            Global.ArenaTeamMgr.RemoveArenaTeam(teamId);
        }

        public void SendStats(WorldSession session)
        {
            /*WorldPacket data = new WorldPacket(ServerOpcodes.ArenaTeamStats);
            data.WriteUInt32(GetId());                                // team id
            data.WriteUInt32(stats.Rating);                           // rating
            data.WriteUInt32(stats.WeekGames);                        // games this week
            data.WriteUInt32(stats.WeekWins);                         // wins this week
            data.WriteUInt32(stats.SeasonGames);                      // played this season
            data.WriteUInt32(stats.SeasonWins);                       // wins this season
            data.WriteUInt32(stats.Rank);                             // rank
            session.SendPacket(data);*/
        }

        public void NotifyStatsChanged()
        {
            // This is called after a rated match ended
            // Updates arena team stats for every member of the team (not only the ones who participated!)
            foreach (var member in Members)
            {
                Player player = Global.ObjAccessor.FindPlayer(member.Guid);
                if (player != null)
                    SendStats(player.GetSession());
            }
        }

        void BroadcastPacket(ServerPacket packet)
        {
            foreach (var member in Members)
            {
                Player player = Global.ObjAccessor.FindPlayer(member.Guid);
                if (player != null)
                    player.SendPacket(packet);
            }
        }

        public static byte GetSlotByType(uint type)
        {
            switch ((ArenaTypes)type)
            {
                case ArenaTypes.Team2v2: return 0;
                case ArenaTypes.Team3v3: return 1;
                case ArenaTypes.Team5v5: return 2;
                default:
                    break;
            }
            Log.outError(LogFilter.Arena, 
                $"FATAL: Unknown arena team Type {type} for some arena team");
            return 0xFF;
        }

        public static byte GetTypeBySlot(byte slot)
        {
            switch (slot)
            {
                case 0: return (byte)ArenaTypes.Team2v2;
                case 1: return (byte)ArenaTypes.Team3v3;
                case 2: return (byte)ArenaTypes.Team5v5;
                default:
                    break;
            }
            Log.outError(LogFilter.Arena, 
                $"FATAL: Unknown arena team slot {slot} for some arena team");
            return 0xFF;
        }

        public bool IsMember(ObjectGuid guid)
        {
            foreach (var member in Members)
                if (member.Guid == guid)
                    return true;

            return false;
        }

        public int GetAverageMMR(Group group)
        {
            if (group == null)
                return 0;

            int matchMakerRating = 0;
            int playerDivider = 0;
            foreach (var member in Members)
            {
                // Skip if player is not online
                if (Global.ObjAccessor.FindPlayer(member.Guid) == null)
                    continue;

                // Skip if player is not a member of group
                if (!group.IsMember(member.Guid))
                    continue;

                matchMakerRating += member.MatchMakerRating;
                ++playerDivider;
            }

            // x/0 = crash
            if (playerDivider == 0)
                playerDivider = 1;

            matchMakerRating /= playerDivider;

            return matchMakerRating;
        }

        float GetChanceAgainst(int ownRating, int opponentRating)
        {
            // Returns the Chance to win against a team with the given rating, used in the rating adjustment calculation
            // ELO system
            return (float)(1.0f / (1.0f + Math.Exp(Math.Log(10.0f) * ((float)opponentRating - ownRating) / 650.0f)));
        }

        int GetMatchmakerRatingMod(int ownRating, int opponentRating, bool won)
        {
            // 'Chance' calculation - to beat the opponent
            // This is a simulation. Not much info on how it really works
            float chance = GetChanceAgainst(ownRating, opponentRating);
            float won_mod = (won) ? 1.0f : 0.0f;
            float mod = won_mod - chance;

            // Work in progress:
            /*
            // This is a simulation, as there is not much info on how it really works
            float confidence_mod = min(1.0f - fabs(mod), 0.5f);

            // Apply confidence factor to the mod:
            mod *= confidence_factor

            // And only after that update the new confidence factor
            confidence_factor -= ((confidence_factor - 1.0f) * confidence_mod) / confidence_factor;
            */

            // Real rating modification
            mod *= WorldConfig.Values[WorldCfg.ArenaMatchmakerRatingModifier].Float;

            return (int)Math.Ceiling(mod);
        }

        int GetRatingMod(int ownRating, int opponentRating, bool won)
        {
            // 'Chance' calculation - to beat the opponent
            // This is a simulation. Not much info on how it really works
            float chance = GetChanceAgainst(ownRating, opponentRating);

            // Calculate the rating modification
            float mod;

            // todo Replace this hack with using the confidence factor (limiting the factor to 2.0f)
            if (won)
            {
                if (ownRating < 1300)
                {
                    float win_rating_modifier1 = WorldConfig.Values[WorldCfg.ArenaWinRatingModifier1].Float;

                    if (ownRating < 1000)
                        mod = win_rating_modifier1 * (1.0f - chance);
                    else
                        mod = ((win_rating_modifier1 / 2.0f) + ((win_rating_modifier1 / 2.0f) * (1300.0f - ownRating) / 300.0f)) * (1.0f - chance);
                }
                else
                    mod = WorldConfig.Values[WorldCfg.ArenaWinRatingModifier2].Float * (1.0f - chance);
            }
            else
                mod = WorldConfig.Values[WorldCfg.ArenaLoseRatingModifier].Float * (-chance);

            return (int)Math.Ceiling(mod);
        }

        public void FinishGame(int mod)
        {
            // Rating can only drop to 0
            if (stats.Rating + mod < 0)
                stats.Rating = 0;
            else
            {
                stats.Rating += (ushort)mod;

                // Check if rating related achivements are met
                foreach (var member in Members)
                {
                    Player player = Global.ObjAccessor.FindPlayer(member.Guid);
                    if (player != null)
                        player.UpdateCriteria(CriteriaType.EarnTeamArenaRating, stats.Rating, type);
                }
            }

            // Update number of games played per season or week
            stats.WeekGames += 1;
            stats.SeasonGames += 1;

            // Update team's rank, start with rank 1 and increase until no team with more rating was found
            stats.Rank = 1;
            foreach (var (_, team) in Global.ArenaTeamMgr.GetArenaTeamMap())
                if (team.GetArenaType() == type && team.GetStats().Rating > stats.Rating)
                    ++stats.Rank;
        }

        public int WonAgainst(int ownMMRating, int opponentMMRating, ref int ratingChange)
        {
            // Called when the team has won
            // Change in Matchmaker rating
            int mod = GetMatchmakerRatingMod(ownMMRating, opponentMMRating, true);

            // Change in Team Rating
            ratingChange = GetRatingMod(stats.Rating, opponentMMRating, true);

            // Modify the team stats accordingly
            FinishGame(ratingChange);

            // Update number of wins per season and week
            stats.WeekWins += 1;
            stats.SeasonWins += 1;

            // Return the rating change, used to display it on the results screen
            return mod;
        }

        public int LostAgainst(int ownMMRating, int opponentMMRating, ref int ratingChange)
        {
            // Called when the team has lost
            // Change in Matchmaker Rating
            int mod = GetMatchmakerRatingMod(ownMMRating, opponentMMRating, false);

            // Change in Team Rating
            ratingChange = GetRatingMod(stats.Rating, opponentMMRating, false);

            // Modify the team stats accordingly
            FinishGame(ratingChange);

            // return the rating change, used to display it on the results screen
            return mod;
        }

        public void MemberLost(Player player, int againstMatchmakerRating, int matchmakerRatingChange = -12)
        {
            // Called for each participant of a match after losing
            foreach (var member in Members)
            {
                if (member.Guid == player.GetGUID())
                {
                    // Update personal rating
                    int mod = GetRatingMod(member.PersonalRating, againstMatchmakerRating, false);
                    member.ModifyPersonalRating(player, mod, GetArenaType());

                    // Update matchmaker rating
                    member.ModifyMatchmakerRating(matchmakerRatingChange, GetSlot());

                    // Update personal played stats
                    member.WeekGames += 1;
                    member.SeasonGames += 1;

                    // update the unit fields
                    player.SetArenaTeamInfoField(GetSlot(), ArenaTeamInfoType.GamesWeek, member.WeekGames);
                    player.SetArenaTeamInfoField(GetSlot(), ArenaTeamInfoType.GamesSeason, member.SeasonGames);
                    return;
                }
            }
        }

        public void OfflineMemberLost(ObjectGuid guid, int againstMatchmakerRating, int matchmakerRatingChange = -12)
        {
            // Called for offline player after ending rated arena match!
            foreach (var member in Members)
            {
                if (member.Guid == guid)
                {
                    // update personal rating
                    int mod = GetRatingMod(member.PersonalRating, againstMatchmakerRating, false);
                    member.ModifyPersonalRating(null, mod, GetArenaType());

                    // update matchmaker rating
                    member.ModifyMatchmakerRating(matchmakerRatingChange, GetSlot());

                    // update personal played stats
                    member.WeekGames += 1;
                    member.SeasonGames += 1;
                    return;
                }
            }
        }

        public void MemberWon(Player player, int againstMatchmakerRating, int matchmakerRatingChange)
        {
            // called for each participant after winning a match
            foreach (var member in Members)
            {
                if (member.Guid == player.GetGUID())
                {
                    // update personal rating
                    int mod = GetRatingMod(member.PersonalRating, againstMatchmakerRating, true);
                    member.ModifyPersonalRating(player, mod, GetArenaType());

                    // update matchmaker rating
                    member.ModifyMatchmakerRating(matchmakerRatingChange, GetSlot());

                    // update personal stats
                    member.WeekGames += 1;
                    member.SeasonGames += 1;
                    member.SeasonWins += 1;
                    member.WeekWins += 1;
                    // update unit fields
                    player.SetArenaTeamInfoField(GetSlot(), ArenaTeamInfoType.GamesWeek, member.WeekGames);
                    player.SetArenaTeamInfoField(GetSlot(), ArenaTeamInfoType.GamesSeason, member.SeasonGames);
                    return;
                }
            }
        }

        public void SaveToDB(bool forceMemberSave = false)
        {
            // Save team and member stats to db
            // Called after a match has ended or when calculating arena_points

            SQLTransaction trans = new();

            PreparedStatement stmt = CharacterDatabase.GetPreparedStatement(CharStatements.UPD_ARENA_TEAM_STATS);
            stmt.SetUInt16(0, stats.Rating);
            stmt.SetUInt16(1, stats.WeekGames);
            stmt.SetUInt16(2, stats.WeekWins);
            stmt.SetUInt16(3, stats.SeasonGames);
            stmt.SetUInt16(4, stats.SeasonWins);
            stmt.SetUInt32(5, stats.Rank);
            stmt.SetInt32(6, GetId());
            trans.Append(stmt);

            foreach (var member in Members)
            {
                // Save the effort and go
                if (member.WeekGames == 0 && !forceMemberSave)
                    continue;

                stmt = CharacterDatabase.GetPreparedStatement(CharStatements.UPD_ARENA_TEAM_MEMBER);
                stmt.SetUInt16(0, member.PersonalRating);
                stmt.SetUInt16(1, member.WeekGames);
                stmt.SetUInt16(2, member.WeekWins);
                stmt.SetUInt16(3, member.SeasonGames);
                stmt.SetUInt16(4, member.SeasonWins);
                stmt.SetInt32(5, GetId());
                stmt.SetInt64(6, member.Guid.GetCounter());
                trans.Append(stmt);

                stmt = CharacterDatabase.GetPreparedStatement(CharStatements.REP_CHARACTER_ARENA_STATS);
                stmt.SetInt64(0, member.Guid.GetCounter());
                stmt.SetUInt8(1, GetSlot());
                stmt.SetUInt16(2, member.MatchMakerRating);
                trans.Append(stmt);
            }

            DB.Characters.CommitTransaction(trans);
        }

        public bool FinishWeek()
        {
            // No need to go further than this
            if (stats.WeekGames == 0)
                return false;

            // Reset team stats
            stats.WeekGames = 0;
            stats.WeekWins = 0;

            // Reset member stats
            foreach (var member in Members)
            {
                member.WeekGames = 0;
                member.WeekWins = 0;
            }

            return true;
        }

        public bool IsFighting()
        {
            foreach (var member in Members)
            {
                Player player = Global.ObjAccessor.FindPlayer(member.Guid);
                if (player != null)
                    if (player.GetMap().IsBattleArena())
                        return true;
            }

            return false;
        }

        public ArenaTeamMember GetMember(string name)
        {
            foreach (var member in Members)
                if (member.Name == name)
                    return member;

            return null;
        }

        public ArenaTeamMember GetMember(ObjectGuid guid)
        {
            foreach (var member in Members)
                if (member.Guid == guid)
                    return member;

            return null;
        }

        public int GetId() { return teamId; }
        public byte GetArenaType() { return type; }
        public byte GetSlot() { return GetSlotByType(GetArenaType()); }

        public ObjectGuid GetCaptain() { return CaptainGuid; }
        public string GetName() { return TeamName; }
        public ArenaTeamStats GetStats() { return stats; }
        public int GetRating() { return stats.Rating; }

        public int GetMembersSize() { return Members.Count; }
        bool Empty() { return Members.Empty(); }
        public List<ArenaTeamMember> GetMembers()
        {
            return Members;
        }

        int teamId;
        byte type;
        string TeamName;
        ObjectGuid CaptainGuid;

        uint BackgroundColor; // ARGB format
        byte EmblemStyle;     // icon id
        uint EmblemColor;     // ARGB format
        byte BorderStyle;     // border image id
        uint BorderColor;     // ARGB format

        List<ArenaTeamMember> Members = new();
        ArenaTeamStats stats;
    }

    public class ArenaTeamMember
    {
        public ObjectGuid Guid;
        public string Name;
        public byte Class;
        public ushort WeekGames;
        public ushort WeekWins;
        public ushort SeasonGames;
        public ushort SeasonWins;
        public ushort PersonalRating;
        public ushort MatchMakerRating;

        public void ModifyPersonalRating(Player player, int mod, uint type)
        {
            if (PersonalRating + mod < 0)
                PersonalRating = 0;
            else
                PersonalRating += (ushort)mod;

            if (player != null)
            {
                player.SetArenaTeamInfoField(ArenaTeam.GetSlotByType(type), ArenaTeamInfoType.PersonalRating, PersonalRating);
                player.UpdateCriteria(CriteriaType.EarnPersonalArenaRating, PersonalRating, type);
            }
        }

        public void ModifyMatchmakerRating(int mod, uint slot)
        {
            if (MatchMakerRating + mod < 0)
                MatchMakerRating = 0;
            else
                MatchMakerRating += (ushort)mod;
        }
    }

    public struct ArenaTeamStats
    {
        public ushort Rating;
        public ushort WeekGames;
        public ushort WeekWins;
        public ushort SeasonGames;
        public ushort SeasonWins;
        public uint Rank;
    }
}
