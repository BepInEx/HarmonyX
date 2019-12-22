using NUnit.Framework;
using HarmonyLib;
using HarmonyLibTests.Assets;

namespace HarmonyLibTests.Tools
{
    [TestFixture]
    public class Test_Attributes
    {
        [Test]
        public void TestAttributes()
        {
            var type = typeof(AllAttributesClass);
            var infos = HarmonyMethodExtensions.GetFromType(type);
            var info = HarmonyMethod.Merge(infos);
            Assert.IsNotNull(info);
            Assert.AreEqual(typeof(string), info.declaringType);
            Assert.AreEqual("foobar", info.methodName);
            Assert.IsNotNull(info.argumentTypes);
            Assert.AreEqual(2, info.argumentTypes.Length);
            Assert.AreEqual(typeof(float), info.argumentTypes[0]);
            Assert.AreEqual(typeof(string), info.argumentTypes[1]);
        }

        [Test]
        public void TestSubClassPatching()
        {
            var instance1 = new Harmony("test1");
            Assert.IsNotNull(instance1);
            var type1 = typeof(MainClassPatch);
            Assert.IsNotNull(type1);
            var processor1 = instance1.ProcessorForAnnotatedClass(type1);
            Assert.IsNotNull(processor1);
            var dynamicMethods1 = processor1.Patch();
            Assert.AreEqual(1, dynamicMethods1.Count);

            var instance2 = new Harmony("test2");
            Assert.IsNotNull(instance2);
            var type2 = typeof(SubClassPatch);
            Assert.IsNotNull(type2);
            try
            {
                instance2.ProcessorForAnnotatedClass(type2);
            }
            catch (System.ArgumentException ex)
            {
                Assert.IsTrue(ex.Message.Contains("No target method specified"));
            }
        }
    }
}