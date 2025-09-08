using HarmonyLib.Internal.Util;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
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

		private static readonly Dictionary<object, object> objectReferences = new();

		internal static void RememberObject(object key, object value)
		{
			lock (objectReferences)
				objectReferences[key] = value;
		}

		public static MethodInfo CreateMethod(string name, Type returnType, List<KeyValuePair<string, Type>> parameters, Action<ILGenerator> generator)
		{
			var parameterTypes = parameters.Select(p => p.Value).ToArray();

			if (AccessTools.IsMonoRuntime && ReflectionTools.isWindows == false)
			{
				var assemblyName = new AssemblyName("TempAssembly");

#if NET2 || NET35
				var assemblyBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
#else
				var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
#endif

				var moduleBuilder = assemblyBuilder.DefineDynamicModule("TempModule");
				var typeBuilder = moduleBuilder.DefineType("TempType", TypeAttributes.Public);

				var methodBuilder = typeBuilder.DefineMethod(name,
					 MethodAttributes.Public | MethodAttributes.Static,
					 returnType, parameterTypes);

				for (var i = 0; i < parameters.Count; i++)
					methodBuilder.DefineParameter(i + 1, ParameterAttributes.None, parameters[i].Key);

				generator(methodBuilder.GetILGenerator());

#if NETSTANDARD2_0
				var createdType = typeBuilder.CreateTypeInfo().AsType();
#else
				var createdType = typeBuilder.CreateType();
#endif
				return createdType.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
			}

			var dynamicMethod = new DynamicMethodDefinition(name, returnType, parameterTypes);

			for (var i = 0; i < parameters.Count; i++)
				dynamicMethod.Definition.Parameters[i].Name = parameters[i].Key;

			generator(dynamicMethod.GetILGenerator());
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
			return [.. AccessTools.GetDeclaredMethods(type)
				.SelectMany(method => AttributePatch.Create(method, collectIncomplete))
				.Where(attributePatch => attributePatch is not null)];
		}

		internal static MethodBase GetOriginalMethod(this HarmonyMethod attr)
		{
			try
			{
				switch (attr.methodType)
				{
					case MethodType.Normal:
						if (string.IsNullOrEmpty(attr.methodName))
							return null;
						return AccessTools.DeclaredMethod(attr.declaringType, attr.methodName, attr.argumentTypes);

					case MethodType.Getter:
						if (string.IsNullOrEmpty(attr.methodName))
							return AccessTools.DeclaredIndexerGetter(attr.declaringType, attr.argumentTypes);
						return AccessTools.DeclaredPropertyGetter(attr.declaringType, attr.methodName);

					case MethodType.Setter:
						if (string.IsNullOrEmpty(attr.methodName))
							return AccessTools.DeclaredIndexerSetter(attr.declaringType, attr.argumentTypes);
						return AccessTools.DeclaredPropertySetter(attr.declaringType, attr.methodName);

					case MethodType.Constructor:
						return AccessTools.DeclaredConstructor(attr.declaringType, attr.argumentTypes);

					case MethodType.StaticConstructor:
						return AccessTools
							.GetDeclaredConstructors(attr.declaringType)
							.FirstOrDefault(c => c.IsStatic);

					case MethodType.Enumerator:
						if (string.IsNullOrEmpty(attr.methodName))
							return null;
						return AccessTools.EnumeratorMoveNext(AccessTools.DeclaredMethod(attr.declaringType,
							attr.methodName, attr.argumentTypes));

#if NET452_OR_GREATER || NETSTANDARD || NETCOREAPP
					case MethodType.Async:
						if (string.IsNullOrEmpty(attr.methodName))
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
