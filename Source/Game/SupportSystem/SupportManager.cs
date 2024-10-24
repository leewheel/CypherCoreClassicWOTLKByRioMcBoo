﻿// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Framework.Database;
using Game.Chat;
using Game.Entities;
using System.Collections.Generic;
using System.Linq;

namespace Game.SupportSystem
{
    public class SupportManager : Singleton<SupportManager>
    {
        SupportManager() { }

        public void Initialize()
        {
            SetSupportSystemStatus(WorldConfig.Values[WorldCfg.SupportEnabled].Bool);
            SetTicketSystemStatus(WorldConfig.Values[WorldCfg.SupportTicketsEnabled].Bool);
            SetBugSystemStatus(WorldConfig.Values[WorldCfg.SupportBugsEnabled].Bool);
            SetComplaintSystemStatus(WorldConfig.Values[WorldCfg.SupportComplaintsEnabled].Bool);
            SetSuggestionSystemStatus(WorldConfig.Values[WorldCfg.SupportSuggestionsEnabled].Bool);
        }

        public T GetTicket<T>(int Id) where T : Ticket
        {
            switch (typeof(T).Name)
            {
                case "BugTicket":
                    return _bugTicketList.LookupByKey(Id) as T;
                case "ComplaintTicket":
                    return _complaintTicketList.LookupByKey(Id) as T;
                case "SuggestionTicket":
                    return _suggestionTicketList.LookupByKey(Id) as T;
            }

            return default;
        }

        public int GetOpenTicketCount<T>() where T : Ticket
        {
            switch (typeof(T).Name)
            {
                case "BugTicket":
                    return _openBugTicketCount;
                case "ComplaintTicket":
                    return _openComplaintTicketCount;
                case "SuggestionTicket":
                    return _openSuggestionTicketCount;
            }
            return 0;
        }

        public void LoadBugTickets()
        {
            RelativeTime oldMSTime = Time.NowRelative;
            _bugTicketList.Clear();

            _lastBugId = 0;
            _openBugTicketCount = 0;

            PreparedStatement stmt = CharacterDatabase.GetPreparedStatement(CharStatements.SEL_GM_BUGS);
            SQLResult result = DB.Characters.Query(stmt);
            if (result.IsEmpty())
            {
                Log.outInfo(LogFilter.ServerLoading, "Loaded 0 GM bugs. DB table `gm_bug` is empty!");
                return;
            }

            uint count = 0;
            do
            {
                BugTicket bug = new();
                bug.LoadFromDB(result.GetFields());

                if (!bug.IsClosed())
                    ++_openBugTicketCount;

                int id = bug.GetId();
                if (_lastBugId < id)
                    _lastBugId = id;

                _bugTicketList[id] = bug;
                ++count;
            } while (result.NextRow());

            Log.outInfo(LogFilter.ServerLoading, $"Loaded {count} GM bugs in {Time.Diff(oldMSTime)} ms.");
        }

        public void LoadComplaintTickets()
        {
            RelativeTime oldMSTime = Time.NowRelative;
            _complaintTicketList.Clear();

            _lastComplaintId = 0;
            _openComplaintTicketCount = 0;

            PreparedStatement stmt = CharacterDatabase.GetPreparedStatement(CharStatements.SEL_GM_COMPLAINTS);
            SQLResult result = DB.Characters.Query(stmt);
            if (result.IsEmpty())
            {
                Log.outInfo(LogFilter.ServerLoading, "Loaded 0 GM complaints. DB table `gm_complaint` is empty!");
                return;
            }

            uint count = 0;         
            do
            {
                ComplaintTicket complaint = new();
                complaint.LoadFromDB(result.GetFields());

                if (!complaint.IsClosed())
                    ++_openComplaintTicketCount;

                int id = complaint.GetId();
                if (_lastComplaintId < id)
                    _lastComplaintId = id;

                PreparedStatement chatLogStmt = CharacterDatabase.GetPreparedStatement(CharStatements.SEL_GM_COMPLAINT_CHATLINES);
                chatLogStmt.SetInt32(0, id);
                SQLResult chatLogResult = DB.Characters.Query(stmt);

                if (!chatLogResult.IsEmpty())
                {
                    do
                    {
                        complaint.LoadChatLineFromDB(chatLogResult.GetFields());
                    } while (chatLogResult.NextRow());
                }

                _complaintTicketList[id] = complaint;
                ++count;
            } while (result.NextRow());

            Log.outInfo(LogFilter.ServerLoading, $"Loaded {count} GM complaints in {Time.Diff(oldMSTime)} ms.");
        }

