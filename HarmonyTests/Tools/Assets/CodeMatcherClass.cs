using System;

namespace HarmonyLibTests.Assets;

public class CodeMatcherClass1
{
	public void Foo()
	{
	}

	public void Bar()
	{
	}

	public void MultipleFooCalls()
	{
		Foo();
		Console.WriteLine("Foo!");
		Foo();
		Console.WriteLine("Foo!");
		Foo();
		Console.WriteLine("Foo!");
	}
}
