using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib.Internal.Util;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;

namespace HarmonyLib.Internal.RuntimeFixes
{
    /// <summary>
    /// Fixes for MonoMod visibility checks.
    ///
    /// While MonoMod supports visibility skipping, currently targeted version does not support skipping visibility for
    /// MethodBuilders.
    /// </summary>
    internal static class VisibilityCheckFixes
    {
        private static bool _applied;
        private static readonly bool IsMono = Type.GetType("Mono.Runtime") != null;

        private static readonly bool IsNewMonoSre =
            IsMono && typeof(DynamicMethod).GetField("il_info", BindingFlags.NonPublic | BindingFlags.Instance) != null;

        private static readonly bool IsOldMonoSre = IsMono && !IsNewMonoSre &&
                                                    typeof(DynamicMethod).GetField(
                                                        "ilgen", BindingFlags.NonPublic | BindingFlags.Instance) !=
                                                    null;

        private static readonly Dictionary<Type, FieldInfo> FmapMonoAssembly = new Dictionary<Type, FieldInfo>();

        private static readonly FieldInfo AssemblyCacheField =
            AccessTools.Field(typeof(ReflectionHelper), "AssemblyCache");

        private static readonly bool MonoAssemblyNameHasArch = new AssemblyName("Dummy, ProcessorArchitecture=MSIL").ProcessorArchitecture == ProcessorArchitecture.MSIL;

        public static void Install()
        {
            if (_applied)
                return;

            var fixup = AccessTools.Method(typeof(VisibilityCheckFixes), nameof(FixupAccessOldMono));

#if !NETSTANDARD
            new NativeDetour(AccessTools.Method(typeof(DMDGenerator<DMDEmitMethodBuilderGenerator>), "_Postbuild"),
                             fixup).Apply();
#endif
            new NativeDetour(AccessTools.Method(typeof(DMDGenerator<DMDCecilGenerator>), "_Postbuild"), fixup).Apply();

            _applied = true;
        }

        private static unsafe MethodInfo FixupAccessOldMono(MethodInfo mi)
        {
            if (mi == null)
                return null;

            if (IsMono)
                if (!(mi is DynamicMethod) && mi.DeclaringType != null)
                {
                    var module = mi?.Module;
                    if (module == null)
                        return mi;
                    var asm = module.Assembly; // Let's hope that this doesn't get optimized into a call.
                    var asmType = asm?.GetType();
                    if (asmType == null)
                        return mi;

                    FieldInfo f_mono_assembly;
                    lock (FmapMonoAssembly)
                    {
                        if (!FmapMonoAssembly.TryGetValue(asmType, out f_mono_assembly))
                        {
                            f_mono_assembly =
                                asmType.GetField("_mono_assembly",
                                                 BindingFlags.NonPublic | BindingFlags.Public |
                                                 BindingFlags.Instance) ??
                                asmType.GetField("dynamic_assembly",
                                                 BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

                            FmapMonoAssembly[asmType] = f_mono_assembly;
                        }
                    }

                    if (f_mono_assembly == null)
                        return mi;

                    // Assembly builders are special: marking them with corlib_internal will hide them from AppDomain.GetAssemblies() result
                    // We fix MonoMod by adding them to the cache manually
                    var assCache = AssemblyCacheField.GetValue(null) as Dictionary<string, Assembly>;

                    // Force parsing of AssemblyName to go through managed side instead of a possible icall
                    var assName = new AssemblyName(asm.FullName);
                    if (assCache != null && asmType.FullName == "System.Reflection.Emit.AssemblyBuilder")
                        lock (assCache)
                        {
                            assCache[assName.FullName] = asm;
                            assCache[assName.Name] = asm;
                        }


                    var offs =
                        // ref_count (4 + padding)
                        IntPtr.Size +
                        // basedir
                        IntPtr.Size +

                        // aname
                        // name
                        IntPtr.Size +
                        // culture
                        IntPtr.Size +
                        // hash_value
                        IntPtr.Size +
                        // public_key
                        IntPtr.Size +
                        // public_key_token (17 + padding)
                        20 +
                        // hash_alg
                        4 +
                        // hash_len
                        4 +
                        // flags
                        4 +

                        // major, minor, build, revision[, arch] (10 framework / 20 core + padding)
                        (
                            !MonoAssemblyNameHasArch ? (
                                typeof(object).Assembly.GetName().Name == "System.Private.CoreLib" ?
                                    16 : 8
                            ) : (
                                typeof(object).Assembly.GetName().Name == "System.Private.CoreLib" ?
                                    (IntPtr.Size == 4 ? 20 : 24) :
                                    (IntPtr.Size == 4 ? 12 : 16)
                            )
                        ) +

                        // image
                        IntPtr.Size +
                        // friend_assembly_names
                        IntPtr.Size +
                        // friend_assembly_names_inited
                        1 +
                        // in_gac
                        1 +
                        // dynamic
                        1;

                    var asmPtrO = f_mono_assembly.GetValue(asm);
                    byte* corlibInternalPtr = null;

                    if (asmPtrO is IntPtr asmIPtr)
                        corlibInternalPtr = (byte*) ((long) asmIPtr + offs);
                    else if (asmPtrO is UIntPtr asmUPtr)
                        corlibInternalPtr = (byte*) ((long) asmUPtr + offs);

                    if (corlibInternalPtr == null)
                        return mi;

                    *corlibInternalPtr = 1;
                }

            return mi;
        }
    }
}