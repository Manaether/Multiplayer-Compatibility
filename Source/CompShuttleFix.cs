using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Multiplayer.Compat
{
    /// <summary>
    /// Prevents null reference exceptions during shuttle departure and post-loading in Multiplayer.
    /// <para>
    /// <b>Problem:</b>
    /// In vanilla RimWorld Royalty, <see cref="CompShuttle.SendLaunchedSignals"/> accesses <c>requiredPawns</c>
    /// and <c>requiredItems</c> directly without null checks. During multiplayer save transfers, mid-game sync,
    /// or despawns, these collections can be null if omitted from the serialized packet.
    /// When the shuttle attempts to launch, <c>SendLaunchedSignals</c> throws a <see cref="NullReferenceException"/>
    /// inside <c>ShipJob_FlyAway.TryStart</c>. Because <c>TryStart</c> fails and returns false, the transport ship
    /// retries takeoff every tick, causing an endless exception loop and freezing progression.
    /// </para>
    /// <para>
    /// <b>Solution:</b>
    /// 1. Coalesce null collections in <see cref="CompShuttle.PostExposeData"/> to ensure lists are always populated.
    /// 2. Apply a finalizer guard on <see cref="CompShuttle.SendLaunchedSignals"/> to safely suppress any remaining
    /// exceptions during the departure sequence, allowing the shuttle to take off rather than bricking the game.
    /// </para>
    /// </summary>
    [HarmonyPatch]
    internal static class CompShuttleFix
    {
        [HarmonyPrepare]
        private static bool Prepare()
        {
            return AccessTools.TypeByName("RimWorld.CompShuttle") != null;
        }

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
