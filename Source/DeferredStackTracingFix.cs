using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace Multiplayer.Compat
{
    /// <summary>
    /// Fixes an IndexOutOfRangeException in Multiplayer's DeferredStackTracingImpl.TraceImpl.
    /// When stack frames have nameHash == 0 (e.g. dynamic wrappers or inlined frames), the frame counter
    /// is not incremented while the raw array index continues incrementing, causing traceIn[num4] to overflow
    /// the 32-element buffer.
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
                Log.Error("MPCompat :: Could not find exit instruction in DeferredStackTracingImpl.TraceImpl");
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
                Log.Error("MPCompat :: Could not find array write instruction in DeferredStackTracingImpl.TraceImpl");
                return instructions;
            }

            var ldlocNum4 = matcher.InstructionAt(1);
            var labels = matcher.Labels;
            matcher.Labels = new List<Label>();

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
                __result = 0;
                return null;
            }
            return null;
        }
    }

    /// <summary>
    /// Finalizer guard on Multiplayer.Client.Desyncs.DeferredStackTracing.Postfix.
    /// Because Postfix runs on Verse.Rand.get_Int and Verse.Rand.get_Value, any unhandled exception inside it
    /// breaks core simulation RNG. This finalizer ensures any tracing error is suppressed.
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
                return null;
            }
            return null;
        }
    }
}
