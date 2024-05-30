using HarmonyLib;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace HarmonyLibTests.Assets
{
	[HarmonyPatch]
	public class NoAttributesClass
	{
		[HarmonyPrepare]
		public void Method1() { }
	}

	[HarmonyPatch(typeof(string))]
	[HarmonyPatch("foobar")]
	[HarmonyPriority(Priority.High)]
	[HarmonyPatch([typeof(float), typeof(string)])]
	public class AllAttributesClass
	{
		[HarmonyPrepare]
		public void Method1() { }

		[HarmonyTargetMethod]
		public void Method2() { }

		[HarmonyPrefix]
		[HarmonyPriority(Priority.High)]
		public void Method3() { }

		[HarmonyPostfix]
		[HarmonyBefore("foo", "bar")]
		[HarmonyAfter("test")]
		public void Method4() { }
	}

	public class NoAnnotationsClass
	{
		[HarmonyPatch(typeof(List<string>), "TestMethod")]
		[HarmonyPatch([typeof(string), typeof(string), typeof(string)], [ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Normal])]
		static void Patch() { }
	}

	public class MainClass
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void SomeMethod()
		{
		}
	}

	public class SubClass : MainClass
	{
	}

	[HarmonyPatch(typeof(MainClass), "SomeMethod")]
	public class MainClassPatch
	{
		public static void Prefix()
		{
		}
	}

	[HarmonyPatch(typeof(SubClass), "SomeMethod")]
	public class SubClassPatch
	{
		public static void Prefix()
		{
		}
	}
}
