using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace HarmonyLib
{
	internal static class PatchTools
	{
		internal static readonly string harmonyMethodFullName = typeof(HarmonyMethod).FullName;
		internal static readonly string harmonyAttributeFullName = typeof(HarmonyAttribute).FullName;
		internal static readonly string harmonyPatchAllFullName = typeof(HarmonyPatchAll).FullName;

		// Note: Even though this Dictionary is only stored to and never read from, it still needs to be thread-safe:
		// https://stackoverflow.com/a/33153868
		// ThreadStatic has pitfalls (see RememberObject below), but since we must support net35, it's the best available option.
		[ThreadStatic]
		private static Dictionary<object, object> objectReferences;

		internal static void RememberObject(object key, object value)
		{
			// ThreadStatic fields are only initialized for one thread, so ensure it's initialized for current thread.
			objectReferences ??= new Dictionary<object, object>();
			objectReferences[key] = value;
		}

		internal static readonly MethodInfo m_GetExecutingAssemblyReplacementTranspiler = SymbolExtensions.GetMethodInfo(() => GetExecutingAssemblyTranspiler(null));
		internal static readonly MethodInfo m_GetExecutingAssembly = SymbolExtensions.GetMethodInfo(() => Assembly.GetExecutingAssembly());
		internal static readonly MethodInfo m_GetExecutingAssemblyReplacement = SymbolExtensions.GetMethodInfo(() => GetExecutingAssemblyReplacement());
		static Assembly GetExecutingAssemblyReplacement()
		{
			var frames = new StackTrace().GetFrames();
			if (frames?.Skip(1).FirstOrDefault() is { } frame && Harmony.GetOriginalMethodFromStackframe(frame) is { } original)
				return original.Module.Assembly;
			return Assembly.GetExecutingAssembly();
		}
		internal static IEnumerable<CodeInstruction> GetExecutingAssemblyTranspiler(IEnumerable<CodeInstruction> instructions) => instructions.MethodReplacer(m_GetExecutingAssembly, m_GetExecutingAssemblyReplacement);

		public static MethodInfo CreateMethod(string name, Type returnType, List<KeyValuePair<string, Type>> parameters, Action<ILGenerator> generator)
		{
			var parameterTypes = parameters.Select(p => p.Value).ToArray();
			var dynamicMethod = new DynamicMethodDefinition(name, returnType, parameterTypes);

			for (var i = 0; i < parameters.Count; i++)
				dynamicMethod.Definition.Parameters[i].Name = parameters[i].Key;

			var il = dynamicMethod.GetILGenerator();
			generator(il);

			return dynamicMethod.Generate();
		}

		internal static MethodInfo GetPatchMethod(Type patchType, string attributeName)
		{
			var method = patchType.GetMethods(AccessTools.all)
				.FirstOrDefault(m => m.GetCustomAttributes(true).Any(a => a.GetType().FullName == attributeName));
			if (method is null)
			{
				// not-found is common and normal case, don't use AccessTools which will generate not-found warnings
				var methodName = attributeName.Replace("HarmonyLib.Harmony", "");
				method = patchType.GetMethod(methodName, AccessTools.all);
			}
			return method;
		}

		internal static AssemblyBuilder DefineDynamicAssembly(string name)
		{
			var assemblyName = new AssemblyName(name);
#if NETCOREAPP || NETSTANDARD
			return AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
#else
			return AppDomain.CurrentDomain.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
#endif
		}

		internal static List<AttributePatch> GetPatchMethods(Type type, bool collectIncomplete = false)
		{
			return AccessTools.GetDeclaredMethods(type)
				.SelectMany(method => AttributePatch.Create(method, collectIncomplete))
				.Where(attributePatch => attributePatch is not null)
				.ToList();
		}

		internal static MethodBase GetOriginalMethod(this HarmonyMethod attr)
		{
			try
			{
				switch (attr.methodType)
				{
					case MethodType.Normal:
						if (attr.methodName is null)
							return null;
						return AccessTools.DeclaredMethod(attr.declaringType, attr.methodName, attr.argumentTypes);

					case MethodType.Getter:
						if (attr.methodName is null)
							return AccessTools.DeclaredIndexer(attr.declaringType, attr.argumentTypes)?.GetGetMethod(true);
						return AccessTools.DeclaredProperty(attr.declaringType, attr.methodName)?.GetGetMethod(true);

					case MethodType.Setter:
						if (attr.methodName is null)
							return AccessTools.DeclaredIndexer(attr.declaringType, attr.argumentTypes)?.GetSetMethod(true);
						return AccessTools.DeclaredProperty(attr.declaringType, attr.methodName)?.GetSetMethod(true);

					case MethodType.Constructor:
						return AccessTools.DeclaredConstructor(attr.declaringType, attr.argumentTypes);

					case MethodType.StaticConstructor:
						return AccessTools
							.GetDeclaredConstructors(attr.declaringType)
							.FirstOrDefault(c => c.IsStatic);

					case MethodType.Enumerator:
						if (attr.methodName is null)
							return null;
						return AccessTools.EnumeratorMoveNext(AccessTools.DeclaredMethod(attr.declaringType,
							attr.methodName, attr.argumentTypes));

#if NET452_OR_GREATER || NETSTANDARD || NETCOREAPP
					case MethodType.Async:
						if (attr.methodName is null)
							return null;
						return AccessTools.AsyncMoveNext(AccessTools.DeclaredMethod(attr.declaringType, attr.methodName, attr.argumentTypes));
#endif
				}
			}
			catch (AmbiguousMatchException ex)
			{
				throw new HarmonyException($"Ambiguous match for HarmonyMethod[{attr.Description()}]",
					ex.InnerException ?? ex);
			}

			return null;
		}
	}
}
