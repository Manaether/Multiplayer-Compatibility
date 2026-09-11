using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat
{
    /// <summary>Sleek Work Priorities by squishyjellyfish</summary>
    /// <see href="https://steamcommunity.com/sharedfiles/filedetails/?id=3764537806"/>
    [MpCompatFor("squishyjellyfish.sleekworkpriorities")]
    internal class SleekWorkPriorities
    {
        public SleekWorkPriorities(ModContentPack mod) => LongEventHandler.ExecuteWhenFinished(LatePatch);

        private static void LatePatch()
        {
            var bridgeType = AccessTools.TypeByName("SleekWorkPriorities.MultiplayerBridge");
            if (bridgeType == null)
            {
                Log.Warning("[Multiplayer Compat] SleekWorkPriorities: Could not find SleekWorkPriorities.MultiplayerBridge");
                return;
            }

            // 1. The mod attempts TryApply in [StaticConstructorOnStartup] where MP.enabled is false,
            // which sets attempted = true and failed = true. Reset both flags and re-run now that MP is active.
            AccessTools.Field(bridgeType, "attempted")?.SetValue(null, false);
            AccessTools.Field(bridgeType, "failed")?.SetValue(null, false);

            var tryApply = AccessTools.DeclaredMethod(bridgeType, "TryApply");
            tryApply?.Invoke(null, null);

            var appliedField = AccessTools.Field(bridgeType, "applied");
            bool applied = (bool)(appliedField?.GetValue(null) ?? false);

            if (applied)
            {
                Log.Message("[Multiplayer Compat] SleekWorkPriorities: Successfully initialized native MultiplayerBridge.");
            }
            else
            {
                // Fallback manual registration if TryApply failed
                try
                {
                    AccessTools.Field(bridgeType, "isInMultiplayer")?.SetValue(null, (Func<bool>)(() => MP.IsInMultiplayer));
                    AccessTools.Field(bridgeType, "isExecutingSyncCommand")?.SetValue(null, (Func<bool>)(() => MP.IsExecutingSyncCommand));
                    AccessTools.Field(bridgeType, "inInterface")?.SetValue(null, (Func<bool>)(() => MP.InInterface));
                    AccessTools.Field(bridgeType, "issuedBySelf")?.SetValue(null, (Func<bool>)(() => MP.IsExecutingSyncCommandIssuedBySelf));

                    var parentMethod = AccessTools.Method(bridgeType, "SyncedApplyParent");
                    var childMethod = AccessTools.Method(bridgeType, "SyncedApplyChild");
                    var rulesMethod = AccessTools.Method(bridgeType, "SyncedApplyRules");

                    if (parentMethod != null) MP.RegisterSyncMethod(parentMethod);
                    if (childMethod != null) MP.RegisterSyncMethod(childMethod);
                    if (rulesMethod != null) MP.RegisterSyncMethod(rulesMethod);

                    appliedField?.SetValue(null, true);
                    AccessTools.Field(bridgeType, "failed")?.SetValue(null, false);
                    Log.Message("[Multiplayer Compat] SleekWorkPriorities: Manually initialized native MultiplayerBridge fallback.");
                }
                catch (Exception e)
                {
                    Log.Error($"[Multiplayer Compat] SleekWorkPriorities fallback failed: {e}");
                }
            }

            // 2. Sync toggling between checkbox mode ("no number for work") and manual numeric priorities
            var drawToggleMethod = AccessTools.DeclaredMethod("SleekWorkPriorities.Patch_Toolbar:DrawToggle");
            if (drawToggleMethod != null)
            {
                MpCompat.harmony.Patch(
                    drawToggleMethod,
                    prefix: new HarmonyMethod(typeof(SleekWorkPriorities), nameof(DrawToggle_Prefix)),
                    postfix: new HarmonyMethod(typeof(SleekWorkPriorities), nameof(DrawToggle_Postfix))
                );
            }
            else
            {
                Log.Warning("[Multiplayer Compat] SleekWorkPriorities: Could not find Patch_Toolbar.DrawToggle");
            }

            MP.RegisterSyncMethod(typeof(SleekWorkPriorities), nameof(SyncedSetUseWorkPriorities));
        }

        private static void DrawToggle_Prefix(out bool __state)
        {
            __state = Current.Game?.playSettings?.useWorkPriorities ?? false;
        }

        private static void DrawToggle_Postfix(bool __state)
        {
            if (!MP.IsInMultiplayer) return;
            var playSettings = Current.Game?.playSettings;
            if (playSettings == null) return;

            if (playSettings.useWorkPriorities != __state)
            {
                bool targetValue = playSettings.useWorkPriorities;
                // Revert local unsynced mutation immediately; it will be applied synchronously via SyncedSetUseWorkPriorities
                playSettings.useWorkPriorities = __state;
                SyncedSetUseWorkPriorities(targetValue);
            }
        }

        private static void SyncedSetUseWorkPriorities(bool value)
        {
            if (Current.Game?.playSettings == null) return;
            Current.Game.playSettings.useWorkPriorities = value;
            foreach (Pawn item in PawnsFinder.AllMapsWorldAndTemporary_Alive)
            {
                if (item.Faction == Faction.OfPlayer && item.workSettings != null)
                {
                    item.workSettings.Notify_UseWorkPrioritiesChanged();
                }
            }
            var workCacheType = AccessTools.TypeByName("SleekWorkPriorities.WorkCache");
            AccessTools.Method(workCacheType, "Invalidate")?.Invoke(null, null);
        }
    }
}
