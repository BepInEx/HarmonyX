using HarmonyLib;
using HarmonyLibTests.Assets;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using static HarmonyLib.Code;

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

		var writeLine = AccessTools.Method(typeof(Console), nameof(Console.WriteLine), new[] {typeof(string)});
		AssertSameCode(result,
			new CodeInstruction[]
			{
				Ldarg_0, //
				Call[matchTarget], //
				Call[matchReplacement], //
				Ldstr["Foo!"], //
				Call[writeLine], //
				Ldarg_0, //
				Call[matchTarget], //
				Call[matchReplacement], //
				Ldstr["Foo!"], //
				Call[writeLine], //
				Ldarg_0, //
				Call[matchTarget], //
				Call[matchReplacement], //
				Ldstr["Foo!"], //
				Call[writeLine], //
				Ret //
			}
		);
	}

	private void AssertSameCode(List<CodeInstruction> ins, CodeInstruction[] expected)
	{
		Assert.AreEqual(expected.Select(i => i.ToString()), ins.Select(i => i.ToString()).ToArray());
	}
}
