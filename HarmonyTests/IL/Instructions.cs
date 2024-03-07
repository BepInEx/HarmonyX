using HarmonyLib;
using HarmonyLib.Internal.Patching;
using HarmonyLib.Internal.Util;
using HarmonyLibTests.Assets;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using static HarmonyLib.Code;
using OpCodes = System.Reflection.Emit.OpCodes;

namespace HarmonyLibTests.Assets
{
	internal class TryCatchMethodClass
	{
		private static bool run = true;

		public void TryCatchMethod()
		{
			try
			{
				if (run) Console.WriteLine("Run");
			}
			finally
			{
				Console.WriteLine("Finally");
			}

			Console.WriteLine("Post-finally");

			try
			{
				if (run) Console.WriteLine("Run 2");
			}
			catch (Exception)
			{
				Console.WriteLine("Catch 2");
			}
			finally
			{
				Console.WriteLine("Finally 2");
			}

			Console.WriteLine("Post-finally 2");
		}
	}
}

namespace HarmonyLibTests.IL
{
	[TestFixture, NonParallelizable]
	public class Instructions : TestLogger
	{
		[Test]
		public void Test_MalformedStringOperand()
		{
			var expectedOperand = "this should not fail {4}";
			var inst = new CodeInstruction(OpCodes.Ldstr, expectedOperand);
			Assert.AreEqual($"ldstr \"{expectedOperand}\"", inst.ToString());
		}

		[Test]
		public void Test_Code()
		{
			var c0 = Operand;
			Assert.False(c0.opcode.IsValid());
			Assert.AreEqual(null, c0.operand);
			Assert.AreEqual(null, c0.name);

			var c1 = Nop;
			Assert.AreEqual(OpCodes.Nop, c1.opcode);
			Assert.AreEqual(null, c1.operand);
			Assert.AreEqual(null, c1.name);

			var c2 = Nop["test"];
			Assert.AreEqual(OpCodes.Nop, c2.opcode);
			Assert.AreEqual("test", c2.operand);
			Assert.AreEqual(null, c2.name);

			var c3 = Nop[name: "test"];
			Assert.AreEqual(OpCodes.Nop, c3.opcode);
			Assert.AreEqual(null, c3.operand);
			Assert.AreEqual("test", c3.name);

			var c4 = Nop[typeof(void), "test2"];
			Assert.AreEqual(OpCodes.Nop, c4.opcode);
			Assert.AreEqual(typeof(void), c4.operand);
			Assert.AreEqual("test2", c4.name);

			var c5 = Nop[123][name: "test"];
			Assert.AreEqual(OpCodes.Nop, c5.opcode);
			Assert.AreEqual(123, c5.operand);
			Assert.AreEqual("test", c5.name);

			var label = new Label();
			var c6 = Nop.WithLabels(label);
			Assert.AreEqual(1, c6.labels.Count);
			Assert.AreEqual(label, c6.labels[0]);

			static IEnumerable<CodeInstruction> Emitter()
			{
				yield return Nop;
			}
			var list = Emitter().ToList();
			Assert.AreEqual(1, list.Count);
			Assert.AreEqual(OpCodes.Nop, list[0].opcode);
		}

