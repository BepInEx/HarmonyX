using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib.Internal.Native;
using HarmonyLib.Internal.Patching;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using MonoMod.Utils.Cil;
using OpCode = System.Reflection.Emit.OpCode;
using OpCodes = System.Reflection.Emit.OpCodes;

namespace HarmonyLib.Internal.CIL
{
    internal static class MethodPatcher
    {
        /// special parameter names that can be used in prefix and postfix methods
        public static string INSTANCE_PARAM = "__instance";

        public static string ORIGINAL_METHOD_PARAM = "__originalMethod";
        public static string RESULT_VAR = "__result";
        public static string STATE_VAR = "__state";
        public static string EXCEPTION_VAR = "__exception";
        public static string PARAM_INDEX_PREFIX = "__";
        public static string INSTANCE_FIELD_PREFIX = "___";


        private static void WriteTranspiledMethod(ILContext ctx, MethodBase original, List<MethodInfo> transpilers)
        {
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
            var resultIns = ctx.IL.Create(Mono.Cecil.Cil.OpCodes.Nop);

            foreach (var ins in ctx.Instrs.Where(ins => ins.MatchRet()))
            {
                ins.OpCode = Mono.Cecil.Cil.OpCodes.Br;
                ins.Operand = resultIns;
            }

            ctx.IL.Append(resultIns);
            ctx.IL.Emit(Mono.Cecil.Cil.OpCodes.Ret);

            return resultIns;
        }

        private static void WritePostfixes(ILContext ctx, MethodBase original, List<MethodInfo> postfixes)
        {

        }

        private static void WritePrefixes(ILContext ctx, MethodBase original, Instruction returnLabel, Dictionary<string, VariableDefinition> variables, List<MethodInfo> prefixes)
        {
            // Prefix layout:
            // Make return value (if needed) into a variable
            // Call prefixes
            // If prefix returns true, load return value onto stack and branch into return label => simulates return value

            if (!variables.TryGetValue(RESULT_VAR, out var returnValueVar))
            {
                var retVal = AccessTools.GetReturnedType(original);
                returnValueVar =  retVal == typeof(void) ? null : ctx.IL.DeclareVariable(retVal);
            }

            var ins = ctx.Instrs.First(); // Grab the instruction from the top of the method
            foreach (var prefix in prefixes)
            {
                EmitCallParameter(ins, ctx, original, prefix, variables, false);
                ctx.IL.EmitBefore(ins, Mono.Cecil.Cil.OpCodes.Call, prefix);

                if (!AccessTools.IsVoid(prefix.ReturnType))
                {
                    if(prefix.ReturnType != typeof(bool))
                            throw new Exception($"Prefix patch {prefix} has not \"bool\" or \"void\" return type: {prefix.ReturnType}");

                    // If we skip, load the result onto the stack so as to simulate a "normal return"
                    if(returnValueVar != null)
                        ctx.IL.EmitBefore(ins, Mono.Cecil.Cil.OpCodes.Ldloc, returnValueVar);
                    ctx.IL.EmitBefore(ins, Mono.Cecil.Cil.OpCodes.Brfalse, returnLabel);
                }
            }
        }

        private static void WriteFinalizers(ILContext ctx, MethodBase original, List<MethodInfo> postfixes)
        {

        }

