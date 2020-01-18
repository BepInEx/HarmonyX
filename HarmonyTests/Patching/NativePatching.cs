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
            // Currently NativeDetours don't work properly on .NET Core (except when running in debug mode)
            // ¯\_(ツ)_/¯
#if NETCOREAPP3_0
            return;
#endif
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
            Assert.AreEqual(-1, result);
            Assert.IsTrue(NativeClassPatch.prefixCalled, "Prefix wasn't run");
            Assert.IsTrue(NativeClassPatch.postfixCalled, "Postfix wasn't run");
            Assert.IsTrue(NativeClassPatch.transpilerCalled, "Transpiler wasn't run");
        }
    }
}