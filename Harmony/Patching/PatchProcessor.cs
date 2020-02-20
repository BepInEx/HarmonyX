using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib.Internal;
using HarmonyLib.Internal.Patching;
using HarmonyLib.Internal.RuntimeFixes;
using HarmonyLib.Tools;
using MonoMod.Utils;
using MonoMod.Utils.Cil;

namespace HarmonyLib
{
    /// <summary>A patch processor</summary>
    public class PatchProcessor
    {
        private readonly Harmony instance;

        private readonly Type container;
        private readonly HarmonyMethod containerAttributes;

        private readonly List<MethodBase> originals = new List<MethodBase>();
        private HarmonyMethod prefix;
        private HarmonyMethod postfix;
        private HarmonyMethod transpiler;
        private HarmonyMethod finalizer;

        static PatchProcessor()
        {
            StackTraceFixes.Install();
            VisibilityCheckFixes.Install();
        }

        /// <summary>Creates an empty patch processor</summary>
        /// <param name="instance">The Harmony instance</param>
        /// <param name="original">An optional original method</param>
        ///
        public PatchProcessor(Harmony instance, MethodBase original = null)
        {
            this.instance = instance;
            if (original != null)
                originals.Add(original);
        }

        /// <summary>Creates a patch processor</summary>
        /// <param name="instance">The Harmony instance</param>
        /// <param name="type">The patch class</param>
        /// <param name="attributes">The Harmony attributes</param>
        ///
        public PatchProcessor(Harmony instance, Type type, HarmonyMethod attributes)
        {
            this.instance = instance;
            container = type;
            containerAttributes = attributes ?? new HarmonyMethod();
            prefix = containerAttributes.Clone();
            postfix = containerAttributes.Clone();
            transpiler = containerAttributes.Clone();
            finalizer = containerAttributes.Clone();
            PrepareType();
        }

        /// <summary>Creates a patch processor</summary>
        /// <param name="instance">The Harmony instance.</param>
        /// <param name="originals">The original methods</param>
        /// <param name="prefix">The optional prefix.</param>
        /// <param name="postfix">The optional postfix.</param>
        /// <param name="transpiler">The optional transpiler.</param>
        /// <param name="finalizer">The optional finalizer.</param>
        [Obsolete("Use other constructors and Add* methods")]
        public PatchProcessor(Harmony instance,
                              List<MethodBase> originals,
                              HarmonyMethod prefix = null,
                              HarmonyMethod postfix = null,
                              HarmonyMethod transpiler = null,
                              HarmonyMethod finalizer = null)
        {
            this.instance = instance;
            this.originals = originals;
            this.prefix = prefix;
            this.postfix = postfix;
            this.transpiler = transpiler;
            this.finalizer = finalizer;
        }

        /// <summary>Add an original method</summary>
        /// <param name="original">The method that will be patched.</param>
        ///
        public PatchProcessor AddOriginal(MethodBase original)
        {
            if (originals.Contains(original) == false)
                originals.Add(original);
            return this;
        }

        /// <summary>Sets the original methods</summary>
        /// <param name="originals">The methods that will be patched.</param>
        ///
        public PatchProcessor SetOriginals(List<MethodBase> originals)
        {
            this.originals.Clear();
            this.originals.AddRange(originals);
            return this;
        }

        /// <summary>Add a prefix</summary>
        /// <param name="prefix">The prefix.</param>
        ///
        public PatchProcessor AddPrefix(HarmonyMethod prefix)
        {
            this.prefix = prefix;
            return this;
        }

        /// <summary>Add a prefix</summary>
        /// <param name="fixMethod">The method.</param>
        ///
        public PatchProcessor AddPrefix(MethodInfo fixMethod)
        {
            prefix = new HarmonyMethod(fixMethod);
            return this;
        }

        /// <summary>Add a postfix</summary>
        /// <param name="postfix">The postfix.</param>
        ///
        public PatchProcessor AddPostfix(HarmonyMethod postfix)
        {
            this.postfix = postfix;
            return this;
        }

