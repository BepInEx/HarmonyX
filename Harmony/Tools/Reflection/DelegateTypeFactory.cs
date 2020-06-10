using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using MonoMod.Utils;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using MethodImplAttributes = Mono.Cecil.MethodImplAttributes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace HarmonyLib
{
    /// <summary>A factory to create delegate types</summary>
    public class DelegateTypeFactory
    {
        private static int _counter;
        private static readonly Dictionary<MethodInfo, Type> TypeCache = new Dictionary<MethodInfo, Type>();

        /// <summary>
        /// Instance for the delegate type factory
        /// </summary>
        /// <remarks>
        /// Exists for API compatibility with Harmony
        /// </remarks>
        public static readonly DelegateTypeFactory instance = new DelegateTypeFactory();

        /// <summary>
        /// Creates a delegate type for a method
        /// </summary>
        /// <param name="returnType">Type of the return value</param>
        /// <param name="argTypes">Types of the arguments</param>
        /// <returns>The new delegate type for the given type info</returns>
        public Type CreateDelegateType(Type returnType, Type[] argTypes)
        {
            _counter++;
            var assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition($"HarmonyDTFAssembly{_counter}", new Version(1, 0)),
                $"HarmonyDTFModule{_counter}", ModuleKind.Dll);

            var module = assembly.MainModule;
            var dtfType = new TypeDefinition("", $"HarmonyDTFType{_counter}",
                                             TypeAttributes.Sealed | TypeAttributes.Public)
            {
                BaseType = module.ImportReference(typeof(MulticastDelegate))
            };
            module.Types.Add(dtfType);

            var ctor = new MethodDefinition(
                ".ctor", MethodAttributes.RTSpecialName | MethodAttributes.HideBySig | MethodAttributes.Public,
                module.ImportReference(typeof(void)))
            {
                ImplAttributes = MethodImplAttributes.CodeTypeMask
            };
            dtfType.Methods.Add(ctor);


            var invokeMethod =
                new MethodDefinition(
                    "Invoke", MethodAttributes.HideBySig | MethodAttributes.Virtual | MethodAttributes.Public,
                    module.ImportReference(returnType))
                {
                    ImplAttributes = MethodImplAttributes.CodeTypeMask
                };

            invokeMethod.Parameters.AddRange(argTypes.Select(t => new ParameterDefinition(module.ImportReference(t))));
            dtfType.Methods.Add(invokeMethod);

            var loadedAss = ReflectionHelper.Load(assembly.MainModule);
            var delegateType = loadedAss.GetType($"HarmonyDTFType{_counter}");
            return delegateType;
        }

        /// <summary>Creates a delegate type for a method</summary>
        /// <param name="method">The method</param>
        /// <returns>The new delegate type</returns>
        public Type CreateDelegateType(MethodInfo method)
        {
            if (TypeCache.TryGetValue(method, out var type))
                return type;

            type = CreateDelegateType(method.ReturnType,
                                          method.GetParameters().Select(p => p.ParameterType).ToArray());
            TypeCache[method] = type;
            return type;
        }
    }
}