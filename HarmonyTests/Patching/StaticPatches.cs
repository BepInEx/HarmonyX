using HarmonyLib;
using HarmonyLibTests.Assets;
using NUnit.Framework;
using MonoMod.RuntimeDetour;

namespace HarmonyLibTests
{
    [TestFixture]
    public class StaticPatches
    {
        [Test]
        public void TestMethod0()
        {
            var originalClass = typeof(Class0);
            Assert.IsNotNull(originalClass);
            var originalMethod = originalClass.GetMethod("Method0");
            Assert.IsNotNull(originalMethod);

            var patchClass = typeof(Class0Patch);
            var postfix = patchClass.GetMethod("Postfix");
            Assert.IsNotNull(postfix);

            var instance = new Harmony("test");
            Assert.IsNotNull(instance);

            var patcher = new PatchProcessor(instance, originalMethod);
            Assert.IsNotNull(patcher);
            patcher.AddPostfix(postfix);
            patcher.Patch();

            var result = new Class0().Method0();
            Assert.AreEqual("patched", result);
        }

        [Test]
        public void TestMethod1()
        {
            var originalClass = typeof(Class1);
            Assert.IsNotNull(originalClass);
            var originalMethod = originalClass.GetMethod("Method1");
            Assert.IsNotNull(originalMethod);

            var patchClass = typeof(Class1Patch);
            var prefix = patchClass.GetMethod("Prefix");
            var postfix = patchClass.GetMethod("Postfix");
            var transpiler = patchClass.GetMethod("Transpiler");
            Assert.IsNotNull(prefix);
            Assert.IsNotNull(postfix);
            Assert.IsNotNull(transpiler);

            Class1Patch.ResetTest();

            var instance = new Harmony("test");
            Assert.IsNotNull(instance);

            var patcher = new PatchProcessor(instance, originalMethod);
            Assert.IsNotNull(patcher);
            patcher.AddPrefix(prefix);
            patcher.AddPostfix(postfix);
            patcher.AddTranspiler(transpiler);

            patcher.Patch();
            // unsafe
            // {
            //     var patchedCode = *(byte*) originalMethodStartPre;
            //     if (IntPtr.Size == sizeof(long))
            //         Assert.IsTrue(patchedCode == 0x48);
            //     else
            //         Assert.IsTrue(patchedCode == 0x68);
            // }

            Class1.Method1();
            Assert.IsTrue(Class1Patch.prefixed, "Prefix was not executed");
            Assert.IsTrue(Class1Patch.originalExecuted, "Original was not executed");
            Assert.IsTrue(Class1Patch.postfixed, "Postfix was not executed");
            }

        [Test]
        public void TestMethod2()
        {
            var originalClass = typeof(Class2);
            Assert.IsNotNull(originalClass);
            var originalMethod = originalClass.GetMethod("Method2");
            Assert.IsNotNull(originalMethod);

            var patchClass = typeof(Class2Patch);
            var prefix = patchClass.GetMethod("Prefix");
            var postfix = patchClass.GetMethod("Postfix");
            var transpiler = patchClass.GetMethod("Transpiler");
            Assert.IsNotNull(prefix);
            Assert.IsNotNull(postfix);
            Assert.IsNotNull(transpiler);

            Class2Patch.ResetTest();

            var instance = new Harmony("test");
            Assert.IsNotNull(instance);

            var patcher = new PatchProcessor(instance, originalMethod);
            Assert.IsNotNull(patcher);
            patcher.AddPrefix(prefix);
            patcher.AddPostfix(postfix);
            patcher.AddTranspiler(transpiler);

            patcher.Patch();
            // unsafe
            // {
            //     var patchedCode = *(byte*) originalMethodStartPre;
            //     if (IntPtr.Size == sizeof(long))
            //         Assert.IsTrue(patchedCode == 0x48);
            //     else
            //         Assert.IsTrue(patchedCode == 0x68);
            // }

            new Class2().Method2();
            Assert.IsTrue(Class2Patch.prefixed, "Prefix was not executed");
            Assert.IsTrue(Class2Patch.originalExecuted, "Original was not executed");
            Assert.IsTrue(Class2Patch.postfixed, "Postfix was not executed");
        }

        [Test]
        public void TestMethod4()
        {
            var originalClass = typeof(Class4);
            Assert.IsNotNull(originalClass);
            var originalMethod = originalClass.GetMethod("Method4");
            Assert.IsNotNull(originalMethod);

            var patchClass = typeof(Class4Patch);
            var prefix = patchClass.GetMethod("Prefix");
            Assert.IsNotNull(prefix);

            Class4Patch.ResetTest();

            var instance = new Harmony("test");
            Assert.IsNotNull(instance);

            var patcher = new PatchProcessor(instance, originalMethod);
            Assert.IsNotNull(patcher);
            patcher.AddPrefix(prefix);

            patcher.Patch();
            // unsafe
            // {
            //     var patchedCode = *(byte*) originalMethodStartPre;
            //     if (IntPtr.Size == sizeof(long))
            //         Assert.IsTrue(patchedCode == 0x48);
            //     else
            //         Assert.IsTrue(patchedCode == 0x68);
            // }

            new Class4().Method4("foo");
            Assert.IsTrue(Class4Patch.prefixed, "Prefix was not executed");
            Assert.IsTrue(Class4Patch.originalExecuted, "Original was not executed");
            Assert.AreEqual(Class4Patch.senderValue, "foo");
        }