        /// <summary>Add a postfix</summary>
        /// <param name="fixMethod">The method.</param>
        ///
        public PatchProcessor AddPostfix(MethodInfo fixMethod)
        {
            postfix = new HarmonyMethod(fixMethod);
            return this;
        }

        /// <summary>Add a transpiler</summary>
        /// <param name="transpiler">The transpiler.</param>
        ///
        public PatchProcessor AddTranspiler(HarmonyMethod transpiler)
        {
            this.transpiler = transpiler;
            return this;
        }

        /// <summary>Add a transpiler</summary>
        /// <param name="fixMethod">The method.</param>
        ///
        public PatchProcessor AddTranspiler(MethodInfo fixMethod)
        {
            transpiler = new HarmonyMethod(fixMethod);
            return this;
        }

        /// <summary>Add a finalizer</summary>
        /// <param name="finalizer">The finalizer.</param>
        ///
        public PatchProcessor AddFinalizer(HarmonyMethod finalizer)
        {
            this.finalizer = finalizer;
            return this;
        }

        /// <summary>Add a finalizer</summary>
        /// <param name="fixMethod">The method.</param>
        ///
        public PatchProcessor AddFinalizer(MethodInfo fixMethod)
        {
            finalizer = new HarmonyMethod(fixMethod);
            return this;
        }

        /// <summary>Gets patch information</summary>
        /// <param name="method">The original method</param>
        /// <returns>The patch information</returns>
        ///
        public static Patches GetPatchInfo(MethodBase method)
        {
            var patchInfo = method.GetPatchInfo();
            if (patchInfo == null)
                return null;
            lock (patchInfo)
                return new Patches(patchInfo.prefixes, patchInfo.postfixes, patchInfo.transpilers,
                                   patchInfo.finalizers);
        }

        /// <summary>Gets Harmony version for all active Harmony instances</summary>
        /// <param name="currentVersion">[out] The current Harmony version</param>
        /// <returns>A dictionary containing assembly versions keyed by Harmony IDs</returns>
        ///
        public static Dictionary<string, Version> VersionInfo(out Version currentVersion)
        {
            currentVersion = typeof(Harmony).Assembly.GetName().Version;
            var assemblies = new Dictionary<string, Assembly>();

            void AddAssemblies(IEnumerable<Patch> patches)
            {
                foreach (var patch in patches)
                    assemblies[patch.owner] = patch.patch.DeclaringType?.Assembly;
            }

            foreach (var method in GetAllPatchedMethods())
            {
                var info = GetPatchInfo(method);

                AddAssemblies(info.Prefixes);
                AddAssemblies(info.Postfixes);
                AddAssemblies(info.Transpilers);
                AddAssemblies(info.Finalizers);
            }

            var result = new Dictionary<string, Version>();

            foreach (var info in assemblies)
            {
                var assemblyName = info.Value.GetReferencedAssemblies()
                                       .FirstOrDefault(
                                            a => a.FullName.StartsWith("0Harmony, Version", StringComparison.Ordinal));
                if (assemblyName != null)
                    result[info.Key] = assemblyName.Version;
            }
            return result;
        }

        /// <summary>Returns the methods unmodified list of CodeInstructions</summary>
        /// <param name="original">The original method</param>
        /// <param name="generator">Optionally an existing generator that will be used to create all local variables and labels contained in the result (if not specified, an internal generator is used)</param>
        /// <returns>A list containing all the original CodeInstructions</returns>
        public static List<CodeInstruction> GetOriginalInstructions(MethodBase original, ILGenerator generator = null)
        {
            // Create a copy
            var dmd = new DynamicMethodDefinition(original);
            // Create a manipulator to obtain the instructions
            var manipulator = new ILManipulator(dmd.Definition.Body);
            var il = generator ?? new CecilILGenerator(dmd.GetILProcessor()).GetProxy();
            return manipulator.GetInstructions(il);
        }

