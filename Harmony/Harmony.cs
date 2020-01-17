using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib.Internal;
using HarmonyLib.Tools;
using MonoMod.Utils;

namespace HarmonyLib
{
    /// <summary>The Harmony instance is the main entry to Harmony. After creating one with an unique identifier, it is used to patch and query the current application domain</summary>
    public class Harmony
    {
        /// <summary>The unique identifier</summary>
        public string Id { get; private set; }

        /// <summary>Set to true before instantiating Harmony to debug Harmony</summary>
        [Obsolete("No longer used, subscribe to Logger.LogChannel.Info")]
        public static bool DEBUG;

        /// <summary>Set to false before instantiating Harmony to prevent Harmony from patching other older instances of itself</summary>
        [Obsolete("Not supported by HarmonyX", true)]
        public static bool SELF_PATCHING = false;

        /// <summary>Creates a new Harmony instance</summary>
        /// <param name="id">A unique identifier</param>
        /// <returns>A Harmony instance</returns>
        ///
        public Harmony(string id)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException($"{nameof(id)} cannot be null or empty");

            Logger.Log(Logger.LogChannel.Info, () =>
            {
                var assembly = typeof(Harmony).Assembly;
                var version = assembly.GetName().Version;
                var assemblyLocation = assembly.Location;

                if (string.IsNullOrEmpty(assemblyLocation))
                    assemblyLocation = new Uri(assembly.CodeBase).LocalPath;

                var callingMethod = AccessTools.GetOutsideCaller();
                var callingAssembly = callingMethod.DeclaringType.Assembly;

                var callingAssemblyLocation = callingAssembly.Location;
                if (string.IsNullOrEmpty(callingAssemblyLocation))
                    callingAssemblyLocation = new Uri(callingAssembly.CodeBase).LocalPath;

                return $"Created Harmony instance id={id}, version={version}, location={assemblyLocation} - Started from {callingMethod.GetID()} location={callingAssemblyLocation}";
            });

            Id = id;
        }

        /// <summary>Searches current assembly for Harmony annotations and uses them to create patches</summary>
        ///
        public void PatchAll()
        {
            var method = new StackTrace().GetFrame(1).GetMethod();
            var assembly = method.ReflectedType.Assembly;
            PatchAll(assembly);
        }

        /// <summary>Create a patch processor from an annotated class</summary>
        /// <param name="type">The class</param>
        ///
        public PatchProcessor ProcessorForAnnotatedClass(Type type)
        {
            var parentMethodInfos = HarmonyMethodExtensions.GetFromType(type);
            if (parentMethodInfos != null && parentMethodInfos.Any())
            {
                var info = HarmonyMethod.Merge(parentMethodInfos);
                return new PatchProcessor(this, type, info);
            }

            return null;
        }

        /// <summary>Searches an assembly for Harmony annotations and uses them to create patches</summary>
        /// <param name="assembly">The assembly</param>
        ///
        public void PatchAll(Assembly assembly)
        {
            assembly.GetTypes().Do(type => ProcessorForAnnotatedClass(type)?.Patch());
        }

        /// <summary>Creates patches by manually specifying the methods</summary>
        /// <param name="original">The original method</param>
        /// <param name="prefix">An optional prefix method wrapped in a HarmonyMethod object</param>
        /// <param name="postfix">An optional postfix method wrapped in a HarmonyMethod object</param>
        /// <param name="transpiler">An optional transpiler method wrapped in a HarmonyMethod object</param>
        /// <param name="finalizer">An optional finalizer method wrapped in a HarmonyMethod object</param>
        /// <returns>The dynamic method that was created to patch the original method</returns>
        ///
        public DynamicMethod Patch(MethodBase original, HarmonyMethod prefix = null, HarmonyMethod postfix = null,
                                   HarmonyMethod transpiler = null, HarmonyMethod finalizer = null)
        {
            var processor = new PatchProcessor(this, original);
            processor.AddPrefix(prefix);
            processor.AddPostfix(postfix);
            processor.AddTranspiler(transpiler);
            processor.AddFinalizer(finalizer);
            return processor.Patch().FirstOrDefault();
        }

        /// <summary>
        /// Creates a new reverse patch instance that allows to copy the IL of original method into another one.
        /// </summary>
        /// <param name="original">Original method to copy IL from.</param>
        /// <param name="standin">Method to copy IL to.</param>
        /// <returns>Reverse patcher instance that you can apply.</returns>
        public ReversePatcher CreateReversePatcher(MethodBase original, MethodInfo standin)
        {
            return new ReversePatcher(this, original, standin);
        }

