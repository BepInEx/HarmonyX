using HarmonyLib;
using HarmonyTests.Patching.Assets;
using NUnit.Framework;
using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace HarmonyLibTests.Patching;

using EIP = ExternalInstanceMethod_StringIsInterned_Patch;

[TestFixture]
public class NativeDetourPatches : TestLogger
{
	[Test]
	public void Test_PatchInstanceExternalMethod()
	{
		var target = typeof(string).GetMethod("Intern", BindingFlags.Instance|BindingFlags.NonPublic);

		if(target == null)
			Assert.Inconclusive("string.Intern is missing in current runtime");

#if !NET35
		if((target.MethodImplementationFlags & MethodImplAttributes.InternalCall) == 0)
			Assert.Inconclusive("string.Intern is not an InternalCall (extern) in current runtime ");
#endif

		if(target.GetMethodBody() != null)
			Assert.Inconclusive("string.Intern has IL body in current runtime");

		var str1 = new StringBuilder().Append('o').Append('k').Append('4').Append('1').ToString();
		Assert.IsNull(string.IsInterned(str1));
		var internedStr1 = string.Intern(str1);
		Assert.AreEqual(internedStr1, string.IsInterned(str1));

		var instance = new Harmony("test-patch-external-instance-method");
		Assert.NotNull(instance, "Harmony instance");

		instance.Patch(target, transpiler: typeof(EIP).Method("Transpiler"));
		var str2 = new StringBuilder().Append('o').Append('k').Append('4').Append('2').ToString();
		Assert.IsNull(string.IsInterned(str2));
		var internedStr2 = string.Intern(str2);
		Assert.AreEqual(internedStr2, string.IsInterned(str2));

		instance.Patch(target, prefix: typeof(EIP).Method("Prefix"));
		Assert.AreEqual(EIP.PrefixOutput, string.Intern(EIP.PrefixInput));

		instance.Patch(target, postfix: typeof(EIP).Method("Postfix"));
		Assert.AreEqual(EIP.PostfixOutput, string.Intern(EIP.PostfixInput));

		instance.Patch(target, transpiler: typeof(EIP).Method("TranspilerThrow"));
		Assert.Throws(EIP.TranspiledException, () => string.Intern("does not matter"));

		instance.Patch(target, finalizer: typeof(EIP).Method("Finalizer"));
		Assert.AreEqual(EIP.FinalizerOutput, string.Intern(EIP.FinalizerInput));

		instance.UnpatchSelf();
		var str3 = new StringBuilder().Append('o').Append('k').Append('4').Append('3').ToString();
		Assert.IsNull(string.IsInterned(str3));
		Assert.AreEqual(internedStr1, string.IsInterned(str1));
		Assert.AreEqual(internedStr2, string.IsInterned(str2));
	}

	[Test]
	public void Test_PatchStaticExternalMethod()
	{
		var target = SymbolExtensions.GetMethodInfo(() => Math.Cos(0));

		if(target.GetMethodBody() != null)
			Assert.Inconclusive("Math.Cos is IL implemented in current runtime");

		// anti-inlining
		var cos = Math.Cos;
		Assert.AreEqual(1d, cos(0d));

		var instance = new Harmony("test-patch-external-static-method");
		Assert.NotNull(instance, "Harmony instance");

		instance.Patch(target, transpiler: typeof(ExternalStaticMethod_MathCos_Patch).Method("Transpiler"));
		Assert.AreEqual(1d, cos(0d));

		instance.Patch(target, prefix: typeof(ExternalStaticMethod_MathCos_Patch).Method("Prefix"));
		Assert.AreEqual(1d, cos(2d));

		instance.Patch(target, postfix: typeof(ExternalStaticMethod_MathCos_Patch).Method("Postfix"));
		Assert.AreEqual(2d, cos(0d));

		instance.Patch(target, transpiler: typeof(ExternalStaticMethod_MathCos_Patch).Method("TranspilerThrow"));
		Assert.Throws<UnauthorizedAccessException>(() => cos(0d));

		instance.Patch(target, finalizer: typeof(ExternalStaticMethod_MathCos_Patch).Method("Finalizer"));
		Assert.AreEqual(-2d, cos(0d));

		instance.UnpatchSelf();
		Assert.AreEqual(1d, cos(0d));
	}
}
