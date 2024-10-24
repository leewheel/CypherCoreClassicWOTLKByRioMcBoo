// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Game.AI;
using Game.DataStorage;
using Game.Entities;
using Game.Maps;
using Game.Scripting;
using System;

namespace Scripts.EasternKingdoms.AlteracValley
{
    struct SpellIds
    {
        public const int Charge = 22911;
        public const int Cleave = 40504;
        public const int DemoralizingShout = 23511;
        public const int Enrage = 8599;
        public const int Whirlwind = 13736;

        public const int NorthMarshal = 45828;
        public const int SouthMarshal = 45829;
        public const int StonehearthMarshal = 45830;
        public const int IcewingMarshal = 45831;
        public const int IcebloodWarmaster = 45822;
        public const int TowerPointWarmaster = 45823;
        public const int WestFrostwolfWarmaster = 45824;
        public const int EastFrostwolfWarmaster = 45826;
    }

    struct CreatureIds
    {
        public const int NorthMarshal = 14762;
        public const int SouthMarshal = 14763;
        public const int IcewingMarshal = 14764;
        public const int StonehearthMarshal = 14765;
        public const int EastFrostwolfWarmaster = 14772;
        public const int IcebloodWarmaster = 14773;
        public const int TowerPointWarmaster = 14776;
        public const int WestFrostwolfWarmaster = 14777;
    }

    [Script]
    class npc_av_marshal_or_warmaster : ScriptedAI
    {
        (int npcEntry, int spellId)[] _auraPairs =
        [
            new (CreatureIds.NorthMarshal, SpellIds.NorthMarshal),
            new (CreatureIds.SouthMarshal, SpellIds.SouthMarshal),
            new (CreatureIds.StonehearthMarshal, SpellIds.StonehearthMarshal),
            new (CreatureIds.IcewingMarshal, SpellIds.IcewingMarshal),
            new (CreatureIds.EastFrostwolfWarmaster, SpellIds.EastFrostwolfWarmaster),
            new (CreatureIds.WestFrostwolfWarmaster, SpellIds.WestFrostwolfWarmaster),
            new (CreatureIds.TowerPointWarmaster, SpellIds.TowerPointWarmaster),
            new (CreatureIds.IcebloodWarmaster, SpellIds.IcebloodWarmaster)
        ];

        bool _hasAura;

        public npc_av_marshal_or_warmaster(Creature creature) : base(creature)
        {
            Initialize();
        }

        void Initialize()
        {
            _hasAura = false;
        }

        public override void Reset()
        {
            Initialize();

            _scheduler.CancelAll();
            _scheduler.Schedule((Seconds)2, (Seconds)12, task =>
            {
                DoCastVictim(SpellIds.Charge);
                task.Repeat((Seconds)10, (Seconds)25);
            });
            _scheduler.Schedule((Seconds)1, (Seconds)11, task =>
            {
                DoCastVictim(SpellIds.Cleave);
                task.Repeat((Seconds)10, (Seconds)16);
            });
            _scheduler.Schedule((Seconds)2, task =>
            {
                DoCast(me, SpellIds.DemoralizingShout);
                task.Repeat((Seconds)10, (Seconds)15);
            });
            _scheduler.Schedule((Seconds)5, (Seconds)20, task =>
            {
                DoCast(me, SpellIds.Whirlwind);
                task.Repeat((Seconds)10, (Seconds)25);
            });
            _scheduler.Schedule((Seconds)5, (Seconds)20, task =>
            {
                DoCast(me, SpellIds.Enrage);
                task.Repeat((Seconds)10, (Seconds)30);
            });
            _scheduler.Schedule((Seconds)5, task =>
            {
                Position _homePosition = me.GetHomePosition();
                if (me.GetDistance2d(_homePosition.GetPositionX(), _homePosition.GetPositionY()) > 50.0f)
                {
                    EnterEvadeMode();
                    return;
                }
                task.Repeat((Seconds)5);
            });
        }

        public override void JustAppeared()
        {
            Reset();
        }

        public override void UpdateAI(TimeSpan diff)
        {
            // I have a feeling this isn't blizzlike, but owell, I'm only passing by and cleaning up.
            if (!_hasAura)
            {
                for (byte i = 0; i < _auraPairs.Length; ++i)
                    if (_auraPairs[i].npcEntry == me.GetEntry())
                        DoCast(me, _auraPairs[i].spellId);

                _hasAura = true;
            }

            if (!UpdateVictim())
                return;

            _scheduler.Update(diff);

            if (me.HasUnitState(UnitState.Casting))
                return;
        }
    }

    [Script]
    class go_av_capturable_object : GameObjectAI
    {
        public go_av_capturable_object(GameObject go) : base(go) { }

        public override void Reset()
        {
            me.SetActive(true);
        }

        public override bool OnGossipHello(Player player)
        {
            if (me.GetGoState() != GameObjectState.Ready)
                return true;

            ZoneScript zonescript = me.GetZoneScript();
            if (zonescript != null)
            {
                zonescript.DoAction(1, player, me);
                return false;
            }

            return true;
        }
    }

    [Script]
    class go_av_contested_object : GameObjectAI
    {
        public go_av_contested_object(GameObject go) : base(go) { }

        public override void Reset()
        {
            me.SetActive(true);
            _scheduler.Schedule((Minutes)4, _ =>
            {
                ZoneScript zonescript = me.GetZoneScript();
                if (zonescript != null)
                    zonescript.DoAction(2, me, me);
            });
        }

        public override bool OnGossipHello(Player player)
        {
            if (me.GetGoState() != GameObjectState.Ready)
                return true;

            ZoneScript zonescript = me.GetZoneScript();
            if (zonescript != null)
            {
                zonescript.DoAction(1, player, me);
                return false;
            }

            return true;
        }

        public override void UpdateAI(TimeSpan diff)
        {
            _scheduler.Update(diff);
        }
    }

    [Script]
    class at_av_exploit : AreaTriggerScript
    {
        public at_av_exploit() : base("at_av_exploit") { }

        public override bool OnTrigger(Player player, AreaTriggerRecord trigger)
        {
            var battleground = player.GetBattleground();
            if (battleground != null && battleground.GetStatus() == BattlegroundStatus.WaitJoin)
                battleground.TeleportPlayerToExploitLocation(player);

            return true;
        }
    }
}