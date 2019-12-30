using System.Collections.Generic;
using System.Reflection;
using HarmonyLib.Internal.Patching;

namespace HarmonyLib.Internal.Util
{
    internal static class PatchSortExtensions
    {
        /// <summary>Gets sorted patch methods</summary>
        /// <param name="original">The original method</param>
        /// <param name="patches">Patches to sort</param>
        /// <returns>The sorted patch methods</returns>
        internal static List<MethodInfo> Sort(this Patch[] patches, MethodBase original = null)
        {
            return new PatchSorter(patches).Sort(original);
        }
    }
}