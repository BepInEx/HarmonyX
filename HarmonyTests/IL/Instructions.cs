extern alias mmc;
using HarmonyLib;
using HarmonyLib.Internal.Patching;
using HarmonyLib.Internal.Util;
using HarmonyLibTests.Assets;
using mmc::MonoMod.Utils;
using Mono.Cecil.Cil;
using MonoMod.Core.Utils;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Web;
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
    System.Exception V_2
)
IL_0000: nop
.try
{
  IL_0001: nop
  IL_0002: ldsfld System.Boolean HarmonyLibTests.Assets.TryCatchMethodClass::run
  IL_0007: stloc.0
  IL_0008: ldloc.0
  IL_0009: brfalse IL_001b
  IL_000e: nop
  IL_000f: ldstr ""Run""
  IL_0014: call System.Void System.Console::WriteLine(System.String)
  IL_0019: nop
  IL_001a: nop
  IL_001b: nop
  IL_001c: leave IL_002f
} // end .try
finally
{
  IL_0021: nop
  IL_0022: ldstr ""Finally""
  IL_0027: call System.Void System.Console::WriteLine(System.String)
  IL_002c: nop
  IL_002d: nop
  IL_002e: endfinally
} // end handler (finally)
IL_002f: ldstr ""Post-finally""
IL_0034: call System.Void System.Console::WriteLine(System.String)
IL_0039: nop
.try
{
  .try
  {
    IL_003a: nop
    IL_003b: ldsfld System.Boolean HarmonyLibTests.Assets.TryCatchMethodClass::run
    IL_0040: stloc.1
    IL_0041: ldloc.1
    IL_0042: brfalse IL_0054
    IL_0047: nop
    IL_0048: ldstr ""Run 2""
    IL_004d: call System.Void System.Console::WriteLine(System.String)
    IL_0052: nop
    IL_0053: nop
    IL_0054: nop
    IL_0055: leave IL_006d
  } // end .try
  catch System.Exception
  {
    IL_005a: stloc.2
    IL_005b: nop
    IL_005c: ldstr ""Catch 2""
    IL_0061: call System.Void System.Console::WriteLine(System.String)
    IL_0066: nop
    IL_0067: nop
    IL_0068: leave IL_006d
  } // end handler (catch)
  IL_006d: leave IL_0080
} // end .try
finally
{
  IL_0072: nop
  IL_0073: ldstr ""Finally 2""
  IL_0078: call System.Void System.Console::WriteLine(System.String)
  IL_007d: nop
  IL_007e: nop
  IL_007f: endfinally
} // end handler (finally)
IL_0080: ldstr ""Post-finally 2""
IL_0085: call System.Void System.Console::WriteLine(System.String)
IL_008a: nop
IL_008b: ret
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
  IL_0014: leave IL_0024
} // end .try
finally
{
  IL_0019: ldstr ""Finally""
  IL_001e: call System.Void System.Console::WriteLine(System.String)
  IL_0023: endfinally
} // end handler (finally)
IL_0024: ldstr ""Post-finally""
IL_0029: call System.Void System.Console::WriteLine(System.String)
.try
{
  .try
  {
    IL_002e: ldsfld System.Boolean HarmonyLibTests.Assets.TryCatchMethodClass::run
    IL_0033: brfalse IL_0042
    IL_0038: ldstr ""Run 2""
    IL_003d: call System.Void System.Console::WriteLine(System.String)
    IL_0042: leave IL_0057
  } // end .try
  catch System.Exception
  {
    IL_0047: pop
    IL_0048: ldstr ""Catch 2""
    IL_004d: call System.Void System.Console::WriteLine(System.String)
    IL_0052: leave IL_0057
  } // end handler (catch)
  IL_0057: leave IL_0067
} // end .try
finally
{
  IL_005c: ldstr ""Finally 2""
  IL_0061: call System.Void System.Console::WriteLine(System.String)
  IL_0066: endfinally
} // end handler (finally)
IL_0067: ldstr ""Post-finally 2""
IL_006c: call System.Void System.Console::WriteLine(System.String)
IL_0071: ret
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
