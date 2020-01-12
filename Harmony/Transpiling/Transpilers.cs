using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib.Tools;
using MonoMod.Utils;

namespace HarmonyLib
{
    /// <summary>A collection of commonly used transpilers</summary>
    public static class Transpilers
    {
        private static readonly Dictionary<int, Delegate> _delegateCache = new Dictionary<int, Delegate>();
        private static int _delegateCounter = 0;

        /// <summary>
        /// Returns an instruction to call the specified delegate.
        /// </summary>
        /// <typeparam name="T">The delegate type to emit.</typeparam>
        /// <param name="action">The delegate to emit.</param>
        /// <returns>The instruction to </returns>
        public static CodeInstruction EmitDelegate<T>(T action) where T : Delegate
        {
            if (action.Method.IsStatic && action.Target == null)
            {
                return new CodeInstruction(OpCodes.Call, action.Method);
            }

            var paramTypes = action.Method.GetParameters().Select(x => x.ParameterType).ToArray();

            var dynamicMethod = new DynamicMethodDefinition(action.Method.Name,
                action.Method.ReturnType,
                paramTypes);

            var il = dynamicMethod.GetILGenerator();

            var targetType = action.Target.GetType();

            var preserveContext = action.Target != null && targetType.GetFields().Any(x => !x.IsStatic);

            if (preserveContext)
            {
                var currentDelegateCounter = _delegateCounter++;

                _delegateCache[currentDelegateCounter] = action;

                var cacheField = AccessTools.Field(typeof(Transpilers), nameof(_delegateCache));

                var getMethod = AccessTools.Method(typeof(Dictionary<int, Delegate>), "get_Item");

                il.Emit(OpCodes.Ldsfld, cacheField);
                il.Emit(OpCodes.Ldc_I4, currentDelegateCounter);
                il.Emit(OpCodes.Callvirt, getMethod);
            }
            else
            {
                if (action.Target == null)
                    il.Emit(OpCodes.Ldnull);
                else
                    il.Emit(OpCodes.Newobj, AccessTools.FirstConstructor(targetType, x => x.GetParameters().Length == 0 && !x.IsStatic));

                il.Emit(OpCodes.Ldftn, action.Method);
                il.Emit(OpCodes.Newobj, AccessTools.Constructor(typeof(T), new[] { typeof(object), typeof(IntPtr) }));
            }


            for (var i = 0; i < paramTypes.Length; i++)
            {
                il.Emit(OpCodes.Ldarg_S, (short)i);
            }

            il.Emit(OpCodes.Callvirt, typeof(T).GetMethod("Invoke"));

            il.Emit(OpCodes.Ret);

            return new CodeInstruction(OpCodes.Call, dynamicMethod.Generate());
        }

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
            yield return new CodeInstruction(OpCodes.Ldc_I4, (int)Logger.LogChannel.Info);
            yield return new CodeInstruction(OpCodes.Ldstr, text);
            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Logger), nameof(Logger.LogText)));
            foreach (var instruction in instructions) yield return instruction;
        }

        // more added soon
    }
}