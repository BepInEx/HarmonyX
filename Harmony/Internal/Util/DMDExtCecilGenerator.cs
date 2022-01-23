using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;
using System;
using System.IO;
using System.Reflection;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;

namespace HarmonyLib.Internal.Util;

internal class DMDExtCecilGenerator : DMDGenerator<DMDExtCecilGenerator>
{
	private static readonly GenerateDelegate GenerateMethodCecil;

	static DMDExtCecilGenerator()
	{
		// Create a copy of DMDCecilGenerator._Generate that allows extracting the generated module
		var dmd = new DynamicMethodDefinition(AccessTools.DeclaredMethod(typeof(DMDCecilGenerator), nameof(_Generate)));
		var ctx = new ILContext(dmd.Definition);
		dmd.Definition.Parameters[0].ParameterType = ctx.Import(typeof(DMDExtCecilGenerator));
		dmd.Definition.Parameters.Add(new ParameterDefinition("generatedType", ParameterAttributes.Out,
			new ByReferenceType(ctx.Import(typeof(TypeDefinition)))));
		var ilCursor = new ILCursor(ctx);

		// Mark the module as non-temporary as it will disposed of manually
		ilCursor.GotoNext(i => i.MatchLdcI4(1), i => i.MatchStloc(2))
			.Remove()
			.Emit(OpCodes.Ldc_I4_0);
		// Grab the generated typeDefinition
		ilCursor.GotoNext(i => i.MatchRet())
			.Emit(OpCodes.Ldarg_3)
			.Emit(OpCodes.Ldloc_1)
			.Emit(OpCodes.Stind_Ref);

		GenerateMethodCecil = dmd.Generate().CreateDelegate<GenerateDelegate>();
	}

	protected override MethodInfo _Generate(DynamicMethodDefinition dmd, object context)
	{
		var settings = context as GeneratorSettings ?? new GeneratorSettings();

		var result = GenerateMethodCecil(this, dmd, settings.otherContext, out var generatedType);
		var module = generatedType.Module;
		module.Name = $"HarmonyDump.{dmd.Definition.GetID(withType: false)}.{Guid.NewGuid().GetHashCode():X8}";

		if (settings.dumpPaths != null)
			foreach (var settingsDumpPath in settings.dumpPaths)
			{
				Directory.CreateDirectory(settingsDumpPath);
				using var stream = File.OpenWrite(Path.Combine(settingsDumpPath, $"{module.Name}.dll"));
				module.Write(stream);
			}

		module.Dispose();

		return result;
	}

	public class GeneratorSettings
	{
		public string[] dumpPaths;
		public object otherContext;
	}

	private delegate MethodInfo GenerateDelegate(DMDExtCecilGenerator generator,
	                                             DynamicMethodDefinition dmd,
	                                             object context,
	                                             out TypeDefinition generatedType);
}
