using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using HarmonyLib.Internal.RuntimeFixes;
using HarmonyLib.Tools;
using MonoMod.Utils;

namespace HarmonyLib
{
    /// <summary>A helper class for reflection related functions</summary>
    public static class AccessTools
    {
        /// <summary>Shortcut for <see cref="BindingFlags"/> to simplify the use of reflections and make it work for any access level</summary>
        public static BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                         BindingFlags.Static | BindingFlags.GetField | BindingFlags.SetField |
                                         BindingFlags.GetProperty | BindingFlags.SetProperty;

        /// <summary>Shortcut for <see cref="BindingFlags"/> to simplify the use of reflections and make it work for any access level but only within the current type</summary>
        public static BindingFlags allDeclared = all | BindingFlags.DeclaredOnly;

        /// <summary>Gets a type by name. Prefers a full name with namespace but falls back to the first type matching the name otherwise</summary>
        /// <param name="name">The name of the type, either fully qualified name or just name</param>
        /// <returns>A <see cref="Type"/> if found, null otherwise</returns>
        public static Type TypeByName(string name)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));

            var type = Type.GetType(name, false);
            if (type == null)
                type = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(name))
                                .FirstOrDefault(t => t != null);
            if (type == null)
                type = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetTypes())
                                .FirstOrDefault(x => x.Name == name);
            if (type == null)
                Logger.Log(Logger.LogChannel.Warn, () => $"AccessTools.TypeByName: Could not find type named {name}");
            return type;
        }

        /// <summary>Applies a function going up the type hierarchy and stops at the first non null result</summary>
        /// <typeparam name="T">Result type of func()</typeparam>
        /// <param name="type">The type to start with</param>
        /// <param name="func">The evaluation function returning T</param>
        /// <returns>Returns the first non null result or default(T) when reaching the top level type object</returns>
        public static T FindIncludingBaseTypes<T>(Type type, Func<Type, T> func) where T : class
        {
            while (true)
            {
                var result = func(type);
#pragma warning disable RECS0017
                if (result != null) return result;
#pragma warning restore RECS0017
                if (type == typeof(object)) return default(T);
                type = type.BaseType;
            }
        }

        /// <summary>Applies a function going into inner types and stops at the first non null result</summary>
        /// <typeparam name="T">Generic type parameter</typeparam>
        /// <param name="type">The type to start with</param>
        /// <param name="func">The evaluation function returning T</param>
        /// <returns>Returns the first non null result or null with no match</returns>
        public static T FindIncludingInnerTypes<T>(Type type, Func<Type, T> func) where T : class
        {
            var result = func(type);
#pragma warning disable RECS0017
            if (result != null) return result;
#pragma warning restore RECS0017
            foreach (var subType in type.GetNestedTypes(all))
            {
                result = FindIncludingInnerTypes(subType, func);
#pragma warning disable RECS0017
                if (result != null)
                    break;
#pragma warning restore RECS0017
            }

            return result;
        }

        /// <summary>Gets the reflection information for a directly declared field</summary>
        /// <param name="type">The type where the field is defined</param>
        /// <param name="name">The name of the field</param>
        /// <returns>A <see cref="FieldInfo"/> if field found, otherwise null</returns>
        public static FieldInfo DeclaredField(Type type, string name)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (name == null) throw new ArgumentNullException(nameof(name));

            var field = type.GetField(name, allDeclared);
            if (field == null)
                Logger.Log(Logger.LogChannel.Warn, () => $"AccessTools.DeclaredField: Could not find field for type {type} and name {name}");
            return field;
        }

        /// <summary>Gets the reflection information for a field by searching the type and all its super types</summary>
        /// <param name="type">The type where the field is defined</param>
        /// <param name="name">The name of the field (case sensitive)</param>
        /// <returns>A <see cref="FieldInfo"/> if field found, otherwise null</returns>
        public static FieldInfo Field(Type type, string name)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (name == null) throw new ArgumentNullException(nameof(name));

            var field = FindIncludingBaseTypes(type, t => t.GetField(name, all));
            if (field == null)
                Logger.Log(Logger.LogChannel.Warn, () => $"AccessTools.Field: Could not find field for type {type} and name {name}");
            return field;
        }

        /// <summary>Gets the reflection information for a field</summary>
        /// <param name="type">The type where the field is declared</param>
        /// <param name="idx">The zero-based index of the field inside The type definition</param>
        /// <returns>A <see cref="FieldInfo"/> if field found, otherwise null</returns>
        public static FieldInfo DeclaredField(Type type, int idx)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));

            var field = GetDeclaredFields(type).ElementAtOrDefault(idx);
            if (field == null)
                Logger.Log(Logger.LogChannel.Warn, () => $"AccessTools.DeclaredField: Could not find field for type {type} and idx {idx}");
            return field;
        }

        /// <summary>Gets the reflection information for a directly declared property</summary>
        /// <param name="type">The type where the property is declared</param>
        /// <param name="name">The name of the property (case sensitive)</param>
        /// <returns>A <see cref="PropertyInfo"/> if property found, otherwise null</returns>
        public static PropertyInfo DeclaredProperty(Type type, string name)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (name == null) throw new ArgumentNullException(nameof(name));

            var property = type.GetProperty(name, allDeclared);
            if (property == null)
                Logger.Log(Logger.LogChannel.Warn, () => $"AccessTools.DeclaredProperty: Could not find property for type {type} and name {name}");
            return property;
        }

        /// <summary>Gets the reflection information for the getter method of a directly declared property</summary>
        /// <param name="type">The type where the property is declared</param>
        /// <param name="name">The name of the property (case sensitive)</param>
        /// <returns>A <see cref="MethodInfo"/> if method found, otherwise null</returns>
        public static MethodInfo DeclaredPropertyGetter(Type type, string name)
        {
            return DeclaredProperty(type, name)?.GetGetMethod(true);
        }

        /// <summary>Gets the reflection information for the setter method of a directly declared property</summary>
        /// <param name="type">The type where the property is declared</param>
        /// <param name="name">The name of the property (case sensitive)</param>
        /// <returns>A <see cref="MethodInfo"/> if method found, otherwise null</returns>
        public static MethodInfo DeclaredPropertySetter(Type type, string name)
        {
            return DeclaredProperty(type, name)?.GetSetMethod(true);
        }

        /// <summary>Gets the reflection information for a property by searching the type and all its super types</summary>
        /// <param name="type">The type</param>
        /// <param name="name">The name of the property</param>
        /// <returns>A <see cref="PropertyInfo"/> if property found, otherwise null</returns>
        public static PropertyInfo Property(Type type, string name)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (name == null) throw new ArgumentNullException(nameof(name));

            var property = FindIncludingBaseTypes(type, t => t.GetProperty(name, all));
            if (property == null)
                Logger.Log(Logger.LogChannel.Warn, () => $"AccessTools.Property: Could not find property for type {type} and name {name}");
            return property;
        }

        /// <summary>Gets the reflection information for the getter method of a property by searching the type and all its super types</summary>
        /// <param name="type">The type</param>
        /// <param name="name">The name of the property</param>
        /// <returns>A <see cref="MethodInfo"/> of the property getter if method found, otherwise null</returns>
        public static MethodInfo PropertyGetter(Type type, string name)
        {
            return Property(type, name)?.GetGetMethod(true);
        }

        /// <summary>Gets the reflection information for the setter method of a property by searching the type and all its super types</summary>
        /// <param name="type">The type</param>
        /// <param name="name">The name</param>
        /// <returns>A <see cref="MethodInfo"/> of the property setter if method found, otherwise null</returns>
        public static MethodInfo PropertySetter(Type type, string name)
        {
            return Property(type, name)?.GetSetMethod(true);
        }

        /// <summary>Gets the reflection information for a directly declared method</summary>
        /// <param name="type">The type where the method is declared</param>
        /// <param name="name">The name of the method (case sensitive)</param>
        /// <param name="parameters">Optional parameters to target a specific overload of the method</param>
        /// <param name="generics">Optional list of types that define the generic version of the method</param>
        /// <returns>A <see cref="MethodInfo"/> if method found, otherwise null</returns>
        public static MethodInfo DeclaredMethod(Type type, string name, Type[] parameters = null,
                                                Type[] generics = null)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (name == null) throw new ArgumentNullException(nameof(name));

            MethodInfo result;
            var modifiers = new ParameterModifier[] { };

            if (parameters == null)
                result = type.GetMethod(name, allDeclared);
            else
                result = type.GetMethod(name, allDeclared, null, parameters, modifiers);

            if (result == null)
            {
                Logger.Log(Logger.LogChannel.Warn, () => $"AccessTools.DeclaredMethod: Could not find method for type {type} and name {name} and parameters {parameters?.Description()}");
                return null;
            }

            if (generics != null) result = result.MakeGenericMethod(generics);
            return result;
        }

        /// <summary>Gets the reflection information for a method by searching the type and all its super types</summary>
        /// <param name="type">The type where the method is declared</param>
        /// <param name="name">The name of the method (case sensitive)</param>
        /// <param name="parameters">Optional parameters to target a specific overload of the method</param>
        /// <param name="generics">Optional list of types that define the generic version of the method</param>
        /// <returns>A <see cref="MethodInfo"/> if method found, otherwise null</returns>
        public static MethodInfo Method(Type type, string name, Type[] parameters = null, Type[] generics = null)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (name == null) throw new ArgumentNullException(nameof(name));

            MethodInfo result;
            var modifiers = new ParameterModifier[] { };
            if (parameters == null)
                try
                {
                    result = FindIncludingBaseTypes(type, t => t.GetMethod(name, all));
                }
                catch (AmbiguousMatchException ex)
                {
                    result = FindIncludingBaseTypes(type, t => t.GetMethod(name, all, null, new Type[0], modifiers));

                    if (result == null)
                        throw new AmbiguousMatchException($"Ambiguous match in Harmony patch for {type}:{name}.{ex}");
                }
            else
                result = FindIncludingBaseTypes(type, t => t.GetMethod(name, all, null, parameters, modifiers));

            if (result == null)
            {
                Logger.Log(Logger.LogChannel.Warn, () => $"AccessTools.Method: Could not find method for type {type} and name {name} and parameters {parameters?.Description()}");
                return null;
            }

            if (generics != null) result = result.MakeGenericMethod(generics);
            return result;
        }

        /// <summary>Gets the reflection information for a method by searching the type and all its super types</summary>
        /// <param name="typeColonMethodname">The target method in form <c>Namespace.Type1.Type2:MethodName</c> of the type where the method is declared</param>
        /// <param name="parameters">Optional parameters to target a specific overload of the method</param>
        /// <param name="generics">Optional list of types that define the generic version of the method</param>
        /// <returns>A <see cref="MethodInfo"/> if method found, otherwise null</returns>
        public static MethodInfo Method(string typeColonMethodname, Type[] parameters = null, Type[] generics = null)
        {
            if (typeColonMethodname == null) throw new ArgumentNullException(nameof(typeColonMethodname));

            var parts = typeColonMethodname.Split(':');
            if (parts.Length != 2)
                throw new ArgumentException("Method must be specified as 'Namespace.Type1.Type2:MethodName", nameof(typeColonMethodname));

            var type = TypeByName(parts[0]);
            return Method(type, parts[1], parameters, generics);
        }

        /// <summary>Gets the names of all method that are declared in a type</summary>
        /// <param name="type">The declaring type</param>
        /// <returns>A list of method names</returns>
        public static List<string> GetMethodNames(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));

            return GetDeclaredMethods(type).Select(m => m.Name).ToList();
        }

        /// <summary>Gets the names of all method that are declared in the type of the instance</summary>
        /// <param name="instance">An instance of the type to search in</param>
        /// <returns>A list of method names</returns>
        public static List<string> GetMethodNames(object instance)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));

            return GetMethodNames(instance.GetType());
        }

        /// <summary>Gets the names of all fields that are declared in a type</summary>
        /// <param name="type">The declaring type</param>
        /// <returns>A list of field names</returns>
        public static List<string> GetFieldNames(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));

            return GetDeclaredFields(type).Select(f => f.Name).ToList();
        }

        /// <summary>Gets the names of all fields that are declared in the type of the instance</summary>
        /// <param name="instance">An instance of the type to search in</param>
        /// <returns>A list of field names</returns>
        public static List<string> GetFieldNames(object instance)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));

            return GetFieldNames(instance.GetType());
        }

        /// <summary>Gets the names of all properties that are declared in a type</summary>
        /// <param name="type">The declaring type</param>
        /// <returns>A list of property names</returns>
        public static List<string> GetPropertyNames(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));

            return GetDeclaredProperties(type).Select(f => f.Name).ToList();
        }

        /// <summary>Gets the names of all properties that are declared in the type of the instance</summary>
        /// <param name="instance">An instance of the type to search in</param>
        /// <returns>A list of property names</returns>
        public static List<string> GetPropertyNames(object instance)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));

            return GetPropertyNames(instance.GetType());
        }

        /// <summary>
        /// Gets the type of a given member<br/>
        /// * For fields and properties, this is the type of the field/property<br/>
        /// * For methods, this is the return type of the method<br/>
        /// * For event handlers, this is the event handler type
        /// </summary>
        /// <param name="member">A <see cref="MemberInfo"/> the type of which to get</param>
        /// <returns>The type that represents the member</returns>
        public static Type GetUnderlyingType(this MemberInfo member)
        {
            if (member == null) throw new ArgumentNullException(nameof(member));

            switch (member.MemberType)
            {
                case MemberTypes.Event:
                    return ((EventInfo) member).EventHandlerType;
                case MemberTypes.Field:
                    return ((FieldInfo) member).FieldType;
                case MemberTypes.Method:
                    return ((MethodInfo) member).ReturnType;
                case MemberTypes.Property:
                    return ((PropertyInfo) member).PropertyType;
                default:
                    throw new ArgumentException(
                        "Member must be of type EventInfo, FieldInfo, MethodInfo, or PropertyInfo");
            }
        }

        /// <summary>Gets the reflection information for a directly declared constructor</summary>
        /// <param name="type">The type where the constructor is declared</param>
        /// <param name="parameters">Optional parameters to target a specific overload of the constructor</param>
        /// <returns>A <see cref="ConstructorInfo"/> if found, otherwise null</returns>
        public static ConstructorInfo DeclaredConstructor(Type type, Type[] parameters = null)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));

            if (parameters == null) parameters = new Type[0];
            return type.GetConstructor(allDeclared, null, parameters, new ParameterModifier[] { });
        }

        /// <summary>Gets the reflection information for a directly declared constructor</summary>
        /// <param name="type">The type where the constructor is declared</param>
        /// <param name="parameters">Optional parameters to target a specific overload of the constructor</param>
        /// <param name="searchForStatic">Optional parameters to only consider static constructors</param>
        /// <returns>A <see cref="ConstructorInfo"/> if found, otherwise null</returns>
        public static ConstructorInfo DeclaredConstructor(Type type, Type[] parameters, bool searchForStatic)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));

            if (parameters == null) parameters = new Type[0];
            var exclude = searchForStatic ? BindingFlags.Instance : BindingFlags.Static;
            return type.GetConstructor(allDeclared & ~exclude, null, parameters, new ParameterModifier[] { });
        }

        /// <summary>Gets the reflection information for a constructor by searching the type and all its super types</summary>
        /// <param name="type">The type where the constructor is declared</param>
        /// <param name="parameters">Optional parameters to target a specific overload of the method</param>
        /// <returns>A <see cref="ConstructorInfo"/> if found, otherwise null</returns>
        public static ConstructorInfo Constructor(Type type, Type[] parameters = null)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));

            if (parameters == null) parameters = new Type[0];
            return FindIncludingBaseTypes(
                type, t => t.GetConstructor(all, null, parameters, new ParameterModifier[] { }));
        }

        /// <summary>Gets the reflection information for a constructor by searching the type and all its super types</summary>
        /// <param name="type">The type where the constructor is declared</param>
        /// <param name="parameters">Optional parameters to target a specific overload of the method</param>
        /// <param name="searchForStatic">Optional parameters to only consider static constructors</param>
        /// <returns>A <see cref="ConstructorInfo"/> if found, otherwise null</returns>
        public static ConstructorInfo Constructor(Type type, Type[] parameters, bool searchForStatic)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));

            if (parameters == null) parameters = new Type[0];
            var exclude = searchForStatic ? BindingFlags.Instance : BindingFlags.Static;
            return FindIncludingBaseTypes(
                type, t => t.GetConstructor(all & ~exclude, null, parameters, new ParameterModifier[] { }));
        }

        /// <summary>Gets reflection information for all declared constructors</summary>
        /// <param name="type">The type where the constructors are declared</param>
        /// <returns>A list of <see cref="ConstructorInfo"/> declared in the type</returns>
        public static List<ConstructorInfo> GetDeclaredConstructors(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));

            return type.GetConstructors(allDeclared).Where(method => method.DeclaringType == type).ToList();
        }

        /// <summary>Gets reflection information for all declared constructors</summary>
        /// <param name="type">The type where the constructors are declared</param>
        /// <param name="searchForStatic">Optional parameters to only consider static constructors</param>
        /// <returns>A list of <see cref="ConstructorInfo"/> declared in the type</returns>
        public static List<ConstructorInfo> GetDeclaredConstructors(Type type, bool? searchForStatic)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            var exclude = BindingFlags.Default;
            if (searchForStatic.HasValue)
                exclude = searchForStatic.Value ? BindingFlags.Instance : BindingFlags.Static;
            return type.GetConstructors(allDeclared & ~exclude).Where(method => method.DeclaringType == type).ToList();
        }

        /// <summary>Gets reflection information for all declared methods</summary>
        /// <param name="type">The type where the methods are declared</param>
        /// <returns>A list of <see cref="MethodInfo"/> declared in the type</returns>
        public static List<MethodInfo> GetDeclaredMethods(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));

            return type.GetMethods(allDeclared).ToList();
        }

        /// <summary>Gets reflection information for all declared properties</summary>
        /// <param name="type">The type where the properties are declared</param>
        /// <returns>A list of <see cref="PropertyInfo"/> declared in the type</returns>
        public static List<PropertyInfo> GetDeclaredProperties(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));

            return type.GetProperties(allDeclared).ToList();
        }

        /// <summary>Gets reflection information for all declared fields</summary>
        /// <param name="type">The type where the fields are declared</param>
        /// <returns>A list of <see cref="FieldInfo"/> declared in the type</returns>
        public static List<FieldInfo> GetDeclaredFields(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));

            return type.GetFields(allDeclared).ToList();
        }

        /// <summary>Gets the return type of a method or constructor</summary>
        /// <param name="methodOrConstructor">The method or constructor</param>
        /// <returns>The return type of the method</returns>
        public static Type GetReturnedType(MethodBase methodOrConstructor)
        {
            if (methodOrConstructor == null) throw new ArgumentNullException(nameof(methodOrConstructor));

            if (methodOrConstructor is ConstructorInfo constructor) return typeof(void);
            return ((MethodInfo) methodOrConstructor).ReturnType;
        }

        /// <summary>Given a type, returns the first inner type matching a recursive search by name</summary>
        /// <param name="type">The type to start searching at</param>
        /// <param name="name">The name of the inner type (case sensitive)</param>
        /// <returns>The inner type if found, otherwise null</returns>
        public static Type Inner(Type type, string name)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (name == null) throw new ArgumentNullException(nameof(name));

            return FindIncludingBaseTypes(type, t => t.GetNestedType(name, all));
        }

        /// <summary>Given a type, returns the first inner type matching a recursive search with a predicate</summary>
        /// <param name="type">The type to start searching at</param>
        /// <param name="predicate">The predicate to search with</param>
        /// <returns>The inner type if found, otherwise null</returns>
        public static Type FirstInner(Type type, Func<Type, bool> predicate)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return type.GetNestedTypes(all).FirstOrDefault(predicate);
        }

        /// <summary>Given a type, returns the first method matching a predicate</summary>
        /// <param name="type">The type to start searching at</param>
        /// <param name="predicate">The predicate to search with</param>
        /// <returns>The <see cref="MethodInfo"/> if found, otherwise null</returns>
        public static MethodInfo FirstMethod(Type type, Func<MethodInfo, bool> predicate)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return type.GetMethods(allDeclared).FirstOrDefault(predicate);
        }

        /// <summary>Given a type, returns the first constructor matching a predicate</summary>
        /// <param name="type">The type to start searching at</param>
        /// <param name="predicate">The predicate to search with</param>
        /// <returns>The <see cref="ConstructorInfo"/> if found, otherwise null</returns>
        public static ConstructorInfo FirstConstructor(Type type, Func<ConstructorInfo, bool> predicate)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return type.GetConstructors(allDeclared).FirstOrDefault(predicate);
        }

        /// <summary>Given a type, returns the first property matching a predicate</summary>
        /// <param name="type">The type to start searching at</param>
        /// <param name="predicate">The predicate to search with</param>
        /// <returns>The <see cref="PropertyInfo"/> if found, otherwise null</returns>
        public static PropertyInfo FirstProperty(Type type, Func<PropertyInfo, bool> predicate)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return type.GetProperties(allDeclared).FirstOrDefault(predicate);
        }

        /// <summary>Returns an array containing the type of each object in the given array</summary>
        /// <param name="parameters">An array of objects</param>
        /// <returns>An array of types or an empty array if parameters is null</returns>
        public static Type[] GetTypes(object[] parameters)
        {
            if (parameters == null) return new Type[0];
            return parameters.Select(p => p == null ? typeof(object) : p.GetType()).ToArray();
        }

        /// <summary>Creates an array of input parameters for a given method and a given set of potential inputs</summary>
        /// <param name="method">The method you are planing to call</param>
        /// <param name="inputs"> The possible input parameters in any order</param>
        /// <returns>An object array matching the method signature</returns>
        public static object[] ActualParameters(MethodBase method, object[] inputs)
        {
            if (method == null) throw new ArgumentNullException(nameof(method));
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));

            var inputTypes = inputs.Select(obj => obj?.GetType()).ToList();
            return method.GetParameters().Select(p => p.ParameterType).Select(pType =>
            {
                var index = inputTypes.FindIndex(inType => inputTypes != null && pType.IsAssignableFrom(inType));
                if (index >= 0)
                    return inputs[index];
                return GetDefaultValue(pType);
            }).ToArray();
        }

        /// <summary>A read/writable reference to a field</summary>
        /// <typeparam name="T">The type the field is defined in</typeparam>
        /// <typeparam name="U">The type of the field</typeparam>
        /// <param name="obj">The runtime instance to access the field (leave empty for static fields)</param>
        /// <returns>The value of the field (or an assignable object)</returns>
        public delegate ref U FieldRef<T, U>(T obj = default(T));

        /// <summary>Creates a field reference</summary>
        /// <typeparam name="T">The type the field is defined in</typeparam>
        /// <typeparam name="U">The type of the field</typeparam>
        /// <param name="fieldName">The name of the field</param>
        /// <returns>A read and writable field reference</returns>
        public static FieldRef<T, U> FieldRefAccess<T, U>(string fieldName)
        {
            const BindingFlags bf = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

            var fi = typeof(T).GetField(fieldName, bf);
            return FieldRefAccess<T, U>(fi);
        }

        /// <summary>Creates a field reference</summary>
        /// <typeparam name="T">The type the field is defined in or "object" if type cannot be accessed at compile time</typeparam>
        /// <typeparam name="U">The type of the field</typeparam>
        /// <param name="fieldInfo">FieldInfo for the field</param>
        /// <returns>A readable and writable field reference</returns>
        public static FieldRef<T, U> FieldRefAccess<T, U>(FieldInfo fieldInfo)
        {
            if (fieldInfo == null)
                throw new ArgumentNullException(nameof(fieldInfo));
            if (!typeof(U).IsAssignableFrom(fieldInfo.FieldType))
                throw new ArgumentException("FieldInfo type does not match FieldRefAccess return type.");
            if (typeof(T) != typeof(object))
                if (fieldInfo.DeclaringType == null || !fieldInfo.DeclaringType.IsAssignableFrom(typeof(T)))
                    throw new MissingFieldException(typeof(T).Name, fieldInfo.Name);

            var dm = new DynamicMethodDefinition($"__refget_{typeof(T).Name}_fi_{fieldInfo.Name}", typeof(U).MakeByRefType(), new []{ typeof(T) });

            var il = dm.GetILGenerator();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldflda, fieldInfo);
            il.Emit(OpCodes.Ret);

            return (FieldRef<T, U>) dm.Generate().CreateDelegate<FieldRef<T, U>>();
        }

        /// <summary>Creates a field reference for a specific instance</summary>
        /// <typeparam name="T">The type the field is defined in</typeparam>
        /// <typeparam name="U">The type of the field</typeparam>
        /// <param name="instance">The instance</param>
        /// <param name="fieldName">The name of the field</param>
        /// <returns>A readable and writable field reference</returns>
        public static ref U FieldRefAccess<T, U>(T instance, string fieldName)
        {
            return ref FieldRefAccess<T, U>(fieldName)(instance);
        }

        /// <summary>Creates an instance field reference delegate for a private type</summary>
        /// <typeparam name="F">The type of the field</typeparam>
        /// <param name="type">The class/type</param>
        /// <param name="fieldName">The name of the field</param>
        /// <returns>A read and writable <see cref="FieldRef{T,F}"/> delegate</returns>
        public static FieldRef<object, F> FieldRefAccess<F>(Type type, string fieldName)
        {
            return FieldRefAccess<object, F>(Field(type, fieldName));
        }

        /// <summary>A readable/writable reference delegate to a static field</summary>
        /// <typeparam name="F">The type of the field</typeparam>
        /// <returns>An readable/assignable object representing the static field</returns>
        public delegate ref F FieldRef<F>();

        /// <summary>Creates a static field reference</summary>
        /// <typeparam name="T">The type the field is defined in or "object" if type cannot be accessed at compile time</typeparam>
        /// <typeparam name="F">The type of the field</typeparam>
        /// <param name="fieldName">The name of the field</param>
        /// <returns>An readable/assignable object representing the static field</returns>
        public static ref F StaticFieldRefAccess<T, F>(string fieldName)
        {
            return ref StaticFieldRefAccess<F>(typeof(T), fieldName);
        }

        /// <summary>Creates a static field reference</summary>
        /// <typeparam name="T">The class the field is defined in or "object" if type cannot be accessed at compile time</typeparam>
        /// <typeparam name="F">The type of the field</typeparam>
        /// <param name="fieldInfo">The field</param>
        /// <returns>An readable/assignable object representing the static field</returns>
        ///
        public static ref F StaticFieldRefAccess<T, F>(FieldInfo fieldInfo)
        {
            return ref StaticFieldRefAccess<F>(fieldInfo)();
        }

        /// <summary>Creates a static field reference delegate</summary>
        /// <typeparam name="F">The type of the field</typeparam>
        /// <param name="fieldInfo">FieldInfo for the field</param>
        /// <returns>A readable and writable field reference delegate</returns>
        public static FieldRef<F> StaticFieldRefAccess<F>(FieldInfo fieldInfo)
        {
            if (fieldInfo == null)
                throw new ArgumentNullException(nameof(fieldInfo));
            var t = fieldInfo.DeclaringType;

            var dm = new DynamicMethodDefinition($"__refget_{t.Name}_static_fi_{fieldInfo.Name}", typeof(F).MakeByRefType(), new Type[0]);

            var il = dm.GetILGenerator();
            il.Emit(OpCodes.Ldsflda, fieldInfo);
            il.Emit(OpCodes.Ret);

            return (FieldRef<F>)dm.Generate().CreateDelegate<FieldRef<F>>();
        }

        /// <summary>Creates a static field reference</summary>
        /// <typeparam name="F">The type of the field</typeparam>
        /// <param name="type">The class/type</param>
        /// <param name="fieldName">The name of the field</param>
        /// <returns>An readable/assignable object representing the static field</returns>
        public static ref F StaticFieldRefAccess<F>(Type type, string fieldName)
        {
            const BindingFlags bf = BindingFlags.NonPublic |
                                    BindingFlags.Static |
                                    BindingFlags.DeclaredOnly;

            var fi = type.GetField(fieldName, bf);
            return ref StaticFieldRefAccess<F>(fi)();
        }

        /// <summary>Returns who called the current method</summary>
        /// <returns>The calling method (excluding the current method)</returns>
        public static MethodBase GetOutsideCaller()
        {
            var trace = new StackTrace(true);
            foreach (var frame in trace.GetFrames())
            {
                var method = frame.GetMethod();
                if (method.DeclaringType?.Namespace != typeof(Harmony).Namespace)
                    return method;
            }

            throw new InvalidOperationException("Unexpected end of stack trace");
        }

