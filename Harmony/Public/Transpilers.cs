using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace HarmonyLib
{
    /// <summary>A collection of commonly used transpilers</summary>
    public static class Transpilers
    {
        /// <summary>A transpiler that replaces all occurrences of a given method with another one</summary>
        /// <param name="instructions">The instructions to act on</param>
        /// <param name="from">Method or constructor to search for</param>
        /// <param name="to">Method or constructor to replace with</param>
        /// <returns>Modified instructions</returns>
        ///
        public static IEnumerable<CodeInstruction> MethodReplacer(this IEnumerable<CodeInstruction> instructions,
                                                                  MethodBase from, MethodBase to)
        {
            if (from == null)
                throw new ArgumentException("Unexpected null argument", nameof(from));
            if (to == null)
                throw new ArgumentException("Unexpected null argument", nameof(to));

            foreach (var instruction in instructions)
            {
                var method = instruction.operand as MethodBase;
                if (method == from)
                {
                    instruction.opcode = to.IsConstructor ? OpCodes.Newobj : OpCodes.Call;
                    instruction.operand = to;
                }

                yield return instruction;
            }
        }

        /// <summary>A transpiler that alters instructions that match a predicate by calling an action</summary>
        /// <param name="instructions">The instructions to act on</param>
        /// <param name="predicate">A predicate selecting the instructions to change</param>
        /// <param name="action">An action to apply to matching instructions</param>
        /// <returns>Modified instructions</returns>
        ///
        public static IEnumerable<CodeInstruction> Manipulator(this IEnumerable<CodeInstruction> instructions,
                                                               Func<CodeInstruction, bool> predicate,
                                                               Action<CodeInstruction> action)
        {
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            return instructions.Select(instruction =>
            {
                if (predicate(instruction))
                    action(instruction);
                return instruction;
            }).AsEnumerable();
        }

        /// <summary>A transpiler that logs a text at the beginning of the method</summary>
        /// <param name="instructions">The instructions to act on</param>
        /// <param name="text">The log text</param>
        /// <returns>Modified instructions</returns>
        ///
        public static IEnumerable<CodeInstruction> DebugLogger(this IEnumerable<CodeInstruction> instructions,
                                                               string text)
        {
            yield return new CodeInstruction(OpCodes.Ldstr, text);
            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(FileLog), nameof(FileLog.Log)));
            foreach (var instruction in instructions) yield return instruction;
        }

        // more added soon
    }
}