using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib.Tools;
using MonoMod.RuntimeDetour;
using System.Linq;

namespace HarmonyLib.Internal.RuntimeFixes
{
    /// <summary>
    /// Patching methods potentially messes up the stack.
    /// Especially calls to GetExecutingAssembly won't turn in correct methods
    /// </summary>
    internal static class StackTraceFixes
    {
        private static bool _applied;

        private static readonly Dictionary<MethodBase, MethodBase> RealMethodMap =
            new Dictionary<MethodBase, MethodBase>();

        private static Hook stackTraceHook;

        public static void Install()
        {
            if (_applied)
                return;

            try
            {
	            DetourManager.ILHookApplied += OnILChainRefresh;
	            DetourManager.ILHookUndone += OnILChainRefresh;

	            stackTraceHook = new Hook(AccessTools.Method(typeof(Assembly), nameof(Assembly.GetExecutingAssembly)),
		            AccessTools.Method(typeof(StackTraceFixes), nameof(GetAssemblyFix)));
            }
            catch (Exception e)
            {
	            Logger.LogText(Logger.LogChannel.Error, $"Failed to apply stack trace fix: ({e.GetType().FullName}) {e.Message}");
            }
            _applied = true;
        }

        // We need to force GetExecutingAssembly make use of stack trace
        // This is to fix cases where calling assembly is actually the patch
        // This solves issues with code where it uses the method to get current filepath etc
        private static Assembly GetAssemblyFix(Func<Assembly> orig)
        {
	        var method = new StackTrace().GetFrames()!.SkipWhile(frame => frame.GetMethod() != stackTraceHook.DetourInfo.Entry).Skip(1).First().GetMethod();
	        if (RealMethodMap.TryGetValue(method, out var real))
	        {
		        return real.Module.Assembly;
	        }
	        return orig();
        }

        private static readonly AccessTools.FieldRef<MethodDetourInfo, object> GetDetourState = AccessTools.FieldRefAccess<MethodDetourInfo, object>(AccessTools.DeclaredField(typeof(MethodDetourInfo), "state"));

        private static readonly AccessTools.FieldRef<object, MethodBase> GetEndOfChain =
	        AccessTools.FieldRefAccess<object, MethodBase>(AccessTools.DeclaredField(typeof(DetourManager).GetNestedType("ManagedDetourState", AccessTools.all), "EndOfChain"));

        // Helper to save the detour info after patch is complete
        private static void OnILChainRefresh(ILHookInfo self)
        {
            lock (RealMethodMap)
            {
                RealMethodMap[GetEndOfChain(GetDetourState(self.Method))] = self.Method.Method;
            }
        }
    }
}