        /// <summary>Unpatches methods</summary>
        /// <param name="harmonyID">The optional Harmony ID to restrict unpatching to a specific instance</param>
        ///
        public void UnpatchAll(string harmonyID = null)
        {
            bool IDCheck(Patch patchInfo)
            {
                return harmonyID == null || patchInfo.owner == harmonyID;
            }

            var originals = GetPatchedMethods().ToList();
            foreach (var original in originals)
            {
                var info = GetPatchInfo(original);
                info.Prefixes.DoIf(IDCheck, patchInfo => Unpatch(original, patchInfo.patch));
                info.Postfixes.DoIf(IDCheck, patchInfo => Unpatch(original, patchInfo.patch));
                info.Transpilers.DoIf(IDCheck, patchInfo => Unpatch(original, patchInfo.patch));
                info.Finalizers.DoIf(IDCheck, patchInfo => Unpatch(original, patchInfo.patch));
            }
        }

        /// <summary>Unpatches a method</summary>
        /// <param name="original">The original method</param>
        /// <param name="type">The patch type</param>
        /// <param name="harmonyID">The optional Harmony ID to restrict unpatching to a specific instance</param>
        ///
        public void Unpatch(MethodBase original, HarmonyPatchType type, string harmonyID = null)
        {
            var processor = new PatchProcessor(this, original);
            processor.Unpatch(type, harmonyID);
        }

        /// <summary>Unpatches a method</summary>
        /// <param name="original">The original method</param>
        /// <param name="patch">The patch method to remove</param>
        ///
        public void Unpatch(MethodBase original, MethodInfo patch)
        {
            var processor = new PatchProcessor(this, original);
            processor.Unpatch(patch);
        }

        /// <summary>Test for patches from a specific Harmony ID</summary>
        /// <param name="harmonyID">The Harmony ID</param>
        /// <returns>True if patches for this ID exist</returns>
        ///
        public static bool HasAnyPatches(string harmonyID)
        {
            return GetAllPatchedMethods().Select(GetPatchInfo)
                                         .Any(info => info.Owners.Contains(harmonyID));
        }

        /// <summary>Gets patch information for a given original method</summary>
        /// <param name="method">The original method</param>
        /// <returns>The patch information</returns>
        ///
        public static Patches GetPatchInfo(MethodBase method)
        {
            return PatchProcessor.GetPatchInfo(method);
        }

        /// <summary>Gets the methods this instance has patched</summary>
        /// <returns>An enumeration of original methods</returns>
        ///
        public IEnumerable<MethodBase> GetPatchedMethods()
        {
            return GetAllPatchedMethods().Where(original => GetPatchInfo(original).Owners.Contains(Id));
        }

        /// <summary>Gets all patched methods in the appdomain</summary>
        /// <returns>An enumeration of original methods</returns>
        ///
        public static IEnumerable<MethodBase> GetAllPatchedMethods()
        {
            return GlobalPatchState.GetPatchedMethods();
        }

        /// <summary>Gets Harmony version for all active Harmony instances</summary>
        /// <param name="currentVersion">[out] The current Harmony version</param>
        /// <returns>A dictionary containing assembly versions keyed by Harmony IDs</returns>
        ///
        public static Dictionary<string, Version> VersionInfo(out Version currentVersion)
        {
            currentVersion = typeof(Harmony).Assembly.GetName().Version;
            var assemblies = new Dictionary<string, Assembly>();
            GetAllPatchedMethods().Do(method =>
            {
                var info = GetPatchInfo(method);
                info.Prefixes.Do(fix => assemblies[fix.owner] = fix.patch.DeclaringType.Assembly);
                info.Postfixes.Do(fix => assemblies[fix.owner] = fix.patch.DeclaringType.Assembly);
                info.Transpilers.Do(fix => assemblies[fix.owner] = fix.patch.DeclaringType.Assembly);
                info.Finalizers.Do(fix => assemblies[fix.owner] = fix.patch.DeclaringType.Assembly);
            });

            var result = new Dictionary<string, Version>();
            assemblies.Do(info =>
            {
                var assemblyName = info.Value.GetReferencedAssemblies()
                                       .FirstOrDefault(
                                           a => a.FullName.StartsWith("0Harmony, Version", StringComparison.Ordinal));
                if (assemblyName != null)
                    result[info.Key] = assemblyName.Version;
            });
            return result;
        }

