using System;
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
        public static VariableDefinition DeclareVariable(this ILProcessor il, Type type)
        {
            var varDef = new VariableDefinition(il.Import(type));
            il.Body.Variables.Add(varDef);
            return varDef;
        }

        public static void EmitBefore(this ILProcessor il, Instruction ins, Mono.Cecil.Cil.OpCode opcode)
        {
            il.InsertBefore(ins, il.Create(opcode));
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

        [MethodImpl(MethodImplOptions.Synchronized)]
        static EmitterExtensions()
        {
            if (emitDMD != null)
                return;
            InitEmitterHelperDMD();
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
            emitCodeDelegate =
                (Action<CecilILGenerator, OpCode, object>) Delegate.CreateDelegate(
                    typeof(Action<CecilILGenerator, OpCode, object>), emitDMDMethod);
        }

        public static void Emit(this CecilILGenerator il, OpCode opcode, object operand)
        {
            emitCodeDelegate(il, opcode, operand);
        }

        public static void MarkBlockBefore(this CecilILGenerator il, ExceptionBlock block, out Label? label)
        {
            label = null;

            switch (block.blockType)
            {
                case ExceptionBlockType.BeginExceptionBlock:
                    label = il.BeginExceptionBlock();
                    break;
                case ExceptionBlockType.BeginCatchBlock:
                    il.BeginCatchBlock(block.catchType);
                    break;
                case ExceptionBlockType.BeginExceptFilterBlock:
                    il.BeginExceptFilterBlock();
                    break;
                case ExceptionBlockType.BeginFaultBlock:
                    il.BeginFaultBlock();
                    break;
                case ExceptionBlockType.BeginFinallyBlock:
                    il.BeginFinallyBlock();
                    break;
                case ExceptionBlockType.EndExceptionBlock:
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public static void MarkBlockAfter(this CecilILGenerator il, ExceptionBlock block)
        {
            if (block.blockType == ExceptionBlockType.EndExceptionBlock)
                il.EndExceptionBlock();
        }
    }
}