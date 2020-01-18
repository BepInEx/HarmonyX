using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using HarmonyLib;
using NUnit.Framework;

namespace HarmonyTests.Patching
{
    public class NativeClass
    {
        [DllImport(
            #if _WINDOWS
                "msvcrt"
            #elif _OSX
                "libSystem"
            #elif _UNIX
                "libc"
            #endif
          , EntryPoint = "rand", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Rand();
    }

    public class NativeClassPatch
    {
        public static bool prefixCalled, postfixCalled, transpilerCalled;

        public static bool Prefix()
        {
            prefixCalled = true;
            return false;
        }

        public static int Postfix(int result)
        {
            postfixCalled = true;
            return -1;
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            transpilerCalled = true;
            return instructions;
        }
    }

    [TestFixture]
    public class NativePatching
    {
        [Test]
        public void TestNativePatch()
        {
            var originalClass = typeof(NativeClass);
            Assert.IsNotNull(originalClass);
            var originalMethod = originalClass.GetMethod("Rand");
            Assert.IsNotNull(originalMethod);

            var patchClass = typeof(NativeClassPatch);
            var prefix = patchClass.GetMethod("Prefix");
            Assert.IsNotNull(prefix);
            var postfix = patchClass.GetMethod("Postfix");
            Assert.IsNotNull(postfix);
            var transpiler = patchClass.GetMethod("Transpiler");
            Assert.IsNotNull(transpiler);

            var instance = new Harmony("test-native");
            Assert.IsNotNull(instance);

            var patcher = new PatchProcessor(instance, originalMethod);
            Assert.IsNotNull(patcher);
            patcher.AddPrefix(prefix);
            patcher.AddPostfix(postfix);
            patcher.AddTranspiler(transpiler);
            patcher.Patch();

            var result = NativeClass.Rand();
            Assert.IsTrue(NativeClassPatch.prefixCalled);
            Assert.IsTrue(NativeClassPatch.postfixCalled);
            Assert.IsTrue(NativeClassPatch.transpilerCalled);
            Assert.AreEqual(-1, result);
        }
    }
}