#if NET35 || NET40
        static readonly MethodInfo m_PrepForRemoting = Method(typeof(Exception), "PrepForRemoting")       // MS .NET
                                                    ?? Method(typeof(Exception), "FixRemotingException"); // mono .NET
        static readonly FastInvokeHandler PrepForRemoting = MethodInvoker.GetHandler(m_PrepForRemoting);
#endif

        /// <summary>Rethrows an exception while preserving its stack trace (throw statement typically clobbers existing stack traces)</summary>
        /// <param name="exception">The exception to rethrow</param>
        public static void RethrowException(Exception exception)
        {
#if NET35 || NET40
            PrepForRemoting(exception);
#else
			System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
#endif
            // For the sake of any static code analyzer, always throw exception, even if ExceptionDispatchInfo.Throw above was called.
            throw exception;
        }

        /// <summary>Checks if the current code is running on Mono runtime</summary>
        /// <returns>True if we are running under Mono, false otherwise (.NET)</returns>
        public static bool IsMonoRuntime { get; } = Type.GetType("Mono.Runtime") != null;

        /// <summary>Throws a missing member runtime exception</summary>
        /// <param name="type">The type that is involved</param>
        /// <param name="names">A list of names</param>
        public static void ThrowMissingMemberException(Type type, params string[] names)
        {
            var fields = string.Join(",", GetFieldNames(type).ToArray());
            var properties = string.Join(",", GetPropertyNames(type).ToArray());
            throw new MissingMemberException(
                $"{string.Join(",", names)}; available fields: {fields}; available properties: {properties}");
        }

        /// <summary>Gets default value for a specific type</summary>
        /// <param name="type">The type</param>
        /// <returns>The default value</returns>
        public static object GetDefaultValue(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));

            if (type == typeof(void)) return null;
            if (type.IsValueType)
                return Activator.CreateInstance(type);
            return null;
        }

        /// <summary>Creates an (possibly uninitialized) instance of a given type</summary>
        /// <param name="type">The type</param>
        /// <returns>The new instance</returns>
        public static object CreateInstance(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));
            var ctor = type.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, CallingConventions.Any,
                                           new Type[0], null);
            if (ctor != null)
                return Activator.CreateInstance(type);
            return FormatterServices.GetUninitializedObject(type);
        }

        /// <summary>Makes a deep copy of any object</summary>
        /// <typeparam name="T">The type of the instance that should be created</typeparam>
        /// <param name="source">The original object</param>
        /// <returns>A copy of the original object but of type T</returns>
        public static T MakeDeepCopy<T>(object source) where T : class
        {
            return MakeDeepCopy(source, typeof(T)) as T;
        }

        /// <summary>Makes a deep copy of any object</summary>
        /// <typeparam name="T">The type of the instance that should be created</typeparam>
        /// <param name="source">The original object</param>
        /// <param name="result">[out] The copy of the original object</param>
        /// <param name="processor">Optional value transformation function (taking a field name and src/dst <see cref="Traverse"/> instances)</param>
        /// <param name="pathRoot">The optional path root to start with</param>
        public static void MakeDeepCopy<T>(object source, out T result,
                                           Func<string, Traverse, Traverse, object> processor = null,
                                           string pathRoot = "")
        {
            result = (T) MakeDeepCopy(source, typeof(T), processor, pathRoot);
        }

        /// <summary>Makes a deep copy of any object</summary>
        /// <param name="source">The original object</param>
        /// <param name="resultType">The type of the instance that should be created</param>
        /// <param name="processor">Optional value transformation function (taking a field name and src/dst <see cref="Traverse"/> instances)</param>
        /// <param name="pathRoot">The optional path root to start with</param>
        /// <returns>The copy of the original object</returns>
        public static object MakeDeepCopy(object source, Type resultType,
                                          Func<string, Traverse, Traverse, object> processor = null,
                                          string pathRoot = "")
        {
            if (resultType == null) throw new ArgumentNullException(nameof(resultType));
            if (pathRoot == null) throw new ArgumentNullException(nameof(pathRoot));

            if (source == null)
                return null;

            resultType = Nullable.GetUnderlyingType(resultType) ?? resultType;
            var type = source.GetType();

            if (type.IsPrimitive)
                return source;

            if (type.IsEnum)
                return Enum.ToObject(resultType, (int) source);

            if (type.IsGenericType && resultType.IsGenericType)
            {
                var addOperation = FirstMethod(resultType, m => m.Name == "Add" && m.GetParameters().Length == 1);
                if (addOperation != null)
                {
                    var addableResult = Activator.CreateInstance(resultType);
                    var addInvoker = MethodInvoker.GetHandler(addOperation);
                    var newElementType = resultType.GetGenericArguments()[0];
                    var i = 0;
                    foreach (var element in source as IEnumerable)
                    {
                        var iStr = (i++).ToString();
                        var path = pathRoot.Length > 0 ? $"{pathRoot}.{iStr}" : iStr;
                        var newElement = MakeDeepCopy(element, newElementType, processor, path);
                        addInvoker(addableResult, new object[] {newElement});
                    }

                    return addableResult;
                }

                // TODO: add dictionaries support
                // maybe use methods in Dictionary<KeyValuePair<TKey,TVal>>
            }

            if (type.IsArray && resultType.IsArray)
            {
                var elementType = resultType.GetElementType();
                var length = ((Array) source).Length;
                var arrayResult = Activator.CreateInstance(resultType, new object[] {length}) as object[];
                var originalArray = source as object[];
                for (var i = 0; i < length; i++)
                {
                    var iStr = i.ToString();
                    var path = pathRoot.Length > 0 ? $"{pathRoot}.{iStr}" : iStr;
                    arrayResult[i] = MakeDeepCopy(originalArray[i], elementType, processor, path);
                }

                return arrayResult;
            }

            var ns = type.Namespace;
            if (ns == "System" || (ns?.StartsWith("System.") ?? false))
                return source;

            var result = CreateInstance(resultType == typeof(object) ? type : resultType);
            Traverse.IterateFields(source, result, (name, src, dst) =>
            {
                var path = pathRoot.Length > 0 ? $"{pathRoot}.{name}" : name;
                var value = processor != null ? processor(path, src, dst) : src.GetValue();
                dst.SetValue(MakeDeepCopy(value, dst.GetValueType(), processor, path));
            });
            return result;
        }

        /// <summary>Tests if a type is a struct</summary>
        /// <param name="type">The type</param>
        /// <returns>True if the type is a struct</returns>
        public static bool IsStruct(Type type)
        {
            return type.IsValueType && !IsValue(type) && !IsVoid(type);
        }

        /// <summary>Tests if a type is a class</summary>
        /// <param name="type">The type</param>
        /// <returns>True if the type is a class</returns>
        public static bool IsClass(Type type)
        {
            return !type.IsValueType;
        }

        /// <summary>Tests if a type is a value type</summary>
        /// <param name="type">The type</param>
        /// <returns>True if the type is a value type</returns>
        public static bool IsValue(Type type)
        {
            return type.IsPrimitive || type.IsEnum;
        }

        /// <summary>Tests if a type is void</summary>
        /// <param name="type">The type</param>
        /// <returns>True if the type is void</returns>
        public static bool IsVoid(Type type)
        {
            return type == typeof(void);
        }

        /// <summary>Test whether an instance is of a nullable type</summary>
        /// <typeparam name="T">Type of instance</typeparam>
        /// <param name="instance">An instance to test</param>
        /// <returns>True if instance is of nullable type, false if not</returns>
        public static bool IsOfNullableType<T>(this T instance)
        {
            return Nullable.GetUnderlyingType(typeof(T)) != null;
        }

        /// <summary>Test if a class member is declared directly in the type and not in a base type</summary>
        /// <param name="member">A member</param>
        /// <returns>True if the member is a declared</returns>
        public static bool IsDeclaredMember<T>(this T member) where T : MemberInfo
        {
            return member.DeclaringType == member.ReflectedType;
        }

        /// <summary>Gets the real implementation of a class member</summary>
        /// <param name="member">A member</param>
        /// <returns>The member itself if its declared. Otherwise the member that is actually implemented in a base type</returns>
        public static T GetDeclaredMember<T>(this T member) where T : MemberInfo
        {
            if (member.DeclaringType == null || member.IsDeclaredMember())
                return member;

            var metaToken = member.MetadataToken;
            foreach (var other in member.DeclaringType.GetMembers(all))
                if (other.MetadataToken == metaToken)
                    return (T) other;

            return member;
        }

        /// <summary>Tests if a type is an integer type</summary>
        /// <param name="type">The type</param>
        /// <returns>True if the type represents some integer</returns>
        ///
        public static bool IsInteger(Type type)
        {
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Tests if a type is a floating point type</summary>
        /// <param name="type">The type</param>
        /// <returns>True if the type represents some floating point</returns>
        ///
        public static bool IsFloatingPoint(Type type)
        {
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Decimal:
                case TypeCode.Double:
                case TypeCode.Single:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Tests if a type is a numerical type</summary>
        /// <param name="type">The type</param>
        /// <returns>True if the type represents some number</returns>
        ///
        public static bool IsNumber(Type type)
        {
            return IsInteger(type) || IsFloatingPoint(type);
        }

        /// <summary>Calculates a combined hash code for an enumeration of objects</summary>
        /// <param name="objects">The objects</param>
        /// <returns>The hash code</returns>
        ///
        public static int CombinedHashCode(IEnumerable<object> objects)
        {
            var hash1 = (5381 << 16) + 5381;
            var hash2 = hash1;
            var i = 0;
            foreach (var obj in objects)
            {
                if (i % 2 == 0)
                    hash1 = ((hash1 << 5) + hash1 + (hash1 >> 27)) ^ obj.GetHashCode();
                else
                    hash2 = ((hash2 << 5) + hash2 + (hash2 >> 27)) ^ obj.GetHashCode();
                ++i;
            }
            return hash1 + (hash2 * 1566083941);
        }
    }
}