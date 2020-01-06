using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib.Internal.Patching;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using OpCodes = Mono.Cecil.Cil.OpCodes;

namespace HarmonyLib
{
    /// <summary>A reverse patcher</summary>
    public class ReversePatcher
    {
        private readonly Harmony instance;
        private readonly MethodBase original;
        private readonly MethodInfo standin;
        private readonly ILHook ilHook;

        /// <summary>Creates an empty reverse patcher</summary>
        /// <param name="instance">The Harmony instance</param>
        /// <param name="original">The original method</param>
        /// <param name="standin">The stand-in method</param>
        ///
        public ReversePatcher(Harmony instance, MethodBase original, MethodInfo standin)
        {
            this.instance = instance;
            this.original = original;
            this.standin = standin;
            ilHook = new ILHook(standin, ApplyReversePatch, new ILHookConfig
            {
                ManualApply = true
            });
        }

        private void ApplyReversePatch(ILContext ctx)
        {
            // Make a cecil copy of the original method for convenience sake
            var dmd = new DynamicMethodDefinition(original);

            var manipulator = new ILManipulator(dmd.Definition.Body);

            // Copy over variables from the original code
            ctx.Body.Variables.Clear();
            foreach (var variableDefinition in dmd.Definition.Body.Variables)
                ctx.Body.Variables.Add(new VariableDefinition(ctx.Module.ImportReference(variableDefinition.VariableType)));

            var transpiler = GetTranspiler(standin);

            if(transpiler != null)
                manipulator.AddTranspiler(transpiler);

            manipulator.WriteTo(ctx.Body, standin);

            // Write a ret in case it got removed (wrt. HarmonyManipulator)
            ctx.IL.Emit(OpCodes.Ret);
        }

        /// <summary>Applies the patch</summary>
        ///
        public void Patch(HarmonyReversePatchType type = HarmonyReversePatchType.Original)
        {
            if (original == null)
                throw new NullReferenceException($"Null method for {instance.Id}");

            // TODO: Wrapped type (do we even need it?)
            ilHook.Apply();
        }

        private MethodInfo GetTranspiler(MethodInfo method)
        {
            var methodName = method.Name;
            var type = method.DeclaringType;
            var methods = AccessTools.GetDeclaredMethods(type);
            var ici = typeof(IEnumerable<CodeInstruction>);
            return methods.FirstOrDefault(m => m.ReturnType == ici && m.Name.StartsWith($"<{methodName}>"));
        }
    }
}