        public static void MakePatched(MethodBase original, MethodBase source, ILContext ctx, List<MethodInfo> prefixes,
                                       List<MethodInfo> postfixes, List<MethodInfo> transpilers,
                                       List<MethodInfo> finalizers)
        {
            try
            {
                if(original == null)
                    throw new ArgumentException(nameof(original));

                Memory.MarkForNoInlining(original);

                WriteTranspiledMethod(ctx, original, transpilers);

                // If no need to wrap anything, we're basically done!
                if (prefixes.Count + postfixes.Count + finalizers.Count == 0)
                    return;

                var returnLabel = MakeReturnLabel(ctx);
                var variables = new Dictionary<string, VariableDefinition>();

                // Collect state variables
                foreach (var nfix in prefixes.Union(postfixes).Union(finalizers))
                {
                    if (nfix.DeclaringType != null && variables.ContainsKey(nfix.DeclaringType.FullName) == false)
                        foreach (var patchParam in nfix.GetParameters().Where(patchParam => patchParam.Name == STATE_VAR))
                            variables[nfix.DeclaringType.FullName] = ctx.IL.DeclareVariable(patchParam.ParameterType);
                }

                WritePrefixes(ctx, original, returnLabel, variables, prefixes);
                WritePostfixes(ctx, original, postfixes);
                WriteFinalizers(ctx, original, finalizers);
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
            var firstArgIsReturnBuffer = NativeThisPointer.NeedsNativeThisPointerFix(original);

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
                        il.EmitBefore(before, Mono.Cecil.Cil.OpCodes.Ldtoken, constructorInfo);
                        il.EmitBefore(before, Mono.Cecil.Cil.OpCodes.Call, getMethodMethod);
                        continue;
                    }

                    if (original is MethodInfo methodInfo)
                    {
                        il.EmitBefore(before, Mono.Cecil.Cil.OpCodes.Ldtoken, methodInfo);
                        il.EmitBefore(before, Mono.Cecil.Cil.OpCodes.Call, getMethodMethod);
                        continue;
                    }

                    il.EmitBefore(before, Mono.Cecil.Cil.OpCodes.Ldnull);
                    continue;
                }

                if (patchParam.Name == INSTANCE_PARAM)
                {
                    if (original.IsStatic)
                    {
                        il.EmitBefore(before, Mono.Cecil.Cil.OpCodes.Ldnull);
                    }
                    else
                    {
                        var instanceIsRef = AccessTools.IsStruct(original.DeclaringType);
                        var parameterIsRef = patchParam.ParameterType.IsByRef;
                        if (instanceIsRef == parameterIsRef)
                            il.EmitBefore(before, Mono.Cecil.Cil.OpCodes.Ldarg_0);
                        if (instanceIsRef && parameterIsRef == false)
                        {
                            il.EmitBefore(before, Mono.Cecil.Cil.OpCodes.Ldarg_0);
                            il.EmitBefore(before, Mono.Cecil.Cil.OpCodes.Ldobj, original.DeclaringType);
                        }

                        if (instanceIsRef == false && parameterIsRef)
                            il.EmitBefore(before, Mono.Cecil.Cil.OpCodes.Ldarga, 0);
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
                        il.EmitBefore(before, patchParam.ParameterType.IsByRef ? Mono.Cecil.Cil.OpCodes.Ldsflda : Mono.Cecil.Cil.OpCodes.Ldsfld, fieldInfo);
                    }
                    else
                    {
                        il.EmitBefore(before, Mono.Cecil.Cil.OpCodes.Ldarg_0);
                        il.EmitBefore(before, patchParam.ParameterType.IsByRef ? Mono.Cecil.Cil.OpCodes.Ldflda : Mono.Cecil.Cil.OpCodes.Ldfld, fieldInfo);
                    }

                    continue;
                }

                // state is special too since each patch has its own local var
                if (patchParam.Name == STATE_VAR)
                {
                    if (variables.TryGetValue(patch.DeclaringType.FullName, out var stateVar))
                        il.EmitBefore(before, patchParam.ParameterType.IsByRef ? Mono.Cecil.Cil.OpCodes.Ldloca : Mono.Cecil.Cil.OpCodes.Ldloc, stateVar);
                    else
                        il.EmitBefore(before, Mono.Cecil.Cil.OpCodes.Ldnull);
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
                    il.EmitBefore(before, patchParam.ParameterType.IsByRef ? Mono.Cecil.Cil.OpCodes.Ldloca : Mono.Cecil.Cil.OpCodes.Ldloc, variables[RESULT_VAR]);
                    continue;
                }

