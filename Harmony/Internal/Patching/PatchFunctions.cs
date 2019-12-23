using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib.Internal.CIL;
using HarmonyLib.Internal.Native;
using MonoMod.Cil;

namespace HarmonyLib.Internal.Patching
{
    /// <summary>Patch function helpers</summary>
    internal static class PatchFunctions
    {
        /// <summary>Gets all instructions from a method</summary>
        /// <param name="generator">The generator (for defining labels)</param>
        /// <param name="method">The original method</param>
        /// <returns>The instructions</returns>
        ///
        internal static List<ILInstruction> GetInstructions(ILGenerator generator, MethodBase method)
        {
            return MethodBodyReader.GetInstructions(generator, method);
        }

        /// <summary>Gets sorted patch methods</summary>
        /// <param name="original">The original method</param>
        /// <param name="patches">Patches to sort</param>
        /// <returns>The sorted patch methods</returns>
        ///
        internal static List<MethodInfo> GetSortedPatchMethods(MethodBase original, Patch[] patches)
        {
            return new PatchSorter(patches).Sort(original);
        }

        internal static ILContext.Manipulator CreateManipulator(MethodBase original, PatchInfo patchInfo)
        {
            // We need to include the original method in order to obtain the patch info during patching
            return il => Manipulate(original, patchInfo, il);
        }

        private static void Manipulate(MethodBase original, PatchInfo patchInfo, ILContext ctx)
        {
            Console.WriteLine($"Manipulating {original} with {patchInfo} in ctx {ctx}");
            // TODO: Port Harmony wrapper gen into the manipulator
        }

        /// <summary>Creates new dynamic method with the latest patches and detours the original method</summary>
        /// <param name="original">The original method</param>
        /// <param name="patchInfo">Information describing the patches</param>
        /// <param name="instanceID">Harmony ID</param>
        /// <returns>The newly created dynamic method</returns>
        ///
        internal static DynamicMethod UpdateWrapper(MethodBase original, PatchInfo patchInfo, string instanceID)
        {
            var sortedPrefixes = GetSortedPatchMethods(original, patchInfo.prefixes);
            var sortedPostfixes = GetSortedPatchMethods(original, patchInfo.postfixes);
            var sortedTranspilers = GetSortedPatchMethods(original, patchInfo.transpilers);
            var sortedFinalizers = GetSortedPatchMethods(original, patchInfo.finalizers);

            var replacement = MethodPatcher.CreatePatchedMethod(original, null, instanceID, sortedPrefixes,
                                                                sortedPostfixes, sortedTranspilers, sortedFinalizers);
            if (replacement == null)
                throw new MissingMethodException("Cannot create dynamic replacement for " + original.FullDescription());

            var errorString = Memory.DetourMethod(original, replacement);
            if (errorString != null)
                throw new FormatException("Method " + original.FullDescription() + " cannot be patched. Reason: " +
                                          errorString);

            PatchTools.RememberObject(original, replacement); // no gc for new value + release old value to gc

            return replacement;
        }

        internal static void ReversePatch(MethodInfo standin, MethodBase original, string instanceID,
                                          MethodInfo transpiler)
        {
            var emptyFixes = new List<MethodInfo>();
            var transpilers = new List<MethodInfo>();
            if (transpiler != null)
                transpilers.Add(transpiler);

            var replacement =
                MethodPatcher.CreatePatchedMethod(standin, original, instanceID, emptyFixes, emptyFixes, transpilers,
                                                  emptyFixes);
            if (replacement == null)
                throw new MissingMethodException("Cannot create dynamic replacement for " + standin.FullDescription());

            var errorString = Memory.DetourMethod(standin, replacement);
            if (errorString != null)
                throw new FormatException("Method " + standin.FullDescription() + " cannot be patched. Reason: " +
                                          errorString);

            PatchTools.RememberObject(standin, replacement);
        }
    }
}