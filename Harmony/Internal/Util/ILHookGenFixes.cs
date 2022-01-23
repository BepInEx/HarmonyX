using HarmonyLib.Tools;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace HarmonyLib.Internal.Util;

internal static class ILHookGenFixes
{
	private static bool applied;

	public static void Install()
	{
		if (applied)
			return;

		try
		{
			_ = new ILHook(AccessTools.Method(typeof(ILHook).GetNestedType("Context", BindingFlags.NonPublic), "Refresh"),
				ModifyILHookGeneration);
		}
		catch (Exception e)
		{
			Logger.LogText(Logger.LogChannel.Error,
				$"Failed to apply ILHook generator fixes: ({e.GetType().FullName}) {e.Message}");
		}

		applied = true;
	}

	private static void ModifyILHookGeneration(ILContext il)
	{
		var ilCursor = new ILCursor(il);
		ilCursor.GotoNext(i => i.MatchCallvirt<DynamicMethodDefinition>(nameof(DynamicMethodDefinition.Generate)))
			.Remove()
			.Emit(OpCodes.Ldloc_0)
			.EmitDelegate((DynamicMethodDefinition dmd, List<ILHook> chain) =>
			{
				var paths = new List<string>();
				foreach (var ilHook in chain)
					if (ilHook is ILHookExt { dumpPath: { } } ext)
						paths.Add(ext.dumpPath);

				if (paths.Count > 0)
					return DMDExtCecilGenerator.Generate(dmd,
						new DMDExtCecilGenerator.GeneratorSettings { dumpPaths = paths.ToArray() });
				return dmd.Generate();
			});
	}
}
