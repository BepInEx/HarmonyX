using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace HarmonyLib
{
	// PatchJobs holds the information during correlation
	// of methods and patches while processing attribute patches
	//
	internal class PatchJobs<T>
	{
		internal class Job
		{
			internal MethodBase original;
			internal T replacement;
			internal List<HarmonyMethod> prefixes = [];
			internal List<HarmonyMethod> postfixes = [];
			internal List<HarmonyMethod> transpilers = [];
			internal List<HarmonyMethod> finalizers = [];
			internal List<HarmonyMethod> ilmanipulators = [];

			internal void AddPatch(AttributePatch patch)
			{
				switch (patch.type)
				{
					case HarmonyPatchType.Prefix:
						prefixes.Add(patch.info);
						break;
					case HarmonyPatchType.Postfix:
						postfixes.Add(patch.info);
						break;
					case HarmonyPatchType.Transpiler:
						transpilers.Add(patch.info);
						break;
					case HarmonyPatchType.Finalizer:
						finalizers.Add(patch.info);
						break;
					case HarmonyPatchType.ILManipulator:
						ilmanipulators.Add(patch.info);
						break;
				}
			}
		}

		internal Dictionary<MethodBase, Job> state = [];

		internal Job GetJob(MethodBase method)
		{
			if (method is null) return null;
			if (state.TryGetValue(method, out var job) is false)
			{
				job = new Job() { original = method };
				state[method] = job;
			}
			return job;
		}

		internal List<Job> GetJobs()
		{
			return state.Values.Where(job =>
				job.prefixes.Count +
				job.postfixes.Count +
				job.transpilers.Count +
				job.finalizers.Count +
				job.ilmanipulators.Count > 0
			).ToList();
		}

		internal List<T> GetReplacements() => state.Values.Select(job => job.replacement).ToList();
	}

	// AttributePatch contains all information for a patch defined by attributes
	//
	internal class AttributePatch
	{
		static readonly HarmonyPatchType[] allPatchTypes = [
			HarmonyPatchType.Prefix,
			HarmonyPatchType.Postfix,
			HarmonyPatchType.Transpiler,
			HarmonyPatchType.Finalizer,
			HarmonyPatchType.ReversePatch,
			HarmonyPatchType.ILManipulator,
		];

		internal HarmonyMethod info;
		internal HarmonyPatchType? type;

		internal static IEnumerable<AttributePatch> Create(MethodInfo patch, bool collectIncomplete = false)
		{
			if (patch is null)
				throw new NullReferenceException("Patch method cannot be null");

			var allAttributes = patch.GetCustomAttributes(true);
			var methodName = patch.Name;
			var type = GetPatchType(methodName, allAttributes);
			if (type is null)
				return Enumerable.Empty<AttributePatch>();

			if (type != HarmonyPatchType.ReversePatch && patch.IsStatic is false)
				throw new ArgumentException("Patch method " + patch.FullDescription() + " must be static");

			var list = allAttributes
				.Where(attr => attr.GetType().BaseType.FullName == PatchTools.harmonyAttributeFullName)
				.Select(attr =>
				{
					var f_info = AccessTools.Field(attr.GetType(), nameof(HarmonyAttribute.info));
					return f_info.GetValue(attr);
				})
				.Select(AccessTools.MakeDeepCopy<HarmonyMethod>)
				.ToList();

			var completeMethods = new List<HarmonyMethod>();

			static bool Same(HarmonyMethod m1, HarmonyMethod m2) => m1.declaringType == m2.declaringType && m1.methodName == m2.methodName && m1.GetArgumentList().SequenceEqual(m2.GetArgumentList());
			static bool IsComplete(HarmonyMethod m, bool collectIncomplete) => (collectIncomplete || m.declaringType != null) && m.methodName != null;

			var groupedAttributes = list.ToLookup(m => IsComplete(m, collectIncomplete));
			var incomplete = groupedAttributes[false].ToList();
			var info = HarmonyMethod.Merge(incomplete);
			var complete = groupedAttributes[true].Where(m => !Same(m, info)).ToList();

			// Special case: more than one complete method, can be partially complete
			// Merge complete methods with incomplete annotations and register all of them
			if (complete.Count > 1)
				completeMethods.AddRange(complete.Select(m => HarmonyMethod.Merge(incomplete.AddItem(m))));
			else // Usual case: just one complete method => merge all into one
				completeMethods.Add(HarmonyMethod.Merge(list));

			foreach (var completeMethod in completeMethods)
				completeMethod.method = patch;

			return completeMethods.Select(i => new AttributePatch { info = i, type = type }).ToList();
		}

		static HarmonyPatchType? GetPatchType(string methodName, object[] allAttributes)
		{
			var harmonyAttributes = new HashSet<string>(allAttributes
				.Select(attr => attr.GetType().FullName)
				.Where(name => name.StartsWith("Harmony")));

			HarmonyPatchType? type = null;
			foreach (var patchType in allPatchTypes)
			{
				var name = patchType.ToString();
				if (name == methodName || harmonyAttributes.Contains($"HarmonyLib.Harmony{name}"))
				{
					type = patchType;
					break;
				}
			}
			return type;
		}
	}
}
