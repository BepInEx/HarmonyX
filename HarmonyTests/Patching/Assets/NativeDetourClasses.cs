using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace HarmonyTests.Patching.Assets;

public class ExternalMethod_Patch
{
	public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
		instructions;

	public static void Prefix(ref double __0) =>
		__0 = 0d;

	public static void Postfix(ref double __result) =>
		__result = 2d;

	public static IEnumerable<CodeInstruction> TranspilerThrow(IEnumerable<CodeInstruction> instructions)
	{
		yield return new CodeInstruction(OpCodes.Newobj, typeof(UnauthorizedAccessException).GetConstructor([]));
		yield return new CodeInstruction(OpCodes.Throw);
	}

	public static Exception Finalizer(ref double __result)
	{
		__result = -2d;
		return null;
	}
}
