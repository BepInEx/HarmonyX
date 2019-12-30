using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib.Internal.Util;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using OpCode = Mono.Cecil.Cil.OpCode;
using OpCodes = Mono.Cecil.Cil.OpCodes;

namespace HarmonyLib.Internal.Patching
{
    /// <summary>
    ///     IL manipulator to create Harmony-style patches
    /// </summary>
    internal static class HarmonyManipulator
    {
        /// special parameter names that can be used in prefix and postfix methods
        private static readonly string INSTANCE_PARAM = "__instance";

        private static readonly string ORIGINAL_METHOD_PARAM = "__originalMethod";
        private static readonly string RESULT_VAR = "__result";
        private static readonly string STATE_VAR = "__state";
        private static readonly string EXCEPTION_VAR = "__exception";
        private static readonly string PARAM_INDEX_PREFIX = "__";
        private static readonly string INSTANCE_FIELD_PREFIX = "___";

        private static readonly MethodInfo getMethodMethod =
            typeof(MethodBase).GetMethod("GetMethodFromHandle", new[] {typeof(RuntimeMethodHandle)});


        internal static ILContext.Manipulator Create(MethodBase original, PatchInfo patchInfo)
        {
            // We need to include the original method in order to obtain the patch info during patching
            return il => Manipulate(original, patchInfo, il);
        }

        private static void SortPatches(MethodBase original, PatchInfo patchInfo, out List<MethodInfo> prefixes,
                                        out List<MethodInfo> postfixes, out List<MethodInfo> transpilers,
                                        out List<MethodInfo> finalizers)
        {
            Patch[] prefixesArr, postfixesArr, transpilersArr, finalizersArr;

            // Lock to ensure no more patches are added while we're sorting
            lock (patchInfo)
            {
                prefixesArr = patchInfo.prefixes.ToArray();
                postfixesArr = patchInfo.postfixes.ToArray();
                transpilersArr = patchInfo.transpilers.ToArray();
                finalizersArr = patchInfo.finalizers.ToArray();
            }

            prefixes = prefixesArr.Sort(original);
            postfixes = postfixesArr.Sort(original);
            transpilers = transpilersArr.Sort(original);
            finalizers = finalizersArr.Sort(original);
        }

        private static void Manipulate(MethodBase original, PatchInfo patchInfo, ILContext ctx)
        {
            SortPatches(original, patchInfo, out var sortedPrefixes, out var sortedPostfixes, out var sortedTranspilers,
                        out var sortedFinalizers);

            MakePatched(original, null, ctx, sortedPrefixes, sortedPostfixes, sortedTranspilers, sortedFinalizers);
        }

        /// <summary>Mark method for no inlining</summary>
        /// <param name="method">The method to change</param>
        private static unsafe void MarkForNoInlining(MethodBase method)
        {
            //var methodDef = method.MetadataToken;

            // TODO for now, this only works on mono
            if (Type.GetType("Mono.Runtime") != null)
            {
                var iflags = (ushort*) method.MethodHandle.Value + 1;
                *iflags |= (ushort) MethodImplOptions.NoInlining;
            }
        }

        private static void WriteTranspiledMethod(ILContext ctx, MethodBase original, List<MethodInfo> transpilers)
        {
            if (transpilers.Count == 0)
                return;

            // Create a high-level manipulator for the method
            var manipulator = new ILManipulator(ctx.Body, original);

            // Add in all transpilers
            foreach (var transpilerMethod in transpilers)
                manipulator.AddTranspiler(transpilerMethod);

            // Write new manipulated code to our body
            manipulator.WriteTo(ctx.Body, original);
        }

        private static Instruction MakeReturnLabel(ILContext ctx)
        {
            var resultIns = ctx.IL.Create(OpCodes.Nop);

            foreach (var ins in ctx.Instrs.Where(ins => ins.MatchRet()))
            {
                ins.OpCode = OpCodes.Br;
                ins.Operand = resultIns;
            }

            ctx.IL.Append(resultIns);
            ctx.IL.Emit(OpCodes.Ret);

            return resultIns;
        }

