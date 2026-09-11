using System;
using System.Reflection;
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
        private static bool isComputingLayout;

        private static PropertyInfo gridStateCompInstanceProp;
        private static FastInvokeHandler setPosHandler;

        public TrueRPGInventory(ModContentPack mod) => LongEventHandler.ExecuteWhenFinished(LatePatch);

        private static void LatePatch()
        {
            InitDirtyAction();

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
                    MP.RegisterSyncMethod(gearCommandsType, methodName)
                        .CancelIfAnyArgNull()
                        .SetPostInvoke((target, args) => MarkGearDirty());
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

            // 3. GridStateComponent layout and visual settings
            var gridStateCompType = AccessTools.TypeByName("TrueRPGInventory.GridStateComponent");
            if (gridStateCompType != null)
            {
                gridStateCompInstanceProp = AccessTools.Property(gridStateCompType, "Instance");
                var setPosMethod = AccessTools.DeclaredMethod(gridStateCompType, "SetPos");
                if (setPosMethod != null)
                {
                    setPosHandler = MethodInvoker.GetHandler(setPosMethod);
                    MpCompat.harmony.Patch(
                        setPosMethod,
                        prefix: new HarmonyMethod(typeof(TrueRPGInventory), nameof(PrefixSetPos))
                    );
                }

                // Wrap GridLayoutEngine.Compute so automatic layout placement during rendering is never synced or looped
                var computeMethod = AccessTools.DeclaredMethod("TrueRPGInventory.GridLayoutEngine:Compute");
                if (computeMethod != null)
                {
                    MpCompat.harmony.Patch(
                        computeMethod,
                        prefix: new HarmonyMethod(typeof(TrueRPGInventory), nameof(PreCompute)),
                        finalizer: new HarmonyMethod(typeof(TrueRPGInventory), nameof(PostCompute))
                    );
                }

                // Register dedicated static method for syncing manual item movements in the RPG grid
                MP.RegisterSyncMethod(typeof(TrueRPGInventory), nameof(SyncedSetPos)).CancelIfAnyArgNull();

                MP.RegisterSyncMethod(gridStateCompType, "SetHeadgearHidden")
                    .CancelIfAnyArgNull()
                    .SetPostInvoke((target, args) => MarkGearDirty());

                MP.RegisterSyncMethod(gridStateCompType, "SetShownWeapon")
                    .CancelIfAnyArgNull()
                    .SetPostInvoke((target, args) => MarkGearDirty());
            }
            else
            {
                Log.Warning("[Multiplayer Compat] TrueRPGInventory: Could not find TrueRPGInventory.GridStateComponent");
            }

            // 4. Sync Dialog_RPGExchange item transfers
            var exchangeType = AccessTools.TypeByName("TrueRPGInventory.Dialog_RPGExchange");
            if (exchangeType != null)
            {
                MP.RegisterSyncMethod(exchangeType, "MoveItemTo")
                    .CancelIfAnyArgNull()
                    .SetPostInvoke((target, args) => MarkGearDirty());
            }
            else
            {
                Log.Warning("[Multiplayer Compat] TrueRPGInventory: Could not find TrueRPGInventory.Dialog_RPGExchange");
            }
        }

        private static void PreCompute() => isComputingLayout = true;
        private static void PostCompute() => isComputingLayout = false;

        private static bool PrefixSetPos(object __instance, Thing t, int x, int y)
        {
            // If called during automatic layout calculation in UI rendering, allow local execution without syncing or marking dirty
            if (isComputingLayout)
                return true;

            if (t == null)
                return false;

            // If in multiplayer and called from player UI interaction (drag & drop)
            if (MP.IsInMultiplayer && MP.InInterface)
            {
                // Apply locally immediately for instant feedback on the dragging client
                isComputingLayout = true;
                try
                {
                    setPosHandler?.Invoke(__instance, t, x, y);
                }
                finally
                {
                    isComputingLayout = false;
                }

                MarkGearDirty();

                // Send synchronized command to other clients
                SyncedSetPos(t, x, y);

                return false;
            }

            return true;
        }

        [SyncMethod]
        public static void SyncedSetPos(Thing t, int x, int y)
        {
            if (t == null) return;

            var comp = gridStateCompInstanceProp?.GetValue(null, null);
            if (comp != null)
            {
                isComputingLayout = true;
                try
                {
                    setPosHandler?.Invoke(comp, t, x, y);
                }
                finally
                {
                    isComputingLayout = false;
                }
            }

            MarkGearDirty();
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
