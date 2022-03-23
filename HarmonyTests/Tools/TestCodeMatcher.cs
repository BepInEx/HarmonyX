using HarmonyLib;
using HarmonyLibTests.Assets;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;

namespace HarmonyLibTests.Tools;

[TestFixture]
public class TestCodeMatcher
{
	[Test]
	public void TestRepeatReplaceMultiple()
	{
		var target = AccessTools.Method(typeof(CodeMatcherClass1), nameof(CodeMatcherClass1.MultipleFooCalls));
		Assert.IsNotNull(target);

		var matchTarget = AccessTools.Method(typeof(CodeMatcherClass1), nameof(CodeMatcherClass1.Foo));
		Assert.IsNotNull(matchTarget);
		var matchReplacement = AccessTools.Method(typeof(CodeMatcherClass1), nameof(CodeMatcherClass1.Bar));
		Assert.IsNotNull(matchReplacement);

		var instructions = PatchProcessor.GetOriginalInstructions(target);
		var result = new CodeMatcher(instructions)
			.MatchForward(true, new CodeMatch(null, matchTarget))
			.Repeat(m => m.Advance(1).InsertAndAdvance(new CodeInstruction(OpCodes.Call, matchReplacement)),
				err => throw new Exception($"Nothing replaced .{err}"))
			.Instructions();

		AssertSameCode(result,
			new[]
			{
				"ldarg.0 NULL", "call void HarmonyLibTests.Assets.CodeMatcherClass1::Foo()",
				"call void HarmonyLibTests.Assets.CodeMatcherClass1::Bar()", "ldstr \"Foo!\"",
				"call static void Console::WriteLine(string value)", "ldarg.0 NULL",
				"call void HarmonyLibTests.Assets.CodeMatcherClass1::Foo()",
				"call void HarmonyLibTests.Assets.CodeMatcherClass1::Bar()", "ldstr \"Foo!\"",
				"call static void Console::WriteLine(string value)", "ldarg.0 NULL",
				"call void HarmonyLibTests.Assets.CodeMatcherClass1::Foo()",
				"call void HarmonyLibTests.Assets.CodeMatcherClass1::Bar()", "ldstr \"Foo!\"",
				"call static void Console::WriteLine(string value)", "ret NULL"
			});
	}

	private void AssertSameCode(List<CodeInstruction> ins, string[] expected)
	{
		Assert.AreEqual(expected, ins.Select(i => i.ToString()).ToArray());
	}
}
