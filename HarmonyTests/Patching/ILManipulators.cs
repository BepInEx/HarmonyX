using HarmonyLib;
using HarmonyLibTests.Assets;
using NUnit.Framework;

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

			var prefix = AccessTools.Method(typeof(ILManipulatorsAndOthersClassPatch), nameof(ILManipulatorsAndOthersClassPatch.Prefix));
			Assert.NotNull(prefix);

			var manipulator = AccessTools.Method(typeof(ILManipulatorsAndOthersClassPatch), nameof(ILManipulatorsAndOthersClassPatch.ILManipulator));
			Assert.NotNull(manipulator);

			var transpiler = AccessTools.Method(typeof(ILManipulatorsAndOthersClassPatch), nameof(ILManipulatorsAndOthersClassPatch.Transpiler));
			Assert.NotNull(transpiler);

			Assert.AreEqual(14, ILManipulatorsAndOthersClass.SomeMethod(4));

			var instance = new Harmony("test-ilmanipulators-and-other-patches");
			_ = instance.Patch(original, prefix: new HarmonyMethod(prefix), ilmanipulator: new HarmonyMethod(manipulator), transpiler: new HarmonyMethod(transpiler));

			Assert.AreEqual(18, ILManipulatorsAndOthersClass.SomeMethod(4));
		}

		[Test]
		public void Test_ILManipulatorName()
		{
			Assert.AreEqual("string1", ILManipulatorNameClass.SomeMethod("string"));

			var instance = new Harmony("test-ilmanipulators-name");
			instance.PatchAll(typeof(ILManipulatorNameClassPatch));

			Assert.AreEqual("string2", ILManipulatorNameClass.SomeMethod("string"));
		}

		[Test]
		public void Test_ILManipulatorAttribute()
		{
			Assert.AreEqual(2, ILManipulatorAttributeClass.SomeMethod(6, 3));

			var instance = new Harmony("test-ilmanipulators-attribute");
			instance.PatchAll(typeof(ILManipulatorAttributeClassPatch));

			Assert.AreEqual(8, ILManipulatorAttributeClass.SomeMethod(2, 4));
		}

		[Test]
		public void Test_ILManipulatorReturnLabel()
		{
			var original = AccessTools.Method(typeof(ILManipulatorReturnLabelClass), nameof(ILManipulatorReturnLabelClass.SomeMethod));
			Assert.NotNull(original);

			var postfix = AccessTools.Method(typeof(ILManipulatorReturnLabelClassPatch), nameof(ILManipulatorReturnLabelClassPatch.Postfix));
			Assert.NotNull(postfix);

			var manipulator = AccessTools.Method(typeof(ILManipulatorReturnLabelClassPatch), nameof(ILManipulatorReturnLabelClassPatch.ILManipulator));
			Assert.NotNull(manipulator);

			Assert.AreEqual(3, ILManipulatorReturnLabelClass.SomeMethod(2));

			var instance = new Harmony("test-ilmanipulators-return-label");
			var a = instance.Patch(original, postfix: new HarmonyMethod(postfix), ilmanipulator: new HarmonyMethod(manipulator));

			Assert.AreEqual(7, ILManipulatorReturnLabelClass.SomeMethod(5));
		}
	}
}