                // any other declared variables
                if (variables.TryGetValue(patchParam.Name, out var localBuilder))
                {
                    il.EmitBefore(before, patchParam.ParameterType.IsByRef ? Mono.Cecil.Cil.OpCodes.Ldloca : Mono.Cecil.Cil.OpCodes.Ldloc, localBuilder);
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
                var patchArgIndex = idx + (isInstance ? 1 : 0) + (firstArgIsReturnBuffer ? 1 : 0);

                // Case 1 + 4
                if (originalIsNormal == patchIsNormal)
                {
                    il.EmitBefore(before, Mono.Cecil.Cil.OpCodes.Ldarg, patchArgIndex);
                    continue;
                }

                // Case 2
                if (originalIsNormal && patchIsNormal == false)
                {
                    il.EmitBefore(before, Mono.Cecil.Cil.OpCodes.Ldarga, patchArgIndex);
                    continue;
                }

                // Case 3
                il.EmitBefore(before, Mono.Cecil.Cil.OpCodes.Ldarg, patchArgIndex);
                il.EmitBefore(before, GetIndOpcode(originalParameters[idx].ParameterType));
            }
        }

        private static Mono.Cecil.Cil.OpCode GetIndOpcode(Type type)
        {
            if (type.IsEnum)
                return Mono.Cecil.Cil.OpCodes.Ldind_I4;

            if (type == typeof(float)) return Mono.Cecil.Cil.OpCodes.Ldind_R4;
            if (type == typeof(double)) return Mono.Cecil.Cil.OpCodes.Ldind_R8;

            if (type == typeof(byte)) return Mono.Cecil.Cil.OpCodes.Ldind_U1;
            if (type == typeof(ushort)) return Mono.Cecil.Cil.OpCodes.Ldind_U2;
            if (type == typeof(uint)) return Mono.Cecil.Cil.OpCodes.Ldind_U4;
            if (type == typeof(ulong)) return Mono.Cecil.Cil.OpCodes.Ldind_I8;

            if (type == typeof(sbyte)) return Mono.Cecil.Cil.OpCodes.Ldind_I1;
            if (type == typeof(short)) return Mono.Cecil.Cil.OpCodes.Ldind_I2;
            if (type == typeof(int)) return Mono.Cecil.Cil.OpCodes.Ldind_I4;
            if (type == typeof(long)) return Mono.Cecil.Cil.OpCodes.Ldind_I8;

            return Mono.Cecil.Cil.OpCodes.Ldind_Ref;
        }

