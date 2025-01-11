using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace HarmonyTests.Patching.Assets;

public class ExternalInstanceMethod_StringIsInterned_Patch
{
	public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
		instructions;

	private const string UniqueString = nameof(ExternalInstanceMethod_StringIsInterned_Patch);

	public const string PrefixInput = $"{UniqueString} {nameof(PrefixInput)}";
	public const string PrefixOutput = $"{UniqueString} {nameof(PrefixOutput)}";
	public static void Prefix(ref string __instance, ref string __result, ref bool __runOriginal)
	{
		if (__instance == PrefixInput)
		{
			__result = PrefixOutput;
			__runOriginal = false;
		}
	}

	public const string PostfixInput = $"{UniqueString} {nameof(PostfixInput)}";
	public const string PostfixOutput = $"{UniqueString} {nameof(PostfixOutput)}";
	public static void Postfix(ref string __instance, ref string __result)
	{
		if (__instance == PostfixInput)
		{
			__result = PostfixOutput;
		}
	}

	public static readonly Type TranspiledException = typeof(UnauthorizedAccessException);
	public static IEnumerable<CodeInstruction> TranspilerThrow(IEnumerable<CodeInstruction> instructions)
	{
		yield return new CodeInstruction(OpCodes.Newobj, TranspiledException.GetConstructor([]));
		yield return new CodeInstruction(OpCodes.Throw);
	}

	public const string FinalizerInput = $"{UniqueString} {nameof(FinalizerInput)}";
	public const string FinalizerOutput = $"{UniqueString} {nameof(FinalizerOutput)}";
	public static Exception Finalizer(ref string __instance, ref string __result)
	{
		if (__instance == FinalizerInput)
		{
			__result = FinalizerOutput;
		}
		return null;
	}
}

public class ExternalStaticMethod_MathCos_Patch
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
