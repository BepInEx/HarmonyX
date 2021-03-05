using HarmonyLib;
using HarmonyLibTests.Assets;
using NUnit.Framework;

namespace HarmonyLibTests.ReversePatching
{
	[TestFixture]
	public class ILManipulatorReversePatches
	{
		[Test]
		public void Test_ILManipulatorReversePatch()
		{
			var class2 = new Class2Reverse();

			Assert.AreEqual("some string", class2.SomeMethod());

			var original = AccessTools.Method(typeof(Class2Reverse), nameof(Class2Reverse.SomeMethod));
			Assert.NotNull(original);

			var stub = AccessTools.Method(typeof(Class2ReversePatch), nameof(Class2ReversePatch.SomeMethodReverse));
			Assert.NotNull(stub);

			var instance = new Harmony("test-ilmanipulator-reverse");
			var reversePatcher = instance.CreateReversePatcher(original, new HarmonyMethod(stub));
			_ = reversePatcher.Patch();

			Assert.AreEqual("some other string", Class2ReversePatch.SomeMethodReverse());
		}
	}
}
