using System;
using HarmonyLib.Internal.Util;
using HarmonyLibTests.Assets;
using MonoMod.Utils;
using NUnit.Framework;

namespace HarmonyLibTests
{
    [TestFixture]
    public class ILDasmToString
    {
#if !DEBUG
        private static string expected = @".locals init (
    System.Collections.Generic.List`1/Enumerator<System.String> V_0
)
.try
{
  .try
  {
    IL_0000: ldstr ""Hello, world""
    IL_0005: call System.Void System.Console::WriteLine(System.String)
    IL_000a: ldarg.0
    IL_000b: ldfld System.Collections.Generic.List`1<System.String> HarmonyLibTests.Assets.ILDasmToStringObject::items
    IL_0010: callvirt System.Collections.Generic.List`1/Enumerator<T> System.Collections.Generic.List`1<System.String>::GetEnumerator()
    IL_0015: stloc.0
    .try
    {
      IL_0016: br.s IL_0024
      IL_0018: ldloca.s V_0
      IL_001a: call T System.Collections.Generic.List`1/Enumerator<System.String>::get_Current()
      IL_001f: call System.Void System.Console::WriteLine(System.String)
      IL_0024: ldloca.s V_0
      IL_0026: call System.Boolean System.Collections.Generic.List`1/Enumerator<System.String>::MoveNext()
      IL_002b: brtrue.s IL_0018
      IL_002d: leave.s IL_003d
    } // end .try
    finally
    {
      IL_002f: ldloca.s V_0
      IL_0031: constrained. System.Collections.Generic.List`1/Enumerator<System.String>
      IL_0037: callvirt System.Void System.IDisposable::Dispose()
      IL_003c: endfinally
    } // end handler (finally)
    IL_003d: leave.s IL_0051
  } // end .try
  catch System.Exception
  {
    IL_003f: call System.Void System.Console::WriteLine(System.Object)
    IL_0044: rethrow
  } // end handler (catch)
} // end .try
finally
{
  IL_0046: ldstr ""Done""
  IL_004b: call System.Void System.Console::WriteLine(System.String)
  IL_0050: endfinally
} // end handler (finally)
IL_0051: ret
";
#else
        private static string expected = @".locals init (
    System.Collections.Generic.List`1/Enumerator<System.String> V_0
    System.String V_1
    System.Exception V_2
)
IL_0000: nop
.try
{
  .try
  {
    IL_0001: nop
    IL_0002: ldstr ""Hello, world""
    IL_0007: call System.Void System.Console::WriteLine(System.String)
    IL_000c: nop
    IL_000d: nop
    IL_000e: ldarg.0
    IL_000f: ldfld System.Collections.Generic.List`1<System.String> HarmonyLibTests.Assets.ILDasmToStringObject::items
    IL_0014: callvirt System.Collections.Generic.List`1/Enumerator<T> System.Collections.Generic.List`1<System.String>::GetEnumerator()
    IL_0019: stloc.0
    .try
    {
      IL_001a: br.s IL_002d
      IL_001c: ldloca.s V_0
      IL_001e: call T System.Collections.Generic.List`1/Enumerator<System.String>::get_Current()
      IL_0023: stloc.1
      IL_0024: nop
      IL_0025: ldloc.1
      IL_0026: call System.Void System.Console::WriteLine(System.String)
      IL_002b: nop
      IL_002c: nop
      IL_002d: ldloca.s V_0
      IL_002f: call System.Boolean System.Collections.Generic.List`1/Enumerator<System.String>::MoveNext()
      IL_0034: brtrue.s IL_001c
      IL_0036: leave.s IL_0047
    } // end .try
    finally
    {
      IL_0038: ldloca.s V_0
      IL_003a: constrained. System.Collections.Generic.List`1/Enumerator<System.String>
      IL_0040: callvirt System.Void System.IDisposable::Dispose()
      IL_0045: nop
      IL_0046: endfinally
    } // end handler (finally)
    IL_0047: nop
    IL_0048: leave.s IL_0055
  } // end .try
  catch System.Exception
  {
    IL_004a: stloc.2
    IL_004b: nop
    IL_004c: ldloc.2
    IL_004d: call System.Void System.Console::WriteLine(System.Object)
    IL_0052: nop
    IL_0053: rethrow
  } // end handler (catch)
  IL_0055: leave.s IL_0065
} // end .try
finally
{
  IL_0057: nop
  IL_0058: ldstr ""Done""
  IL_005d: call System.Void System.Console::WriteLine(System.String)
  IL_0062: nop
  IL_0063: nop
  IL_0064: endfinally
} // end handler (finally)
IL_0065: ret
";
#endif

        [Test]
        public void TestILDasmToString()
        {
            var testObject = typeof(ILDasmToStringObject);
            var testMethod = testObject.GetMethod(nameof(ILDasmToStringObject.Test));
            var dmd = new DynamicMethodDefinition(testMethod);
            Assert.AreEqual(expected.Replace("\r\n", "\n"), dmd.Definition.Body.ToILDasmString().Replace("\r\n", "\n"));
        }
    }
}