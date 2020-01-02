using System;
using HarmonyLib;
using HarmonyLibTests.Assets;
using NUnit.Framework;

namespace HarmonyLibTests
{
    [TestFixture]
    public class TestPrivateImplDetailsInvoker
    {
        [Test]
        public void TestPrivateImplDetailsInvokerGeneral()
        {
            var originalClass = typeof(PrivateImplDetailsInvokerObject);
            Assert.IsNotNull(originalClass);
            var originalMethod = originalClass.GetMethod(nameof(PrivateImplDetailsInvokerObject.Test));
            Assert.IsNotNull(originalMethod);

            var patchClass = typeof(PrivateImplDetailsInvokerObjectPatch);
            var prefix = patchClass.GetMethod(nameof(PrivateImplDetailsInvokerObjectPatch.Prefix));
            Assert.IsNotNull(prefix);

            var instance = new Harmony("test");
            Assert.IsNotNull(instance);

            var patcher = new PatchProcessor(instance, originalMethod);
            Assert.IsNotNull(patcher);
            patcher.AddPrefix(prefix);

            Environment.SetEnvironmentVariable("MONOMOD_DMD_TYPE", "mb");
            patcher.Patch();

            PrivateImplDetailsInvokerObject.Test("test");
        }
    }
}