        public static DynamicMethod CreatePatchedMethod(MethodBase original, MethodBase source,
                                                        string harmonyInstanceID, List<MethodInfo> prefixes,
                                                        List<MethodInfo> postfixes, List<MethodInfo> transpilers,
                                                        List<MethodInfo> finalizers)
        {
            try
            {
                if (original == null)
                    throw new ArgumentNullException(nameof(original));

                Memory.MarkForNoInlining(original);

                if (HarmonyLib.Harmony.DEBUG)
                {
                    FileLog.LogBuffered("### Patch " + original.FullDescription());
                    FileLog.FlushBuffer();
                }

                var idx = prefixes.Count() + postfixes.Count() + finalizers.Count();
                var firstArgIsReturnBuffer = NativeThisPointer.NeedsNativeThisPointerFix(original);
                var returnType = AccessTools.GetReturnedType(original);
                var hasFinalizers = finalizers.Any();
                var patch = DynamicTools.CreateDynamicMethod(original, "_Patch" + idx);
                if (patch == null)
                    return null;

                var il = patch.GetILGenerator();

                var originalVariables = DynamicTools.DeclareLocalVariables(source ?? original, il);
                var privateVars = new Dictionary<string, LocalBuilder>();

                LocalBuilder resultVariable = null;
                if (idx > 0)
                {
                    resultVariable = DynamicTools.DeclareLocalVariable(il, returnType);
                    privateVars[RESULT_VAR] = resultVariable;
                }

                prefixes.Union(postfixes).Union(finalizers).ToList().ForEach(fix =>
                {
                    if (fix.DeclaringType != null && privateVars.ContainsKey(fix.DeclaringType.FullName) == false)
                        fix.GetParameters().Where(patchParam => patchParam.Name == STATE_VAR).Do(patchParam =>
                        {
                            var privateStateVariable = DynamicTools.DeclareLocalVariable(il, patchParam.ParameterType);
                            privateVars[fix.DeclaringType.FullName] = privateStateVariable;
                        });
                });

                LocalBuilder finalizedVariable = null;
                if (hasFinalizers)
                {
                    finalizedVariable = DynamicTools.DeclareLocalVariable(il, typeof(bool));

                    privateVars[EXCEPTION_VAR] = DynamicTools.DeclareLocalVariable(il, typeof(Exception));

                    // begin try
                    Emitter.MarkBlockBefore(il, new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock), out var _);
                }

                if (firstArgIsReturnBuffer)
                    Emitter.Emit(il, OpCodes.Ldarg_1); // load ref to return value

                var skipOriginalLabel = il.DefineLabel();
                var canHaveJump = AddPrefixes(il, original, prefixes, privateVars, skipOriginalLabel);

                var copier = new MethodCopier(source ?? original, il, originalVariables);
                foreach (var transpiler in transpilers)
                    copier.AddTranspiler(transpiler);
                if (firstArgIsReturnBuffer)
                    copier.AddTranspiler(NativeThisPointer.m_ArgumentShiftTranspiler);

                var endLabels = new List<Label>();
                copier.Finalize(endLabels);

                foreach (var label in endLabels)
                    Emitter.MarkLabel(il, label);
                if (resultVariable != null)
                    Emitter.Emit(il, OpCodes.Stloc, resultVariable);
                if (canHaveJump)
                    Emitter.MarkLabel(il, skipOriginalLabel);

                AddPostfixes(il, original, postfixes, privateVars, false);

                if (resultVariable != null)
                    Emitter.Emit(il, OpCodes.Ldloc, resultVariable);

                AddPostfixes(il, original, postfixes, privateVars, true);

                if (hasFinalizers)
                {
                    AddFinalizers(il, original, finalizers, privateVars, false);
                    Emitter.Emit(il, OpCodes.Ldc_I4_1);
                    Emitter.Emit(il, OpCodes.Stloc, finalizedVariable);
                    var noExceptionLabel1 = il.DefineLabel();
                    Emitter.Emit(il, OpCodes.Ldloc, privateVars[EXCEPTION_VAR]);
                    Emitter.Emit(il, OpCodes.Brfalse, noExceptionLabel1);
                    Emitter.Emit(il, OpCodes.Ldloc, privateVars[EXCEPTION_VAR]);
                    Emitter.Emit(il, OpCodes.Throw);
                    Emitter.MarkLabel(il, noExceptionLabel1);

                    // end try, begin catch
                    Emitter.MarkBlockBefore(il, new ExceptionBlock(ExceptionBlockType.BeginCatchBlock), out var label);
                    Emitter.Emit(il, OpCodes.Stloc, privateVars[EXCEPTION_VAR]);

                    Emitter.Emit(il, OpCodes.Ldloc, finalizedVariable);
                    var endFinalizerLabel = il.DefineLabel();
                    Emitter.Emit(il, OpCodes.Brtrue, endFinalizerLabel);

                    var rethrowPossible = AddFinalizers(il, original, finalizers, privateVars, true);

                    Emitter.MarkLabel(il, endFinalizerLabel);

                    var noExceptionLabel2 = il.DefineLabel();
                    Emitter.Emit(il, OpCodes.Ldloc, privateVars[EXCEPTION_VAR]);
                    Emitter.Emit(il, OpCodes.Brfalse, noExceptionLabel2);
                    if (rethrowPossible)
                    {
                        Emitter.Emit(il, OpCodes.Rethrow);
                    }
                    else
                    {
                        Emitter.Emit(il, OpCodes.Ldloc, privateVars[EXCEPTION_VAR]);
                        Emitter.Emit(il, OpCodes.Throw);
                    }

                    Emitter.MarkLabel(il, noExceptionLabel2);

                    // end catch
                    Emitter.MarkBlockAfter(il, new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));

                    if (resultVariable != null)
                        Emitter.Emit(il, OpCodes.Ldloc, resultVariable);
                }

                if (firstArgIsReturnBuffer)
                    Emitter.Emit(il, OpCodes.Stobj, returnType); // store result into ref

