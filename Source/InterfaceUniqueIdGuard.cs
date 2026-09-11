using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat
{
    /// <summary>
    /// Prevents UI mods and interface rendering from consuming synchronized IDs from UniqueIDsManager.
    /// When rendering UI (MP.InInterface), any ThingMaker.MakeThing or UniqueIDsManager call returns
    /// a transient client-local negative ID without advancing the shared game state counters.
    /// </summary>
    [HarmonyPatch]
    internal static class InterfaceUniqueIdGuard
    {
        private static int clientDummyThingId = -1000;
        private static int clientDummyJobId = -1000;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ThingIDMaker), nameof(ThingIDMaker.GiveIDTo))]
        private static bool GiveIDTo_Prefix(Thing t)
        {
            if (MP.IsInMultiplayer && MP.InInterface)
            {
                if (t.def != null && t.def.HasThingIDNumber)
                {
                    t.thingIDNumber = clientDummyThingId--;
                    if (clientDummyThingId < -2000000000)
                        clientDummyThingId = -1000;
                }
                return false;
            }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(UniqueIDsManager), nameof(UniqueIDsManager.GetNextThingID))]
        private static bool GetNextThingID_Prefix(ref int __result)
        {
            if (MP.IsInMultiplayer && MP.InInterface)
            {
                __result = clientDummyThingId--;
                if (clientDummyThingId < -2000000000)
                    clientDummyThingId = -1000;
                return false;
            }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(UniqueIDsManager), nameof(UniqueIDsManager.GetNextJobID))]
        private static bool GetNextJobID_Prefix(ref int __result)
        {
            if (MP.IsInMultiplayer && MP.InInterface)
            {
                __result = clientDummyJobId--;
                if (clientDummyJobId < -2000000000)
                    clientDummyJobId = -1000;
                return false;
            }
            return true;
        }
    }
}
