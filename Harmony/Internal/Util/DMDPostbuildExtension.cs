using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;

namespace HarmonyLib.Internal.Util
{
    public static class DMDPostbuildExtension
    {
        private static bool applied;
        internal static readonly bool _IsMono = Type.GetType("Mono.Runtime") != null;

        internal static readonly bool _IsNewMonoSRE =
            _IsMono && typeof(DynamicMethod).GetField("il_info", BindingFlags.NonPublic | BindingFlags.Instance) !=
            null;

        internal static readonly bool _IsOldMonoSRE = _IsMono && !_IsNewMonoSRE &&
                                                      typeof(DynamicMethod).GetField(
                                                          "ilgen", BindingFlags.NonPublic | BindingFlags.Instance) !=
                                                      null;

        internal static readonly Dictionary<Type, FieldInfo> fmap_mono_assembly = new Dictionary<Type, FieldInfo>();

        public static void Install()
        {
            if (applied)
                return;
            var fixup = AccessTools.Method(typeof(DMDPostbuildExtension), nameof(FixupAccessOldMono));

#if !NETSTANDARD2_1
            new NativeDetour(AccessTools.Method(typeof(DMDGenerator<DMDEmitMethodBuilderGenerator>), "_Postbuild"),
                             fixup).Apply();
#endif
            new NativeDetour(AccessTools.Method(typeof(DMDGenerator<DMDCecilGenerator>), "_Postbuild"), fixup).Apply();
            applied = true;
        }

        public static unsafe MethodInfo FixupAccessOldMono(MethodInfo mi)
        {
            if (mi == null)
                return null;

            if (_IsMono)
                if (!(mi is DynamicMethod) && mi.DeclaringType != null)
                {
                    // Mono doesn't know about IgnoresAccessChecksToAttribute,
                    // but it lets some assemblies have unrestricted access.

                    if (_IsOldMonoSRE)
                    {
                        // If you're reading this:
                        // You really should've chosen the SRE backend instead...

                        var module = mi?.Module;
                        if (module == null)
                            return mi;
                        var asm = module.Assembly; // Let's hope that this doesn't get optimized into a call.
                        var asmType = asm?.GetType();
                        if (asmType == null)
                            return mi;

                        // _mono_assembly has changed places between Mono versions.
                        FieldInfo f_mono_assembly;
                        lock (fmap_mono_assembly)
                        {
                            if (!fmap_mono_assembly.TryGetValue(asmType, out f_mono_assembly))
                            {
                                f_mono_assembly = asmType.GetField("_mono_assembly",
                                                                   BindingFlags.NonPublic | BindingFlags.Public |
                                                                   BindingFlags.Instance);
                                fmap_mono_assembly[asmType] = f_mono_assembly;
                            }
                        }

                        if (f_mono_assembly == null)
                            return mi;

                        var asmPtr = (IntPtr) f_mono_assembly.GetValue(asm);
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
                            17 + 3 +
                            // hash_alg
                            4 +
                            // hash_len
                            4 +
                            // flags
                            4 +

                            // major, minor, build, revision
                            2 + 2 + 2 + 2 +

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
                        var corlibInternalPtr = (byte*) ((long) asmPtr + offs);
                        *corlibInternalPtr = 1;

                        return mi;
                    }
                    else
                    {
                        // https://github.com/mono/mono/blob/df846bcbc9706e325f3b5dca4d09530b80e9db83/mono/metadata/metadata-internals.h#L207
                        // https://github.com/mono/mono/blob/1af992a5ffa46e20dd61a64b6dcecef0edb5c459/mono/metadata/appdomain.c#L1286
                        // https://github.com/mono/mono/blob/beb81d3deb068f03efa72be986c96f9c3ab66275/mono/metadata/class.c#L5748
                        // https://github.com/mono/mono/blob/83fc1456dbbd3a789c68fe0f3875820c901b1bd6/mcs/class/corlib/System.Reflection/Assembly.cs#L96
                        // https://github.com/mono/mono/blob/cf69b4725976e51416bfdff22f3e1834006af00a/mcs/class/corlib/System.Reflection/RuntimeAssembly.cs#L59
                        // https://github.com/mono/mono/blob/cf69b4725976e51416bfdff22f3e1834006af00a/mcs/class/corlib/System.Reflection.Emit/AssemblyBuilder.cs#L247

                        // get_Assembly is virtual in some versions of Mono (notably older ones and the infamous Unity fork).
                        // ?. results in a call instead of callvirt to skip a redundant nullcheck, which breaks this on ^...
                        var module = mi?.Module;
                        if (module == null)
                            return mi;
                        var asm = module.Assembly; // Let's hope that this doesn't get optimized into a call.
                        var asmType = asm?.GetType();
                        if (asmType == null)
                            return mi;

                        // _mono_assembly has changed places between Mono versions.
                        FieldInfo f_mono_assembly;
                        lock (fmap_mono_assembly)
                        {
                            if (!fmap_mono_assembly.TryGetValue(asmType, out f_mono_assembly))
                            {
                                f_mono_assembly = asmType.GetField("_mono_assembly",
                                                                   BindingFlags.NonPublic | BindingFlags.Public |
                                                                   BindingFlags.Instance);
                                fmap_mono_assembly[asmType] = f_mono_assembly;
                            }
                        }

                        if (f_mono_assembly == null)
                            return mi;

                        var asmPtr = (IntPtr) f_mono_assembly.GetValue(asm);
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

                            // major, minor, build, revision, arch (10 framework / 20 core + padding)
                            (typeof(object).Assembly.GetName().Name == "System.Private.CoreLib" ? IntPtr.Size == 4
                                    ?
                                    20
                                    : 24 :
                                IntPtr.Size == 4 ? 12 : 16) +

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
                        var corlibInternalPtr = (byte*) ((long) asmPtr + offs);
                        *corlibInternalPtr = 1;
                    }
                }

            return mi;
        }
    }
}