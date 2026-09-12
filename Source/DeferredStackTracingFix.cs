using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace Multiplayer.Compat
{
    /// <summary>
    /// Prevents <see cref="IndexOutOfRangeException"/> in Multiplayer's desync stack tracing subsystem.
    /// <para>
    /// <b>Problem:</b>
    /// When desync tracing is active, <c>Multiplayer.Client.Desyncs.DeferredStackTracingImpl.TraceImpl</c>
    /// records method hashes into a fixed 32-element buffer (<c>long[32] traceIn</c>). When inspecting call
    /// stacks involving dynamic Harmony wrappers, lambdas, or inlined frames (<c>nameHash == 0</c>), or when
    /// deeply modded stacks exceed 32 frames, the array index <c>num4</c> overruns the 32-element buffer.
    /// </para>
    /// <para>
    /// <b>Impact on Simulation:</b>
    /// <c>TraceImpl</c> is invoked from a postfix on <see cref="Verse.Rand.get_Int"/> and <see cref="Verse.Rand.get_Value"/>.
    /// Any unhandled exception escaping from tracing immediately crashes the game during core RNG consumption.
    /// </para>
    /// <para>
    /// <b>Solution:</b>
    /// 1. A transpiler on <c>TraceImpl</c> injects an explicit bounds check (<see cref="ShouldStopTrace"/>)
    ///    before the array write instruction, safely truncating the trace at the buffer boundary.
    /// 2. Finalizers on <c>TraceImpl</c> and <c>DeferredStackTracing.Postfix</c> isolate simulation RNG
    ///    so that no tracing anomaly can ever crash the game.
    /// </para>
    /// </summary>
    [HarmonyPatch]
    public static class DeferredStackTracingImplFix
    {
        [HarmonyPrepare]
        private static bool Prepare()
        {
            return AccessTools.Method("Multiplayer.Client.Desyncs.DeferredStackTracingImpl:TraceImpl") != null;
        }

        [HarmonyTargetMethod]
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method("Multiplayer.Client.Desyncs.DeferredStackTracingImpl:TraceImpl");
        }

        /// <summary>
        /// Validates that the buffer index is within bounds of the tracing array.
        /// </summary>
        public static bool ShouldStopTrace(int num4, long[] traceIn)
        {
            return traceIn == null || (uint)num4 >= (uint)traceIn.Length;
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            var matcher = new CodeMatcher(instructions, il);

            // Locate the method exit (the final ldloc loading num4 before ret)
            matcher.End();
            matcher.MatchStartBackwards(
                new CodeMatch(i => i.IsLdloc()),
                new CodeMatch(OpCodes.Ret)
            );

            if (matcher.IsInvalid)
            {
                Log.Error("[MpCompat] Could not find exit instruction in DeferredStackTracingImpl.TraceImpl");
                return instructions;
            }

            var exitLabel = il.DefineLabel();
            matcher.Instruction.labels.Add(exitLabel);

            // Locate the array write: ldarg.0, ldloc.s 4, ldloc.s 5, stelem.i8
            matcher.Start();
            matcher.MatchStartForward(
                new CodeMatch(OpCodes.Ldarg_0),
                new CodeMatch(i => i.IsLdloc()),
                new CodeMatch(i => i.IsLdloc()),
                new CodeMatch(OpCodes.Stelem_I8)
            );

            if (matcher.IsInvalid)
            {
                Log.Error("[MpCompat] Could not find array write instruction in DeferredStackTracingImpl.TraceImpl");
                return instructions;
            }

            var ldlocNum4 = matcher.InstructionAt(1);
            var labels = matcher.Labels;
            matcher.Labels = new List<Label>();

            // If ShouldStopTrace(num4, traceIn) is true, branch directly to method return
            var checkInstructions = new[]
            {
                new CodeInstruction(ldlocNum4.opcode, ldlocNum4.operand) { labels = labels },
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(DeferredStackTracingImplFix), nameof(ShouldStopTrace))),
                new CodeInstruction(OpCodes.Brtrue, exitLabel)
            };

            matcher.Insert(checkInstructions);

            return matcher.InstructionEnumeration();
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, ref int __result)
        {
            if (__exception != null)
            {
                Log.WarningOnce($"[MpCompat] Suppressed exception in DeferredStackTracingImpl.TraceImpl: {__exception}", 71948201);
                __result = 0;
                return null;
            }
            return null;
        }
    }

    /// <summary>
    /// Finalizer guard on <c>Multiplayer.Client.Desyncs.DeferredStackTracing.Postfix</c>.
    /// Because Postfix runs on <see cref="Verse.Rand.get_Int"/> and <see cref="Verse.Rand.get_Value"/>,
    /// any unhandled exception inside it breaks core simulation RNG. This finalizer guarantees that any
    /// tracing error is safely swallowed.
    /// </summary>
    [HarmonyPatch]
    public static class DeferredStackTracingPostfixFix
    {
        [HarmonyPrepare]
        private static bool Prepare()
        {
            return AccessTools.Method("Multiplayer.Client.Desyncs.DeferredStackTracing:Postfix") != null;
        }

        [HarmonyTargetMethod]
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method("Multiplayer.Client.Desyncs.DeferredStackTracing:Postfix");
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception)
        {
            if (__exception != null)
            {
                Log.WarningOnce($"[MpCompat] Suppressed exception in DeferredStackTracing.Postfix: {__exception}", 71948202);
                return null;
            }
            return null;
        }
    }
}
