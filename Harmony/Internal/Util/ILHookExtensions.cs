using MonoMod.Cil;
using System;
using System.Reflection;
using System.Reflection.Emit;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;

namespace HarmonyLib.Internal.Util
{
	internal class ILHookExt : ILHook
	{
		public string dumpPath;

		public ILHookExt(MethodBase @from, ILContext.Manipulator manipulator, ILHookConfig config) : base(@from, manipulator, config)
		{
		}
	}

	internal static class ILHookExtensions
	{
		private static readonly MethodInfo IsAppliedSetter =
			AccessTools.PropertySetter(typeof(ILHook), nameof(ILHook.IsApplied));

		public static readonly Action<ILHook, bool> SetIsApplied = IsAppliedSetter.CreateDelegate<Action<ILHook, bool>>();

		private static Func<ILHook, Detour> GetAppliedDetour;

		static ILHookExtensions()
		{
			var detourGetter = new DynamicMethodDefinition("GetDetour", typeof(Detour), new[] {typeof(ILHook)});
			var il = detourGetter.GetILGenerator();
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Call, AccessTools.PropertyGetter(typeof(ILHook), "_Ctx"));
			il.Emit(OpCodes.Ldfld, AccessTools.Field(AccessTools.Inner(typeof(ILHook), "Context"), "Detour"));
			il.Emit(OpCodes.Ret);
			GetAppliedDetour = detourGetter.Generate().CreateDelegate<Func<ILHook, Detour>>();
		}

		public static MethodBase GetCurrentTarget(this ILHook hook)
		{
			var detour = GetAppliedDetour(hook);
			return detour.Target;
		}
	}
}
