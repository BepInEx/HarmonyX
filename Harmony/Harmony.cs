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
                var callingAssembly = callingMethod.DeclaringType?.Assembly; // Can be null in <Module>

                var callingAssemblyLocation = callingAssembly?.Location;
                if (string.IsNullOrEmpty(callingAssemblyLocation))
                    callingAssemblyLocation = callingAssembly != null ? new Uri(callingAssembly.CodeBase).LocalPath : string.Empty;

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
            if (assembly == null) throw new ArgumentNullException(nameof(assembly));

            var patchProcessors = assembly.GetTypes().Select(ProcessorForAnnotatedClass).Where(x => x != null).ToList();

            foreach (var type in patchProcessors)
                type.Patch();

            if (patchProcessors.Count == 0)
                Logger.Log(Logger.LogChannel.Warn, () => $"Could not find any valid Harmony patches inside of assembly {assembly.FullName}");
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
            // TODO: Figure out allow MethodInfo return type (maybe shimming?)
            var processor = new PatchProcessor(this, original);
            processor.AddPrefix(prefix);
            processor.AddPostfix(postfix);
            processor.AddTranspiler(transpiler);
            processor.AddFinalizer(finalizer);
            return processor.Patch().FirstOrDefault();
        }

        /// <summary>Patches a foreign method onto a stub method of yours and optionally applies transpilers during the process</summary>
        /// <param name="original">The original method/constructor you want to duplicate</param>
        /// <param name="standin">Your stub method that will become the original. Needs to have the correct signature (either original or whatever your transpilers generates)</param>
        /// <param name="transpiler">An optional transpiler that will be applied during the process</param>
        public MethodInfo ReversePatch(MethodBase original, HarmonyMethod standin, MethodInfo transpiler = null)
        {
            // TODO: Implement (along with ILHook returning the detour it creates)
            return null;
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
            void UnpatchAllId(MethodBase original, IEnumerable<Patch> patches)
            {
                foreach (var patchInfo in patches)
                    if (harmonyID == null || patchInfo.owner == harmonyID)
                        Unpatch(original, patchInfo.patch);
            }

            var originals = GetAllPatchedMethods().ToList();
            foreach (var original in originals)
            {
                var info = GetPatchInfo(original);

                UnpatchAllId(original, info.Prefixes);
                UnpatchAllId(original, info.Postfixes);
                UnpatchAllId(original, info.Transpilers);
                UnpatchAllId(original, info.Finalizers);
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
            return PatchProcessor.VersionInfo(out currentVersion);
        }

        /// <summary>
		/// Applies all patches specified in the type.
		/// </summary>
		/// <param name="type">The type to scan.</param>
		public void PatchAll(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var patchAttributeMethods = HarmonyMethodExtensions.GetFromMethod(method);
                if (patchAttributeMethods != null && patchAttributeMethods.Any())
                {
                    var attributes = method.GetCustomAttributes(true);

                    var combinedInfo = HarmonyMethod.Merge(patchAttributeMethods);

                    bool IsMethodComplete(HarmonyMethod m)
                    {
                        return m.declaringType != null &&
                               (m.methodName != null || m.methodType == MethodType.Constructor || m.methodType == MethodType.StaticConstructor);
                    }

                    var completeMethods = patchAttributeMethods.Where(IsMethodComplete).ToList();

                    if (patchAttributeMethods.All(x => x.declaringType != combinedInfo.declaringType && x.methodName != combinedInfo.methodName))
                        completeMethods.Add(combinedInfo);

                    var originalMethods = new List<MethodBase>();

                    foreach (var methodToPatch in completeMethods)
                    {
                        foreach (var index in attributes.OfType<ParameterByRefAttribute>().SelectMany(x => x.ParameterIndices))
                        {
                            if (!methodToPatch.argumentTypes[index].IsByRef)
                                methodToPatch.argumentTypes[index] = methodToPatch.argumentTypes[index].MakeByRefType();
                        }

                        if (!methodToPatch.methodType.HasValue)
                            methodToPatch.methodType = MethodType.Normal;

                        if (methodToPatch.method == null)
                            methodToPatch.method = method;

                        var originalMethod = PatchProcessor.GetOriginalMethod(methodToPatch);

                        if (originalMethod != null)
                            originalMethods.Add(originalMethod);
                    }

                    var processor = new PatchProcessor(this);

                    foreach (var originalMethod in originalMethods)
                        processor.AddOriginal(originalMethod);

                    if (attributes.Any(x => x is HarmonyPrefix))
                        processor.AddPrefix(new HarmonyMethod(method));

                    if (attributes.Any(x => x is HarmonyTranspiler))
                        processor.AddTranspiler(new HarmonyMethod(method));

                    if (attributes.Any(x => x is HarmonyPostfix))
                        processor.AddPostfix(new HarmonyMethod(method));

                    if (attributes.Any(x => x is HarmonyFinalizer))
                        processor.AddFinalizer(new HarmonyMethod(method));

                    processor.Patch();
                }
                else
                {
                    // Only check when logging warnings
                    if ((Logger.ChannelFilter & Logger.LogChannel.Warn) != 0)
                    {
                        if (method.GetCustomAttributes(typeof(HarmonyAttribute), true).Any())
                            Logger.LogText(Logger.LogChannel.Warn, "Method " + method.FullDescription() + " has an invalid combination of Harmony attributes and will be ignored");
                    }
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
