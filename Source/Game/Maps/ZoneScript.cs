﻿// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Framework.Dynamic;
using Game.Entities;

namespace Game.Maps
{
    public class ZoneScript
    {
        public virtual void TriggerGameEvent(int gameEventId, WorldObject source = null, WorldObject target = null)
        {
            if (source != null)
                GameEvents.Trigger(gameEventId, source, target);
            else
                ProcessEvent(null, gameEventId, null);
        }

        public virtual int GetCreatureEntry(long guidlow, CreatureData data) { return data.Id; }
        public virtual int GetGameObjectEntry(long spawnId, int entry) { return entry; }

        public virtual void OnCreatureCreate(Creature creature) { }
        public virtual void OnCreatureRemove(Creature creature) { }

        public virtual void OnGameObjectCreate(GameObject go) { }
        public virtual void OnGameObjectRemove(GameObject go) { }

        public virtual void OnAreaTriggerCreate(AreaTrigger areaTrigger) { }
        public virtual void OnAreaTriggerRemove(AreaTrigger areaTrigger) { }

        public virtual void OnUnitDeath(Unit unit) { }

        //All-purpose data storage 64 bit
        public virtual ObjectGuid GetGuidData(int DataId) { return ObjectGuid.Empty; }
        public virtual void SetGuidData(int DataId, ObjectGuid Value) { }

        public virtual long GetData64(int dataId) { return 0; }
        public virtual void SetData64(int dataId, long value) { }

        //All-purpose data storage 32 bit
        public virtual int GetData(int dataId) { return 0; }
        public virtual void SetData(int dataId, int value) { }

        public virtual void ProcessEvent(WorldObject obj, int eventId, WorldObject invoker) { }
        public virtual void DoAction(int actionId, WorldObject source = null, WorldObject target = null) { }
        public virtual void OnFlagStateChange(GameObject flagInBase, FlagState oldValue, FlagState newValue, Player player) { }

        public virtual bool CanCaptureFlag(AreaTrigger areaTrigger, Player player) { return false; }
        public virtual void OnCaptureFlag(AreaTrigger areaTrigger, Player player) { }

        protected EventMap _events = new();
    }

    public class ControlZoneHandler
    {
        public virtual void HandleCaptureEventHorde(GameObject controlZone) { }
        public virtual void HandleCaptureEventAlliance(GameObject controlZone) { }
        public virtual void HandleContestedEventHorde(GameObject controlZone) { }
        public virtual void HandleContestedEventAlliance(GameObject controlZone) { }
        public virtual void HandleProgressEventHorde(GameObject controlZone) { }
        public virtual void HandleProgressEventAlliance(GameObject controlZone) { }
        public virtual void HandleNeutralEventHorde(GameObject controlZone) { HandleNeutralEvent(controlZone); }
        public virtual void HandleNeutralEventAlliance(GameObject controlZone) { HandleNeutralEvent(controlZone); }
        public virtual void HandleNeutralEvent(GameObject controlZone) { }
    }
}
