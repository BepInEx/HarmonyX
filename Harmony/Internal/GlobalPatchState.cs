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
        private static readonly Dictionary<MethodBase, MethodPatcher> MethodPatchers = new Dictionary<MethodBase, MethodPatcher>();

        public static MethodPatcher GetMethodPatcher(this MethodBase methodBase)
        {
            lock (MethodPatchers)
            {
                if (MethodPatchers.TryGetValue(methodBase, out var methodPatcher))
                    return methodPatcher;
                return MethodPatchers[methodBase] = MethodPatcher.Create(methodBase);
            }
        }

        public static PatchInfo GetPatchInfo(this MethodBase methodBase)
        {
            lock (PatchInfos)
            {
                return PatchInfos.GetValueSafe(methodBase);
            }
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
            lock (PatchInfos)
            {
                return PatchInfos.Keys.ToList();
            }
        }
    }
}