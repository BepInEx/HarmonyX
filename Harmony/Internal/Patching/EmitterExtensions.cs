using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using MonoMod.Utils.Cil;
using OpCode = System.Reflection.Emit.OpCode;
using OpCodes = System.Reflection.Emit.OpCodes;

namespace HarmonyLib.Internal.Patching
{
    internal static class ILExtensions
    {
        public class ExceptionBlock
        {
            public Instruction start, skip;
            public ExceptionHandler prev, cur;
        }

        public static ExceptionBlock BeginExceptionBlock(this ILProcessor il, Instruction start)
        {
            return new ExceptionBlock { start = start };
        }

        public static void EndExceptionBlock(this ILProcessor il, Instruction before, ExceptionBlock block)
        {
            il.EndHandler(before, block, block.cur);
        }

        public static ExceptionHandler BeginHandler(this ILProcessor il, Instruction before, ExceptionBlock block, ExceptionHandlerType handlerType)
        {
            var prev = (block.prev = block.cur);
            if (prev != null)
                il.EndHandler(before, block, prev);

            block.skip = il.Create(Mono.Cecil.Cil.OpCodes.Nop);

            il.EmitBefore(before, Mono.Cecil.Cil.OpCodes.Leave, block.skip);

            var handlerIns = il.EmitBefore(before, Mono.Cecil.Cil.OpCodes.Nop);
            block.cur = new ExceptionHandler(0)
            {
                TryStart = block.start,
                TryEnd = handlerIns,
                HandlerType = handlerType
            };
            if (handlerType == ExceptionHandlerType.Filter)
                block.cur.FilterStart = handlerIns;
            else
                block.cur.HandlerStart = handlerIns;

            il.Body.ExceptionHandlers.Add(block.cur);
            return block.cur;
        }

        public static void EndHandler(this ILProcessor il, Instruction before, ExceptionBlock block, ExceptionHandler handler)
        {
            switch (handler.HandlerType)
            {
                case ExceptionHandlerType.Filter:
                    il.EmitBefore(before, Mono.Cecil.Cil.OpCodes.Endfilter);
                    break;
                case ExceptionHandlerType.Finally:
                    il.EmitBefore(before, Mono.Cecil.Cil.OpCodes.Endfinally);
                    break;
                default:
                    il.EmitBefore(before, Mono.Cecil.Cil.OpCodes.Leave, block.skip);
                    break;
            }

            il.InsertBefore(before, block.skip);
            handler.HandlerEnd = block.skip;
        }

        public static VariableDefinition DeclareVariable(this ILProcessor il, Type type)
        {
            var varDef = new VariableDefinition(il.Import(type));
            il.Body.Variables.Add(varDef);
            return varDef;
        }

        public static Instruction EmitBefore(this ILProcessor il, Instruction ins, Mono.Cecil.Cil.OpCode opcode)
        {
            var newIns = il.Create(opcode);
            il.InsertBefore(ins, newIns);
            return newIns;
        }

        public static void EmitBefore(this ILProcessor il, Instruction ins, Mono.Cecil.Cil.OpCode opcode, ConstructorInfo cInfo)
        {
            il.InsertBefore(ins, il.Create(opcode, il.Import(cInfo)));
        }

        public static void EmitBefore(this ILProcessor il, Instruction ins, Mono.Cecil.Cil.OpCode opcode, MethodInfo mInfo)
        {
            il.InsertBefore(ins, il.Create(opcode, il.Import(mInfo)));
        }

        public static void EmitBefore(this ILProcessor il, Instruction ins, Mono.Cecil.Cil.OpCode opcode, Type cls)
        {
            il.InsertBefore(ins, il.Create(opcode, il.Import(cls)));
        }

        public static void EmitBefore(this ILProcessor il, Instruction ins, Mono.Cecil.Cil.OpCode opcode, int arg)
        {
            il.InsertBefore(ins, il.Create(opcode, arg));
        }

        public static void EmitBefore(this ILProcessor il, Instruction ins, Mono.Cecil.Cil.OpCode opcode, FieldInfo fInfo)
        {
            il.InsertBefore(ins, il.Create(opcode, il.Import(fInfo)));
        }

        public static void EmitBefore(this ILProcessor il, Instruction ins, Mono.Cecil.Cil.OpCode opcode, VariableDefinition varDef)
        {
            il.InsertBefore(ins, il.Create(opcode, varDef));
        }

