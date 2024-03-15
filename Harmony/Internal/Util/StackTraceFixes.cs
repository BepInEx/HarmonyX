using HarmonyLib.Internal.Util;
using HarmonyLib.Public.Patching;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using System;
using System.Diagnostics;
using System.Reflection;
using MonoMod.Core.Platforms;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using System.Linq;

namespace HarmonyLib.Internal.RuntimeFixes
{
    /// <summary>
    /// Patching methods potentially messes up the stack.
    /// </summary>
    internal static class StackTraceFixes
    {
        private static bool _applied;

        public static void Install()
        {
            if (_applied)
                return;

            DetourManager.ILHookApplied += OnILChainRefresh;
            DetourManager.ILHookUndone += OnILChainRefresh;

            _applied = true;
        }

        // Helper to save the detour info after patch is complete
        private static void OnILChainRefresh(ILHookInfo self)
        {
            PatchManager.AddReplacementOriginal(
	            PlatformTriple.Current.GetIdentifiable(self.Method.Method),
	            PlatformTriple.Current.GetIdentifiable(self.Method.GetEndOfChain())
	         );
        }

        static Assembly GetExecutingAssemblyReplacement()
        {
	        var frames = new StackTrace().GetFrames();
	        if (frames?.Skip(1).FirstOrDefault() is { } frame && Harmony.GetOriginalMethodFromStackframe(frame) is { } original)
		        return original.Module.Assembly;
	        return Assembly.GetExecutingAssembly();
        }

        private static MethodBase GetMethodReplacement(StackFrame self)
        {
	        var method = self.GetMethod();
	        var original = PatchManager.GetOriginal(PlatformTriple.Current.GetIdentifiable(method) as MethodInfo);
	        return original ?? method;
        }

        // ReSharper disable InconsistentNaming
        private static readonly MethodInfo GetExecutingAssembly_MethodInfo =
	        SymbolExtensions.GetMethodInfo(() => Assembly.GetExecutingAssembly());
        private static readonly MethodInfo GetExecutingAssemblyReplacement_MethodInfo =
	        SymbolExtensions.GetMethodInfo(() => GetExecutingAssemblyReplacement());
        private static readonly MethodInfo GetMethod_MethodInfo =
	        AccessTools.DeclaredMethod(typeof(StackFrame), nameof(StackFrame.GetMethod), Type.EmptyTypes);
        private static readonly MethodInfo GetMethodReplacement_MethodInfo =
	        SymbolExtensions.GetMethodInfo(() => GetMethodReplacement(null));
        // ReSharper restore InconsistentNaming

        internal static void FixStackTrace(ILContext il)
        {
	        MethodReference getExecutingAssemblyReplacement = null;
	        MethodReference getMethodReplacement = null;

	        var c = new ILCursor(il);

	        foreach (var instr in c.Instrs)
	        {
		        if (instr.MatchCall(GetExecutingAssembly_MethodInfo))
		        {
			        getExecutingAssemblyReplacement ??= il.Import(GetExecutingAssemblyReplacement_MethodInfo);
			        instr.Operand = getExecutingAssemblyReplacement;
		        }
		        else if (instr.MatchCallvirt(GetMethod_MethodInfo))
		        {
			        getMethodReplacement ??= il.Import(GetMethodReplacement_MethodInfo);
			        instr.OpCode = OpCodes.Call;
			        instr.Operand = getMethodReplacement;
		        }
	        }
        }

    }
}