		[Test]
		public void Test_IL_TryFinally()
		{
			var expectedIL =
#if DEBUG
				@".locals init (
    System.Boolean V_0
    System.Boolean V_1
)
IL_0000: nop
.try
{
  IL_0001: nop
  IL_0002: ldsfld System.Boolean HarmonyLibTests.Assets.TryCatchMethodClass::run
  IL_0007: stloc.0
  IL_0008: ldloc.0
  IL_0009: brfalse IL_0019
  IL_000e: ldstr ""Run""
  IL_0013: call System.Void System.Console::WriteLine(System.String)
  IL_0018: nop
  IL_0019: nop
  IL_001a: leave IL_0032
  IL_001f: leave IL_0032
} // end .try
finally
{
  IL_0024: nop
  IL_0025: ldstr ""Finally""
  IL_002a: call System.Void System.Console::WriteLine(System.String)
  IL_002f: nop
  IL_0030: nop
  IL_0031: endfinally
} // end handler (finally)
IL_0032: ldstr ""Post-finally""
IL_0037: call System.Void System.Console::WriteLine(System.String)
IL_003c: nop
.try
{
  .try
  {
    IL_003d: nop
    IL_003e: ldsfld System.Boolean HarmonyLibTests.Assets.TryCatchMethodClass::run
    IL_0043: stloc.1
    IL_0044: ldloc.1
    IL_0045: brfalse IL_0055
    IL_004a: ldstr ""Run 2""
    IL_004f: call System.Void System.Console::WriteLine(System.String)
    IL_0054: nop
    IL_0055: nop
    IL_0056: leave IL_0078
    IL_005b: leave IL_0078
  } // end .try
  catch System.Exception
  {
    IL_0060: pop
    IL_0061: nop
    IL_0062: ldstr ""Catch 2""
    IL_0067: call System.Void System.Console::WriteLine(System.String)
    IL_006c: nop
    IL_006d: nop
    IL_006e: leave IL_0078
    IL_0073: leave IL_0078
  } // end handler (catch)
  IL_0078: leave IL_0090
  IL_007d: leave IL_0090
} // end .try
finally
{
  IL_0082: nop
  IL_0083: ldstr ""Finally 2""
  IL_0088: call System.Void System.Console::WriteLine(System.String)
  IL_008d: nop
  IL_008e: nop
  IL_008f: endfinally
} // end handler (finally)
IL_0090: ldstr ""Post-finally 2""
IL_0095: call System.Void System.Console::WriteLine(System.String)
IL_009a: nop
IL_009b: ret
";
#else
@".locals init (
)
.try
{
  IL_0000: ldsfld System.Boolean HarmonyLibTests.Assets.TryCatchMethodClass::run
  IL_0005: brfalse IL_0014
  IL_000a: ldstr ""Run""
  IL_000f: call System.Void System.Console::WriteLine(System.String)
  IL_0014: leave IL_0029
  IL_0019: leave IL_0029
} // end .try
finally
{
  IL_001e: ldstr ""Finally""
  IL_0023: call System.Void System.Console::WriteLine(System.String)
  IL_0028: endfinally
} // end handler (finally)
IL_0029: ldstr ""Post-finally""
IL_002e: call System.Void System.Console::WriteLine(System.String)
.try
{
  .try
  {
    IL_0033: ldsfld System.Boolean HarmonyLibTests.Assets.TryCatchMethodClass::run
    IL_0038: brfalse IL_0047
    IL_003d: ldstr ""Run 2""
    IL_0042: call System.Void System.Console::WriteLine(System.String)
    IL_0047: leave IL_0076
    IL_004c: leave IL_0066
  } // end .try
  catch System.Exception
  {
    IL_0051: pop
    IL_0052: ldstr ""Catch 2""
    IL_0057: call System.Void System.Console::WriteLine(System.String)
    IL_005c: leave IL_0076
    IL_0061: leave IL_0066
  } // end handler (catch)
  IL_0066: leave IL_0076
} // end .try
finally
{
  IL_006b: ldstr ""Finally 2""
  IL_0070: call System.Void System.Console::WriteLine(System.String)
  IL_0075: endfinally
} // end handler (finally)
IL_0076: ldstr ""Post-finally 2""
IL_007b: call System.Void System.Console::WriteLine(System.String)
IL_0080: ret
";
#endif
			var m = AccessTools.Method(typeof(TryCatchMethodClass), nameof(TryCatchMethodClass.TryCatchMethod));
			Assert.NotNull(m);

			static void Normalize(MethodBody body)
			{
				foreach (var ins in body.Instructions)
				{
					if (ins.OpCode == Mono.Cecil.Cil.OpCodes.Leave_S)
						ins.OpCode = Mono.Cecil.Cil.OpCodes.Leave;
					if (ins.OpCode == Mono.Cecil.Cil.OpCodes.Brfalse_S)
						ins.OpCode = Mono.Cecil.Cil.OpCodes.Brfalse;
				}
			}

			var dmd = new DynamicMethodDefinition(m);
			var body = dmd.Definition.Body;
			Assert.NotNull(body);

			var ilManipulator = new ILManipulator(body, false);
			ilManipulator.WriteTo(body);

			Normalize(body);
			var transpiledBody = body.ToILDasmString();
			Assert.AreEqual(expectedIL, transpiledBody);
		}

		// Doesn't seem to properly work on dotnet 6 test runner
#if false
		[Test]
		public void FixIssue45()
		{
			if (AccessTools.IsMonoRuntime)
				Assert.Ignore("Mono runtime does not generally provide this method.");

			var method = typeof(HttpRuntime).GetMethod("ReleaseResourcesAndUnloadAppDomain",
				System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

			var body = new DynamicMethodDefinition(method).Definition.Body;

			Assert.NotNull(body);

			Assert.AreEqual(29, new ILManipulator(body, false).GetRawInstructions().Count());
		}
#endif
	}
}
