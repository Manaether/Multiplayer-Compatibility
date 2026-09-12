using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat
{
    /// <summary>
    /// Prevents UI mods and interface rendering from advancing simulation ID counters in UniqueIDsManager.
    /// During UI rendering (UIRootOnGUI), preview items and transient interface elements are created
    /// with normal positive IDs without crashing game data structures, and the simulation counters
    /// are restored at the end of the GUI pass to keep host and client perfectly synchronized.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootOnGUI))]
    internal static class UniqueIDsManagerGuard
    {
        private static FieldInfo[] idFields;
        private static int[] snapshot;
        private static bool hasSnapshot;
        private static int guiDepth;

        private static Func<bool> tickingGetter;
        private static Func<bool> executingCmdsGetter;
        private static bool reflectionInitialized;

        static UniqueIDsManagerGuard()
        {
            try
            {
                var fields = new List<FieldInfo>();
                foreach (var f in typeof(UniqueIDsManager).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    if (f.FieldType == typeof(int) && f.Name.StartsWith("next", StringComparison.OrdinalIgnoreCase))
                    {
                        fields.Add(f);
                    }
                }
                idFields = fields.ToArray();
                snapshot = new int[idFields.Length];
            }
            catch (Exception ex)
            {
                Log.Warning($"[MpCompat] UniqueIDsManagerGuard: Failed to inspect UniqueIDsManager fields: {ex}");
                idFields = new FieldInfo[0];
                snapshot = new int[0];
            }
        }

        private static void EnsureReflection()
        {
            if (reflectionInitialized)
                return;

            var mpType = AccessTools.TypeByName("Multiplayer.Client.Multiplayer");
            if (mpType != null)
            {
                var tickingProp = AccessTools.Property(mpType, "Ticking");
                if (tickingProp?.GetGetMethod() != null)
                {
                    try
                    {
                        tickingGetter = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), tickingProp.GetGetMethod());
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[MpCompat] UniqueIDsManagerGuard: Failed to bind tickingGetter: {ex}");
                    }
                }

                var executingCmdsProp = AccessTools.Property(mpType, "ExecutingCmds");
                if (executingCmdsProp?.GetGetMethod() != null)
                {
                    try
                    {
                        executingCmdsGetter = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), executingCmdsProp.GetGetMethod());
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[MpCompat] UniqueIDsManagerGuard: Failed to bind executingCmdsGetter: {ex}");
                    }
                }
            }

            reflectionInitialized = true;
        }

        private static bool IsTickingOrExecutingCmds()
        {
            EnsureReflection();
            return (tickingGetter != null && tickingGetter()) ||
                   (executingCmdsGetter != null && executingCmdsGetter()) ||
                   MP.IsExecutingSyncCommand;
        }

        private static void TakeSnapshot(UniqueIDsManager manager)
        {
            for (int i = 0; i < idFields.Length; i++)
            {
                snapshot[i] = (int)idFields[i].GetValue(manager);
            }
            hasSnapshot = true;
        }

        private static void RestoreSnapshot(UniqueIDsManager manager)
        {
            if (!hasSnapshot)
                return;

            for (int i = 0; i < idFields.Length; i++)
            {
                idFields[i].SetValue(manager, snapshot[i]);
            }
            hasSnapshot = false;
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix()
        {
            if (!MP.IsInMultiplayer || Current.ProgramState != ProgramState.Playing)
                return;

            if (IsTickingOrExecutingCmds())
                return;

            var manager = Find.UniqueIDsManager;
            if (manager == null)
                return;

            if (guiDepth++ == 0)
            {
                TakeSnapshot(manager);
            }
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            Cleanup();
        }

        [HarmonyFinalizer]
        [HarmonyPriority(Priority.Last)]
        private static Exception Finalizer(Exception __exception)
        {
            Cleanup();
            return __exception;
        }

        private static void Cleanup()
        {
            if (guiDepth <= 0)
                return;

            if (--guiDepth == 0)
            {
                if (MP.IsInMultiplayer && Current.ProgramState == ProgramState.Playing && !IsTickingOrExecutingCmds())
                {
                    var manager = Find.UniqueIDsManager;
                    if (manager != null)
                    {
                        RestoreSnapshot(manager);
                    }
                }
                else
                {
                    hasSnapshot = false;
                }
            }
        }
    }
}
