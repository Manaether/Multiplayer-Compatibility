using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat
{
    /// <summary>
    /// Guards <see cref="UniqueIDsManager"/> counters against ID drift caused by UI rendering passes.
    /// <para>
    /// <b>Problem:</b>
    /// Various UI overhaul mods (e.g., Radius UI, Modern UI, Character Editor, Adaptive Work Priorities,
    /// RPG Inventory) and some vanilla interface panels (quest previews, trade dialogs) instantiate temporary
    /// <see cref="Thing"/>s, jobs, or apparel previews during UI passes (<see cref="UIRoot_Play.UIRootOnGUI"/>).
    /// Instantiating these objects off-tick advances <see cref="UniqueIDsManager"/> counters on the local client
    /// only. When a synchronized simulation tick or player command later executes, the host and peers have divergent
    /// ID counters, causing instant multiplayer desyncs.
    /// </para>
    /// <para>
    /// <b>Why Snapshotting vs Dummy Negative IDs:</b>
    /// Previous approaches intercepted ID generation and minted negative IDs. However, negative IDs break game
    /// data structures, dictionary keys, wanderer quest joiners, and pawn generation that require valid positive
    /// IDs. Snapshotting allows temporary UI objects to receive valid positive IDs during rendering, and then
    /// rolls back the ID counters at the end of the GUI pass, leaving the simulation state completely untouched.
    /// </para>
    /// <para>
    /// <b>Performance:</b>
    /// All field getters and setters are precompiled into JIT expression-tree delegates at startup, guaranteeing
    /// zero heap allocations (0 boxing) during every frame's GUI passes.
    /// </para>
    /// </summary>
    [HarmonyPatch(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootOnGUI))]
    internal static class UniqueIDsManagerGuard
    {
        private delegate int IdGetter(UniqueIDsManager manager);
        private delegate void IdSetter(UniqueIDsManager manager, int value);

        private static readonly IdGetter[] getters;
        private static readonly IdSetter[] setters;
        private static readonly int[] snapshot;
        private static bool hasSnapshot;
        private static int guiDepth;

        private static Func<bool> tickingGetter;
        private static Func<bool> executingCmdsGetter;
        private static bool reflectionInitialized;

        static UniqueIDsManagerGuard()
        {
            try
            {
                var getterList = new List<IdGetter>();
                var setterList = new List<IdSetter>();

                var paramManager = Expression.Parameter(typeof(UniqueIDsManager), "manager");
                var paramValue = Expression.Parameter(typeof(int), "value");

                foreach (var field in typeof(UniqueIDsManager).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    if (field.FieldType == typeof(int) && field.Name.StartsWith("next", StringComparison.OrdinalIgnoreCase))
                    {
                        // Expression: (manager) => manager.field
                        var fieldExpr = Expression.Field(paramManager, field);
                        var getter = Expression.Lambda<IdGetter>(fieldExpr, paramManager).Compile();

                        // Expression: (manager, value) => manager.field = value
                        var assignExpr = Expression.Assign(fieldExpr, paramValue);
                        var setter = Expression.Lambda<IdSetter>(assignExpr, paramManager, paramValue).Compile();

                        getterList.Add(getter);
                        setterList.Add(setter);
                    }
                }

                getters = getterList.ToArray();
                setters = setterList.ToArray();
                snapshot = new int[getters.Length];
            }
            catch (Exception ex)
            {
                Log.Warning($"[MpCompat] UniqueIDsManagerGuard: Failed to compile ID accessor delegates: {ex}");
                getters = new IdGetter[0];
                setters = new IdSetter[0];
                snapshot = new int[0];
            }
        }

        [HarmonyPrepare]
        private static bool Prepare()
        {
            return AccessTools.Method(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootOnGUI)) != null;
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

        /// <summary>
        /// Returns true if the game is currently running an active simulation tick or executing a synchronized
        /// player command across the network. During ticking/command execution, ID generation is deterministic
        /// and MUST advance the real simulation counters.
        /// </summary>
        private static bool IsTickingOrExecutingCmds()
        {
            EnsureReflection();
            return (tickingGetter != null && tickingGetter()) ||
                   (executingCmdsGetter != null && executingCmdsGetter()) ||
                   MP.IsExecutingSyncCommand;
        }

        private static void TakeSnapshot(UniqueIDsManager manager)
        {
            for (int i = 0; i < getters.Length; i++)
            {
                snapshot[i] = getters[i](manager);
            }
            hasSnapshot = true;
        }

        private static void RestoreSnapshot(UniqueIDsManager manager)
        {
            if (!hasSnapshot)
                return;

            for (int i = 0; i < setters.Length; i++)
            {
                setters[i](manager, snapshot[i]);
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

            // Only capture the snapshot at the topmost UI pass to support re-entrant OnGUI calls safely
            if (guiDepth++ == 0)
            {
                TakeSnapshot(manager);
            }
        }

        /// <summary>
        /// A single Harmony Finalizer guarantees execution on both normal method returns and exceptional exits.
        /// Using a finalizer exclusively prevents double-decrementing <see cref="guiDepth"/> which would occur
        /// if both Postfix and Finalizer were registered.
        /// </summary>
        [HarmonyFinalizer]
        [HarmonyPriority(Priority.Last)]
        private static Exception Finalizer(Exception __exception)
        {
            if (guiDepth <= 0)
                return __exception;

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

            return __exception;
        }
    }
}
