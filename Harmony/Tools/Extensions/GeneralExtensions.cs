using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace HarmonyLib
{
    /// <summary>General extensions for common cases</summary>
    public static class GeneralExtensions
    {
        /// <summary>Joins an enumeration with a value converter and a delimiter to a string</summary>
        /// <typeparam name="T">The inner type of the enumeration</typeparam>
        /// <param name="enumeration">The enumeration</param>
        /// <param name="converter">An optional value converter (from T to string)</param>
        /// <param name="delimiter">An optional delimiter</param>
        /// <returns>The values joined into a string</returns>
        ///
        public static string Join<T>(this IEnumerable<T> enumeration,
                                     Func<T, string> converter = null,
                                     string delimiter = ", ")
        {
            if (converter == null)
                converter = t => t.ToString();
            return enumeration.Aggregate("", (prev, curr) => prev + (prev != "" ? delimiter : "") + converter(curr));
        }

        /// <summary>Converts an array of types (for example methods arguments) into a human readable form</summary>
        /// <param name="parameters">The array of types</param>
        /// <returns>A human readable description including brackets</returns>
        ///
        public static string Description(this Type[] parameters)
        {
            if (parameters == null)
                return "NULL";
            return $"({parameters.Join(p => p.FullDescription())})";
        }

        /// <summary>A full description of a type</summary>
        /// <param name="type">The type</param>
        /// <returns>A human readable description</returns>
        ///
        public static string FullDescription(this Type type)
        {
            if (type == null)
                return "null";

            var ns = type.Namespace;
            if (string.IsNullOrEmpty(ns) == false)
                ns += ".";
            var result = ns + type.Name;

            if (type.IsGenericType)
            {
                result += "<";
                var subTypes = type.GetGenericArguments();
                for (var i = 0; i < subTypes.Length; i++)
                {
                    if (result.EndsWith("<", StringComparison.Ordinal) == false)
                        result += ", ";
                    result += subTypes[i].FullDescription();
                }

                result += ">";
            }

            return result;
        }

        /// <summary>A a full description of a method or a constructor without assembly details but with generics</summary>
        /// <param name="method">The method or constructor</param>
        /// <returns>A human readable description</returns>
        ///
        public static string FullDescription(this MethodBase method)
        {
            if (method == null)
                return "null";
            var parameters = method.GetParameters().Select(p => p.ParameterType).ToArray();
            var returnType = AccessTools.GetReturnedType(method);
            return
                $"{returnType.FullDescription()} {method.DeclaringType.FullDescription()}.{method.Name}{parameters.Description()}";
        }

        /// <summary>A helper converting parameter infos to types</summary>
        /// <param name="pinfo">The array of ParameterInfo</param>
        /// <returns>The parameter types</returns>
        ///
        public static Type[] Types(this ParameterInfo[] pinfo)
        {
            return pinfo.Select(pi => pi.ParameterType).ToArray();
        }

        /// <summary>A helper to access a value via key from a dictionary</summary>
        /// <typeparam name="S">The key type</typeparam>
        /// <typeparam name="T">The value type</typeparam>
        /// <param name="dictionary">The dictionary</param>
        /// <param name="key">The key</param>
        /// <returns>The value for the key or the default value (of T) if that key does not exist</returns>
        ///
        public static T GetValueSafe<S, T>(this Dictionary<S, T> dictionary, S key)
        {
            T result;
            if (dictionary.TryGetValue(key, out result))
                return result;
            return default (T);
        }

        /// <summary>A helper to access a value via key from a dictionary with extra casting</summary>
        /// <typeparam name="T">The value type</typeparam>
        /// <param name="dictionary">The dictionary</param>
        /// <param name="key">The key</param>
        /// <returns>The value for the key or the default value (of T) if that key does not exist or cannot be cast to T</returns>
        ///
        public static T GetTypedValue<T>(this Dictionary<string, object> dictionary, string key)
        {
            object result;
            if (dictionary.TryGetValue(key, out result))
                if (result is T)
                    return (T)result;
            return default (T);
        }
    }
}