                Emitter.Emit(il, OpCodes.Ret);

                if (HarmonyLib.Harmony.DEBUG)
                {
                    FileLog.LogBuffered("DONE");
                    FileLog.LogBuffered("");
                    FileLog.FlushBuffer();
                }

                DynamicTools.PrepareDynamicMethod(patch);
                return patch;
            }
            catch (Exception ex)
            {
                var exceptionString = "Exception from HarmonyInstance \"" + harmonyInstanceID + "\" patching " +
                                      original.FullDescription() + ": " + ex;
                if (HarmonyLib.Harmony.DEBUG)
                {
                    var savedIndentLevel = FileLog.indentLevel;
                    FileLog.indentLevel = 0;
                    FileLog.Log(exceptionString);
                    FileLog.indentLevel = savedIndentLevel;
                }

                throw new Exception(exceptionString, ex);
            }
            finally
            {
                if (HarmonyLib.Harmony.DEBUG)
                    FileLog.FlushBuffer();
            }
        }

        private static OpCode LoadIndOpCodeFor(Type type)
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

        private static readonly MethodInfo getMethodMethod =
            typeof(MethodBase).GetMethod("GetMethodFromHandle", new[] {typeof(RuntimeMethodHandle)});

        private static void EmitCallParameter(ILGenerator il, MethodBase original, MethodInfo patch,
                                              Dictionary<string, LocalBuilder> variables,
                                              bool allowFirsParamPassthrough)
        {
            var isInstance = original.IsStatic == false;
            var originalParameters = original.GetParameters();
            var originalParameterNames = originalParameters.Select(p => p.Name).ToArray();
            var firstArgIsReturnBuffer = NativeThisPointer.NeedsNativeThisPointerFix(original);

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
                        Emitter.Emit(il, OpCodes.Ldtoken, constructorInfo);
                        Emitter.Emit(il, OpCodes.Call, getMethodMethod);
                        continue;
                    }

                    if (original is MethodInfo methodInfo)
                    {
                        Emitter.Emit(il, OpCodes.Ldtoken, methodInfo);
                        Emitter.Emit(il, OpCodes.Call, getMethodMethod);
                        continue;
                    }

                    Emitter.Emit(il, OpCodes.Ldnull);
                    continue;
                }

                if (patchParam.Name == INSTANCE_PARAM)
                {
                    if (original.IsStatic)
                    {
                        Emitter.Emit(il, OpCodes.Ldnull);
                    }
                    else
                    {
                        var instanceIsRef = AccessTools.IsStruct(original.DeclaringType);
                        var parameterIsRef = patchParam.ParameterType.IsByRef;
                        if (instanceIsRef == parameterIsRef) Emitter.Emit(il, OpCodes.Ldarg_0);
                        if (instanceIsRef && parameterIsRef == false)
                        {
                            Emitter.Emit(il, OpCodes.Ldarg_0);
                            Emitter.Emit(il, OpCodes.Ldobj, original.DeclaringType);
                        }

                        if (instanceIsRef == false && parameterIsRef) Emitter.Emit(il, OpCodes.Ldarga, 0);
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
                        Emitter.Emit(il, patchParam.ParameterType.IsByRef ? OpCodes.Ldsflda : OpCodes.Ldsfld,
                                     fieldInfo);
                    }
                    else
                    {
                        Emitter.Emit(il, OpCodes.Ldarg_0);
                        Emitter.Emit(il, patchParam.ParameterType.IsByRef ? OpCodes.Ldflda : OpCodes.Ldfld, fieldInfo);
                    }

                    continue;
                }

                // state is special too since each patch has its own local var
                if (patchParam.Name == STATE_VAR)
                {
                    var ldlocCode = patchParam.ParameterType.IsByRef ? OpCodes.Ldloca : OpCodes.Ldloc;
                    if (variables.TryGetValue(patch.DeclaringType.FullName, out var stateVar))
                        Emitter.Emit(il, ldlocCode, stateVar);
                    else
                        Emitter.Emit(il, OpCodes.Ldnull);
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
                    var ldlocCode = patchParam.ParameterType.IsByRef ? OpCodes.Ldloca : OpCodes.Ldloc;
                    Emitter.Emit(il, ldlocCode, variables[RESULT_VAR]);
                    continue;
                }

                // any other declared variables
                if (variables.TryGetValue(patchParam.Name, out var localBuilder))
                {
                    var ldlocCode = patchParam.ParameterType.IsByRef ? OpCodes.Ldloca : OpCodes.Ldloc;
                    Emitter.Emit(il, ldlocCode, localBuilder);
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
                var patchArgIndex = idx + (isInstance ? 1 : 0) + (firstArgIsReturnBuffer ? 1 : 0);

                // Case 1 + 4
                if (originalIsNormal == patchIsNormal)
                {
                    Emitter.Emit(il, OpCodes.Ldarg, patchArgIndex);
                    continue;
                }

                // Case 2
                if (originalIsNormal && patchIsNormal == false)
                {
                    Emitter.Emit(il, OpCodes.Ldarga, patchArgIndex);
                    continue;
                }

                // Case 3
                Emitter.Emit(il, OpCodes.Ldarg, patchArgIndex);
                Emitter.Emit(il, LoadIndOpCodeFor(originalParameters[idx].ParameterType));
            }
        }

        private static bool AddPrefixes(ILGenerator il, MethodBase original, List<MethodInfo> prefixes,
                                        Dictionary<string, LocalBuilder> variables, Label label)
        {
            var canHaveJump = false;
            prefixes.ForEach(fix =>
            {
                EmitCallParameter(il, original, fix, variables, false);
                Emitter.Emit(il, OpCodes.Call, fix);

                if (fix.ReturnType != typeof(void))
                {
                    if (fix.ReturnType != typeof(bool))
                        throw new Exception("Prefix patch " + fix + " has not \"bool\" or \"void\" return type: " +
                                            fix.ReturnType);
                    Emitter.Emit(il, OpCodes.Brfalse, label);
                    canHaveJump = true;
                }
            });
            return canHaveJump;
        }

        private static void AddPostfixes(ILGenerator il, MethodBase original, List<MethodInfo> postfixes,
                                         Dictionary<string, LocalBuilder> variables, bool passthroughPatches)
        {
            postfixes.Where(fix => passthroughPatches == (fix.ReturnType != typeof(void))).Do(fix =>
            {
                EmitCallParameter(il, original, fix, variables, true);
                Emitter.Emit(il, OpCodes.Call, fix);

                if (fix.ReturnType != typeof(void))
                {
                    var firstFixParam = fix.GetParameters().FirstOrDefault();
                    var hasPassThroughResultParam =
                        firstFixParam != null && fix.ReturnType == firstFixParam.ParameterType;
                    if (!hasPassThroughResultParam)
                    {
                        if (firstFixParam != null)
                            throw new Exception("Return type of pass through postfix " + fix +
                                                " does not match type of its first parameter");

                        throw new Exception("Postfix patch " + fix + " must have a \"void\" return type");
                    }
                }
            });
        }

        private static bool AddFinalizers(ILGenerator il, MethodBase original, List<MethodInfo> finalizers,
                                          Dictionary<string, LocalBuilder> variables, bool catchExceptions)
        {
            var rethrowPossible = true;
            finalizers.Do(fix =>
            {
                if (catchExceptions)
                    Emitter.MarkBlockBefore(il, new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock),
                                            out var label);

                EmitCallParameter(il, original, fix, variables, false);
                Emitter.Emit(il, OpCodes.Call, fix);
                if (fix.ReturnType != typeof(void))
                {
                    Emitter.Emit(il, OpCodes.Stloc, variables[EXCEPTION_VAR]);
                    rethrowPossible = false;
                }

                if (catchExceptions)
                {
                    Emitter.MarkBlockBefore(il, new ExceptionBlock(ExceptionBlockType.BeginCatchBlock), out var _);
                    Emitter.Emit(il, OpCodes.Pop);
                    Emitter.MarkBlockAfter(il, new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));
                }
            });
            return rethrowPossible;
        }
    }
}