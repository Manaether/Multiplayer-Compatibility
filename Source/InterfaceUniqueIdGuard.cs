using System;
using System.Threading;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat
{
    /// <summary>
    /// Prevents UI mods and interface rendering from consuming synchronized IDs from UniqueIDsManager.
    /// When rendering UI or running off-tick outside synchronized simulation/commands, any ThingMaker.MakeThing,
    /// ThingIDMaker.GiveIDTo, or UniqueIDsManager ID calls return transient client-local negative IDs
    /// without advancing the shared game state counters.
    /// </summary>
    [HarmonyPatch]
    internal static class InterfaceUniqueIdGuard
    {
        private static int clientDummyThingId = -1000;
        private static int clientDummyJobId = -1000;
        private static int clientDummyGeneralId = -1000;

        private static readonly Func<bool> tickingGetter;

        static InterfaceUniqueIdGuard()
        {
            var tickingProp = AccessTools.Property("Multiplayer.Client.Multiplayer:Ticking");
            if (tickingProp?.GetGetMethod() != null)
            {
                try
                {
                    tickingGetter = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), tickingProp.GetGetMethod());
                }
                catch (Exception ex)
                {
                    Log.Warning($"[MpCompat] InterfaceUniqueIdGuard: Failed to bind tickingGetter delegate: {ex}");
                }
            }
        }

        private static int GetNextDummyThingId()
        {
            int id = Interlocked.Decrement(ref clientDummyThingId);
            if (id < -2000000000)
            {
                Interlocked.Exchange(ref clientDummyThingId, -1000);
            }
            return id;
        }

        private static int GetNextDummyJobId()
        {
            int id = Interlocked.Decrement(ref clientDummyJobId);
            if (id < -2000000000)
            {
                Interlocked.Exchange(ref clientDummyJobId, -1000);
            }
            return id;
        }

        private static int GetNextDummyGeneralId()
        {
            int id = Interlocked.Decrement(ref clientDummyGeneralId);
            if (id < -2000000000)
            {
                Interlocked.Exchange(ref clientDummyGeneralId, -1000);
            }
            return id;
        }

        public static bool ShouldGuardId()
        {
            if (!MP.IsInMultiplayer)
                return false;

            // Any Unity IMGUI pass is strictly local UI rendering and must never advance shared counters
            if (Event.current != null)
                return true;

            // Standard Multiplayer interface state
            if (MP.InInterface)
                return true;

            // Synced commands mutating simulation state
            if (MP.IsExecutingSyncCommand)
                return false;

            // Active simulation tick
            if (tickingGetter != null && tickingGetter())
                return false;

            // Non-playing program states
            if (Current.ProgramState != ProgramState.Playing)
            {
                // Main menu: always guard
                if (Current.ProgramState == ProgramState.Entry)
                    return true;

                // Map initializing / loading: guard if GUI rendering
                return Event.current != null;
            }

            // In ProgramState.Playing, any call outside ticking and synced commands is off-tick / client-local
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(ThingIDMaker), nameof(ThingIDMaker.GiveIDTo))]
        private static bool GiveIDTo_Prefix(Thing t)
        {
            if (ShouldGuardId())
            {
                if (t.def != null && t.def.HasThingIDNumber)
                {
                    t.thingIDNumber = GetNextDummyThingId();
                }
                return false;
            }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(UniqueIDsManager), nameof(UniqueIDsManager.GetNextThingID))]
        private static bool GetNextThingID_Prefix(ref int __result)
        {
            if (ShouldGuardId())
            {
                __result = GetNextDummyThingId();
                return false;
            }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(UniqueIDsManager), nameof(UniqueIDsManager.GetNextJobID))]
        private static bool GetNextJobID_Prefix(ref int __result)
        {
            if (ShouldGuardId())
            {
                __result = GetNextDummyJobId();
                return false;
            }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(UniqueIDsManager), "GetNextID")]
        private static bool GetNextID_Prefix(ref int nextID, ref int __result)
        {
            if (ShouldGuardId())
            {
                __result = GetNextDummyGeneralId();
                return false;
            }
            return true;
        }
    }
}

