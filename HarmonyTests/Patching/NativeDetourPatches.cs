using HarmonyLib;
using HarmonyTests.Patching.Assets;
using NUnit.Framework;
using System;

namespace HarmonyLibTests.Patching;

[TestFixture]
public class NativeDetourPatches : TestLogger
{
	[Test]
	public void Test_PatchExternalMethod()
	{
		var target = SymbolExtensions.GetMethodInfo(() => Math.Cos(0));

		if(target.GetMethodBody() != null)
			Assert.Inconclusive("Math.Cos is IL implemented in current runtime");

		// anti-inlining
		var cos = Math.Cos;
		Assert.AreEqual(1d, cos(0d));

		var instance = new Harmony("test-patch-external-method");
		Assert.NotNull(instance, "Harmony instance");

		instance.Patch(target, transpiler: typeof(ExternalMethod_Patch).Method("Transpiler"));
		Assert.AreEqual(1d, cos(0d));

		instance.Patch(target, prefix: typeof(ExternalMethod_Patch).Method("Prefix"));
		Assert.AreEqual(1d, cos(2d));

		instance.Patch(target, postfix: typeof(ExternalMethod_Patch).Method("Postfix"));
		Assert.AreEqual(2d, cos(0d));

		instance.Patch(target, transpiler: typeof(ExternalMethod_Patch).Method("TranspilerThrow"));
		Assert.Throws<UnauthorizedAccessException>(() => cos(0d));

		instance.Patch(target, finalizer: typeof(ExternalMethod_Patch).Method("Finalizer"));
		Assert.AreEqual(-2d, cos(0d));

		instance.UnpatchSelf();
		Assert.AreEqual(1d, cos(0d));
	}
}
