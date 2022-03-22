using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib.Internal.Patching;
using HarmonyLib.Internal.Util;
using HarmonyLib.Public.Patching;
using HarmonyLib.Tools;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using System.Text;
using MethodBody = Mono.Cecil.Cil.MethodBody;
using OpCodes = Mono.Cecil.Cil.OpCodes;

namespace HarmonyLib
{
	/// <summary>Patch function helpers</summary>
	internal static class PatchFunctions
	{
		/// <summary>Sorts patch methods by their priority rules</summary>
		/// <param name="original">The original method</param>
		/// <param name="patches">Patches to sort</param>
		/// <param name="debug">Use debug mode</param>
		/// <returns>The sorted patch methods</returns>
		///
		internal static List<MethodInfo> GetSortedPatchMethods(MethodBase original, Patch[] patches, bool debug)
		{
			return new PatchSorter(patches, debug).Sort(original);
		}

		/// <summary>Sorts patch methods by their priority rules</summary>
		/// <param name="original">The original method</param>
		/// <param name="patches">Patches to sort</param>
		/// <param name="debug">Use debug mode</param>
		/// <returns>The sorted patch methods</returns>
		///
		internal static Patch[] GetSortedPatchMethodsAsPatches(MethodBase original, Patch[] patches, bool debug)
		{
			return new PatchSorter(patches, debug).SortAsPatches(original);
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

		internal static MethodInfo ReversePatch(HarmonyMethod standin, MethodBase original, MethodInfo postTranspiler, MethodInfo postManipulator)
		{
			if (standin is null)
				throw new ArgumentNullException(nameof(standin));
			if (standin.method is null)
				throw new ArgumentNullException(nameof(standin), $"{nameof(standin)}.{nameof(standin.method)} is NULL");
			if (!standin.method.IsStatic)
				throw new ArgumentException(nameof(standin), $"{nameof(standin)}.{nameof(standin.method)} is not static");

			var debug = standin.debug ?? false;
			var transpilers = new List<MethodInfo>();
			var ilmanipulators = new List<MethodInfo>();
			if (standin.reversePatchType == HarmonyReversePatchType.Snapshot)
			{
				var info = Harmony.GetPatchInfo(original);
				transpilers.AddRange(GetSortedPatchMethods(original, info.Transpilers.ToArray(), debug));
				ilmanipulators.AddRange(GetSortedPatchMethods(original, info.ILManipulators.ToArray(), debug));
			}
			if (postTranspiler is object) transpilers.Add(postTranspiler);
			if (postManipulator is object) ilmanipulators.Add(postManipulator);

			Logger.Log(Logger.LogChannel.Info, () =>
			{
				var sb = new StringBuilder();
				sb.AppendLine($"Reverse patching {standin.method.FullDescription()} with {original.FullDescription()}");
				static void PrintInfo(StringBuilder sb, ICollection<MethodInfo> methods, string name)
				{
					if (methods.Count <= 0) return;
					sb.AppendLine($"{name}:");
					foreach (var method in methods)
						sb.AppendLine($"  * {method.FullDescription()}");
				}
				PrintInfo(sb, transpilers, "Transpiler");
				PrintInfo(sb, ilmanipulators, "Manipulators");
				return sb.ToString();
			}, debug);

			MethodBody patchBody = null;
			var hook = new ILHook(standin.method, ctx =>
			{
				if (!(original is MethodInfo mi))
					return;

				patchBody = ctx.Body;

				var patcher = mi.GetMethodPatcher();
				var dmd = patcher.CopyOriginal();

				if (dmd == null)
					throw new NullReferenceException($"Cannot reverse patch {mi.FullDescription()}: method patcher ({patcher.GetType().FullDescription()}) can't copy original method body");

				var manipulator = new ILManipulator(dmd.Definition.Body, debug);

				// Copy over variables from the original code
				ctx.Body.Variables.Clear();
				foreach (var variableDefinition in manipulator.Body.Variables)
					ctx.Body.Variables.Add(new VariableDefinition(ctx.Module.ImportReference(variableDefinition.VariableType)));

				foreach (var methodInfo in transpilers)
					manipulator.AddTranspiler(methodInfo);

				manipulator.WriteTo(ctx.Body, standin.method);

				HarmonyManipulator.ApplyManipulators(ctx, original, ilmanipulators, null);

				// Normalize rets in case they get removed
				Instruction retIns = null;
				foreach (var instruction in ctx.Instrs.Where(i => i.OpCode == OpCodes.Ret))
				{
					retIns ??= ctx.IL.Create(OpCodes.Ret);
					instruction.OpCode = OpCodes.Br;
					instruction.Operand = retIns;
				}
				if (retIns != null)
					ctx.IL.Append(retIns);

				// TODO: Handle new debugType
				Logger.Log(Logger.LogChannel.IL,
					() => $"Generated reverse patcher ({ctx.Method.FullName}):\n{ctx.Body.ToILDasmString()}", debug);
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
		}

		internal static IEnumerable<CodeInstruction> ApplyTranspilers(MethodBase methodBase, ILGenerator generator, int maxTranspilers = 0)
		{
			var patcher = methodBase.GetMethodPatcher();
			var dmd = patcher.CopyOriginal();

			if (dmd == null)
				throw new NullReferenceException($"Cannot reverse patch {methodBase.FullDescription()}: method patcher ({patcher.GetType().FullDescription()}) can't copy original method body");

			var manipulator = new ILManipulator(dmd.Definition.Body, false);

			var info = methodBase.GetPatchInfo();
			if (info is object)
			{
				var sortedTranspilers = GetSortedPatchMethods(methodBase, info.transpilers, false);
				for (var i = 0; i < maxTranspilers && i < sortedTranspilers.Count; i++)
					manipulator.AddTranspiler(sortedTranspilers[i]);
			}

			return manipulator.GetInstructions(generator, methodBase);
		}

		internal static void UnpatchConditional(Func<Patch, bool> executionCondition)
		{
			var originals = PatchProcessor.GetAllPatchedMethods().ToList(); // keep as is to avoid "Collection was modified"
			foreach (var original in originals)
			{
				var hasBody = original.HasMethodBody();
				var info = PatchProcessor.GetPatchInfo(original);
				var patchProcessor = new PatchProcessor(null, original);

				if (hasBody)
				{
					info.Postfixes.DoIf(executionCondition, patchInfo => patchProcessor.Unpatch(patchInfo.PatchMethod));
					info.Prefixes.DoIf(executionCondition, patchInfo => patchProcessor.Unpatch(patchInfo.PatchMethod));
				}

				info.ILManipulators.DoIf(executionCondition, patchInfo => patchProcessor.Unpatch(patchInfo.PatchMethod));
				info.Transpilers.DoIf(executionCondition, patchInfo => patchProcessor.Unpatch(patchInfo.PatchMethod));
				if (hasBody)
					info.Finalizers.DoIf(executionCondition, patchInfo => patchProcessor.Unpatch(patchInfo.PatchMethod));
			}
		}
	}
}
