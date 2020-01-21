using HarmonyLib;
using HarmonyLibTests.Assets;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib.Internal;

namespace HarmonyLibTests
{
    [TestFixture]
    public class Test_AccessCache
    {
        private void InjectField(AccessCache cache)
        {
            var f_fields = cache.GetType().GetField("fields", AccessTools.all);
            Assert.IsNotNull(f_fields);
            var fields = (Dictionary<Type, Dictionary<string, FieldInfo>>)f_fields.GetValue(cache);
            Assert.IsNotNull(fields);
            _ = fields.TryGetValue(typeof(AccessToolsClass), out var infos);
            Assert.IsNotNull(infos);

            _ = infos.Remove("field1");
            infos.Add("field1", typeof(AccessToolsClass).GetField("field2", AccessTools.all));
        }

        private void InjectProperty(AccessCache cache)
        {
            var f_properties = cache.GetType().GetField("properties", AccessTools.all);
            Assert.IsNotNull(f_properties);
            var properties = (Dictionary<Type, Dictionary<string, PropertyInfo>>)f_properties.GetValue(cache);
            Assert.IsNotNull(properties);
            _ = properties.TryGetValue(typeof(AccessToolsClass), out var infos);
            Assert.IsNotNull(infos);

            _ = infos.Remove("Property");
            infos.Add("Property", typeof(AccessToolsClass).GetProperty("Property2", AccessTools.all));
        }

        private void InjectMethod(AccessCache cache)
        {
            var f_methods = cache.GetType().GetField("methods", AccessTools.all);
            Assert.IsNotNull(f_methods);
            var methods = (Dictionary<Type, Dictionary<string, Dictionary<int, MethodBase>>>)f_methods.GetValue(cache);
            Assert.IsNotNull(methods);
            _ = methods.TryGetValue(typeof(AccessToolsClass), out var dicts);
            Assert.IsNotNull(dicts);
            _ = dicts.TryGetValue("Method1", out var infos);
            Assert.IsNotNull(dicts);
            var argumentHash = infos.Keys.ToList().First();
            _ = infos.Remove(argumentHash);
            infos.Add(argumentHash, typeof(AccessToolsClass).GetMethod("Method2", AccessTools.all));
        }

        [Test]
        public void AccessCache_Field()
        {
            var type = typeof(AccessToolsClass);

            Assert.IsNotNull(new AccessCache().GetFieldInfo(type, "field1"));

            var cache1 = new AccessCache();
            var finfo1 = cache1.GetFieldInfo(type, "field1");
            InjectField(cache1);
            var cache2 = new AccessCache();
            var finfo2 = cache2.GetFieldInfo(type, "field1");
            Assert.AreSame(finfo1, finfo2);

            var cache = new AccessCache();
            var finfo3 = cache.GetFieldInfo(type, "field1");
            InjectField(cache);
            var finfo4 = cache.GetFieldInfo(type, "field1");
            Assert.AreNotSame(finfo3, finfo4);
        }

        [Test]
        public void AccessCache_Property()
        {
            var type = typeof(AccessToolsClass);

            Assert.IsNotNull(new AccessCache().GetPropertyInfo(type, "Property"));

            var cache1 = new AccessCache();
            var pinfo1 = cache1.GetPropertyInfo(type, "Property");
            InjectProperty(cache1);
            var cache2 = new AccessCache();
            var pinfo2 = cache2.GetPropertyInfo(type, "Property");
            Assert.AreSame(pinfo1, pinfo2);

            var cache = new AccessCache();
            var pinfo3 = cache.GetPropertyInfo(type, "Property");
            InjectProperty(cache);
            var pinfo4 = cache.GetPropertyInfo(type, "Property");
            Assert.AreNotSame(pinfo3, pinfo4);
        }

        [Test]
        public void AccessCache_Method()
        {
            var type = typeof(AccessToolsClass);

            Assert.IsNotNull(new AccessCache().GetMethodInfo(type, "Method1", Type.EmptyTypes));

            var cache1 = new AccessCache();
            var minfo1 = cache1.GetMethodInfo(type, "Method1", Type.EmptyTypes);
            InjectMethod(cache1);
            var cache2 = new AccessCache();
            var minfo2 = cache2.GetMethodInfo(type, "Method1", Type.EmptyTypes);
            Assert.AreSame(minfo1, minfo2);

            var cache = new AccessCache();
            var minfo3 = cache.GetMethodInfo(type, "Method1", Type.EmptyTypes);
            InjectMethod(cache);
            var minfo4 = cache.GetMethodInfo(type, "Method1", Type.EmptyTypes);
            Assert.AreNotSame(minfo3, minfo4);
        }
    }
}