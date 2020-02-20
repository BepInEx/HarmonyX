using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace HarmonyLib
{
    /// <summary>A wrapper around a method to use it as a patch (for example a Prefix)</summary>
    public class HarmonyMethod
    {
        /// <summary>The original method</summary>
        public MethodInfo method; // need to be called 'method'

        /// <summary>Declaring class</summary>
        public Type declaringType;

        /// <summary>Method name</summary>
        public string methodName;

        /// <summary>Method type</summary>
        public MethodType? methodType;

        /// <summary>Argument types</summary>
        public Type[] argumentTypes;

        /// <summary>Priority</summary>
        public int priority = -1;

        /// <summary>Before parameter</summary>
        public string[] before;

        /// <summary>After parameter</summary>
        public string[] after;

        /// <summary>Reverse patch type</summary>
        public HarmonyReversePatchType? reversePatchType;

        /// <summary>Default constructor</summary>
        public HarmonyMethod()
        {
        }

        private void ImportMethod(MethodInfo theMethod)
        {
            method = theMethod;
            if (method != null)
            {
                var infos = HarmonyMethodExtensions.GetFromMethod(method);
                if (infos != null)
                    Merge(infos).CopyTo(this);
            }
        }

        /// <summary>Creates an annotation from a method</summary>
        /// <param name="method">The original method</param>
        ///
        public HarmonyMethod(MethodInfo method)
        {
            if (method == null)
                throw new ArgumentNullException(nameof(method));
            ImportMethod(method);
        }

        /// <summary>Creates an annotation from a method.</summary>
        /// <param name="type">The type</param>
        /// <param name="name">The method name</param>
        /// <param name="parameters">The optional argument types for overloaded methods</param>
        ///
        public HarmonyMethod(Type type, string name, Type[] parameters = null)
        {
            var result = AccessTools.Method(type, name, parameters);
            if (result == null)
                throw new ArgumentException(
                    $"Cannot not find method for type {type} and name {name} and parameters {parameters?.Description()}");
            ImportMethod(result);
        }

        /// <summary>Gets the names of all internal patch info fields</summary>
        /// <returns>A list of field names</returns>
        ///
        public static List<string> HarmonyFields()
        {
            return AccessTools.GetFieldNames(typeof(HarmonyMethod)).Where(s => s != "method").ToList();
        }

        /// <summary>Merges annotations</summary>
        /// <param name="attributes">The annotations</param>
        /// <returns>A merged annotation</returns>
        ///
        public static HarmonyMethod Merge(List<HarmonyMethod> attributes)
        {
            var result = new HarmonyMethod();
            if (attributes == null) return result;
            var resultTrv = Traverse.Create(result);
            attributes.ForEach(attribute =>
            {
                var trv = Traverse.Create(attribute);
                HarmonyFields().ForEach(f =>
                {
                    var val = trv.Field(f).GetValue();
                    if (val != null)
                        HarmonyMethodExtensions.SetValue(resultTrv, f, val);
                });
            });
            return result;
        }

        /// <summary>Returns a string that represents the annotation</summary>
        /// <returns>A string representation</returns>
        ///
        public override string ToString()
        {
            var result = "HarmonyMethod[";
            var trv = Traverse.Create(this);
            HarmonyFields().ForEach(f => { result += $"{f}{'='}{trv.Field(f).GetValue()}"; });
            return $"{result}]";
        }
    }

    /// <summary>Annotation extensions</summary>
    public static class HarmonyMethodExtensions
    {
        internal static void SetValue(Traverse trv, string name, object val)
        {
            if (val == null) return;
            var fld = trv.Field(name);
            if (name == nameof(HarmonyMethod.methodType) || name == nameof(HarmonyMethod.reversePatchType))
            {
                var enumType = Nullable.GetUnderlyingType(fld.GetValueType());
                val = Enum.ToObject(enumType, (int) val);
            }

            fld.SetValue(val);
        }

        /// <summary>Copies annotation information</summary>
        /// <param name="from">from</param>
        /// <param name="to">to</param>
        ///
        public static void CopyTo(this HarmonyMethod from, HarmonyMethod to)
        {
            if (to == null) return;
            var fromTrv = Traverse.Create(from);
            var toTrv = Traverse.Create(to);
            HarmonyMethod.HarmonyFields().ForEach(f =>
            {
                var val = fromTrv.Field(f).GetValue();
                if (val != null)
                    SetValue(toTrv, f, val);
            });
        }

        /// <summary>Clones an annotation</summary>
        /// <param name="original">The annotation to clone</param>
        /// <returns>A copy of the annotation</returns>
        ///
        public static HarmonyMethod Clone(this HarmonyMethod original)
        {
            var result = new HarmonyMethod();
            original.CopyTo(result);
            return result;
        }

        /// <summary>Merges annotations</summary>
        /// <param name="master">The master</param>
        /// <param name="detail">The detail</param>
        /// <returns>A new, merged copy</returns>
        ///
        public static HarmonyMethod Merge(this HarmonyMethod master, HarmonyMethod detail)
        {
            if (detail == null) return master;
            var result = new HarmonyMethod();
            var resultTrv = Traverse.Create(result);
            var masterTrv = Traverse.Create(master);
            var detailTrv = Traverse.Create(detail);
            HarmonyMethod.HarmonyFields().ForEach(f =>
            {
                var baseValue = masterTrv.Field(f).GetValue();
                var detailValue = detailTrv.Field(f).GetValue();
                SetValue(resultTrv, f, detailValue ?? baseValue);
            });
            return result;
        }

        private static HarmonyMethod GetHarmonyMethodInfo(object attribute)
        {
            var f_info = attribute.GetType().GetField(nameof(HarmonyAttribute.info), AccessTools.all);
            if (f_info == null) return null;
            if (f_info.FieldType.Name != nameof(HarmonyMethod)) return null;
            var info = f_info.GetValue(attribute);
            return AccessTools.MakeDeepCopy<HarmonyMethod>(info);
        }

        /// <summary>Gets all annotations on a class</summary>
        /// <param name="type">The class</param>
        /// <returns>All annotations</returns>
        ///
        public static List<HarmonyMethod> GetFromType(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            return type.GetCustomAttributes(true).Select(attr => GetHarmonyMethodInfo(attr)).Where(info => info != null)
                       .ToList();
        }

        /// <summary>Gets all annotations on a class in merged form</summary>
        /// <param name="type">The class</param>
        /// <returns>The merged HarmonyMethod</returns>
        ///
        public static HarmonyMethod GetMergedFromType(Type type)
        {
            return HarmonyMethod.Merge(GetFromType(type));
        }


        /// <summary>Gets all annotations on a method in merged form</summary>
        /// <param name="method">The method</param>
        /// <returns>The merged HarmonyMethod</returns>
        ///
        public static HarmonyMethod GetMergedFromMethod(MethodBase method)
        {
            return HarmonyMethod.Merge(GetFromMethod(method));
        }

        /// <summary>Gets all annotations on a method</summary>
        /// <param name="method">The method</param>
        /// <returns>All annotations</returns>
        ///
        public static List<HarmonyMethod> GetFromMethod(MethodBase method)
        {
            return method.GetCustomAttributes(true).Select(attr => GetHarmonyMethodInfo(attr))
                         .Where(info => info != null).ToList();
        }
    }
}