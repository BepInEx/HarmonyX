using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using HarmonyLib.Internal;
using HarmonyLib.Internal.CIL;
using HarmonyLibTests.Assets;
using NUnit.Framework;

namespace HarmonyLibTests.IL
{
    [TestFixture]
    public class TestMethodBodyReader
    {
        [Test]
        public void CanGetInstructionsWithNoILGenerator()
        {
            var method = typeof(Class12).GetMethod(nameof(Class12.FizzBuzz));
            var instrsNoGen = MethodBodyReader.GetInstructions(null, method);

            var dynamicMethod = DynamicTools.CreateDynamicMethod(method, "_Patch");
            var instrsHasGen = MethodBodyReader.GetInstructions(dynamicMethod.GetILGenerator(), method);

            Assert.AreEqual(instrsNoGen.Count, instrsHasGen.Count);
            for (var i = 0; i < instrsNoGen.Count; i++)
            {
                var instrNoGen = instrsNoGen[i];
                var instrHasGen = instrsHasGen[i];
                Assert.AreEqual(instrNoGen.offset, instrHasGen.offset, "offset @ {0} ({1})", i, instrNoGen);
                Assert.AreEqual(instrNoGen.opcode, instrHasGen.opcode, "opcode @ {0} ({1})", i, instrNoGen);
                AssertAreEqual(instrNoGen.operand, instrHasGen.operand, "operand", i, instrNoGen);
                CollectionAssert.AreEqual(instrNoGen.labels, instrHasGen.labels, "labels @ {0}", i);
                CollectionAssert.AreEqual(instrNoGen.blocks, instrHasGen.blocks, "blocks @ {0}", i);
                AssertAreEqual(instrNoGen.operand, instrHasGen.operand, "argument", i, instrNoGen);

                // The only difference between w/o gen and w/ gen is this:
                var operandType = instrNoGen.opcode.OperandType;
                if ((operandType == OperandType.ShortInlineVar || operandType == OperandType.InlineVar) &&
                    !(instrNoGen.argument is null))
                {
                    Assert.AreEqual(typeof(LocalVariableInfo), instrNoGen.argument.GetType(),
                                    "w/o generator argument type @ {0} ({1})", i, instrNoGen);
                    Assert.AreEqual(typeof(LocalBuilder), instrHasGen.argument.GetType(),
                                    "w/ generator argument type @ {0} ({1})", i, instrNoGen);
                }
            }
        }

        private static void AssertAreEqual(object x, object y, string label, int currentIndex,
                                           ILInstruction currentInstr)
        {
            if (x is ILInstruction xInstr && y is ILInstruction yInstr)
                Assert.AreEqual(xInstr.offset, yInstr.offset, "{0} @ {1} ({2})", label, currentIndex, currentInstr);
            else if (x is ILInstruction[] xInstrs && y is ILInstruction[] yInstrs)
                CollectionAssert.AreEqual(xInstrs, yInstrs, new ILInstructionOffsetComparer(), "{0} @ {1} ({2})", label,
                                          currentIndex, currentInstr);
            else if (x is LocalVariableInfo xLocal && y is LocalVariableInfo yLocal)
                AssertAreEqual(xLocal, yLocal, label, currentIndex, currentInstr);
            else
                Assert.AreEqual(x, y, "{0} @ {1} ({2})", label, currentIndex, currentInstr);
        }

        private static void AssertAreEqual(LocalVariableInfo x, LocalVariableInfo y, string label, int currentIndex,
                                           ILInstruction currentInstr)
        {
            Assert.AreEqual(x.LocalType, y.LocalType, "{0}.{1} @ {2} ({3})", label, "LocalType", currentIndex,
                            currentInstr);
            Assert.AreEqual(x.IsPinned, y.IsPinned, "{0}.{1} @ {2} ({3})", label, "IsPinned", currentIndex,
                            currentInstr);
            Assert.AreEqual(x.LocalIndex, y.LocalIndex, "{0}.{1} @ {2} ({3})", label, "LocalIndex", currentIndex,
                            currentInstr);
        }

        private struct ILInstructionOffsetComparer : IComparer
        {
            public int Compare(object x, object y)
            {
                return ((ILInstruction) x).offset.CompareTo(((ILInstruction) y).offset);
            }
        }
    }
}