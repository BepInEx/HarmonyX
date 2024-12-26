using HarmonyLib.Tools;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
using MonoMod.Cil;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using MethodImplAttributes = Mono.Cecil.MethodImplAttributes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace HarmonyLib.Internal.Util;

/// <summary>
/// Basic safe DLL emitter for dynamically generated <see cref="MethodDefinition"/>s.
/// </summary>
/// <remarks>Based on https://github.com/MonoMod/MonoMod/blob/reorganize/src/MonoMod.Utils/DMDGenerators/DMDCecilGenerator.cs</remarks>
internal static class CecilEmitter
{
	private static readonly ConstructorInfo UnverifiableCodeAttributeConstructor =
		typeof(UnverifiableCodeAttribute).GetConstructor(Type.EmptyTypes);

	public static void Dump(MethodDefinition md, IEnumerable<string> dumpPaths, MethodBase original = null)
	{
		using var module = DumpImpl(md, original);

		foreach (var settingsDumpPath in dumpPaths)
		{
			var fullPath = Path.GetFullPath(settingsDumpPath);
			try
			{
				Directory.CreateDirectory(fullPath);
				using var stream = File.OpenWrite(Path.Combine(fullPath, $"{module.Name}.dll"));
				module.Write(stream);
			}
			catch (Exception e)
			{
				Logger.Log(Logger.LogChannel.Error, () => $"Failed to dump {md.GetID(simple: true)} to {fullPath}: {e}");
			}
		}
	}

	internal static ModuleDefinition DumpImpl(MethodDefinition md, MethodBase original = null)
	{
		if(md.Body is null)
			throw new ArgumentException($"Body of MethodDefinition '{md.Name}' to dump is null");

		var name = $"HarmonyDump.{SanitizeTypeName(md.GetID(withType: false, simple: true))}.{Guid.NewGuid().GetHashCode():X8}";
		var originalName = (original?.Name ?? md.Name).Replace('.', '_');
		var module = ModuleDefinition.CreateModule(name,
			new ModuleParameters
			{
				Kind = ModuleKind.Dll, ReflectionImporterProvider = MMReflectionImporter.ProviderNoDefault
			});

		module.Assembly.CustomAttributes.Add(
			new CustomAttribute(module.ImportReference(UnverifiableCodeAttributeConstructor)));

		var hash = Guid.NewGuid().GetHashCode();
		var td = new TypeDefinition("", $"HarmonyDump<{originalName}>?{hash}",
			TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.Class)
		{
			BaseType = module.TypeSystem.Object
		};

		module.Types.Add(td);

		var clone =
			new MethodDefinition(original?.Name ?? "_" + md.Name.Replace(".", "_"), md.Attributes, module.TypeSystem.Void)
			{
				MethodReturnType = md.MethodReturnType,
				Attributes = MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Static,
				ImplAttributes = MethodImplAttributes.IL | MethodImplAttributes.Managed,
				DeclaringType = td,
				HasThis = false,
				NoInlining = true
			};
		td.Methods.Add(clone);

#pragma warning disable IDE0039 // Use local function
		Relinker relinker = (mtp, _) =>
		{
			if (mtp == md)
				return clone!;
			if (mtp is MethodReference mr)
			{
				if (mr.FullName == md.FullName
				    && mr.DeclaringType.FullName == md.DeclaringType.FullName
				    && mr.DeclaringType.Scope.Name == md.DeclaringType.Scope.Name)
					return clone!;
			}
			return module.ImportReference(mtp);
		};
#pragma warning restore IDE0039 // Use local function

		foreach (var param in md.Parameters)
			clone.Parameters.Add(param.Clone().Relink(relinker, clone));

		clone.ReturnType = md.ReturnType.Relink(relinker, clone);
		var body = clone.Body = md.Body.Clone(clone);

		foreach (var variable in body.Variables)
			variable.VariableType = variable.VariableType.Relink(relinker, clone);

		foreach (var handler in body.ExceptionHandlers.Where(handler => handler.CatchType != null))
			handler.CatchType = handler.CatchType.Relink(relinker, clone);

		foreach (var instr in body.Instructions)
		{
			var operand = instr.Operand;
			operand = operand switch
			{
				ParameterDefinition param  => clone.Parameters[param.Index],
				ILLabel label => LabelFix(label, body.Instructions, md.Body.Instructions, originalName),
				IMetadataTokenProvider mtp => mtp.Relink(relinker, clone),
				_                          => operand
			};
			instr.Operand = operand;
		}

		if (md.HasThis)
		{
			TypeReference type = md.DeclaringType;
			if (type.IsValueType)
				type = new ByReferenceType(type);
			clone.Parameters.Insert(0,
				new ParameterDefinition("<>_this", ParameterAttributes.None, type.Relink(relinker, clone)));
		}

		return module;
	}

	private static string SanitizeTypeName(string typeName)
	{
		return typeName
			.Replace(":", "_")
			.Replace(" ", "_")
			.Replace("<", "{")
			.Replace(">", "}");
	}

	private static Instruction LabelFix(ILLabel label, Collection<Instruction> clone, Collection<Instruction> original, string originalName)
	{
		Debug.Assert(clone.Count == original.Count);
		var target = label.Target;
		var idx = original.IndexOf(target);
		if (idx == -1)
			throw new IndexOutOfRangeException($"ILLabel from dump copy of '{originalName}' cannot be relinked. It points to '{target}', which was not found in the original body");
		return clone[idx];
	}
}