        public void LoadSuggestionTickets()
        {
            RelativeTime oldMSTime = Time.NowRelative;
            _suggestionTicketList.Clear();

            _lastSuggestionId = 0;
            _openSuggestionTicketCount = 0;

            PreparedStatement stmt = CharacterDatabase.GetPreparedStatement(CharStatements.SEL_GM_SUGGESTIONS);
            SQLResult result = DB.Characters.Query(stmt);
            if (result.IsEmpty())
            {
                Log.outInfo(LogFilter.ServerLoading, "Loaded 0 GM suggestions. DB table `gm_suggestion` is empty!");
                return;
            }

            uint count = 0;
            do
            {
                SuggestionTicket suggestion = new();
                suggestion.LoadFromDB(result.GetFields());

                if (!suggestion.IsClosed())
                    ++_openSuggestionTicketCount;

                int id = suggestion.GetId();
                if (_lastSuggestionId < id)
                    _lastSuggestionId = id;

                _suggestionTicketList[id] = suggestion;
                ++count;
            } while (result.NextRow());

            Log.outInfo(LogFilter.ServerLoading, $"Loaded {count} GM suggestions in {Time.Diff(oldMSTime)} ms.");
        }

        public void AddTicket<T>(T ticket)where T : Ticket
        {
            switch (typeof(T).Name)
            {
                case "BugTicket":
                    _bugTicketList[ticket.GetId()] = ticket as BugTicket;
                    if (!ticket.IsClosed())
                        ++_openBugTicketCount;
                    break;
                case "ComplaintTicket":
                    _complaintTicketList[ticket.GetId()] = ticket as ComplaintTicket;
                    if (!ticket.IsClosed())
                        ++_openComplaintTicketCount;
                    break;
                case "SuggestionTicket":
                    _suggestionTicketList[ticket.GetId()] = ticket as SuggestionTicket;
                    if (!ticket.IsClosed())
                        ++_openSuggestionTicketCount;
                    break;
            }

            ticket.SaveToDB();
        }

        public void RemoveTicket<T>(int ticketId) where T : Ticket
        {
            T ticket = GetTicket<T>(ticketId);
            if (ticket != null)
            {
                ticket.DeleteFromDB();

                switch (typeof(T).Name)
                {
                    case "BugTicket":
                        _bugTicketList.Remove(ticketId);
                        break;
                    case "ComplaintTicket":
                        _complaintTicketList.Remove(ticketId);
                        break;
                    case "SuggestionTicket":
                        _suggestionTicketList.Remove(ticketId);
                        break;
                }
            }
        }

        public void CloseTicket<T>(int ticketId, ObjectGuid closedBy) where T : Ticket
        {
            T ticket = GetTicket<T>(ticketId);
            if (ticket != null)
            {
                ticket.SetClosedBy(closedBy);
                if (!closedBy.IsEmpty())
                {
                    switch (typeof(T).Name)
                    {
                        case "BugTicket":
                            --_openBugTicketCount;
                            break;
                        case "ComplaintTicket":
                            --_openComplaintTicketCount;
                            break;
                        case "SuggestionTicket":
                            --_openSuggestionTicketCount;
                            break;
                    }
                }
                ticket.SaveToDB();
            }
        }

        public void ResetTickets<T>() where T : Ticket
        {
            PreparedStatement stmt;
            switch (typeof(T).Name)
            {
                case "BugTicket":
                    _bugTicketList.Clear();

                    _lastBugId = 0;

                    stmt = CharacterDatabase.GetPreparedStatement(CharStatements.DEL_ALL_GM_BUGS);
                    DB.Characters.Execute(stmt);
                    break;
                case "ComplaintTicket":
                    _complaintTicketList.Clear();

                    _lastComplaintId = 0;

                    SQLTransaction trans = new();
                    trans.Append(CharacterDatabase.GetPreparedStatement(CharStatements.DEL_ALL_GM_COMPLAINTS));
                    trans.Append(CharacterDatabase.GetPreparedStatement(CharStatements.DEL_ALL_GM_COMPLAINT_CHATLOGS));
                    DB.Characters.CommitTransaction(trans);
                    break;
                case "SuggestionTicket":
                    _suggestionTicketList.Clear();

                    _lastSuggestionId = 0;

                    stmt = CharacterDatabase.GetPreparedStatement(CharStatements.DEL_ALL_GM_SUGGESTIONS);
                    DB.Characters.Execute(stmt);
                    break;
            }


        }

