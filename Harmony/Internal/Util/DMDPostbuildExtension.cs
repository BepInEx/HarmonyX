using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        private static FieldInfo AssemblyCacheField = AccessTools.Field(typeof(ReflectionHelper), "AssemblyCache");

        private static readonly Dictionary<MethodBase, MethodBase> realMethodMap =
            new Dictionary<MethodBase, MethodBase>();

        private static Func<Assembly> _realGetAss;

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

            new Hook(typeof(ILHook).GetNestedType("Context", BindingFlags.NonPublic).GetMethod("Refresh"),
                     typeof(DMDPostbuildExtension).GetMethod(nameof(OnILChainRefresh))).Apply();

            var nat = new NativeDetour(typeof(Assembly).GetMethod(nameof(Assembly.GetExecutingAssembly)),
                                       typeof(DMDPostbuildExtension).GetMethod(nameof(GetAssemblyFix)));
            nat.Apply();
            _realGetAss = nat.GenerateTrampoline<Func<Assembly>>();

            new Hook(typeof(StackFrame).GetMethod(nameof(StackFrame.GetMethod)),
                     typeof(DMDPostbuildExtension).GetMethod(nameof(GetMethodFix))).Apply();

            applied = true;
        }

        // Fix StackFrame's GetMethod to map patched method to unpatched one instead
        public static MethodBase GetMethodFix(Func<StackFrame, MethodBase> orig, StackFrame self)
        {
            var m = orig(self);
            if (m == null)
                return null;
            lock (realMethodMap)
            {
                return realMethodMap.TryGetValue(m, out var real) ? real : m;
            }
        }

        // We need to force GetExecutingAssembly make use of stack trace
        // This is to fix cases where calling assembly is actually the patch
        // This solves issues with code where it uses the method to get current filepath etc
        public static Assembly GetAssemblyFix()
        {
            return new StackFrame(1).GetMethod()?.Module.Assembly ?? _realGetAss();
        }

        // Helper to save the detour info after patch is complete
        public static void OnILChainRefresh(Action<object> orig, object self)
        {
            orig(self);

            if (!(AccessTools.Field(self.GetType(), "Detour").GetValue(self) is Detour detour))
                return;

            lock (realMethodMap)
            {
                realMethodMap[detour.Target] = detour.Method;
            }
        }

        public static unsafe MethodInfo FixupAccessOldMono(MethodInfo mi)
        {
            if (mi == null)
                return null;

            if (_IsMono)
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
                    lock (fmap_mono_assembly)
                    {
                        if (!fmap_mono_assembly.TryGetValue(asmType, out f_mono_assembly))
                        {
                            f_mono_assembly =
                                asmType.GetField("_mono_assembly",
                                                 BindingFlags.NonPublic | BindingFlags.Public |
                                                 BindingFlags.Instance) ??
                                asmType.GetField("dynamic_assembly",
                                                 BindingFlags.NonPublic | BindingFlags.Public |
                                                 BindingFlags.Instance);

                            fmap_mono_assembly[asmType] = f_mono_assembly;
                        }
                    }

                    if (f_mono_assembly == null)
                        return mi;

                    // Assembly builders are special: marking them with corlib_internal will hide them from AppDomain.GetAssemblies() result
                    // We fix MonoMod by adding them to the cache manually
                    var assCache = AssemblyCacheField.GetValue(null) as Dictionary<string, Assembly>;

                    // Force parsing of AssemblyName to go through managed side instead of a possible icall
                    var assName = new AssemblyName(asm.FullName);
                    if(assCache != null && asmType.FullName == "System.Reflection.Emit.AssemblyBuilder")
                        lock (assCache)
                        {
                            assCache[assName.FullName] = asm;
                            assCache[assName.Name] = asm;
                        }

                    var asmPtrO = f_mono_assembly.GetValue(asm);

                    var offs = 0;

                    if (_IsOldMonoSRE)
                        offs =
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
                    else if (_IsNewMonoSRE)
                        offs =
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

                    if (offs == 0)
                        return mi;

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