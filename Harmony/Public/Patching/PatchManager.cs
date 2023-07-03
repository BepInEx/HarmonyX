using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using MonoMod.Core.Platforms;

namespace HarmonyLib.Public.Patching
{
	/// <summary>
	/// A global manager for handling Harmony patch state. Contains information about all patched methods and all
	/// actual <see cref="MethodPatcher"/> instances that handle patching implementation.
	/// </summary>
	///
	public static class PatchManager
	{
		private static readonly Dictionary<MethodBase, PatchInfo> PatchInfos = new Dictionary<MethodBase, PatchInfo>();
		private static readonly Dictionary<MethodBase, MethodPatcher> MethodPatchers = new Dictionary<MethodBase, MethodPatcher>();
		// Keep replacements as weak references to allow GC to collect them (e.g. if replacement is DynamicMethod)
		private static readonly List<KeyValuePair<WeakReference, MethodBase>> ReplacementToOriginals = new List<KeyValuePair<WeakReference, MethodBase>>();

		// typeof(StackFrame).methodAddress
		private static FieldInfo methodAddress;

		static PatchManager()
		{
			ResolvePatcher += ManagedMethodPatcher.TryResolve;
			ResolvePatcher += NativeMethodPatcher.TryResolve;
		}

		/// <summary>
		/// Method patcher resolve event.
		/// </summary>
		/// <remarks>
		/// When a method is to be patched, this resolver event is called once on the method to determine which
		/// <see cref="MethodPatcher"/> backend to use in order to patch the method.
		/// To make Harmony use the specified backend, set <see cref="PatcherResolverEventArgs.MethodPatcher"/> to an
		/// instance of the method patcher backend to use.
		/// </remarks>
		///
		public static event EventHandler<PatcherResolverEventArgs> ResolvePatcher;

		/// <summary>
		/// Creates or gets an existing instance of <see cref="MethodPatcher"/> that handles patching the method.
		/// </summary>
		/// <param name="methodBase">Method to patch.</param>
		/// <returns>Instance of <see cref="MethodPatcher"/> that handles patching the method.</returns>
		/// <exception cref="NullReferenceException">No suitable patcher found for the method.</exception>
		///
		public static MethodPatcher GetMethodPatcher(this MethodBase methodBase)
		{
			lock (MethodPatchers)
			{
				if (MethodPatchers.TryGetValue(methodBase, out var methodPatcher))
					return methodPatcher;
				var args = new PatcherResolverEventArgs {Original = methodBase};
				ResolvePatcher?.Invoke(null, args);
				if (args.MethodPatcher == null)
					throw new NullReferenceException($"No suitable patcher found for {methodBase.FullDescription()}");
				return MethodPatchers[methodBase] = args.MethodPatcher;
			}
		}

		/// <summary>
		/// Gets patch info for the given target method.
		/// </summary>
		/// <param name="methodBase">Method to get patch info for.</param>
		/// <returns>Current patch info of the method.</returns>
		///
		public static PatchInfo GetPatchInfo(this MethodBase methodBase)
		{
			lock (PatchInfos)
				return PatchInfos.GetValueSafe(methodBase);
		}

		/// <summary>
		/// Gets or creates patch info for the given method.
		/// </summary>
		/// <param name="methodBase">Method to get info from.</param>
		/// <returns>An existing or new patch info for the method containing information about the applied patches.</returns>
		///
		public static PatchInfo ToPatchInfo(this MethodBase methodBase)
		{
			lock (PatchInfos)
			{
				if (PatchInfos.TryGetValue(methodBase, out var info))
					return info;

				return PatchInfos[methodBase] = new PatchInfo();
			}
		}

		/// <summary>
		/// Gets all methods that have been patched.
		/// </summary>
		/// <returns>List of methods that have been patched.</returns>
		///
		public static IEnumerable<MethodBase> GetPatchedMethods()
		{
			lock (PatchInfos)
				return PatchInfos.Keys.ToList();
		}

		internal static MethodBase GetOriginal(MethodInfo replacement)
		{
			lock (ReplacementToOriginals)
			{
				ReplacementToOriginals.RemoveAll(kv => !kv.Key.IsAlive);
				foreach (var replacementToOriginal in ReplacementToOriginals)
				{
					var method = replacementToOriginal.Key.Target as MethodInfo;
					if (method == replacement)
						return replacementToOriginal.Value;
				}
				return null;
			}
		}

		internal static MethodBase FindReplacement(StackFrame frame)
		{
			methodAddress ??= typeof(StackFrame).GetField("methodAddress", BindingFlags.Instance | BindingFlags.NonPublic);

			var frameMethod = frame.GetMethod();
			var methodStart = 0L;

			if (frameMethod is null)
			{
				if (methodAddress == null)
					return null;

				methodStart = (long) methodAddress.GetValue(frame);
			}
			else
			{
				var baseMethod = PlatformTriple.Current.Runtime.GetIdentifiable(frameMethod);
				methodStart = PlatformTriple.Current.Runtime.GetMethodEntryPoint(baseMethod).ToInt64();
			}

			// Failed to find any usable method, if `frameMethod` is null, we can not find any
			// method from the stacktrace.
			if (methodStart == 0)
				return frameMethod;

			lock (ReplacementToOriginals)
				return ReplacementToOriginals
					.FirstOrDefault(kv => kv.Key.IsAlive && PlatformTriple.Current.Runtime.GetMethodEntryPoint((MethodBase)kv.Key.Target).ToInt64() == methodStart).Key.Target as MethodBase;
		}

		internal static void AddReplacementOriginal(MethodBase original, MethodInfo replacement)
		{
			if (replacement == null)
				return;
			lock (ReplacementToOriginals)
				ReplacementToOriginals.Add(new KeyValuePair<WeakReference, MethodBase>(new WeakReference(replacement), original));
		}

		/// <summary>
		/// Removes all method resolvers. Use with care, this removes the default ones too!
		/// </summary>
		public static void ClearAllPatcherResolvers()
		{
			ResolvePatcher = null;
		}

		/// <summary>
		/// Patcher resolve event arguments.
		/// </summary>
		///
		public class PatcherResolverEventArgs : EventArgs
		{
			/// <summary>
			/// Original method that is to be patched.
			/// </summary>
			///
			public MethodBase Original { get; internal set; }

			/// <summary>
			/// Method patcher to use to patch <see cref="Original"/>.
			/// Set this value to specify which one to use.
			/// </summary>
			///
			public MethodPatcher MethodPatcher { get; set; }
		}
	}
}