        /// <summary>Returns the methods unmodified list of CodeInstructions</summary>
        /// <param name="original">The original method</param>
        /// <param name="generator">A new generator that now contains all local variables and labels contained in the result</param>
        /// <returns>A list containing all the original CodeInstructions</returns>
        public static List<CodeInstruction> GetOriginalInstructions(MethodBase original, out ILGenerator generator)
        {
            // Create a copy
            var dmd = new DynamicMethodDefinition(original);
            // Create a manipulator to obtain the instructions
            var manipulator = new ILManipulator(dmd.Definition.Body);
            generator = new CecilILGenerator(dmd.GetILProcessor()).GetProxy();
            return manipulator.GetInstructions(generator);
        }

        /// <summary>Gets all patched original methods</summary>
        /// <returns>All patched original methods</returns>
        ///
        [Obsolete("Use GetAllPatchedMethods instead")]
        public static IEnumerable<MethodBase> AllPatchedMethods() { return GlobalPatchState.GetPatchedMethods(); }

        /// <summary>Gets all patched original methods</summary>
        /// <returns>All patched original methods</returns>
        ///
        public static IEnumerable<MethodBase> GetAllPatchedMethods() { return GlobalPatchState.GetPatchedMethods(); }


        /// <summary>Applies the patch</summary>
        /// <returns>A list of all created dynamic methods</returns>
        ///
        public List<DynamicMethod> Patch()
        {
            // TODO: Replace return type to MethodInfo

            Stopwatch sw = null;
            Logger.Log(Logger.LogChannel.Info, () =>
            {
                sw = Stopwatch.StartNew();
                return $"Patching {instance.Id}...";
            });

            var dynamicMethods = new List<DynamicMethod>();
            foreach (var original in originals)
            {
                if (original == null)
                    throw new NullReferenceException($"Null method for {instance.Id}");

                Logger.Log(Logger.LogChannel.Info, () => $"Patching {original.GetID()}");

                var individualPrepareResult = RunMethod<HarmonyPrepare, bool>(true, original);

                Logger.Log(Logger.LogChannel.Info, () => $"HarmonyPrepare result: {individualPrepareResult}");

                if (individualPrepareResult)
                {
                    var patchInfo = original.ToPatchInfo();

                    // Lock patch info so we can assign the patches all at once
                    lock (patchInfo)
                    {
                        patchInfo.AddPrefix(instance.Id, prefix);
                        patchInfo.AddPostfix(instance.Id, postfix);
                        patchInfo.AddTranspiler(instance.Id, transpiler);
                        patchInfo.AddFinalizer(instance.Id, finalizer);
                    }

                    original.GetMethodPatcher().Apply();

                    RunMethod<HarmonyCleanup>(original);
                }
            }

            Logger.Log(Logger.LogChannel.Info, () => $"Patching {instance.Id} took {sw.ElapsedMilliseconds}ms");

            return dynamicMethods;
        }

        /// <summary>Unpatches patches of a given type and/or Harmony ID</summary>
        /// <param name="type">The patch type</param>
        /// <param name="harmonyID">Harmony ID or (*) for any</param>
        ///
        public PatchProcessor Unpatch(HarmonyPatchType type, string harmonyID)
        {
            foreach (var original in originals)
            {
                var patchInfo = original.ToPatchInfo();

                lock (patchInfo)
                {
                    if (type == HarmonyPatchType.All || type == HarmonyPatchType.Prefix)
                        patchInfo.RemovePrefix(harmonyID);
                    if (type == HarmonyPatchType.All || type == HarmonyPatchType.Postfix)
                        patchInfo.RemovePostfix(harmonyID);
                    if (type == HarmonyPatchType.All || type == HarmonyPatchType.Transpiler)
                        patchInfo.RemoveTranspiler(harmonyID);
                    if (type == HarmonyPatchType.All || type == HarmonyPatchType.Finalizer)
                        patchInfo.RemoveFinalizer(harmonyID);
                }

                original.GetMethodPatcher().Apply();
            }

            return this;
        }

        /// <summary>Unpatches the given patch</summary>
        /// <param name="patch">The patch</param>
        ///
        public PatchProcessor Unpatch(MethodInfo patch)
        {
            foreach (var original in originals)
            {
                var patchInfo = original.ToPatchInfo();

                patchInfo.RemovePatch(patch);

                original.GetMethodPatcher().Apply();
            }

            return this;
        }

