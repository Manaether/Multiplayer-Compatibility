using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat
{
    /// <summary>True RPG Inventory by Astryl</summary>
    /// <see href="https://steamcommunity.com/sharedfiles/filedetails/?id=3744201621"/>
    [MpCompatFor("astryl.truerpginventory")]
    internal class TrueRPGInventory
    {
        public TrueRPGInventory(ModContentPack mod) => LongEventHandler.ExecuteWhenFinished(LatePatch);

        private static void LatePatch()
        {
            // 1. Sync direct gear commands
            var gearCommandsType = AccessTools.TypeByName("TrueRPGInventory.GearCommands");
            if (gearCommandsType != null)
            {
                MP.RegisterSyncMethod(gearCommandsType, "Wear").CancelIfAnyArgNull();
                MP.RegisterSyncMethod(gearCommandsType, "Equip").CancelIfAnyArgNull();
                MP.RegisterSyncMethod(gearCommandsType, "UnequipToInventory").CancelIfAnyArgNull();
                MP.RegisterSyncMethod(gearCommandsType, "DropAtFeet").CancelIfAnyArgNull();
                MP.RegisterSyncMethod(gearCommandsType, "DropNearby").CancelIfAnyArgNull();
                MP.RegisterSyncMethod(gearCommandsType, "ToggleForced").CancelIfAnyArgNull();
                MP.RegisterSyncMethod(gearCommandsType, "CycleWeapon").CancelIfAnyArgNull();
                MP.RegisterSyncMethod(gearCommandsType, "MakeSidearm").CancelIfAnyArgNull();
                MP.RegisterSyncMethod(gearCommandsType, "ToggleStripDesignation").CancelIfAnyArgNull();
            }
            else
            {
                Log.Warning("[Multiplayer Compat] TrueRPGInventory: Could not find TrueRPGInventory.GearCommands");
            }

            // 2. Bypass trade window redirect in multiplayer to keep Multiplayer's trade session working
            var tradeRedirectPrefix = AccessTools.DeclaredMethod("TrueRPGInventory.Patch_TradeRedirect:Prefix");
            if (tradeRedirectPrefix != null)
            {
                MpCompat.harmony.Patch(
                    tradeRedirectPrefix,
                    prefix: new HarmonyMethod(typeof(TrueRPGInventory), nameof(CancelTradeRedirectInMp))
                );
            }
            else
            {
                Log.Warning("[Multiplayer Compat] TrueRPGInventory: Could not find TrueRPGInventory.Patch_TradeRedirect:Prefix");
            }

            // 3. Sync GridStateComponent persistent layout and visual settings
            var gridStateCompType = AccessTools.TypeByName("TrueRPGInventory.GridStateComponent");
            if (gridStateCompType != null)
            {
                MP.RegisterSyncMethod(gridStateCompType, "SetPos").CancelIfAnyArgNull();
                MP.RegisterSyncMethod(gridStateCompType, "SetHeadgearHidden");
                MP.RegisterSyncMethod(gridStateCompType, "SetShownWeapon").CancelIfAnyArgNull();
            }
            else
            {
                Log.Warning("[Multiplayer Compat] TrueRPGInventory: Could not find TrueRPGInventory.GridStateComponent");
            }

            // 4. Sync Dialog_RPGExchange item transfers
            var exchangeType = AccessTools.TypeByName("TrueRPGInventory.Dialog_RPGExchange");
            if (exchangeType != null)
            {
                MP.RegisterSyncMethod(exchangeType, "MoveItemTo").CancelIfAnyArgNull();
            }
            else
            {
                Log.Warning("[Multiplayer Compat] TrueRPGInventory: Could not find TrueRPGInventory.Dialog_RPGExchange");
            }
        }

        private static bool CancelTradeRedirectInMp()
        {
            // If in multiplayer, cancel the redirect so vanilla/multiplayer Dialog_Trade remains open
            return !MP.IsInMultiplayer;
        }
    }
}