        public static void EmitBefore(this ILProcessor il, Instruction ins, Mono.Cecil.Cil.OpCode opcode, Instruction tgtIns)
        {
            il.InsertBefore(ins, il.Create(opcode, tgtIns));
        }
    }

    internal static class EmitterExtensions
    {
        private static DynamicMethodDefinition emitDMD;
        private static MethodInfo emitDMDMethod;
        private static Action<CecilILGenerator, OpCode, object> emitCodeDelegate;
        private static AccessTools.FieldRef<CecilILGenerator, Dictionary<LocalBuilder, VariableDefinition>> cilVars;

        [MethodImpl(MethodImplOptions.Synchronized)]
        static EmitterExtensions()
        {
            if (emitDMD != null)
                return;
            InitEmitterHelperDMD();
            cilVars =
                AccessTools
                    .FieldRefAccess<CecilILGenerator, Dictionary<LocalBuilder, VariableDefinition>>("_Variables");
        }

        private static void InitEmitterHelperDMD()
        {
            emitDMD = new DynamicMethodDefinition("EmitOpcodeWithOperand", typeof(void),
                                                  new[] {typeof(CecilILGenerator), typeof(OpCode), typeof(object)});
            var il = emitDMD.GetILGenerator();

            var current = il.DefineLabel();

            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Brtrue, current);

            il.Emit(OpCodes.Ldstr, "Provided operand is null!");
            il.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor(new[] {typeof(string)}));
            il.Emit(OpCodes.Throw);

            foreach (var method in typeof(CecilILGenerator).GetMethods().Where(m => m.Name == "Emit"))
            {
                var paramInfos = method.GetParameters();
                if (paramInfos.Length != 2)
                    continue;
                var types = paramInfos.Select(p => p.ParameterType).ToArray();
                if (types[0] != typeof(OpCode))
                    continue;

                var paramType = types[1];

                il.MarkLabel(current);
                current = il.DefineLabel();

                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Isinst, paramType);
                il.Emit(OpCodes.Brfalse, current);

                il.Emit(OpCodes.Ldarg_2);

                if (paramType.IsValueType)
                    il.Emit(OpCodes.Unbox_Any, paramType);

                var loc = il.DeclareLocal(paramType);
                il.Emit(OpCodes.Stloc, loc);

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldloc, loc);
                il.Emit(OpCodes.Callvirt, method);
                il.Emit(OpCodes.Ret);
            }

            il.MarkLabel(current);
            il.Emit(OpCodes.Ldstr, "The operand is none of the supported types!");
            il.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor(new[] {typeof(string)}));
            il.Emit(OpCodes.Throw);
            il.Emit(OpCodes.Ret);

            emitDMDMethod = emitDMD.Generate();
            emitCodeDelegate = (Action<CecilILGenerator, OpCode, object>) emitDMDMethod.CreateDelegate<Action<CecilILGenerator, OpCode, object>>();
        }

        public static void Emit(this CecilILGenerator il, OpCode opcode, object operand)
        {
            emitCodeDelegate(il, opcode, operand);
        }

        public static void MarkBlockBefore(this CecilILGenerator il, ExceptionBlock block)
        {
            switch (block.blockType)
            {
                case ExceptionBlockType.BeginExceptionBlock:
                    il.BeginExceptionBlock();
                    return;
                case ExceptionBlockType.BeginCatchBlock:
                    il.BeginCatchBlock(block.catchType);
                    return;
                case ExceptionBlockType.BeginExceptFilterBlock:
                    il.BeginExceptFilterBlock();
                    return;
                case ExceptionBlockType.BeginFaultBlock:
                    il.BeginFaultBlock();
                    return;
                case ExceptionBlockType.BeginFinallyBlock:
                    il.BeginFinallyBlock();
                    return;
                case ExceptionBlockType.EndExceptionBlock:
                    return;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public static void MarkBlockAfter(this CecilILGenerator il, ExceptionBlock block)
        {
            if (block.blockType == ExceptionBlockType.EndExceptionBlock)
                il.EndExceptionBlock();
        }

        public static LocalBuilder GetLocal(this CecilILGenerator il, VariableDefinition varDef)
        {
            var vars = cilVars(il);
            var loc = vars.FirstOrDefault(kv => kv.Value == varDef).Key;
            if (loc != null)
                return loc;
            loc = il.DeclareLocal(varDef.VariableType.ResolveReflection());
            il.IL.Body.Variables.Remove(vars[loc]);
            vars[loc] = varDef;
            return loc;
        }
    }
}