using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace HarmonyLib
{
    /// <summary>Extensions for <see cref="CodeInstruction"/></summary>
    ///
    public static class CodeInstructionExtensions
    {
        private static readonly HashSet<OpCode> LoadVarCodes = new HashSet<OpCode>
        {
            OpCodes.Ldloc_0, OpCodes.Ldloc_1, OpCodes.Ldloc_2, OpCodes.Ldloc_3,
            OpCodes.Ldloc, OpCodes.Ldloca, OpCodes.Ldloc_S, OpCodes.Ldloca_S
        };

        private static readonly HashSet<OpCode> StoreVarCodes = new HashSet<OpCode>
        {
            OpCodes.Stloc_0, OpCodes.Stloc_1, OpCodes.Stloc_2, OpCodes.Stloc_3,
            OpCodes.Stloc, OpCodes.Stloc_S
        };

        private static readonly HashSet<OpCode> BranchCodes = new HashSet<OpCode>
        {
            OpCodes.Br_S, OpCodes.Brfalse_S, OpCodes.Brtrue_S, OpCodes.Beq_S, OpCodes.Bge_S, OpCodes.Bgt_S,
            OpCodes.Ble_S, OpCodes.Blt_S, OpCodes.Bne_Un_S, OpCodes.Bge_Un_S, OpCodes.Bgt_Un_S, OpCodes.Ble_Un_S,
            OpCodes.Blt_Un_S, OpCodes.Br, OpCodes.Brfalse, OpCodes.Brtrue, OpCodes.Beq, OpCodes.Bge, OpCodes.Bgt,
            OpCodes.Ble, OpCodes.Blt, OpCodes.Bne_Un, OpCodes.Bge_Un, OpCodes.Bgt_Un, OpCodes.Ble_Un, OpCodes.Blt_Un
        };

        private static readonly HashSet<OpCode> ConstantLoadingCodes = new HashSet<OpCode>
        {
            OpCodes.Ldc_I4_M1, OpCodes.Ldc_I4_0, OpCodes.Ldc_I4_1, OpCodes.Ldc_I4_2, OpCodes.Ldc_I4_3,
            OpCodes.Ldc_I4_4, OpCodes.Ldc_I4_5, OpCodes.Ldc_I4_6, OpCodes.Ldc_I4_7, OpCodes.Ldc_I4_8,
            OpCodes.Ldc_I4, OpCodes.Ldc_I4_S, OpCodes.Ldc_I8, OpCodes.Ldc_R4, OpCodes.Ldc_R8
        };

        /// <summary>Shortcut for testing whether the operand is equal to a non-null value</summary>
        /// <param name="code">The <see cref="CodeInstruction"/></param>
        /// <param name="value">The value</param>
        /// <returns>True if the operand has the same type and is equal to the value</returns>
        ///
        public static bool OperandIs(this CodeInstruction code, object value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (code.operand == null) return false;
            var type = value.GetType();
            var operandType = code.operand.GetType();
            if (AccessTools.IsInteger(type) && AccessTools.IsNumber(operandType))
                return Convert.ToInt64(code.operand) == Convert.ToInt64(value);
            if (AccessTools.IsFloatingPoint(type) && AccessTools.IsNumber(operandType))
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                return Convert.ToDouble(code.operand) == Convert.ToDouble(value);
            return Equals(code.operand, value);
        }

        /// <summary>Shortcut for <code>code.opcode == opcode &amp;&amp; code.OperandIs(operand)</code></summary>
        /// <param name="code">The <see cref="CodeInstruction"/></param>
        /// <param name="opcode">The <see cref="OpCode"/></param>
        /// <param name="operand">The operand value</param>
        /// <returns>True if the opcode is equal to the given opcode and the operand has the same type and is equal to the given operand</returns>
        ///
        public static bool Is(this CodeInstruction code, OpCode opcode, object operand)
        {
            return code.opcode == opcode && code.OperandIs(operand);
        }

        /// <summary>Tests for any form of Ldarg*</summary>
        /// <param name="code">The <see cref="CodeInstruction"/></param>
        /// <param name="n">The (optional) index</param>
        /// <returns>True if it matches one of the variations</returns>
        ///
        public static bool IsLdarg(this CodeInstruction code, int? n = null)
        {
            if ((n.HasValue == false || n.Value == 0) && code.opcode == OpCodes.Ldarg_0) return true;
            if ((n.HasValue == false || n.Value == 1) && code.opcode == OpCodes.Ldarg_1) return true;
            if ((n.HasValue == false || n.Value == 2) && code.opcode == OpCodes.Ldarg_2) return true;
            if ((n.HasValue == false || n.Value == 3) && code.opcode == OpCodes.Ldarg_3) return true;
            if (code.opcode == OpCodes.Ldarg &&
                (n.HasValue == false || n.Value == Convert.ToInt32(code.operand))) return true;
            if (code.opcode == OpCodes.Ldarg_S &&
                (n.HasValue == false || n.Value == Convert.ToInt32(code.operand))) return true;
            return false;
        }

        /// <summary>Tests for Ldarga/Ldarga_S</summary>
        /// <param name="code">The <see cref="CodeInstruction"/></param>
        /// <param name="n">The (optional) index</param>
        /// <returns>True if it matches one of the variations</returns>
        ///
        public static bool IsLdarga(this CodeInstruction code, int? n = null)
        {
            if (code.opcode != OpCodes.Ldarga && code.opcode != OpCodes.Ldarga_S) return false;
            return n.HasValue == false || n.Value == Convert.ToInt32(code.operand);
        }

        /// <summary>Tests for Starg/Starg_S</summary>
        /// <param name="code">The <see cref="CodeInstruction"/></param>
        /// <param name="n">The (optional) index</param>
        /// <returns>True if it matches one of the variations</returns>
        ///
        public static bool IsStarg(this CodeInstruction code, int? n = null)
        {
            if (code.opcode != OpCodes.Starg && code.opcode != OpCodes.Starg_S) return false;
            return n.HasValue == false || n.Value == Convert.ToInt32(code.operand);
        }

        /// <summary>Tests for any form of Ldloc*</summary>
        /// <param name="code">The <see cref="CodeInstruction"/></param>
        /// <param name="variable">The optional local variable</param>
        /// <returns>True if it matches one of the variations</returns>
        ///
        public static bool IsLdloc(this CodeInstruction code, LocalBuilder variable = null)
        {
            if (LoadVarCodes.Contains(code.opcode) == false) return false;
            return variable == null || Equals(variable, code.operand);
        }

        /// <summary>Tests for any form of Stloc*</summary>
        /// <param name="code">The <see cref="CodeInstruction"/></param>
        /// <param name="variable">The optional local variable</param>
        /// <returns>True if it matches one of the variations</returns>
        ///
        public static bool IsStloc(this CodeInstruction code, LocalBuilder variable = null)
        {
            if (StoreVarCodes.Contains(code.opcode) == false) return false;
            return variable == null || Equals(variable, code.operand);
        }

        /// <summary>Tests if the code instruction branches</summary>
        /// <param name="code">The <see cref="CodeInstruction"/></param>
        /// <param name="label">The label if the instruction is a branch operation or <see langword="null"/> if not</param>
        /// <returns>True if the instruction branches</returns>
        ///
        public static bool Branches(this CodeInstruction code, out Label? label)
        {
            if (BranchCodes.Contains(code.opcode))
            {
                label = (Label) code.operand;
                return true;
            }

            label = null;
            return false;
        }

        /// <summary>Tests if the code instruction calls the method/constructor</summary>
        /// <param name="code">The <see cref="CodeInstruction"/></param>
        /// <param name="method">The method</param>
        /// <returns>True if the instruction calls the method or constructor</returns>
        ///
        public static bool Calls(this CodeInstruction code, MethodInfo method)
        {
            if (method == null) throw new ArgumentNullException(nameof(method));
            if (code.opcode != OpCodes.Call && code.opcode != OpCodes.Callvirt) return false;
            return Equals(code.operand, method);
        }

        /// <summary>Tests if the code instruction loads a constant</summary>
        /// <param name="code">The <see cref="CodeInstruction"/></param>
        /// <returns>True if the instruction loads a constant</returns>
        ///
        public static bool LoadsConstant(this CodeInstruction code)
        {
            return ConstantLoadingCodes.Contains(code.opcode);
        }

        /// <summary>Tests if the code instruction loads an integer constant</summary>
        /// <param name="code">The <see cref="CodeInstruction"/></param>
        /// <param name="number">The integer constant</param>
        /// <returns>True if the instruction loads the constant</returns>
        ///
        public static bool LoadsConstant(this CodeInstruction code, long number)
        {
            var op = code.opcode;
            if (number == -1 && op == OpCodes.Ldc_I4_M1) return true;
            if (number == 0 && op == OpCodes.Ldc_I4_0) return true;
            if (number == 1 && op == OpCodes.Ldc_I4_1) return true;
            if (number == 2 && op == OpCodes.Ldc_I4_2) return true;
            if (number == 3 && op == OpCodes.Ldc_I4_3) return true;
            if (number == 4 && op == OpCodes.Ldc_I4_4) return true;
            if (number == 5 && op == OpCodes.Ldc_I4_5) return true;
            if (number == 6 && op == OpCodes.Ldc_I4_6) return true;
            if (number == 7 && op == OpCodes.Ldc_I4_7) return true;
            if (number == 8 && op == OpCodes.Ldc_I4_8) return true;
            if (op != OpCodes.Ldc_I4 && op != OpCodes.Ldc_I4_S && op != OpCodes.Ldc_I8) return false;
            return Convert.ToInt64(code.operand) == number;
        }

        /// <summary>Tests if the code instruction loads a floating point constant</summary>
        /// <param name="code">The <see cref="CodeInstruction"/></param>
        /// <param name="number">The floating point constant</param>
        /// <returns>True if the instruction loads the constant</returns>
        ///
        public static bool LoadsConstant(this CodeInstruction code, double number)
        {
            if (code.opcode != OpCodes.Ldc_R4 && code.opcode != OpCodes.Ldc_R8) return false;
            var val = Convert.ToDouble(code.operand);
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            return val == number;
        }

        /// <summary>Tests if the code instruction loads an enum constant</summary>
        /// <param name="code">The <see cref="CodeInstruction"/></param>
        /// <param name="e">The enum</param>
        /// <returns>True if the instruction loads the constant</returns>
        ///
        public static bool LoadsConstant(this CodeInstruction code, Enum e)
        {
            return code.LoadsConstant(Convert.ToInt64(e));
        }

        /// <summary>Tests if the code instruction loads a field</summary>
        /// <param name="code">The <see cref="CodeInstruction"/></param>
        /// <param name="field">The field</param>
        /// <param name="byAddress">Set to true if the address of the field is loaded</param>
        /// <returns>True if the instruction loads the field</returns>
        ///
        public static bool LoadsField(this CodeInstruction code, FieldInfo field, bool byAddress = false)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            var ldfldCode = field.IsStatic ? OpCodes.Ldsfld : OpCodes.Ldfld;
            if (byAddress == false && code.opcode == ldfldCode && Equals(code.operand, field)) return true;
            var ldfldaCode = field.IsStatic ? OpCodes.Ldsflda : OpCodes.Ldflda;
            if (byAddress && code.opcode == ldfldaCode && Equals(code.operand, field)) return true;
            return false;
        }

        /// <summary>Tests if the code instruction stores a field</summary>
        /// <param name="code">The <see cref="CodeInstruction"/></param>
        /// <param name="field">The field</param>
        /// <returns>True if the instruction stores this field</returns>
        ///
        public static bool StoresField(this CodeInstruction code, FieldInfo field)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            var stfldCode = field.IsStatic ? OpCodes.Stsfld : OpCodes.Stfld;
            return code.opcode == stfldCode && Equals(code.operand, field);
        }

        /// <summary>Adds labels to the code instruction and return it</summary>
        /// <param name="code">The <see cref="CodeInstruction"/></param>
        /// <param name="labels">One or several <see cref="Label"/> to add</param>
        /// <returns>The same code instruction</returns>
        public static CodeInstruction WithLabels(this CodeInstruction code, params Label[] labels)
        {
            code.labels.AddRange(labels);
            return code;
        }

        /// <summary>Adds labels to the code instruction and return it</summary>
        /// <param name="code">The <see cref="CodeInstruction"/></param>
        /// <param name="labels">An enumeration of <see cref="Label"/></param>
        /// <returns>The same code instruction</returns>
        public static CodeInstruction WithLabels(this CodeInstruction code, IEnumerable<Label> labels)
        {
            code.labels.AddRange(labels);
            return code;
        }

        /// <summary>Extracts all labels from the code instruction and returns them</summary>
        /// <param name="code">The <see cref="CodeInstruction"/></param>
        /// <returns>A list of <see cref="Label"/></returns>
        public static List<Label> ExtractLabels(this CodeInstruction code)
        {
            var labels = new List<Label>(code.labels);
            code.labels.Clear();
            return labels;
        }

        /// <summary>Moves all labels from the code instruction to a different one</summary>
        /// <param name="code">The <see cref="CodeInstruction"/> to move the labels from</param>
        /// <param name="other">The <see cref="CodeInstruction"/> to move the labels to</param>
        /// <returns>The code instruction labels were moved from (now empty)</returns>
        public static CodeInstruction MoveLabelsTo(this CodeInstruction code, CodeInstruction other)
        {
            other.WithLabels(code.ExtractLabels());
            return code;
        }

        /// <summary>Moves all labels from a different code instruction to the current one</summary>
        /// <param name="code">The <see cref="CodeInstruction"/> to move the labels from</param>
        /// <param name="other">The <see cref="CodeInstruction"/> to move the labels to</param>
        /// <returns>The code instruction that received the labels</returns>
        public static CodeInstruction MoveLabelsFrom(this CodeInstruction code, CodeInstruction other)
        {
            return code.WithLabels(other.ExtractLabels());
        }

        /// <summary>Adds ExceptionBlocks to the code instruction and return it</summary>
        /// <param name="code">The <see cref="CodeInstruction"/></param>
        /// <param name="blocks">One or several <see cref="ExceptionBlock"/> to add</param>
        /// <returns>The same code instruction</returns>
        public static CodeInstruction WithBlocks(this CodeInstruction code, params ExceptionBlock[] blocks)
        {
            code.blocks.AddRange(blocks);
            return code;
        }

        /// <summary>Adds ExceptionBlocks to the code instruction and return it</summary>
        /// <param name="code">The <see cref="CodeInstruction"/></param>
        /// <param name="blocks">An enumeration of <see cref="ExceptionBlock"/></param>
        /// <returns>The same code instruction</returns>
        public static CodeInstruction WithBlocks(this CodeInstruction code, IEnumerable<ExceptionBlock> blocks)
        {
            code.blocks.AddRange(blocks);
            return code;
        }

        /// <summary>Extracts all ExceptionBlocks from the code instruction and returns them</summary>
        /// <param name="code">The <see cref="CodeInstruction"/></param>
        /// <returns>A list of <see cref="ExceptionBlock"/></returns>
        public static List<ExceptionBlock> ExtractBlocks(this CodeInstruction code)
        {
            var blocks = new List<ExceptionBlock>(code.blocks);
            code.blocks.Clear();
            return blocks;
        }

        /// <summary>Moves all ExceptionBlocks from the code instruction to a different one</summary>
        /// <param name="code">The <see cref="CodeInstruction"/> to move the ExceptionBlocks from</param>
        /// <param name="other">The <see cref="CodeInstruction"/> to move the ExceptionBlocks to</param>
        /// <returns>The code instruction blocks were moved from (now empty)</returns>
        public static CodeInstruction MoveBlocksTo(this CodeInstruction code, CodeInstruction other)
        {
            other.WithBlocks(code.ExtractBlocks());
            return code;
        }

        /// <summary>Moves all ExceptionBlocks from a different code instruction to the current one</summary>
        /// <param name="code">The <see cref="CodeInstruction"/> to move the ExceptionBlocks from</param>
        /// <param name="other">The <see cref="CodeInstruction"/> to move the ExceptionBlocks to</param>
        /// <returns>The code instruction that received the blocks</returns>
        public static CodeInstruction MoveBlocksFrom(this CodeInstruction code, CodeInstruction other)
        {
            return code.WithBlocks(other.ExtractBlocks());
        }
    }
}