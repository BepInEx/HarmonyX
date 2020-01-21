using HarmonyLib;
using HarmonyLibTests.Assets;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace HarmonyLibTests
{
    [TestFixture]
    public class Test_AccessTools
    {
        [Test]
        public void AccessTools_Field1()
        {
            var type = typeof(AccessToolsClass);

            Assert.Throws<ArgumentNullException>(() => AccessTools.DeclaredField(null, null));
            Assert.Throws<ArgumentNullException>(() => AccessTools.DeclaredField(type, null));
            Assert.Throws<ArgumentNullException>(() => AccessTools.DeclaredField(null, "field1"));
            Assert.IsNull(AccessTools.DeclaredField(type, "unknown"));

            var field = AccessTools.DeclaredField(type, "field1");
            Assert.IsNotNull(field);
            Assert.AreEqual(type, field.DeclaringType);
            Assert.AreEqual("field1", field.Name);
        }

        [Test]
        public void AccessTools_Field2()
        {
            var type = typeof(AccessToolsClass);
            Assert.IsNotNull(AccessTools.Field(type, "field1"));
            Assert.IsNotNull(AccessTools.DeclaredField(type, "field1"));

            var subtype = typeof(AccessToolsSubClass);
            Assert.IsNotNull(AccessTools.Field(subtype, "field1"));
            Assert.IsNull(AccessTools.DeclaredField(subtype, "field1"));
        }

        [Test]
        public void AccessTools_Property1()
        {
            var type = typeof(AccessToolsClass);

            Assert.Throws<ArgumentNullException>(() => AccessTools.Property(null, null));
            Assert.Throws<ArgumentNullException>(() => AccessTools.Property(type, null));
            Assert.Throws<ArgumentNullException>(() => AccessTools.Property(null, "Property"));
            Assert.IsNull(AccessTools.Property(type, "unknown"));

            var prop = AccessTools.Property(type, "Property");
            Assert.IsNotNull(prop);
            Assert.AreEqual(type, prop.DeclaringType);
            Assert.AreEqual("Property", prop.Name);
        }

        [Test]
        public void AccessTools_Property2()
        {
            var type = typeof(AccessToolsClass);
            Assert.IsNotNull(AccessTools.Property(type, "Property"));
            Assert.IsNotNull(AccessTools.DeclaredProperty(type, "Property"));

            var subtype = typeof(AccessToolsSubClass);
            Assert.IsNotNull(AccessTools.Property(subtype, "Property"));
            Assert.IsNull(AccessTools.DeclaredProperty(subtype, "Property"));
        }

        [Test]
        public void AccessTools_Method1()
        {
            var type = typeof(AccessToolsClass);

            Assert.Throws<ArgumentNullException>(() => AccessTools.Method(null));
            Assert.Throws<ArgumentNullException>(() => AccessTools.Method(type, null));
            Assert.Throws<ArgumentNullException>(() => AccessTools.Method(null, "Method1"));
            Assert.IsNull(AccessTools.Method(type, "unknown"));

            var m1 = AccessTools.Method(type, "Method1");
            Assert.IsNotNull(m1);
            Assert.AreEqual(type, m1.DeclaringType);
            Assert.AreEqual("Method1", m1.Name);

            var m2 = AccessTools.Method("HarmonyLibTests.Assets.AccessToolsClass:Method1");
            Assert.IsNotNull(m2);
            Assert.AreEqual(type, m2.DeclaringType);
            Assert.AreEqual("Method1", m2.Name);

            var m3 = AccessTools.Method(type, "Method1", new Type[] { });
            Assert.IsNotNull(m3);

            var m4 = AccessTools.Method(type, "SetField", new Type[] { typeof(string) });
            Assert.IsNotNull(m4);
        }

        [Test]
        public void AccessTools_Method2()
        {
            var type = typeof(AccessToolsSubClass);

            var m1 = AccessTools.Method(type, "Method1");
            Assert.IsNotNull(m1);

            var m2 = AccessTools.DeclaredMethod(type, "Method1");
            Assert.IsNull(m2);
        }

        [Test]
        public void AccessTools_InnerClass()
        {
            var type = typeof(AccessToolsClass);

            Assert.Throws<ArgumentNullException>(() => AccessTools.Inner(null, null));
            Assert.Throws<ArgumentNullException>(() => AccessTools.Inner(type, null));
            Assert.Throws<ArgumentNullException>(() => AccessTools.Inner(null, "Inner"));
            Assert.IsNull(AccessTools.Inner(type, "unknown"));

            var cls = AccessTools.Inner(type, "Inner");
            Assert.IsNotNull(cls);
            Assert.AreEqual(type, cls.DeclaringType);
            Assert.AreEqual("Inner", cls.Name);
        }

        [Test]
        public void AccessTools_GetTypes()
        {
            var empty = AccessTools.GetTypes(null);
            Assert.IsNotNull(empty);
            Assert.AreEqual(0, empty.Length);

            // TODO: typeof(null) is ambiguous and resolves for now to <object>. is this a problem?
            var types = AccessTools.GetTypes(new object[]
            {
                "hi",
                123,
                null,
                new Test_AccessTools()
            });
            Assert.IsNotNull(types);
            Assert.AreEqual(4, types.Length);
            Assert.AreEqual(typeof(string), types[0]);
            Assert.AreEqual(typeof(int), types[1]);
            Assert.AreEqual(typeof(object), types[2]);
            Assert.AreEqual(typeof(Test_AccessTools), types[3]);
        }

        [Test]
        public void AccessTools_GetDefaultValue()
        {
            Assert.Throws<ArgumentNullException>(() => AccessTools.GetDefaultValue(null));
            Assert.AreEqual((float)0, AccessTools.GetDefaultValue(typeof(float)));
            Assert.AreEqual(null, AccessTools.GetDefaultValue(typeof(string)));
            Assert.AreEqual(BindingFlags.Default, AccessTools.GetDefaultValue(typeof(BindingFlags)));
            Assert.AreEqual(null, AccessTools.GetDefaultValue(typeof(IEnumerable<bool>)));
            Assert.AreEqual(null, AccessTools.GetDefaultValue(typeof(void)));
        }

        [Test]
        public void AccessTools_TypeExtension_Description()
        {
            var types = new Type[]
            {
                typeof(string),
                typeof(int),
                null,
                typeof(void),
                typeof(Test_AccessTools)
            };
            Assert.AreEqual("(System.String, System.Int32, null, System.Void, HarmonyLibTests.Test_AccessTools)",
                            types.Description());
        }

        [Test]
        public void AccessTools_TypeExtension_Types()
        {
            // public static void Resize<T>(ref T[] array, int newSize);
            var method = typeof(Array).GetMethod("Resize");
            var pinfo = method.GetParameters();
            var types = pinfo.Types();

            Assert.IsNotNull(types);
            Assert.AreEqual(2, types.Length);
            Assert.AreEqual(pinfo[0].ParameterType, types[0]);
            Assert.AreEqual(pinfo[1].ParameterType, types[1]);
        }

        [Test]
        public void AccessTools_FieldRefAccess_ByName()
        {
            var fieldInfo = typeof(AccessToolsClass).GetField("field1", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(fieldInfo);
            var instance = new AccessToolsClass();
            var fieldRef = AccessTools.FieldRefAccess<AccessToolsClass, string>("field1");
            ref var value = ref fieldRef(instance);

            Assert.AreEqual(AccessToolsClass.field1Value, value);
            var newValue = AccessToolsClass.field1Value + "1";
            value = newValue;
            Assert.AreEqual(newValue, fieldInfo.GetValue(instance));
        }

        [Test]
        public void AccessTools_FieldRefAccess_ByFieldInfo()
        {
            var fieldInfo = typeof(AccessToolsClass).GetField("field1", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(fieldInfo);
            var instance = new AccessToolsClass();
            var fieldRef = AccessTools.FieldRefAccess<AccessToolsClass, string>(fieldInfo);
            ref var value = ref fieldRef(instance);

            Assert.AreEqual(AccessToolsClass.field1Value, value);
            var newValue = AccessToolsClass.field1Value + "1";
            value = newValue;
            Assert.AreEqual(newValue, fieldInfo.GetValue(instance));
        }

        [Test]
        public void AccessTools_FieldRefAccess_ByFieldInfo_Readonly()
        {
            var fieldInfo = typeof(AccessToolsClass).GetField("field2", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(fieldInfo);
            var instance = new AccessToolsClass();
            var fieldRef = AccessTools.FieldRefAccess<AccessToolsClass, string>(fieldInfo);
            ref var value = ref fieldRef(instance);

            Assert.AreEqual(AccessToolsClass.field2Value, value);
            var newValue = AccessToolsClass.field2Value + "1";
            value = newValue;
            Assert.AreEqual(newValue, fieldInfo.GetValue(instance));
        }

        [Test]
        public void AccessTools_StaticFieldRefAccess_ByName()
        {
            var fieldInfo = typeof(AccessToolsClass).GetField("field3", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(fieldInfo);
            var instance = new AccessToolsClass();
            ref var value = ref AccessTools.StaticFieldRefAccess<AccessToolsClass, string>("field3");

            Assert.AreEqual(AccessToolsClass.field3Value, value);
            var newValue = AccessToolsClass.field3Value + "1";
            value = newValue;
            Assert.AreEqual(newValue, fieldInfo.GetValue(instance));
        }

        [Test]
        public void AccessTools_StaticFieldRefAccess_ByFieldInfo()
        {
            var fieldInfo = typeof(AccessToolsClass).GetField("field3", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(fieldInfo);
            var instance = new AccessToolsClass();
            var fieldRef = AccessTools.StaticFieldRefAccess<string>(fieldInfo);
            ref var value = ref fieldRef();

            Assert.AreEqual(AccessToolsClass.field3Value, value);
            var newValue = AccessToolsClass.field3Value + "1";
            value = newValue;
            Assert.AreEqual(newValue, fieldInfo.GetValue(instance));
        }

        [Test]
        public void AccessTools_StaticFieldRefAccess_ByFieldInfo_Readonly()
        {
            var fieldInfo = typeof(AccessToolsClass).GetField("field4", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(fieldInfo);
            var instance = new AccessToolsClass();
            var fieldRef = AccessTools.FieldRefAccess<AccessToolsClass, string>(fieldInfo);
            ref var value = ref fieldRef(instance);

            Assert.AreEqual(AccessToolsClass.field4Value, value);
            var newValue = AccessToolsClass.field4Value + "1";
            value = newValue;
            Assert.AreEqual(newValue, fieldInfo.GetValue(instance));
        }

        [Test]
        public void AccessTools_FieldRefAccess_ByFieldInfo_SubClass()
        {
            var fieldInfo = typeof(AccessToolsClass).GetField("field1", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(fieldInfo);
            var instance = new AccessToolsSubClass();
            var fieldRef = AccessTools.FieldRefAccess<AccessToolsSubClass, string>(fieldInfo);
            ref var value = ref fieldRef(instance);

            Assert.AreEqual(AccessToolsClass.field1Value, value);
            var newValue = AccessToolsClass.field1Value + "1";
            value = newValue;
            Assert.AreEqual(newValue, fieldInfo.GetValue(instance));
        }

        [Test]
        public void AccessTools_FieldRefAccess_Anonymous()
        {
            var fieldInfo = typeof(AccessToolsClass).GetField("field1", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(fieldInfo);
            var instance = new AccessToolsClass();
            var fieldRef = AccessTools.FieldRefAccess<object, string>(fieldInfo);
            ref var value = ref fieldRef(instance);

            Assert.AreEqual(AccessToolsClass.field1Value, value);
            var newValue = AccessToolsClass.field1Value + "1";
            value = newValue;
            Assert.AreEqual(newValue, fieldInfo.GetValue(instance));
        }
    }
}