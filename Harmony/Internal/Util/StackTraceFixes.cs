using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib.Tools;
using MonoMod.Core.Platforms;
using MonoMod.RuntimeDetour;

namespace HarmonyLib.Internal.RuntimeFixes
{
    /// <summary>
    /// Patching methods potentially messes up the stack.
    /// </summary>
    internal static class StackTraceFixes
    {
        private static bool _applied;

        private static readonly Dictionary<MethodBase, MethodBase> RealMethodMap = new();

        private static Hook getMethodHook;

        public static void Install()
        {
            if (_applied)
                return;

            try
            {
	            DetourManager.ILHookApplied += OnILChainRefresh;
	            DetourManager.ILHookUndone += OnILChainRefresh;

	            getMethodHook = new Hook(AccessTools.DeclaredMethod(typeof(StackFrame), nameof(StackFrame.GetMethod), Type.EmptyTypes), GetMethodFix);
            }
            catch (Exception e)
            {
	            Logger.LogText(Logger.LogChannel.Error, $"Failed to apply stack trace fix: ({e.GetType().FullName}) {e.Message}");
            }
            _applied = true;
        }

        private static MethodBase GetMethodFix(Func<StackFrame, MethodBase> orig, StackFrame self)
        {
	        var method = orig(self);
	        if (method is not null && RealMethodMap.TryGetValue(PlatformTriple.Current.GetIdentifiable(method), out var real))
	        {
		        return real;
	        }
	        return method;
        }

        private static readonly AccessTools.FieldRef<MethodDetourInfo, object> GetDetourState = AccessTools.FieldRefAccess<MethodDetourInfo, object>(AccessTools.DeclaredField(typeof(MethodDetourInfo), "state"));

        private static readonly AccessTools.FieldRef<object, MethodBase> GetEndOfChain =
	        AccessTools.FieldRefAccess<object, MethodBase>(AccessTools.DeclaredField(typeof(DetourManager).GetNestedType("ManagedDetourState", AccessTools.all), "EndOfChain"));

        // Helper to save the detour info after patch is complete
        private static void OnILChainRefresh(ILHookInfo self)
        {
            lock (RealMethodMap)
            {
                RealMethodMap[PlatformTriple.Current.GetIdentifiable(GetEndOfChain(GetDetourState(self.Method)))] = PlatformTriple.Current.GetIdentifiable(self.Method.Method);
            }
        }
    }
}
