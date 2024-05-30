using HarmonyLib;
using HarmonyTests.Tools.Assets;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;

namespace HarmonyLibTests.Tools;

[TestFixture, NonParallelizable]
public class Test_CodeMatcher : TestLogger
{
	[Test]
	public void Test_CodeMatch()
	{
		var method = SymbolExtensions.GetMethodInfo(() => CodeMatcherClass.Bar(""));
		var match = new CodeMatch(OpCodes.Call, method);
		Assert.AreEqual(match.opcode, OpCodes.Call);
		Assert.AreEqual(match.opcodeSet, new HashSet<OpCode>() { OpCodes.Call });
		Assert.AreEqual(match.operand, method);
		Assert.AreEqual(match.operands, new[] { method });
	}

	[Test]
	public void Test_Code()
	{
		var method = SymbolExtensions.GetMethodInfo(() => CodeMatcherClass.Bar(""));
		var code = Code.Call[method];
		Assert.AreEqual(code.opcode, OpCodes.Call);
		Assert.AreEqual(code.opcodeSet, new HashSet<OpCode>() { OpCodes.Call });
		Assert.AreEqual(code.operand, method);
		Assert.AreEqual(code.operands, new[] { method });
	}

	[Test]
	public void Test_MatchStartForward_Code()
	{
		var method = SymbolExtensions.GetMethodInfo(() => CodeMatcherClass.Method());
		var instructions = PatchProcessor.GetOriginalInstructions(method);

		var mFoo = SymbolExtensions.GetMethodInfo(() => CodeMatcherClass.Foo());
		var mBar = SymbolExtensions.GetMethodInfo(() => CodeMatcherClass.Bar(""));

		var matcher = new CodeMatcher(instructions).MatchStartForward(Code.Call[mBar]).ThrowIfNotMatch("not found");
		Assert.AreEqual(OpCodes.Call, instructions[matcher.Pos].opcode);
		Assert.AreEqual(mBar, instructions[matcher.Pos].operand);
	}

	[Test]
	public void Test_MatchStartForward_CodeMatch()
	{
		var method = SymbolExtensions.GetMethodInfo(() => CodeMatcherClass.Method());
		var instructions = PatchProcessor.GetOriginalInstructions(method);

		var mFoo = SymbolExtensions.GetMethodInfo(() => CodeMatcherClass.Foo());
		var mBar = SymbolExtensions.GetMethodInfo(() => CodeMatcherClass.Bar(""));

		var matcher = new CodeMatcher(instructions).MatchStartForward(new CodeMatch(OpCodes.Call, mBar)).ThrowIfNotMatch("not found");
		Assert.AreEqual(OpCodes.Call, instructions[matcher.Pos].opcode);
		Assert.AreEqual(mBar, instructions[matcher.Pos].operand);
	}

	[Test]
	public void TestRepeatReplaceMultiple()
	{
		var target = AccessTools.Method(typeof(CodeMatcherClass), nameof(CodeMatcherClass.MultipleFooCalls));
		Assert.IsNotNull(target);

		var matchTarget = AccessTools.Method(typeof(CodeMatcherClass), nameof(CodeMatcherClass.Baz));
		Assert.IsNotNull(matchTarget);
		var matchReplacement = AccessTools.Method(typeof(CodeMatcherClass), nameof(CodeMatcherClass.Qux));
		Assert.IsNotNull(matchReplacement);

		var instructions = PatchProcessor.GetOriginalInstructions(target);
		var result = new CodeMatcher(instructions)
			.MatchForward(true, new CodeMatch(null, matchTarget))
			.Repeat(m => m.Advance(1).InsertAndAdvance(new CodeInstruction(OpCodes.Call, matchReplacement)),
				err => throw new Exception($"Nothing replaced .{err}"))
			.Instructions();

		var writeLine = SymbolExtensions.GetMethodInfo(() => Console.WriteLine(string.Empty));
		AssertSameCode(result,
			new CodeInstruction[]
			{
				new(OpCodes.Ldarg_0), //
				new(OpCodes.Call, matchTarget), //
				new(OpCodes.Call, matchReplacement), //
				new(OpCodes.Ldstr, "Baz!"), //
				new(OpCodes.Call, writeLine), //
				new(OpCodes.Ldarg_0), //
				new(OpCodes.Call, matchTarget), //
				new(OpCodes.Call, matchReplacement), //
				new(OpCodes.Ldstr, "Baz!"), //
				new(OpCodes.Call, writeLine), //
				new(OpCodes.Ldarg_0), //
				new(OpCodes.Call, matchTarget), //
				new(OpCodes.Call, matchReplacement), //
				new(OpCodes.Ldstr, "Baz!"), //
				new(OpCodes.Call, writeLine), //
				new(OpCodes.Ret) //
			}
		);
	}

	private static void AssertSameCode(IEnumerable<CodeInstruction> ins, IEnumerable<CodeInstruction> expected)
	{
		Assert.AreEqual(
			expected.Select(i => (i.opcode, i.operand)),
			ins.Where(i => i.opcode != OpCodes.Nop).Select(i => (i.opcode, i.operand))
		);
	}
}