        private void PrepareType()
        {
            var mainPrepareResult = RunMethod<HarmonyPrepare, bool>(true);
            if (mainPrepareResult == false)
                return;

            var originalMethodType = containerAttributes.methodType;

            // MethodType default is Normal
            if (containerAttributes.methodType == null)
                containerAttributes.methodType = MethodType.Normal;

            var reversePatchAttr = typeof(HarmonyReversePatch).FullName;
            var reversePatchMethods = container.GetMethods(AccessTools.all).Where(m => m.GetCustomAttributes(true).Any(a => a.GetType().FullName == reversePatchAttr)).ToList();
            foreach (var reversePatchMethod in reversePatchMethods)
            {
                var attr = containerAttributes.Merge(new HarmonyMethod(reversePatchMethod));
                var originalMethod = GetOriginalMethod(attr);
                var reversePatcher = instance.CreateReversePatcher(originalMethod, reversePatchMethod);
                reversePatcher.Patch();
            }

            var customOriginals = RunMethod<HarmonyTargetMethods, IEnumerable<MethodBase>>(null);
            if (customOriginals != null)
            {
                originals.Clear();
                originals.AddRange(customOriginals);
            }
            else
            {
                var isPatchAll = container.GetCustomAttributes(true).Any(a => a.GetType().FullName == typeof(HarmonyPatchAll).FullName);
                if (isPatchAll)
                {
                    var type = containerAttributes.declaringType;
                    originals.AddRange(AccessTools.GetDeclaredConstructors(type).Cast<MethodBase>());
                    originals.AddRange(AccessTools.GetDeclaredMethods(type).Cast<MethodBase>());
                    var props = AccessTools.GetDeclaredProperties(type);
                    originals.AddRange(props.Select(prop => prop.GetGetMethod(true)).Where(method => method != null)
                                            .Cast<MethodBase>());
                    originals.AddRange(props.Select(prop => prop.GetSetMethod(true)).Where(method => method != null)
                                            .Cast<MethodBase>());
                }
                else
                {
                    var original = RunMethod<HarmonyTargetMethod, MethodBase>(null) ?? GetOriginalMethod(containerAttributes);

                    if (original == null)
                    {
                        var info = "(";
                        info += $"declaringType={containerAttributes.declaringType}, ";
                        info += $"methodName ={containerAttributes.methodName}, ";
                        info += $"methodType={originalMethodType}, ";
                        info += $"argumentTypes={containerAttributes.argumentTypes.Description()}";
                        info += ")";
                        throw new ArgumentException(
                            $"No target method specified for class {container.FullName} {info}");
                    }

                    originals.Add(original);
                }
            }

            GetPatches(container, out var prefixMethod, out var postfixMethod, out var transpilerMethod,
                                  out var finalizerMethod);
            if (prefix != null)
                prefix.method = prefixMethod;
            if (postfix != null)
                postfix.method = postfixMethod;
            if (transpiler != null)
                transpiler.method = transpilerMethod;
            if (finalizer != null)
                finalizer.method = finalizerMethod;

            if (prefixMethod != null)
            {
                if (prefixMethod.IsStatic == false)
                    throw new ArgumentException($"Patch method {prefixMethod.GetID()} must be static");

                var prefixAttributes = HarmonyMethodExtensions.GetFromMethod(prefixMethod);
                containerAttributes.Merge(HarmonyMethod.Merge(prefixAttributes)).CopyTo(prefix);
            }

            if (postfixMethod != null)
            {
                if (postfixMethod.IsStatic == false)
                    throw new ArgumentException($"Patch method {postfixMethod.GetID()} must be static");

                var postfixAttributes = HarmonyMethodExtensions.GetFromMethod(postfixMethod);
                containerAttributes.Merge(HarmonyMethod.Merge(postfixAttributes)).CopyTo(postfix);
            }

            if (transpilerMethod != null)
            {
                if (transpilerMethod.IsStatic == false)
                    throw new ArgumentException($"Patch method {transpilerMethod.GetID()} must be static");

                var transpilerAttributes = HarmonyMethodExtensions.GetFromMethod(transpilerMethod);
                containerAttributes.Merge(HarmonyMethod.Merge(transpilerAttributes)).CopyTo(transpiler);
            }

            if (finalizerMethod != null)
            {
                if (finalizerMethod.IsStatic == false)
                    throw new ArgumentException($"Patch method {finalizerMethod.GetID()} must be static");

                var finalizerAttributes = HarmonyMethodExtensions.GetFromMethod(finalizerMethod);
                containerAttributes.Merge(HarmonyMethod.Merge(finalizerAttributes)).CopyTo(finalizer);
            }
        }

