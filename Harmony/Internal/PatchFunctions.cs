using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib.Internal.Patching;
using HarmonyLib.Internal.Util;
using HarmonyLib.Public.Patching;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using MethodBody = Mono.Cecil.Cil.MethodBody;
using OpCodes = System.Reflection.Emit.OpCodes;

namespace HarmonyLib
{
	/// <summary>Patch function helpers</summary>
	internal static class PatchFunctions
	{
		/// <summary>Sorts patch methods by their priority rules</summary>
		/// <param name="original">The original method</param>
		/// <param name="patches">Patches to sort</param>
		/// <param name="debug">Use debug mode. Present for source parity with Harmony 2, don't use.</param>
		/// <returns>The sorted patch methods</returns>
		///
		internal static List<MethodInfo> GetSortedPatchMethods(MethodBase original, Patch[] patches, bool debug = false)
		{
			return new PatchSorter(patches, debug).Sort(original);
		}

		/// <summary>Creates new replacement method with the latest patches and detours the original method</summary>
		/// <param name="original">The original method</param>
		/// <param name="patchInfo">Information describing the patches</param>
		/// <returns>The newly created replacement method</returns>
		///
		internal static MethodInfo UpdateWrapper(MethodBase original, PatchInfo patchInfo)
		{
			var patcher = original.GetMethodPatcher();
			var dmd = patcher.PrepareOriginal();

			if (dmd != null)
			{
				var ctx = new ILContext(dmd.Definition);
				HarmonyManipulator.Manipulate(original, patchInfo, ctx);
			}

			try
			{
				return patcher.DetourTo(dmd?.Generate()) as MethodInfo;
			}
			catch (Exception ex)
			{
				throw HarmonyException.Create(ex, dmd?.Definition?.Body);
			}
		}

		internal static MethodInfo ReversePatch(HarmonyMethod standin, MethodBase original, MethodInfo postTranspiler)
		{
			if (standin is null)
				throw new ArgumentNullException(nameof(standin));
			if (standin.method is null)
				throw new ArgumentNullException($"{nameof(standin)}.{nameof(standin.method)}");

			var transpilers = new List<MethodInfo>();
			if (standin.reversePatchType == HarmonyReversePatchType.Snapshot)
			{
				var info = Harmony.GetPatchInfo(original);
				transpilers.AddRange(GetSortedPatchMethods(original, info.Transpilers.ToArray()));
			}
			if (postTranspiler is object) transpilers.Add(postTranspiler);

			MethodBody patchBody = null;
			var hook = new ILHook(standin.method, ctx =>
			{
				if (!(original is MethodInfo mi))
					return;

				patchBody = ctx.Body;

				// Make a cecil copy of the original method for convenience sake
				// Here original can have no body, in which case we generate a wrapper that calls it
				// Yes, it's not great, but it's better than hard crashing or giving no method to the user at all
				var manipulator = original.HasMethodBody() ? GetManagedMethodManipulator(mi) : GetBodylessManipulator(mi);

				// Copy over variables from the original code
				ctx.Body.Variables.Clear();
				foreach (var variableDefinition in manipulator.Body.Variables)
					ctx.Body.Variables.Add(new VariableDefinition(ctx.Module.ImportReference(variableDefinition.VariableType)));

				foreach (var methodInfo in transpilers)
					manipulator.AddTranspiler(methodInfo);

				manipulator.WriteTo(ctx.Body, standin.method);

				// Write a ret in case it got removed (wrt. HarmonyManipulator)
				ctx.IL.Emit(Mono.Cecil.Cil.OpCodes.Ret);
			}, new ILHookConfig { ManualApply = true });

			try
			{
				hook.Apply();
			}
			catch (Exception ex)
			{
				throw HarmonyException.Create(ex, patchBody);
			}

			var replacement = hook.GetCurrentTarget() as MethodInfo;
			PatchTools.RememberObject(standin.method, replacement);
			return replacement;

			static ILManipulator GetBodylessManipulator(MethodInfo original)
			{
				var paramList = new List<Type>();
				if (!original.IsStatic)
					paramList.Add(original.GetThisParamType());
				paramList.AddRange(original.GetParameters().Select(p => p.ParameterType));
				var dmd = new DynamicMethodDefinition("OrigWrapper", original.ReturnType, paramList.ToArray());
				var il = dmd.GetILGenerator();
				for (var i = 0; i < paramList.Count; i++)
					il.Emit(OpCodes.Ldarg, i);
				il.Emit(OpCodes.Call, original);
				il.Emit(OpCodes.Ret);
				return new ILManipulator(dmd.Definition.Body);
			}

			static ILManipulator GetManagedMethodManipulator(MethodInfo original)
			{
				var dmd = new DynamicMethodDefinition(original);
				return new ILManipulator(dmd.Definition.Body);
			}
		}
	}
}
