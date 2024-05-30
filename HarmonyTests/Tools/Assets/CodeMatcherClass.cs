using System;

namespace HarmonyTests.Tools.Assets
{
	public class CodeMatcherClass
	{
		public static void Method()
		{
			Foo();
			Bar("hello");
		}

		public static void Foo()
		{
		}

		public static void Bar(string s)
		{
		}

		public void Baz()
		{
		}

		public void Qux()
		{
		}

		public void MultipleFooCalls()
		{
			Baz();
			Console.WriteLine("Baz!");
			Baz();
			Console.WriteLine("Baz!");
			Baz();
			Console.WriteLine("Baz!");
		}
	}
}