        /// <summary>
        /// Get the member specified by the <paramref name="attribute"/>. Throws if the member was not found.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown if the member described in the <paramref name="attribute"/> couldn't be found.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="attribute"/> is <see langword="null"/></exception>
        internal static MethodBase GetOriginalMethod(HarmonyMethod attribute)
        {
            if (attribute == null) throw new ArgumentNullException(nameof(attribute));

            MethodBase MakeFailure(string reason)
            {
                Logger.Log(Logger.LogChannel.Error, () => $"Failed to process patch {attribute.method?.FullDescription() ?? "Unknown patch"} - {reason}");
                return null;
            }

            if (attribute.declaringType == null)
                return MakeFailure("declaringType cannot be null");

            switch (attribute.methodType)
            {
                case MethodType.Normal:
                    {
                        if (string.IsNullOrEmpty(attribute.methodName))
                            return MakeFailure("methodName can't be empty");

                        if (attribute.methodName == ".ctor")
                        {
                            Logger.LogText(Logger.LogChannel.Warn, "MethodType.Constructor should be used instead of setting methodName to .ctor");
                            goto case MethodType.Constructor;
                        }
                        if (attribute.methodName == ".cctor")
                        {
                            Logger.LogText(Logger.LogChannel.Warn, "MethodType.StaticConstructor should be used instead of setting methodName to .cctor");
                            goto case MethodType.StaticConstructor;
                        }

                        if (attribute.methodName.StartsWith("get_") || attribute.methodName.StartsWith("set_"))
                            Logger.LogText(Logger.LogChannel.Warn, "MethodType.Getter and MethodType.Setter should be used instead adding get_ and set_ to property names");

                        var result = AccessTools.DeclaredMethod(attribute.declaringType, attribute.methodName, attribute.argumentTypes);
                        if (result != null) return result;

                        result = AccessTools.Method(attribute.declaringType, attribute.methodName, attribute.argumentTypes);
                        if (result != null)
                        {
                            Logger.LogText(Logger.LogChannel.Warn, $"Could not find method {attribute.methodName} with {attribute.argumentTypes?.Length ?? 0} parameters in type {attribute.declaringType.FullDescription()}, but it was found in base class of this type {result.DeclaringType.FullDescription()}");
                            return result;
                        }

                        return MakeFailure($"Could not find method {attribute.methodName} with {attribute.argumentTypes.Description()} parameters in type {attribute.declaringType.FullDescription()}");
                    }

                case MethodType.Getter:
                    {
                        if (string.IsNullOrEmpty(attribute.methodName))
                            return MakeFailure("methodName can't be empty");

                        var result = AccessTools.DeclaredProperty(attribute.declaringType, attribute.methodName);
                        if (result != null)
                        {
                            var getter = result.GetGetMethod(true);
                            if (getter == null)
                                return MakeFailure($"Property {attribute.methodName} does not have a Getter");
                            return getter;
                        }

                        result = AccessTools.Property(attribute.declaringType, attribute.methodName);
                        if (result != null)
                        {
                            Logger.LogText(Logger.LogChannel.Warn, $"Could not find property {attribute.methodName} in type {attribute.declaringType.FullDescription()}, but it was found in base class of this type: {result.DeclaringType.FullDescription()}");
                            var getter = result.GetGetMethod(true);
                            if (getter == null)
                                return MakeFailure($"Property {attribute.methodName} does not have a Getter");
                            return getter;
                        }

                        return MakeFailure($"Could not find property {attribute.methodName} in type {attribute.declaringType.FullDescription()}");
                    }

                case MethodType.Setter:
                    {
                        if (string.IsNullOrEmpty(attribute.methodName))
                            return MakeFailure("methodName can't be empty");

                        var result = AccessTools.DeclaredProperty(attribute.declaringType, attribute.methodName);
                        if (result != null)
                        {
                            var getter = result.GetSetMethod(true);
                            if (getter == null)
                                return MakeFailure($"Property {attribute.methodName} does not have a Setter");
                            return getter;
                        }

                        result = AccessTools.Property(attribute.declaringType, attribute.methodName);
                        if (result != null)
                        {
                            Logger.LogText(Logger.LogChannel.Warn, $"Could not find property {attribute.methodName} in type {attribute.declaringType.FullDescription()}, but it was found in base class of this type: {result.DeclaringType.FullDescription()}");
                            var getter = result.GetSetMethod(true);
                            if (getter == null)
                                return MakeFailure($"Property {attribute.methodName} does not have a Setter");
                            return getter;
                        }

                        return MakeFailure($"Could not find property {attribute.methodName} in type {attribute.declaringType.FullDescription()}");
                    }

                case MethodType.Constructor:
                    {
                        var constructor = AccessTools.DeclaredConstructor(attribute.declaringType, attribute.argumentTypes);
                        if (constructor != null) return constructor;

                        return MakeFailure($"Could not find constructor with {attribute.argumentTypes.Description()} parameters in type {attribute.declaringType.FullDescription()}");
                    }

                case MethodType.StaticConstructor:
                    {
                        var constructor = AccessTools.GetDeclaredConstructors(attribute.declaringType).FirstOrDefault(c => c.IsStatic);
                        if (constructor != null) return constructor;

                        return MakeFailure($"Could not find static constructor in type {attribute.declaringType.FullDescription()}");
                    }

                default:
                    throw new ArgumentOutOfRangeException(nameof(attribute.methodType), attribute.methodType, "Unknown method type");
            }
        }

