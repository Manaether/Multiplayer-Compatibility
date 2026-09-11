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
        private static Action markGearDirtyAction;

        public TrueRPGInventory(ModContentPack mod) => LongEventHandler.ExecuteWhenFinished(LatePatch);

        private static void LatePatch()
        {
            InitDirtyAction();
            var markDirtyMethod = new HarmonyMethod(typeof(TrueRPGInventory), nameof(MarkGearDirty));

            // 1. Sync direct gear commands
            var gearCommandsType = AccessTools.TypeByName("TrueRPGInventory.GearCommands");
            if (gearCommandsType != null)
            {
                string[] gearCommandMethods = {
                    "Wear",
                    "Equip",
                    "UnequipToInventory",
                    "DropAtFeet",
                    "DropNearby",
                    "ToggleForced",
                    "CycleWeapon",
                    "MakeSidearm",
                    "ToggleStripDesignation"
                };

                foreach (var methodName in gearCommandMethods)
                {
                    MP.RegisterSyncMethod(gearCommandsType, methodName).CancelIfAnyArgNull();
                    var method = AccessTools.DeclaredMethod(gearCommandsType, methodName);
                    if (method != null)
                        MpCompat.harmony.Patch(method, postfix: markDirtyMethod);
                }
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

                var setPosMethod = AccessTools.DeclaredMethod(gridStateCompType, "SetPos");
                if (setPosMethod != null)
                    MpCompat.harmony.Patch(setPosMethod, postfix: markDirtyMethod);

                var setHeadgearHiddenMethod = AccessTools.DeclaredMethod(gridStateCompType, "SetHeadgearHidden");
                if (setHeadgearHiddenMethod != null)
                    MpCompat.harmony.Patch(setHeadgearHiddenMethod, postfix: markDirtyMethod);

                var setShownWeaponMethod = AccessTools.DeclaredMethod(gridStateCompType, "SetShownWeapon");
                if (setShownWeaponMethod != null)
                    MpCompat.harmony.Patch(setShownWeaponMethod, postfix: markDirtyMethod);
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
                var moveItemToMethod = AccessTools.DeclaredMethod(exchangeType, "MoveItemTo");
                if (moveItemToMethod != null)
                    MpCompat.harmony.Patch(moveItemToMethod, postfix: markDirtyMethod);
            }
            else
            {
                Log.Warning("[Multiplayer Compat] TrueRPGInventory: Could not find TrueRPGInventory.Dialog_RPGExchange");
            }
        }

        private static void InitDirtyAction()
        {
            var notifyGearMutatedMethod = AccessTools.DeclaredMethod("TrueRPGInventory.TrueGearTab:NotifyGearMutated");
            if (notifyGearMutatedMethod != null)
            {
                markGearDirtyAction = (Action)Delegate.CreateDelegate(typeof(Action), notifyGearMutatedMethod);
            }
            else
            {
                var dirtyField = AccessTools.DeclaredField("TrueRPGInventory.TrueGearTab:Dirty");
                if (dirtyField != null)
                {
                    var dirtyRef = AccessTools.StaticFieldRefAccess<bool>(dirtyField);
                    markGearDirtyAction = () => dirtyRef() = true;
                }
            }
        }

        private static void MarkGearDirty()
        {
            markGearDirtyAction?.Invoke();
        }

        private static bool CancelTradeRedirectInMp()
        {
            // If in multiplayer, cancel the redirect so vanilla/multiplayer Dialog_Trade remains open
            return !MP.IsInMultiplayer;
        }
    }
}