        /// <summary>
		/// Applies all patches specified in the type.
		/// </summary>
		/// <param name="type">The type to scan.</param>
		public void PatchAll(Type type)
        {
            foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var patchAttributeMethods = HarmonyMethodExtensions.GetFromMethod(method);
                if (patchAttributeMethods != null && patchAttributeMethods.Any())
                {
                    var attributes = method.GetCustomAttributes(true);

                    var combinedInfo = HarmonyMethod.Merge(patchAttributeMethods);

                    if (attributes.Any(x => x is ParameterByRefAttribute))
                    {
                        var byRefAttribute = (ParameterByRefAttribute)attributes.First(x => x is ParameterByRefAttribute);

                        foreach (var index in byRefAttribute.ParameterIndices)
                        {
                            combinedInfo.argumentTypes[index] = combinedInfo.argumentTypes[index].MakeByRefType();
                        }
                    }

                    HarmonyMethod prefix = null;
                    HarmonyMethod transpiler = null;
                    HarmonyMethod postfix = null;
                    HarmonyMethod finalizer = null;

                    if (attributes.Any(x => x is HarmonyPrefix))
                        prefix = new HarmonyMethod(method);

                    if (attributes.Any(x => x is HarmonyTranspiler))
                        transpiler = new HarmonyMethod(method);

                    if (attributes.Any(x => x is HarmonyPostfix))
                        postfix = new HarmonyMethod(method);

                    if (attributes.Any(x => x is HarmonyFinalizer))
                        finalizer = new HarmonyMethod(method);


                    var completeMethods = patchAttributeMethods.Where(x => x.declaringType != null && x.methodName != null).ToList();

                    if (patchAttributeMethods.All(x => x.declaringType != combinedInfo.declaringType && x.methodName != combinedInfo.methodName))
                        completeMethods.Add(combinedInfo);

                    var originalMethods = new List<MethodBase>();

                    foreach (var methodToPatch in completeMethods)
                    {
                        if (!methodToPatch.methodType.HasValue)
                            methodToPatch.methodType = MethodType.Normal;

                        var originalMethod = PatchProcessor.GetOriginalMethod(methodToPatch);

                        if (originalMethod == null)
                            throw new ArgumentException($"Null method for attribute: \n" +
                                                        $"Type={methodToPatch.declaringType.FullName ?? "<null>"}\n" +
                                                        $"Name={methodToPatch.methodName ?? "<null>"}\n" +
                                                        $"MethodType={(methodToPatch.methodType?.ToString())}\n" +
                                                        $"Args={(methodToPatch.argumentTypes == null ? "<null>" : string.Join(",", methodToPatch.argumentTypes.Select(x => x.FullName).ToArray()))}");

                        originalMethods.Add(originalMethod);
                    }

                    var processor = new PatchProcessor(this);

                    foreach (var originalMethod in originalMethods)
                        processor.AddOriginal(originalMethod);

                    processor.AddPrefix(prefix);
                    processor.AddPostfix(postfix);
                    processor.AddTranspiler(transpiler);
                    processor.AddFinalizer(finalizer);

                    processor.Patch();
                }
            }
        }


        /// <summary>
        /// Creates a new Harmony instance and applies all patches specified in the type.
        /// </summary>
        /// <param name="type">The type to scan for patches.</param>
        /// <param name="harmonyInstanceId">The ID for the Harmony instance to create, which will be used.</param>
        public static Harmony CreateAndPatchAll(Type type, string harmonyInstanceId = null)
        {
            var harmony = new Harmony(harmonyInstanceId ?? $"harmony-auto-{Guid.NewGuid()}");
            harmony.PatchAll(type);
            return harmony;
        }

        /// <summary>
        /// Applies all patches specified in the assembly.
        /// </summary>
        /// <param name="assembly">The assembly to scan.</param>
        /// <param name="harmonyInstanceId">The ID for the Harmony instance to create, which will be used.</param>
        public static Harmony CreateAndPatchAll(Assembly assembly, string harmonyInstanceId = null)
        {
            var harmony = new Harmony(harmonyInstanceId ?? $"harmony-auto-{Guid.NewGuid()}");
            harmony.PatchAll(assembly);
            return harmony;
        }
    }
}