        private static void WritePostfixes(ILContext ctx, MethodBase original,
                                           Dictionary<string, VariableDefinition> variables, List<MethodInfo> postfixes)
        {
            // Postfix layout:
            // Make return value (if needed) into a variable
            // If method has return value, store the current stack value into it (since the value on the stack is the return value)
            // Call postfixes that modify return values by __return
            // Call postfixes that modify return values by chaining

            if (postfixes.Count == 0)
                return;

            if (!variables.TryGetValue(RESULT_VAR, out var returnValueVar))
            {
                var retVal = AccessTools.GetReturnedType(original);
                returnValueVar = variables[RESULT_VAR] = retVal == typeof(void) ? null : ctx.IL.DeclareVariable(retVal);
            }

            // Get the last instruction (expected to be `ret`)
            var ins = ctx.Instrs[ctx.Instrs.Count - 1];

            if (returnValueVar != null)
                ctx.IL.EmitBefore(ins, OpCodes.Stloc, returnValueVar);

            foreach (var postfix in postfixes.Where(p => p.ReturnType == typeof(void)))
            {
                EmitCallParameter(ins, ctx, original, postfix, variables, true);
                ctx.IL.EmitBefore(ins, OpCodes.Call, postfix);
            }

            // Load the result for the final time, the chained postfixes will handle the rest
            if (returnValueVar != null)
                ctx.IL.EmitBefore(ins, OpCodes.Ldloc, returnValueVar);

            // If postfix returns a value, it must be chainable
            // The first param is always the return of the previous
            foreach (var postfix in postfixes.Where(p => p.ReturnType != typeof(void)))
            {
                EmitCallParameter(ins, ctx, original, postfix, variables, true);
                ctx.IL.EmitBefore(ins, OpCodes.Call, postfix);

                var firstParam = postfix.GetParameters().FirstOrDefault();

                if (firstParam == null || postfix.ReturnType != firstParam.ParameterType)
                {
                    if (firstParam != null)
                        throw new Exception(
                            $"Return type of pass through postfix {postfix} does not match type of its first parameter");
                    // TODO: Make the error more understandable
                    throw new Exception($"Postfix patch {postfix} must have a \"void\" return type");
                }
            }
        }

        private static void WritePrefixes(ILContext ctx, MethodBase original, Instruction returnLabel,
                                          Dictionary<string, VariableDefinition> variables, List<MethodInfo> prefixes)
        {
            // Prefix layout:
            // Make return value (if needed) into a variable
            // Call prefixes
            // If method returns a value, add additional logic to allow skipping original method

            if (prefixes.Count == 0)
                return;

            if (!variables.TryGetValue(RESULT_VAR, out var returnValueVar))
            {
                var retVal = AccessTools.GetReturnedType(original);
                returnValueVar = variables[RESULT_VAR] = retVal == typeof(void) ? null : ctx.IL.DeclareVariable(retVal);
            }

            // Flag to check if the orignal method should be run (or was run)
            // Only present if method has a return value and there are prefixes that modify control flow
            var runOriginal = returnValueVar != null && prefixes.Any(p => p.ReturnType == typeof(bool))
                ? ctx.IL.DeclareVariable(typeof(bool))
                : null;

            // If runOriginal flag exists, we need to add more logic to the method end
            var postProcessTarget = runOriginal != null ? ctx.IL.Create(OpCodes.Nop) : returnLabel;

            var ins = ctx.Instrs.First(); // Grab the instruction from the top of the method
            foreach (var prefix in prefixes)
            {
                EmitCallParameter(ins, ctx, original, prefix, variables, false);
                ctx.IL.EmitBefore(ins, OpCodes.Call, prefix);

                if (!AccessTools.IsVoid(prefix.ReturnType))
                {
                    if (prefix.ReturnType != typeof(bool))
                        throw new Exception(
                            $"Prefix patch {prefix} has not \"bool\" or \"void\" return type: {prefix.ReturnType}");

                    if (runOriginal != null)
                    {
                        ctx.IL.EmitBefore(ins, OpCodes.Dup);
                        ctx.IL.EmitBefore(ins, OpCodes.Stloc, runOriginal);
                    }

                    ctx.IL.EmitBefore(ins, OpCodes.Brfalse, postProcessTarget);
                }
            }

            if (runOriginal == null)
                return;

            // Finally, ensure the stack is consistent when branching to `ret`:
            // If skip original method => return value not on stack => do nothing
            // If run original method =>  return value on stack     => pop return value from stack
            // Finally, load return value onto stack

            ins = ctx.Instrs[ctx.Instrs.Count - 1];
            ctx.IL.EmitBefore(ins, OpCodes.Stloc, returnValueVar);
            ctx.IL.InsertBefore(ins, postProcessTarget);
            ctx.IL.EmitBefore(ins, OpCodes.Ldloc, returnValueVar);
        }

