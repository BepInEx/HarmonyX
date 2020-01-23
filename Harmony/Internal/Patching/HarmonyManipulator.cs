using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib.Internal.Util;
using HarmonyLib.Tools;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;
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

        internal static void Manipulate(MethodBase original, PatchInfo patchInfo, ILContext ctx)
        {
            SortPatches(original, patchInfo, out var sortedPrefixes, out var sortedPostfixes, out var sortedTranspilers,
                        out var sortedFinalizers);

            Logger.Log(Logger.LogChannel.Info, () =>
            {
                var sb = new StringBuilder();

                sb.AppendLine($"Patching {original.GetID()} with {sortedPrefixes.Count} prefixes, {sortedPostfixes.Count} postfixes, {sortedTranspilers.Count} transpilers, {sortedFinalizers.Count} finalizers");

                void Print(List<MethodInfo> list, string type)
                {
                    if (list.Count == 0)
                        return;
                    sb.AppendLine($"{list.Count} {type}:");
                    foreach (var fix in list)
                        sb.AppendLine($"* {fix.GetID()}");
                }

                Print(sortedPrefixes, "prefixes");
                Print(sortedPostfixes, "postfixes");
                Print(sortedTranspilers, "transpilers");
                Print(sortedFinalizers, "finalizers");

                return sb.ToString();
            });

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

            Logger.Log(Logger.LogChannel.Info, () => $"Transpiling {original.GetID()}");

            // Create a high-level manipulator for the method
            var manipulator = new ILManipulator(ctx.Body);

            // Add in all transpilers
            foreach (var transpilerMethod in transpilers)
                manipulator.AddTranspiler(transpilerMethod);

            // Write new manipulated code to our body
            manipulator.WriteTo(ctx.Body, original);
        }

        private static ILEmitter.Label MakeReturnLabel(ILEmitter il)
        {
            // We replace all `ret`s with a simple branch to force potential execution of post-original code

            // Create a helper label as well
            // We mark the label as not emitted so that potential postfix code can mark it
            var resultLabel = il.DeclareLabel();
            resultLabel.emitted = false;
            resultLabel.instruction = Instruction.Create(OpCodes.Ret);

            foreach (var ins in il.IL.Body.Instructions.Where(ins => ins.MatchRet()))
            {
                ins.OpCode = OpCodes.Br;
                ins.Operand = resultLabel.instruction;
                resultLabel.targets.Add(ins);
            }

            // Already append `ret` for other code to use as emitBefore point
            il.IL.Append(resultLabel.instruction);
            return resultLabel;
        }

        private static void WritePostfixes(ILEmitter il, MethodBase original, ILEmitter.Label returnLabel,
                                           Dictionary<string, VariableDefinition> variables, List<MethodInfo> postfixes)
        {
            // Postfix layout:
            // Make return value (if needed) into a variable
            // If method has return value, store the current stack value into it (since the value on the stack is the return value)
            // Call postfixes that modify return values by __return
            // Call postfixes that modify return values by chaining

            if (postfixes.Count == 0)
                return;

            Logger.Log(Logger.LogChannel.Info, () => "Writing postfixes");

            // Get the last instruction (expected to be `ret`)
            il.emitBefore = il.IL.Body.Instructions[il.IL.Body.Instructions.Count - 1];

            // Mark the original method return label here
            il.MarkLabel(returnLabel);

            if (!variables.TryGetValue(RESULT_VAR, out var returnValueVar))
            {
                var retVal = AccessTools.GetReturnedType(original);
                returnValueVar = variables[RESULT_VAR] = retVal == typeof(void) ? null : il.DeclareVariable(retVal);
            }

            if (returnValueVar != null)
                il.Emit(OpCodes.Stloc, returnValueVar);

            foreach (var postfix in postfixes.Where(p => p.ReturnType == typeof(void)))
            {
                EmitCallParameter(il, original, postfix, variables, true);
                il.Emit(OpCodes.Call, postfix);
            }

            // Load the result for the final time, the chained postfixes will handle the rest
            if (returnValueVar != null)
                il.Emit(OpCodes.Ldloc, returnValueVar);

            // If postfix returns a value, it must be chainable
            // The first param is always the return of the previous
            foreach (var postfix in postfixes.Where(p => p.ReturnType != typeof(void)))
            {
                EmitCallParameter(il, original, postfix, variables, true);
                il.Emit(OpCodes.Call, postfix);

                var firstParam = postfix.GetParameters().FirstOrDefault();

                if (firstParam == null || postfix.ReturnType != firstParam.ParameterType)
                {
                    if (firstParam != null)
                        throw new InvalidHarmonyPatchArgumentException(
                            $"Return type of pass through postfix {postfix.GetID()} does not match type of its first parameter", original, postfix);
                    throw new InvalidHarmonyPatchArgumentException($"Postfix patch {postfix.GetID()} must have `void` as return type", original, postfix);
                }
            }
        }

        private static void WritePrefixes(ILEmitter il, MethodBase original, ILEmitter.Label returnLabel,
                                          Dictionary<string, VariableDefinition> variables, List<MethodInfo> prefixes)
        {
            // Prefix layout:
            // Make return value (if needed) into a variable
            // Call prefixes
            // If method returns a value, add additional logic to allow skipping original method

            if (prefixes.Count == 0)
                return;

            Logger.Log(Logger.LogChannel.Info, () => "Writing prefixes");

            // Start emitting at the start
            il.emitBefore = il.IL.Body.Instructions[0];

            if (!variables.TryGetValue(RESULT_VAR, out var returnValueVar))
            {
                var retVal = AccessTools.GetReturnedType(original);
                returnValueVar = variables[RESULT_VAR] = retVal == typeof(void) ? null : il.DeclareVariable(retVal);
            }

            // Flag to check if the orignal method should be run (or was run)
            // Only present if method has a return value and there are prefixes that modify control flow
            var runOriginal = prefixes.Any(p => p.ReturnType == typeof(bool))
                ? il.DeclareVariable(typeof(bool))
                : null;

            // Init runOriginal to true
            if (runOriginal != null)
            {
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Stloc, runOriginal);
            }

            // If runOriginal flag exists, we need to add more logic to the method end
            var postProcessTarget = returnValueVar != null ? il.DeclareLabel() : returnLabel;

            foreach (var prefix in prefixes)
            {
                EmitCallParameter(il, original, prefix, variables, false);
                il.Emit(OpCodes.Call, prefix);

                if (!AccessTools.IsVoid(prefix.ReturnType))
                {
                    if (prefix.ReturnType != typeof(bool))
                        throw new InvalidHarmonyPatchArgumentException(
                            $"Prefix patch {prefix.GetID()} has return type {prefix.ReturnType}, but only `bool` or `void` are permitted", original, prefix);

                    if (runOriginal != null)
                    {
                        // AND the current runOriginal to return value of the method (if any)
                        il.Emit(OpCodes.Ldloc, runOriginal);
                        il.Emit(OpCodes.And);
                        il.Emit(OpCodes.Stloc, runOriginal);
                    }
                }
            }

            if (runOriginal == null)
                return;

            // If runOriginal is false, branch automatically to the end
            il.Emit(OpCodes.Ldloc, runOriginal);
            il.Emit(OpCodes.Brfalse, postProcessTarget);

            if (returnValueVar == null)
                return;

            // Finally, load return value onto stack at the end
            il.emitBefore = il.IL.Body.Instructions[il.IL.Body.Instructions.Count - 1];
            il.MarkLabel(postProcessTarget);
            il.Emit(OpCodes.Ldloc, returnValueVar);
        }

        private static void WriteFinalizers(ILEmitter il, MethodBase original, ILEmitter.Label returnLabel,
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

            if (finalizers.Count == 0)
                return;

            Logger.Log(Logger.LogChannel.Info, () => "Writing finalizers");

            il.emitBefore = il.IL.Body.Instructions[il.IL.Body.Instructions.Count - 1];

            // Mark the original method return label here if it hasn't been yet
            il.MarkLabel(returnLabel);

            if (!variables.TryGetValue(RESULT_VAR, out var returnValueVar))
            {
                var retVal = AccessTools.GetReturnedType(original);
                returnValueVar = variables[RESULT_VAR] = retVal == typeof(void) ? null : il.DeclareVariable(retVal);
            }

            // Create variables to hold custom exception
            var skipFinalizersVar = il.DeclareVariable(typeof(bool));
            variables[EXCEPTION_VAR] = il.DeclareVariable(typeof(Exception));

            // Start main exception block
            var mainBlock = il.BeginExceptionBlock(il.DeclareLabelFor(il.IL.Body.Instructions[0]));

            bool WriteFinalizerCalls(bool suppressExceptions)
            {
                var canRethrow = true;

                foreach (var finalizer in finalizers)
                {
                    var start = il.DeclareLabel();
                    il.MarkLabel(start);

                    EmitCallParameter(il, original, finalizer, variables, false);
                    il.Emit(OpCodes.Call, finalizer);

                    if (finalizer.ReturnType != typeof(void))
                    {
                        il.Emit(OpCodes.Stloc, variables[EXCEPTION_VAR]);
                        canRethrow = false;
                    }

                    if (suppressExceptions)
                    {
                        var exBlock = il.BeginExceptionBlock(start);

                        il.BeginHandler(exBlock, ExceptionHandlerType.Catch, typeof(object));
                        il.Emit(OpCodes.Pop);
                        il.EndExceptionBlock(exBlock);
                    }
                }

                return canRethrow;
            }

            // First, store potential result into a variable and empty the stack
            if (returnValueVar != null)
                il.Emit(OpCodes.Stloc, returnValueVar);

            // Write finalizers inside the `try`
            WriteFinalizerCalls(false);

            // Mark finalizers as skipped so they won't rerun
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Stloc, skipFinalizersVar);

            // If __exception is not null, throw
            var skipLabel = il.DeclareLabel();
            il.Emit(OpCodes.Ldloc, variables[EXCEPTION_VAR]);
            il.Emit(OpCodes.Brfalse, skipLabel);
            il.Emit(OpCodes.Ldloc, variables[EXCEPTION_VAR]);
            il.Emit(OpCodes.Throw);
            il.MarkLabel(skipLabel);

            // Begin a generic `catch(Exception o)` here and capture exception into __exception
            il.BeginHandler(mainBlock, ExceptionHandlerType.Catch, typeof(Exception));
            il.Emit(OpCodes.Stloc, variables[EXCEPTION_VAR]);

            // Call finalizers or skip them if needed
            il.Emit(OpCodes.Ldloc, skipFinalizersVar);
            var postFinalizersLabel = il.DeclareLabel();
            il.Emit(OpCodes.Brtrue, postFinalizersLabel);

            var rethrowPossible = WriteFinalizerCalls(true);

            il.MarkLabel(postFinalizersLabel);

            // Possibly rethrow if __exception is still not null (i.e. suppressed)
            skipLabel = il.DeclareLabel();
            il.Emit(OpCodes.Ldloc, variables[EXCEPTION_VAR]);
            il.Emit(OpCodes.Brfalse, skipLabel);
            if (rethrowPossible)
            {
                il.Emit(OpCodes.Rethrow);
            }
            else
            {
                il.Emit(OpCodes.Ldloc, variables[EXCEPTION_VAR]);
                il.Emit(OpCodes.Throw);
            }

            il.MarkLabel(skipLabel);
            il.EndExceptionBlock(mainBlock);

            if (returnValueVar != null)
                il.Emit(OpCodes.Ldloc, returnValueVar);
        }

        private static void MakePatched(MethodBase original, MethodBase source, ILContext ctx,
                                        List<MethodInfo> prefixes, List<MethodInfo> postfixes,
                                        List<MethodInfo> transpilers, List<MethodInfo> finalizers)
        {
            try
            {
                if (original == null)
                    throw new ArgumentException(nameof(original));

                Logger.Log(Logger.LogChannel.Info, () => $"Running ILHook manipulator on {original.GetID()}");

                MarkForNoInlining(original);

                WriteTranspiledMethod(ctx, original, transpilers);

                // If no need to wrap anything, we're basically done!
                if (prefixes.Count + postfixes.Count + finalizers.Count == 0)
                {
                    Logger.Log(Logger.LogChannel.IL, () => $"Generated patch ({ctx.Method.FullName}):\n{ctx.Body.ToILDasmString()}");
                    return;
                }

                var il = new ILEmitter(ctx.IL);
                var returnLabel = MakeReturnLabel(il);
                var variables = new Dictionary<string, VariableDefinition>();

                // Collect state variables
                foreach (var nfix in prefixes.Union(postfixes).Union(finalizers))
                    if (nfix.DeclaringType != null && variables.ContainsKey(nfix.DeclaringType.FullName) == false)
                        foreach (var patchParam in nfix
                                                   .GetParameters().Where(patchParam => patchParam.Name == STATE_VAR))
                            variables[nfix.DeclaringType.FullName] =
                                il.DeclareVariable(patchParam.ParameterType.OpenRefType()); // Fix possible reftype

                WritePrefixes(il, original, returnLabel, variables, prefixes);
                WritePostfixes(il, original, returnLabel, variables, postfixes);
                WriteFinalizers(il, original, returnLabel, variables, finalizers);

                // Mark return label in case it hasn't been marked yet and close open labels to return
                il.MarkLabel(returnLabel);
                il.SetOpenLabelsTo(ctx.Instrs[ctx.Instrs.Count - 1]);

                Logger.Log(Logger.LogChannel.IL, () => $"Generated patch ({ctx.Method.FullName}):\n{ctx.Body.ToILDasmString()}");
            }
            catch (Exception e)
            {
                Logger.Log(Logger.LogChannel.Error, () => $"Failed to patch {original.GetID()}: {e}");
            }
        }

        private static void EmitCallParameter(ILEmitter il, MethodBase original, MethodInfo patch,
                                              Dictionary<string, VariableDefinition> variables,
                                              bool allowFirsParamPassthrough)
        {
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
                        il.Emit(OpCodes.Ldtoken, constructorInfo);
                        il.Emit(OpCodes.Call, getMethodMethod);
                        continue;
                    }

                    if (original is MethodInfo methodInfo)
                    {
                        il.Emit(OpCodes.Ldtoken, methodInfo);
                        il.Emit(OpCodes.Call, getMethodMethod);
                        continue;
                    }

                    il.Emit(OpCodes.Ldnull);
                    continue;
                }

                if (patchParam.Name == INSTANCE_PARAM)
                {
                    if (original.IsStatic)
                    {
                        il.Emit(OpCodes.Ldnull);
                    }
                    else
                    {
                        var instanceIsRef = AccessTools.IsStruct(original.DeclaringType);
                        var parameterIsRef = patchParam.ParameterType.IsByRef;
                        if (instanceIsRef == parameterIsRef)
                            il.Emit(OpCodes.Ldarg_0);
                        if (instanceIsRef && parameterIsRef == false)
                        {
                            il.Emit(OpCodes.Ldarg_0);
                            il.Emit(OpCodes.Ldobj, original.DeclaringType);
                        }

                        if (instanceIsRef == false && parameterIsRef)
                            il.Emit(OpCodes.Ldarga, 0);
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
                                $"No field found at given index in class {original.DeclaringType.FullName}", fieldName);
                    }
                    else
                    {
                        fieldInfo = AccessTools.DeclaredField(original.DeclaringType, fieldName);
                        if (fieldInfo == null)
                            throw new ArgumentException(
                                $"No such field defined in class {original.DeclaringType.FullName}", fieldName);
                    }

                    if (fieldInfo.IsStatic)
                    {
                        il.Emit(patchParam.ParameterType.IsByRef ? OpCodes.Ldsflda : OpCodes.Ldsfld, fieldInfo);
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(patchParam.ParameterType.IsByRef ? OpCodes.Ldflda : OpCodes.Ldfld, fieldInfo);
                    }

                    continue;
                }

                // state is special too since each patch has its own local var
                if (patchParam.Name == STATE_VAR)
                {
                    if (variables.TryGetValue(patch.DeclaringType.FullName, out var stateVar))
                        il.Emit(patchParam.ParameterType.IsByRef ? OpCodes.Ldloca : OpCodes.Ldloc, stateVar);
                    else
                        il.Emit(OpCodes.Ldnull);
                    continue;
                }

                // treat __result var special
                if (patchParam.Name == RESULT_VAR)
                {
                    var returnType = AccessTools.GetReturnedType(original);
                    if (returnType == typeof(void))
                        throw new InvalidHarmonyPatchArgumentException($"Cannot get result from void method", original, patch);
                    var resultType = patchParam.ParameterType;
                    if (resultType.IsByRef)
                        resultType = resultType.GetElementType();
                    if (resultType.IsAssignableFrom(returnType) == false)
                        throw new InvalidHarmonyPatchArgumentException(
                            $"Cannot assign method return type {returnType.FullName} to {RESULT_VAR} type {resultType.FullName}", original, patch);
                    il.Emit(patchParam.ParameterType.IsByRef ? OpCodes.Ldloca : OpCodes.Ldloc, variables[RESULT_VAR]);
                    continue;
                }

                // any other declared variables
                if (variables.TryGetValue(patchParam.Name, out var localBuilder))
                {
                    il.Emit(patchParam.ParameterType.IsByRef ? OpCodes.Ldloca : OpCodes.Ldloc, localBuilder);
                    continue;
                }

                int idx;
                if (patchParam.Name.StartsWith(PARAM_INDEX_PREFIX, StringComparison.Ordinal))
                {
                    var val = patchParam.Name.Substring(PARAM_INDEX_PREFIX.Length);
                    if (!int.TryParse(val, out idx))
                        throw new InvalidHarmonyPatchArgumentException($"Parameter {patchParam.Name} does not contain a valid index", original, patch);
                    if (idx < 0 || idx >= originalParameters.Length)
                        throw new InvalidHarmonyPatchArgumentException($"No parameter found at index {idx}", original, patch);
                }
                else
                {
                    idx = GetArgumentIndex(patch, originalParameterNames, patchParam);
                    if (idx == -1)
                        throw new InvalidHarmonyPatchArgumentException(
                            $"Parameter \"{patchParam.Name}\" not found", original, patch);
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
                    il.Emit(OpCodes.Ldarg, patchArgIndex);
                    continue;
                }

                // Case 2
                if (originalIsNormal && patchIsNormal == false)
                {
                    il.Emit(OpCodes.Ldarga, patchArgIndex);
                    continue;
                }

                // Case 3
                il.Emit(OpCodes.Ldarg, patchArgIndex);
                il.Emit(GetIndOpcode(originalParameters[idx].ParameterType));
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