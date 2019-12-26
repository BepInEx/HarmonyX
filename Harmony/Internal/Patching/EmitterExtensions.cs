using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using MonoMod.Utils;
using MonoMod.Utils.Cil;

namespace HarmonyLib.Internal.Patching
{
    internal static class EmitterExtensions
    {
        private static DynamicMethodDefinition emitDMD;
        private static MethodInfo emitDMDMethod;
        private static Action<CecilILGenerator, System.Reflection.Emit.OpCode, object> emitCodeDelegate;

        [MethodImpl(MethodImplOptions.Synchronized)]
        static EmitterExtensions()
        {
            if (emitDMD != null)
                return;
            InitEmitterHelperDMD();
        }

        private static void InitEmitterHelperDMD()
        {
            emitDMD = new DynamicMethodDefinition("EmitOpcodeWithOperand", typeof(void), new []{ typeof(CecilILGenerator), typeof(System.Reflection.Emit.OpCode), typeof(object) });
            var il = emitDMD.GetILGenerator();

            var current = il.DefineLabel();

            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_2);
            il.Emit(System.Reflection.Emit.OpCodes.Brtrue, current);

            il.Emit(System.Reflection.Emit.OpCodes.Ldstr, "Provided operand is null!");
            il.Emit(System.Reflection.Emit.OpCodes.Newobj, typeof(Exception).GetConstructor(new []{typeof(string)}));
            il.Emit(System.Reflection.Emit.OpCodes.Throw);

            foreach (var method in typeof(CecilILGenerator).GetMethods().Where(m => m.Name == "Emit"))
            {
                var paramInfos = method.GetParameters();
                if (paramInfos.Length != 2)
                    continue;
                var types = paramInfos.Select(p => p.ParameterType).ToArray();
                if(types[0] != typeof(System.Reflection.Emit.OpCode))
                    continue;

                var paramType = types[1];

                il.MarkLabel(current);
                current = il.DefineLabel();

                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_2);
                il.Emit(System.Reflection.Emit.OpCodes.Isinst, paramType);
                il.Emit(System.Reflection.Emit.OpCodes.Brfalse, current);

                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_2);

                if(paramType.IsValueType)
                    il.Emit(System.Reflection.Emit.OpCodes.Unbox_Any, paramType);

                var loc = il.DeclareLocal(paramType);
                il.Emit(System.Reflection.Emit.OpCodes.Stloc, loc);

                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
                il.Emit(System.Reflection.Emit.OpCodes.Ldloc, loc);
                il.Emit(System.Reflection.Emit.OpCodes.Callvirt, method);
                il.Emit(System.Reflection.Emit.OpCodes.Ret);
            }

            il.MarkLabel(current);
            il.Emit(System.Reflection.Emit.OpCodes.Ldstr, "The operand is none of the supported types!");
            il.Emit(System.Reflection.Emit.OpCodes.Newobj, typeof(Exception).GetConstructor(new []{typeof(string)}));
            il.Emit(System.Reflection.Emit.OpCodes.Throw);
            il.Emit(System.Reflection.Emit.OpCodes.Ret);

            emitDMDMethod = emitDMD.Generate();
            emitCodeDelegate = (Action<CecilILGenerator, System.Reflection.Emit.OpCode, object>) Delegate.CreateDelegate(typeof(Action<CecilILGenerator, System.Reflection.Emit.OpCode, object>), emitDMDMethod);
        }

        public static void Emit(this CecilILGenerator il, System.Reflection.Emit.OpCode opcode, object operand)
        {
            emitCodeDelegate(il, opcode, operand);
        }
    }
}