        private static void WriteFinalizers(ILContext ctx, MethodBase original,
                                            Dictionary<string, VariableDefinition> variables,
                                            List<MethodInfo> finalizers)
        {
            // Finalizer layout:
            // Create __exception variable to store exception info and a skip flag
            // Wrap the whole method into a try/catch
            // Call finalizers at the end of method (simulate `finally`)
            // If __exception got set, throw it
            // Begin catch block
            // Store exception into __exception
            // If skip flag is set, skip finalizers
            // Call finalizers
            // If __exception is still set, rethrow (if new exception set, otherwise throw the new exception)
            // End catch block

            // We do this if CecilILGenerator since it's much, much easier

            if (finalizers.Count == 0)
                return;

            if (!variables.TryGetValue(RESULT_VAR, out var returnValueVar))
            {
                var retVal = AccessTools.GetReturnedType(original);
                returnValueVar = variables[RESULT_VAR] = retVal == typeof(void) ? null : ctx.IL.DeclareVariable(retVal);
            }

            // Create variables
            var skipFinalizersVar = ctx.IL.DeclareVariable(typeof(bool));
            variables[EXCEPTION_VAR] = ctx.IL.DeclareVariable(typeof(Exception));

            // Start main exception block
            var mainExceptionBlock = ctx.IL.BeginExceptionBlock(ctx.Instrs[0]);

            // Grab the `ret` instruction and start putting stuff before it
            var ins = ctx.Instrs[ctx.Instrs.Count - 1];

            bool WriteFinalizerCalls(bool suppressExceptions)
            {
                var canRethrow = true;

                foreach (var finalizer in finalizers)
                {
                    var startIns = ctx.IL.EmitBefore(ins, OpCodes.Nop);

                    EmitCallParameter(ins, ctx, original, finalizer, variables, false);
                    ctx.IL.EmitBefore(ins, OpCodes.Call, finalizer);

                    if (finalizer.ReturnType != typeof(void))
                    {
                        ctx.IL.EmitBefore(ins, OpCodes.Stloc, variables[EXCEPTION_VAR]);
                        canRethrow = false;
                    }

                    if (suppressExceptions)
                    {
                        var block = ctx.IL.BeginExceptionBlock(startIns);
                        var catchHandler = ctx.IL.BeginHandler(ins, block, ExceptionHandlerType.Catch);
                        catchHandler.CatchType = ctx.Import(typeof(object));
                        ctx.IL.EmitBefore(ins, OpCodes.Pop);
                        ctx.IL.EndExceptionBlock(ins, block);
                    }
                }

                return canRethrow;
            }

            // First, store potential result into a variable and empty the stack
            if (returnValueVar != null)
                ctx.IL.EmitBefore(ins, OpCodes.Stloc, returnValueVar);

            // Write finalizers inside the `try`
            WriteFinalizerCalls(false);

            // Mark finalizers as skipped so they won't rerun
            ctx.IL.EmitBefore(ins, OpCodes.Ldc_I4_1);
            ctx.IL.EmitBefore(ins, OpCodes.Stloc, skipFinalizersVar);

            var skipExceptionsTarget = ctx.IL.Create(OpCodes.Nop);
            // If __exception is not null, throw
            ctx.IL.EmitBefore(ins, OpCodes.Ldloc, variables[EXCEPTION_VAR]);
            ctx.IL.EmitBefore(ins, OpCodes.Brfalse, skipExceptionsTarget);
            ctx.IL.EmitBefore(ins, OpCodes.Ldloc, variables[EXCEPTION_VAR]);
            ctx.IL.EmitBefore(ins, OpCodes.Throw);
            ctx.IL.InsertBefore(ins, skipExceptionsTarget);

            // Begin a generic `catch(object o)` here and capture exception into __exception
            var mainHandler = ctx.IL.BeginHandler(ins, mainExceptionBlock, ExceptionHandlerType.Catch);
            mainHandler.CatchType = ctx.Import(typeof(Exception));
            ctx.IL.EmitBefore(ins, OpCodes.Stloc, variables[EXCEPTION_VAR]);

            // Call finalizers or skip them if needed
            ctx.IL.EmitBefore(ins, OpCodes.Ldloc, skipFinalizersVar);
            var finalizersEndTarget = ctx.IL.Create(OpCodes.Nop);
            ctx.IL.EmitBefore(ins, OpCodes.Brtrue, finalizersEndTarget);

            var rethrowPossible = WriteFinalizerCalls(true);

            ctx.IL.InsertBefore(ins, finalizersEndTarget);

            // Possibly rethrow if __exception is still not null (i.e. suppressed)
            skipExceptionsTarget = ctx.IL.Create(OpCodes.Nop);
            ctx.IL.EmitBefore(ins, OpCodes.Ldloc, variables[EXCEPTION_VAR]);
            ctx.IL.EmitBefore(ins, OpCodes.Brfalse, skipExceptionsTarget);
            if (rethrowPossible)
            {
                ctx.IL.EmitBefore(ins, OpCodes.Rethrow);
            }
            else
            {
                ctx.IL.EmitBefore(ins, OpCodes.Ldloc, variables[EXCEPTION_VAR]);
                ctx.IL.EmitBefore(ins, OpCodes.Throw);
            }

            ctx.IL.InsertBefore(ins, skipExceptionsTarget);

            // end the main exception block
            ctx.IL.EndExceptionBlock(ins, mainExceptionBlock);

            // Push return value back to the stack
            ctx.IL.EmitBefore(ins, OpCodes.Ldloc, returnValueVar);
        }

