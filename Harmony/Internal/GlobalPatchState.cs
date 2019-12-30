using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib.Internal.Patching;
using MonoMod.RuntimeDetour;

namespace HarmonyLib.Internal
{
    internal static class GlobalPatchState
    {
        private static readonly Dictionary<MethodBase, PatchInfo> PatchInfos = new Dictionary<MethodBase, PatchInfo>();
        private static readonly Dictionary<MethodBase, ILHook> ILHooks = new Dictionary<MethodBase, ILHook>();

        public static ILHook GetILHook(this MethodBase methodBase)
        {
            lock (ILHooks)
            {
                if (ILHooks.TryGetValue(methodBase, out var ilHook))
                    return ilHook;
                return ILHooks[methodBase] = new ILHook(
                    methodBase, HarmonyManipulator.Create(methodBase, methodBase.ToPatchInfo()), new ILHookConfig
                    {
                        ManualApply = true // Always apply manually to prevent unneeded manipulation
                    });
            }
        }

        public static PatchInfo GetPatchInfo(this MethodBase methodBase)
        {
            return PatchInfos.GetValueSafe(methodBase);
        }

        public static PatchInfo ToPatchInfo(this MethodBase methodBase)
        {
            lock (PatchInfos)
            {
                if (PatchInfos.TryGetValue(methodBase, out var info))
                    return info;

                return PatchInfos[methodBase] = new PatchInfo();
            }
        }

        public static IEnumerable<MethodBase> GetPatchedMethods()
        {
            return PatchInfos.Keys.AsEnumerable();
        }
    }
}