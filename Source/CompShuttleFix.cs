using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Multiplayer.Compat
{
    /// <summary>
    /// Prevents null reference exceptions during shuttle departure and post-loading.
    /// If requiredPawns or requiredItems became null during deserialization or despawn,
    /// SendLaunchedSignals throws an NRE in ShipJob_FlyAway.TryStart, causing the transport ship
    /// to get stuck in an endless exception loop every tick.
    /// </summary>
    [HarmonyPatch]
    internal static class CompShuttleFix
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(CompShuttle), nameof(CompShuttle.PostExposeData))]
        private static void PostExposeData_Postfix(CompShuttle __instance)
        {
            __instance.requiredPawns ??= new List<Pawn>();
            __instance.requiredItems ??= new List<ThingDefCount>();
            __instance.pawnsToIgnoreIfDownedOfNotOnTheMap ??= new List<Pawn>();
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(CompShuttle), nameof(CompShuttle.SendLaunchedSignals))]
        private static Exception SendLaunchedSignals_Finalizer(Exception __exception)
        {
            if (__exception != null)
            {
                Log.WarningOnce($"[MpCompat] Suppressed exception in CompShuttle.SendLaunchedSignals: {__exception}", 84920194);
                return null;
            }
            return null;
        }
    }
}