        [Test]
        public void TestMethod5()
        {
            var originalClass = typeof(Class5);
            Assert.IsNotNull(originalClass);
            var originalMethod = originalClass.GetMethod("Method5");
            Assert.IsNotNull(originalMethod);

            var patchClass = typeof(Class5Patch);
            var prefix = patchClass.GetMethod("Prefix");
            Assert.IsNotNull(prefix);
            var postfix = patchClass.GetMethod("Postfix");
            Assert.IsNotNull(postfix);

            Class5Patch.ResetTest();

            var instance = new Harmony("test");
            Assert.IsNotNull(instance);

            var patcher = new PatchProcessor(instance, originalMethod);
            Assert.IsNotNull(patcher);
            patcher.AddPrefix(prefix);
            patcher.AddPostfix(postfix);
            patcher.Patch();

            new Class5().Method5("foo");
            Assert.IsTrue(Class5Patch.prefixed, "Prefix was not executed");
            Assert.IsTrue(Class5Patch.postfixed, "Prefix was not executed");
        }

        [Test]
        public void TestPatchUnpatch()
        {
            var originalClass = typeof(Class9);
            Assert.IsNotNull(originalClass);
            var originalMethod = originalClass.GetMethod("ToString");
            Assert.IsNotNull(originalMethod);

            var patchClass = typeof(Class9Patch);
            var prefix = patchClass.GetMethod("Prefix");
            Assert.IsNotNull(prefix);
            var postfix = patchClass.GetMethod("Postfix");
            Assert.IsNotNull(postfix);

            var instance = new Harmony("test");
            Assert.IsNotNull(instance);

            var patcher = new PatchProcessor(instance, originalMethod);
            Assert.IsNotNull(patcher);
            patcher.AddPrefix(prefix);
            patcher.AddPostfix(postfix);
            patcher.Patch();

            var instanceB = new Harmony("test");
            Assert.IsNotNull(instanceB);

            instanceB.UnpatchAll("test");
        }

        [Test]
        public void TestAttributes()
        {
            var originalClass = typeof(AttributesClass);
            Assert.IsNotNull(originalClass);

            var originalMethod = originalClass.GetMethod("Method");
            Assert.IsNotNull(originalMethod);
            Assert.AreEqual(originalMethod, AttributesPatch.Patch0());

            var instance = new Harmony("test");
            Assert.IsNotNull(instance);

            var patchClass = typeof(AttributesPatch);
            Assert.IsNotNull(patchClass);

            AttributesPatch.ResetTest();

            var patcher = instance.ProcessorForAnnotatedClass(patchClass);
            Assert.IsNotNull(patcher);
            patcher.Patch();

            new AttributesClass().Method("foo");
            Assert.IsTrue(AttributesPatch.targeted, "TargetMethod was not executed");
            Assert.IsTrue(AttributesPatch.postfixed, "Prefix was not executed");
            Assert.IsTrue(AttributesPatch.postfixed, "Prefix was not executed");
        }

        [Test]
        public void TestMethod10()
        {
            var originalClass = typeof(Class10);
            Assert.IsNotNull(originalClass);
            var originalMethod = originalClass.GetMethod("Method10");
            Assert.IsNotNull(originalMethod);

            var patchClass = typeof(Class10Patch);
            var postfix = patchClass.GetMethod("Postfix");
            Assert.IsNotNull(postfix);

            var instance = new Harmony("test");
            Assert.IsNotNull(instance);

            var patcher = new PatchProcessor(instance, originalMethod);
            Assert.IsNotNull(patcher);
            patcher.AddPostfix(postfix);
            patcher.Patch();

            new Class10().Method10();
            Assert.IsTrue(Class10Patch.postfixed);
            Assert.IsTrue(Class10Patch.originalResult);
        }

        [Test]
        public void TestSkipOriginalParam()
        {
            var originalClass = typeof(Class11);
            var originalMethod = originalClass.GetMethod("TestMethod");
            Assert.NotNull(originalMethod);

            var patchClass = typeof(Class11PrefixPatches);
            var prefix1 = patchClass.GetMethod("Prefix1");
            Assert.NotNull(prefix1);
            var prefix2 = patchClass.GetMethod("Prefix2");
            Assert.NotNull(prefix2);

            var instance = new Harmony("SkipOriginalParam");
            Assert.NotNull(instance);

            var patcher = new PatchProcessor(instance, originalMethod);
            patcher.AddPrefix(prefix1);
            patcher.Patch();

            var patcher2 = new PatchProcessor(instance, originalMethod);
            patcher2.AddPrefix(prefix2);
            patcher2.Patch();

            var testClass = new Class11();
            testClass.TestMethod(0);
            Assert.IsFalse(testClass.originalMethodRan);
            Assert.IsFalse(Class11PrefixPatches.runOriginal);
        }
    }
}