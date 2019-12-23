using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace HarmonyLib
{
    internal static class GlobalPatchState
    {
        static Dictionary<MethodBase, PatchInfo> patchInfos = new Dictionary<MethodBase, PatchInfo>();

        public static PatchInfo GetPatchInfo(this MethodBase methodBase)
        {
            return patchInfos.GetValueSafe(methodBase);
        }

        public static PatchInfo ToPatchInfo(this MethodBase methodBase)
        {
            if (patchInfos.TryGetValue(methodBase, out var info))
                return info;
            lock (patchInfos)
            {
                return patchInfos[methodBase] = new PatchInfo();
            }
        }

        public static IEnumerable<MethodBase> GetPatchedMethods()
        {
            return patchInfos.Keys.AsEnumerable();
        }
    }
}