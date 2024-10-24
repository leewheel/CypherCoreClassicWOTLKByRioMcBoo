// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Game.Entities;
using Game.Scripting;
using Game.Spells;
using System.Collections.Generic;
using static Global;

namespace Scripts.World.Items
{
    [Script]
    class item_only_for_flight : ItemScript
    {
        const int SpellArcaneCharges = 45072;

        public item_only_for_flight() : base("item_only_for_flight") { }

        public override bool OnUse(Player player, Item item, SpellCastTargets targets, ObjectGuid castId)
        {
            int itemId = item.GetEntry();
            bool disabled = false;

            //for special scripts
            switch (itemId)
            {
                case 24538:
                    if (player.GetAreaId() != 3628)
                        disabled = true;
                    break;
                case 34489:
                    if (player.GetZoneId() != 4080)
                        disabled = true;
                    break;
                case 34475:
                    SpellInfo spellInfo = SpellMgr.GetSpellInfo(SpellArcaneCharges, player.GetMap().GetDifficultyID());
                    if (spellInfo != null)
                        Spell.SendCastResult(player, spellInfo, default, castId, SpellCastResult.NotOnGround);
                    break;
            }

            // allow use in flight only
            if (player.IsInFlight() && !disabled)
                return false;

            // error
            player.SendEquipError(InventoryResult.ClientLockedOut, item, null);
            return true;
        }
    }

    [Script]
    class item_gor_dreks_ointment : ItemScript
    {
        public item_gor_dreks_ointment() : base("item_gor_dreks_ointment") { }

        public override bool OnUse(Player player, Item item, SpellCastTargets targets, ObjectGuid castId)
        {
            if (targets.GetUnitTarget() != null && targets.GetUnitTarget().GetTypeId() == TypeId.Unit &&
                targets.GetUnitTarget().GetEntry() == 20748 && !targets.GetUnitTarget().HasAura(32578))
                return false;

            player.SendEquipError(InventoryResult.ClientLockedOut, item, null);
            return true;
        }
    }

    [Script]
    class item_mysterious_egg : ItemScript
    {
        public item_mysterious_egg() : base("item_mysterious_egg") { }

        public override bool OnExpire(Player player, ItemTemplate pItemProto)
        {
            InventoryResult msg = player.CanStoreNewItem(ItemPos.Undefined, out var dest, 39883, 1); // Cracked Egg
            if (msg == InventoryResult.Ok)
                player.StoreNewItem(dest, 39883, true, ItemEnchantmentManager.GenerateRandomProperties(39883));

            return true;
        }
    }

    [Script]
    class item_disgusting_jar : ItemScript
    {
        public item_disgusting_jar() : base("item_disgusting_jar") { }

        public override bool OnExpire(Player player, ItemTemplate pItemProto)
        {
            InventoryResult msg = player.CanStoreNewItem(ItemPos.Undefined, out var dest, 44718, 1); // Ripe Disgusting Jar
            if (msg == InventoryResult.Ok)
                player.StoreNewItem(dest, 44718, true, ItemEnchantmentManager.GenerateRandomProperties(44718));

            return true;
        }
    }

    [Script]
    class item_petrov_cluster_bombs : ItemScript
    {
        const int SpellPetrovBomb = 42406;
        const int AreaIdShatteredStraits = 4064;
        const int ZoneIdHowling = 495;

        public item_petrov_cluster_bombs() : base("item_petrov_cluster_bombs") { }

        public override bool OnUse(Player player, Item item, SpellCastTargets targets, ObjectGuid castId)
        {
            if (player.GetZoneId() != ZoneIdHowling)
                return false;

            if (player.GetTransport() == null || player.GetAreaId() != AreaIdShatteredStraits)
            {
                SpellInfo spellInfo = SpellMgr.GetSpellInfo(SpellPetrovBomb, Difficulty.None);
                if (spellInfo != null)
                    Spell.SendCastResult(player, spellInfo, default, castId, SpellCastResult.NotHere);

                return true;
            }

            return false;
        }
    }

    [Script]
    class item_captured_frog : ItemScript
    {
        const int QuestThePerfectSpies = 25444;
        const int NpcVanirasSentryTotem = 40187;

        public item_captured_frog() : base("item_captured_frog") { }

        public override bool OnUse(Player player, Item item, SpellCastTargets targets, ObjectGuid castId)
        {
            if (player.GetQuestStatus(QuestThePerfectSpies) == QuestStatus.Incomplete)
            {
                if (player.FindNearestCreature(NpcVanirasSentryTotem, 10.0f) != null)
                    return false;
                else
                    player.SendEquipError(InventoryResult.OutOfRange, item, null);
            }
            else
                player.SendEquipError(InventoryResult.ClientLockedOut, item, null);
            return true;
        }
    }

    // Only used currently for
    [Script] // 19169: Nightfall
    class item_generic_limit_chance_above_60 : ItemScript
    {
        public item_generic_limit_chance_above_60() : base("item_generic_limit_chance_above_60") { }

        public override bool OnCastItemCombatSpell(Player player, Unit victim, SpellInfo spellInfo, Item item)
        {
            // spell proc Chance gets severely reduced on victims > 60 (formula unknown)
            if (victim.GetLevel() > 60)
            {
                // gives ~0.1% proc Chance at lvl 70
                float lvlPenaltyFactor = 9.93f;
                float failureChance = (victim.GetLevelForTarget(player) - 60) * lvlPenaltyFactor;

                // base ppm Chance was already rolled, only roll success Chance
                return !RandomHelper.randChance(failureChance);
            }

            return true;
        }
    }
}