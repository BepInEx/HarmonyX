using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MonoMod.Utils;

namespace HarmonyLib.Public.Patching
{
	public static class PatchManager
	{
		public class PatcherResolverEeventArgs : EventArgs
		{
			public MethodBase Original { get; internal set; }
			public MethodPatcher MethodPatcher { get; set; }
		}

		public static event EventHandler<PatcherResolverEeventArgs> ResolvePatcher;

		private static readonly Dictionary<MethodBase, PatchInfo> PatchInfos = new Dictionary<MethodBase, PatchInfo>();
		private static readonly Dictionary<MethodBase, MethodPatcher> MethodPatchers = new Dictionary<MethodBase, MethodPatcher>();

		public static MethodPatcher GetMethodPatcher(this MethodBase methodBase)
		{
			lock (MethodPatchers)
			{
				if (MethodPatchers.TryGetValue(methodBase, out var methodPatcher))
					return methodPatcher;
				var args = new PatcherResolverEeventArgs {Original = methodBase};
				ResolvePatcher?.Invoke(null, args);
				if (args.MethodPatcher == null)
					throw new NullReferenceException($"No suitable patcher found for {methodBase.GetID()}");
				return MethodPatchers[methodBase] = args.MethodPatcher;
			}
		}

		public static PatchInfo GetPatchInfo(this MethodBase methodBase)
		{
			lock (PatchInfos)
				return PatchInfos.GetValueSafe(methodBase);
		}

		public static PatchInfo ToPatchInfo(this MethodBase methodBase)
		{
			lock (PatchInfos)
			{
				if (PatchInfos.TryGetValue(methodBase, out var info))
					return info;

				return PatchInfos[methodBase] = new PatchInfo();
			}
		}

		public static IEnumerable<MethodBase> GetPatchedMethods()
		{
			lock (PatchInfos)
				return PatchInfos.Keys.ToList();
		}

		public static void ClearAllPatcherResolvers()
		{
			ResolvePatcher = null;
		}
	}
}
