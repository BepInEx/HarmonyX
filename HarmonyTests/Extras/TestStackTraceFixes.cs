using HarmonyLib;
using HarmonyLibTests;
using NUnit.Framework;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace HarmonyTests.Extras;

[TestFixture]
class TestStackTraceFixes : TestLogger
{
	[Test]
	public void Test_StackTraceFixes()
	{
		var harmony = new Harmony("test-stack-trace-fixes");
		var getExecutingAssemblyTarget = SymbolExtensions.GetMethodInfo(() => GetExecutingAssemblyTarget());
		var getMethodTarget = SymbolExtensions.GetMethodInfo(() => GetMethodTarget());
		var dummyPrefix = SymbolExtensions.GetMethodInfo(() => DummyPrefix());

		_ = harmony.Patch(getExecutingAssemblyTarget, new HarmonyMethod(dummyPrefix));
		Assert.AreEqual(getExecutingAssemblyTarget.Module.Assembly, GetExecutingAssemblyTarget());

		_ = harmony.Patch(getMethodTarget, new HarmonyMethod(dummyPrefix));
		Assert.AreEqual(getMethodTarget, GetMethodTarget());
	}


	private static Assembly GetExecutingAssemblyTarget() =>
		Assembly.GetExecutingAssembly();

	private static MethodBase GetMethodTarget() =>
		new StackTrace().GetFrame(0).GetMethod();

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void DummyPrefix()
	{
	}
}
