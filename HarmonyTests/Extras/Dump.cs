using HarmonyLib.Internal.Util;
using HarmonyLibTests;
using MonoMod.Cil;
using MonoMod.Utils;
using NUnit.Framework;
using System;
using System.Linq;


namespace HarmonyTests.Extras;

[TestFixture]
public class Dump : TestLogger
{
	[Test]
	public void Test_DumpIlLabel()
	{
		using var dmd = new DynamicMethodDefinition("DumpIlLabelTestMethod", typeof(void), Type.EmptyTypes);
		var il = new ILContext(dmd.Definition);
		var cur = new ILCursor(il);

		var label = cur.DefineLabel();
		cur.EmitBr(label);
		cur.EmitRet();
		label.Target = cur.Prev;

		var method = cur.Method;

		using var dumpedModule = CecilEmitter.DumpImpl(method);
		var dumpedMethod = dumpedModule.Types.SelectMany(i => i.Methods).FirstOrDefault();
		Assert.NotNull(dumpedMethod);

		var instructions = dumpedMethod!.Body.Instructions;
		Assert.True(instructions[0].Operand == instructions[1]);
	}
}
