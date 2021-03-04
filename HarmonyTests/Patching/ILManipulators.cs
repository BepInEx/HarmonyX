using HarmonyLib;
using HarmonyLibTests;
using HarmonyLibTests.Assets;
using NUnit.Framework;
using System;

namespace HarmonyLibTests.Patching
{
	[TestFixture]
	class ILManipulators : TestLogger
	{
		[Test]
		public void Test_ILManipulator()
		{
			var original = AccessTools.Method(typeof(ILManipulatorClass), nameof(ILManipulatorClass.SomeMethod));
			Assert.NotNull(original);

			var manipulator = AccessTools.Method(typeof(ILManipulatorClassPatch), nameof(ILManipulatorClassPatch.ILManipulator));
			Assert.NotNull(manipulator);

			string a = "a";
			string b = "string";

			Assert.AreEqual("a something string", ILManipulatorClass.SomeMethod(a, b));

			var instance = new Harmony("test-ilmanipulator");
			_ = instance.Patch(original, ilmanipulator: new HarmonyMethod(manipulator));

			Assert.AreEqual("a string", ILManipulatorClass.SomeMethod(a, b));
		}

		[Test]
		public void Test_ILManipulatorsWithOtherPatches()
		{
			var original = AccessTools.Method(typeof(ILManipulatorsAndOthersClass), nameof(ILManipulatorsAndOthersClass.SomeMethod));
			Assert.NotNull(original);

			var postfix = AccessTools.Method(typeof(ILManipulatorsAndOthersClassPatch), nameof(ILManipulatorsAndOthersClassPatch.Postfix));
			Assert.NotNull(postfix);

			var manipulator = AccessTools.Method(typeof(ILManipulatorsAndOthersClassPatch), nameof(ILManipulatorsAndOthersClassPatch.ILManipulator));
			Assert.NotNull(manipulator);

			var transpiler = AccessTools.Method(typeof(ILManipulatorsAndOthersClassPatch), nameof(ILManipulatorsAndOthersClassPatch.Transpiler));
			Assert.NotNull(transpiler);

			Assert.AreEqual(15, ILManipulatorsAndOthersClass.SomeMethod());

			var instance = new Harmony("test-ilmanipulators-and-other-patches");
			_ = instance.Patch(original, postfix: new HarmonyMethod(postfix), ilmanipulator: new HarmonyMethod(manipulator), transpiler: new HarmonyMethod(transpiler));

			Assert.AreEqual(7, ILManipulatorsAndOthersClass.SomeMethod());
		}
	}
}