        public void ShowList<T>(CommandHandler handler) where T : Ticket
        {
            handler.SendSysMessage(CypherStrings.CommandTicketshowlist);
            switch (typeof(T).Name)
            {
                case "BugTicket":
                    foreach (var ticket in _bugTicketList.Values)
                    {
                        if (!ticket.IsClosed())
                            handler.SendSysMessage(ticket.FormatViewMessageString(handler));
                    }
                    break;
                case "ComplaintTicket":
                    foreach (var ticket in _complaintTicketList.Values)
                    {
                        if (!ticket.IsClosed())
                            handler.SendSysMessage(ticket.FormatViewMessageString(handler));
                    }
                    break;
                case "SuggestionTicket":
                    foreach (var ticket in _suggestionTicketList.Values)
                    {
                        if (!ticket.IsClosed())
                            handler.SendSysMessage(ticket.FormatViewMessageString(handler));
                    }
                    break;
            }
        }

        public void ShowClosedList<T>(CommandHandler handler) where T : Ticket
        {
            handler.SendSysMessage(CypherStrings.CommandTicketshowclosedlist);
            switch (typeof(T).Name)
            {
                case "BugTicket":
                    foreach (var ticket in _bugTicketList.Values)
                    {
                        if (ticket.IsClosed())
                            handler.SendSysMessage(ticket.FormatViewMessageString(handler));
                    }
                    break;
                case "ComplaintTicket":
                    foreach (var ticket in _complaintTicketList.Values)
                    {
                        if (ticket.IsClosed())
                            handler.SendSysMessage(ticket.FormatViewMessageString(handler));
                    }
                    break;
                case "SuggestionTicket":
                    foreach (var ticket in _suggestionTicketList.Values)
                    {
                        if (ticket.IsClosed())
                            handler.SendSysMessage(ticket.FormatViewMessageString(handler));
                    }
                    break;
            }
        }

        long GetAge(UnixTime64 t) { return (LoopTime.UnixServerTime - t).Ticks / Time.Day.Ticks; }

        IEnumerable<KeyValuePair<int, ComplaintTicket>> GetComplaintsByPlayerGuid(ObjectGuid playerGuid)
        {
            return _complaintTicketList.Where(ticket => ticket.Value.GetPlayerGuid() == playerGuid);
        }

        public bool GetSupportSystemStatus() { return _supportSystemStatus; }
        public bool GetTicketSystemStatus() { return _supportSystemStatus && _ticketSystemStatus; }
        public bool GetBugSystemStatus() { return _supportSystemStatus && _bugSystemStatus; }
        public bool GetComplaintSystemStatus() { return _supportSystemStatus && _complaintSystemStatus; }
        public bool GetSuggestionSystemStatus() { return _supportSystemStatus && _suggestionSystemStatus; }
        public long GetLastChange() { return _lastChange; }

        public void SetSupportSystemStatus(bool status) { _supportSystemStatus = status; }
        public void SetTicketSystemStatus(bool status) { _ticketSystemStatus = status; }
        public void SetBugSystemStatus(bool status) { _bugSystemStatus = status; }
        public void SetComplaintSystemStatus(bool status) { _complaintSystemStatus = status; }
        public void SetSuggestionSystemStatus(bool status) { _suggestionSystemStatus = status; }

        public void UpdateLastChange() { _lastChange = LoopTime.UnixServerTime; }

        public int GenerateBugId() { return ++_lastBugId; }
        public int GenerateComplaintId() { return ++_lastComplaintId; }
        public int GenerateSuggestionId() { return ++_lastSuggestionId; }

        bool _supportSystemStatus;
        bool _ticketSystemStatus;
        bool _bugSystemStatus;
        bool _complaintSystemStatus;
        bool _suggestionSystemStatus;
        Dictionary<int, BugTicket> _bugTicketList = new();
        Dictionary<int, ComplaintTicket> _complaintTicketList = new();
        Dictionary<int, SuggestionTicket> _suggestionTicketList = new();
        int _lastBugId;
        int _lastComplaintId;
        int _lastSuggestionId;
        int _openBugTicketCount;
        int _openComplaintTicketCount;
        int _openSuggestionTicketCount;
        long _lastChange;
    }
}