        private T RunMethod<S, T>(T defaultIfNotExisting, params object[] parameters)
        {
            if (container == null)
                return defaultIfNotExisting;

            var methodName = typeof(S).Name.Replace("Harmony", "");
            var method = GetPatchMethod<S>(container, methodName);
            if (method != null)
            {
                if (typeof(T).IsAssignableFrom(method.ReturnType))
                    return (T)method.Invoke(null, Type.EmptyTypes);

                var input = (parameters ?? new object[0]).Union(new object[] { instance }).ToArray();
                var actualParameters = AccessTools.ActualParameters(method, input);
                method.Invoke(null, actualParameters);
                return defaultIfNotExisting;
            }

            return defaultIfNotExisting;
        }

        private void RunMethod<S>(params object[] parameters)
        {
            if (container == null)
                return;

            var methodName = typeof(S).Name.Replace("Harmony", "");
            var method = GetPatchMethod<S>(container, methodName);
            if (method != null)
            {
                var input = (parameters ?? new object[0]).Union(new object[] { instance }).ToArray();
                var actualParameters = AccessTools.ActualParameters(method, input);
                method.Invoke(null, actualParameters);
            }
        }

        private MethodInfo GetPatchMethod<T>(Type patchType, string name)
        {
            var attributeType = typeof(T).FullName;
            var method = patchType.GetMethods(AccessTools.all)
                                  .FirstOrDefault(m => m.GetCustomAttributes(true)
                                                        .Any(a => a.GetType().FullName == attributeType));
            if (method == null)
                // not-found is common and normal case, don't use AccessTools which will generate not-found warnings
                method = patchType.GetMethod(name, AccessTools.all);

            return method;
        }

        private void GetPatches(Type patchType, out MethodInfo prefix, out MethodInfo postfix,
                                        out MethodInfo transpiler, out MethodInfo finalizer)
        {
            prefix = GetPatchMethod<HarmonyPrefix>(patchType, "Prefix");
            postfix = GetPatchMethod<HarmonyPostfix>(patchType, "Postfix");
            transpiler = GetPatchMethod<HarmonyTranspiler>(patchType, "Transpiler");
            finalizer = GetPatchMethod<HarmonyFinalizer>(patchType, "Finalizer");
        }
    }
}