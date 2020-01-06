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

        /// <summary>Creates a delegate type for a method</summary>
        /// <param name="method">The method</param>
        /// <returns>The new delegate type</returns>
        public Type CreateDelegateType(MethodInfo method)
        {
            if (TypeCache.TryGetValue(method, out var type))
                return type;

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

            var parameters = method.GetParameters();

            var invokeMethod =
                new MethodDefinition(
                    "Invoke", MethodAttributes.HideBySig | MethodAttributes.Virtual | MethodAttributes.Public,
                    module.ImportReference(method.ReturnType))
                {
                    ImplAttributes = MethodImplAttributes.CodeTypeMask
                };

            invokeMethod.Parameters.AddRange(parameters.Select(
                                                 p => new ParameterDefinition(
                                                     p.Name, ParameterAttributes.None,
                                                     module.ImportReference(p.ParameterType))));

            using (var ms = new MemoryStream())
            {
                assembly.Write(ms);
                var loadedAss = Assembly.Load(ms.ToArray());
                var delegateType = loadedAss.GetType($"HarmonyDTFType{_counter}");
                TypeCache[method] = delegateType;
                return delegateType;
            }
        }
    }
}