        private static void MakePatched(MethodBase original, MethodBase source, ILContext ctx,
                                        List<MethodInfo> prefixes, List<MethodInfo> postfixes,
                                        List<MethodInfo> transpilers, List<MethodInfo> finalizers)
        {
            try
            {
                if (original == null)
                    throw new ArgumentException(nameof(original));

                MarkForNoInlining(original);

                WriteTranspiledMethod(ctx, original, transpilers);

                // If no need to wrap anything, we're basically done!
                if (prefixes.Count + postfixes.Count + finalizers.Count == 0)
                    return;

                var returnLabel = MakeReturnLabel(ctx);
                var variables = new Dictionary<string, VariableDefinition>();

                // Collect state variables
                foreach (var nfix in prefixes.Union(postfixes).Union(finalizers))
                    if (nfix.DeclaringType != null && variables.ContainsKey(nfix.DeclaringType.FullName) == false)
                        foreach (var patchParam in nfix
                                                   .GetParameters().Where(patchParam => patchParam.Name == STATE_VAR))
                            variables[nfix.DeclaringType.FullName] = ctx.IL.DeclareVariable(patchParam.ParameterType.OpenRefType()); // Fix possible reftype

                WritePrefixes(ctx, original, returnLabel, variables, prefixes);
                WritePostfixes(ctx, original, variables, postfixes);
                WriteFinalizers(ctx, original, variables, finalizers);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        private static void EmitCallParameter(Instruction before, ILContext ctx, MethodBase original, MethodInfo patch,
                                              Dictionary<string, VariableDefinition> variables,
                                              bool allowFirsParamPassthrough)
        {
            var il = ctx.IL;

            var isInstance = original.IsStatic == false;
            var originalParameters = original.GetParameters();
            var originalParameterNames = originalParameters.Select(p => p.Name).ToArray();

            // check for passthrough using first parameter (which must have same type as return type)
            var parameters = patch.GetParameters().ToList();
            if (allowFirsParamPassthrough && patch.ReturnType != typeof(void) && parameters.Count > 0 &&
                parameters[0].ParameterType == patch.ReturnType)
                parameters.RemoveRange(0, 1);

            foreach (var patchParam in parameters)
            {
                if (patchParam.Name == ORIGINAL_METHOD_PARAM)
                {
                    if (original is ConstructorInfo constructorInfo)
                    {
                        il.EmitBefore(before, OpCodes.Ldtoken, constructorInfo);
                        il.EmitBefore(before, OpCodes.Call, getMethodMethod);
                        continue;
                    }

                    if (original is MethodInfo methodInfo)
                    {
                        il.EmitBefore(before, OpCodes.Ldtoken, methodInfo);
                        il.EmitBefore(before, OpCodes.Call, getMethodMethod);
                        continue;
                    }

                    il.EmitBefore(before, OpCodes.Ldnull);
                    continue;
                }

                if (patchParam.Name == INSTANCE_PARAM)
                {
                    if (original.IsStatic)
                    {
                        il.EmitBefore(before, OpCodes.Ldnull);
                    }
                    else
                    {
                        var instanceIsRef = AccessTools.IsStruct(original.DeclaringType);
                        var parameterIsRef = patchParam.ParameterType.IsByRef;
                        if (instanceIsRef == parameterIsRef)
                            il.EmitBefore(before, OpCodes.Ldarg_0);
                        if (instanceIsRef && parameterIsRef == false)
                        {
                            il.EmitBefore(before, OpCodes.Ldarg_0);
                            il.EmitBefore(before, OpCodes.Ldobj, original.DeclaringType);
                        }

                        if (instanceIsRef == false && parameterIsRef)
                            il.EmitBefore(before, OpCodes.Ldarga, 0);
                    }

                    continue;
                }

                if (patchParam.Name.StartsWith(INSTANCE_FIELD_PREFIX, StringComparison.Ordinal))
                {
                    var fieldName = patchParam.Name.Substring(INSTANCE_FIELD_PREFIX.Length);
                    FieldInfo fieldInfo;
                    if (fieldName.All(char.IsDigit))
                    {
                        fieldInfo = AccessTools.DeclaredField(original.DeclaringType, int.Parse(fieldName));
                        if (fieldInfo == null)
                            throw new ArgumentException(
                                "No field found at given index in class " + original.DeclaringType.FullName, fieldName);
                    }
                    else
                    {
                        fieldInfo = AccessTools.DeclaredField(original.DeclaringType, fieldName);
                        if (fieldInfo == null)
                            throw new ArgumentException(
                                "No such field defined in class " + original.DeclaringType.FullName, fieldName);
                    }

                    if (fieldInfo.IsStatic)
                    {
                        il.EmitBefore(before, patchParam.ParameterType.IsByRef ? OpCodes.Ldsflda : OpCodes.Ldsfld,
                                      fieldInfo);
                    }
                    else
                    {
                        il.EmitBefore(before, OpCodes.Ldarg_0);
                        il.EmitBefore(before, patchParam.ParameterType.IsByRef ? OpCodes.Ldflda : OpCodes.Ldfld,
                                      fieldInfo);
                    }

                    continue;
                }

                // state is special too since each patch has its own local var
                if (patchParam.Name == STATE_VAR)
                {
                    if (variables.TryGetValue(patch.DeclaringType.FullName, out var stateVar))
                        il.EmitBefore(before, patchParam.ParameterType.IsByRef ? OpCodes.Ldloca : OpCodes.Ldloc,
                                      stateVar);
                    else
                        il.EmitBefore(before, OpCodes.Ldnull);
                    continue;
                }

                // treat __result var special
                if (patchParam.Name == RESULT_VAR)
                {
                    var returnType = AccessTools.GetReturnedType(original);
                    if (returnType == typeof(void))
                        throw new Exception("Cannot get result from void method " + original.FullDescription());
                    var resultType = patchParam.ParameterType;
                    if (resultType.IsByRef)
                        resultType = resultType.GetElementType();
                    if (resultType.IsAssignableFrom(returnType) == false)
                        throw new Exception("Cannot assign method return type " + returnType.FullName + " to " +
                                            RESULT_VAR + " type " + resultType.FullName + " for method " +
                                            original.FullDescription());
                    il.EmitBefore(before, patchParam.ParameterType.IsByRef ? OpCodes.Ldloca : OpCodes.Ldloc,
                                  variables[RESULT_VAR]);
                    continue;
                }

                // any other declared variables
                if (variables.TryGetValue(patchParam.Name, out var localBuilder))
                {
                    il.EmitBefore(before, patchParam.ParameterType.IsByRef ? OpCodes.Ldloca : OpCodes.Ldloc,
                                  localBuilder);
                    continue;
                }

                int idx;
                if (patchParam.Name.StartsWith(PARAM_INDEX_PREFIX, StringComparison.Ordinal))
                {
                    var val = patchParam.Name.Substring(PARAM_INDEX_PREFIX.Length);
                    if (!int.TryParse(val, out idx))
                        throw new Exception("Parameter " + patchParam.Name + " does not contain a valid index");
                    if (idx < 0 || idx >= originalParameters.Length)
                        throw new Exception("No parameter found at index " + idx);
                }
                else
                {
                    idx = GetArgumentIndex(patch, originalParameterNames, patchParam);
                    if (idx == -1)
                        throw new Exception("Parameter \"" + patchParam.Name + "\" not found in method " +
                                            original.FullDescription());
                }

                //   original -> patch     opcode
                // --------------------------------------
                // 1 normal   -> normal  : LDARG
                // 2 normal   -> ref/out : LDARGA
                // 3 ref/out  -> normal  : LDARG, LDIND_x
                // 4 ref/out  -> ref/out : LDARG
                //
                var originalIsNormal = originalParameters[idx].IsOut == false &&
                                       originalParameters[idx].ParameterType.IsByRef == false;
                var patchIsNormal = patchParam.IsOut == false && patchParam.ParameterType.IsByRef == false;
                var patchArgIndex = idx + (isInstance ? 1 : 0);

                // Case 1 + 4
                if (originalIsNormal == patchIsNormal)
                {
                    il.EmitBefore(before, OpCodes.Ldarg, patchArgIndex);
                    continue;
                }

                // Case 2
                if (originalIsNormal && patchIsNormal == false)
                {
                    il.EmitBefore(before, OpCodes.Ldarga, patchArgIndex);
                    continue;
                }

                // Case 3
                il.EmitBefore(before, OpCodes.Ldarg, patchArgIndex);
                il.EmitBefore(before, GetIndOpcode(originalParameters[idx].ParameterType));
            }
        }

        private static OpCode GetIndOpcode(Type type)
        {
            if (type.IsEnum)
                return OpCodes.Ldind_I4;

            if (type == typeof(float)) return OpCodes.Ldind_R4;
            if (type == typeof(double)) return OpCodes.Ldind_R8;

            if (type == typeof(byte)) return OpCodes.Ldind_U1;
            if (type == typeof(ushort)) return OpCodes.Ldind_U2;
            if (type == typeof(uint)) return OpCodes.Ldind_U4;
            if (type == typeof(ulong)) return OpCodes.Ldind_I8;

            if (type == typeof(sbyte)) return OpCodes.Ldind_I1;
            if (type == typeof(short)) return OpCodes.Ldind_I2;
            if (type == typeof(int)) return OpCodes.Ldind_I4;
            if (type == typeof(long)) return OpCodes.Ldind_I8;

            return OpCodes.Ldind_Ref;
        }

        private static HarmonyArgument[] AllHarmonyArguments(object[] attributes)
        {
            return attributes.Select(attr =>
            {
                if (attr.GetType().Name != nameof(HarmonyArgument)) return null;
                return AccessTools.MakeDeepCopy<HarmonyArgument>(attr);
            }).Where(harg => harg != null).ToArray();
        }

        private static HarmonyArgument GetArgumentAttribute(this ParameterInfo parameter)
        {
            var attributes = parameter.GetCustomAttributes(false);
            return AllHarmonyArguments(attributes).FirstOrDefault();
        }

        private static HarmonyArgument[] GetArgumentAttributes(this MethodInfo method)
        {
            if (method == null || method is DynamicMethod)
                return default;

            var attributes = method.GetCustomAttributes(false);
            return AllHarmonyArguments(attributes);
        }

        private static HarmonyArgument[] GetArgumentAttributes(this Type type)
        {
            var attributes = type.GetCustomAttributes(false);
            return AllHarmonyArguments(attributes);
        }

        private static string GetOriginalArgumentName(this ParameterInfo parameter, string[] originalParameterNames)
        {
            var attribute = parameter.GetArgumentAttribute();

            if (attribute == null)
                return null;

            if (string.IsNullOrEmpty(attribute.OriginalName) == false)
                return attribute.OriginalName;

            if (attribute.Index >= 0 && attribute.Index < originalParameterNames.Length)
                return originalParameterNames[attribute.Index];

            return null;
        }

        private static string GetOriginalArgumentName(HarmonyArgument[] attributes, string name,
                                                      string[] originalParameterNames)
        {
            if ((attributes?.Length ?? 0) <= 0)
                return null;

            var attribute = attributes.SingleOrDefault(p => p.NewName == name);
            if (attribute == null)
                return null;

            if (string.IsNullOrEmpty(attribute.OriginalName) == false)
                return attribute.OriginalName;

            if (originalParameterNames != null && attribute.Index >= 0 &&
                attribute.Index < originalParameterNames.Length)
                return originalParameterNames[attribute.Index];

            return null;
        }

        private static string GetOriginalArgumentName(this MethodInfo method, string[] originalParameterNames,
                                                      string name)
        {
            var argumentName = GetOriginalArgumentName(method?.GetArgumentAttributes(), name, originalParameterNames);
            if (argumentName != null)
                return argumentName;

            argumentName = GetOriginalArgumentName(method?.DeclaringType?.GetArgumentAttributes(), name,
                                                   originalParameterNames);
            if (argumentName != null)
                return argumentName;

            return name;
        }

        private static int GetArgumentIndex(MethodInfo patch, string[] originalParameterNames, ParameterInfo patchParam)
        {
            if (patch is DynamicMethod)
                return Array.IndexOf(originalParameterNames, patchParam.Name);

            var originalName = patchParam.GetOriginalArgumentName(originalParameterNames);
            if (originalName != null)
                return Array.IndexOf(originalParameterNames, originalName);

            originalName = patch.GetOriginalArgumentName(originalParameterNames, patchParam.Name);
            if (originalName != null)
                return Array.IndexOf(originalParameterNames, originalName);

            return -1;
        }
    }
}