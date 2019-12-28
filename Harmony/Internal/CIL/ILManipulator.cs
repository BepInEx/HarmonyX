using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib.Internal.Patching;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using MonoMod.Utils.Cil;
using MethodBody = Mono.Cecil.Cil.MethodBody;
using OperandType = Mono.Cecil.Cil.OperandType;
using SRE = System.Reflection.Emit;

namespace HarmonyLib.Internal.CIL
{
    /// <summary>
    /// High-level IL code manipulator for MonoMod that allows to manipulate a method as a stream of CodeInstructions.
    /// </summary>
    internal class ILManipulator
    {
        private static readonly Dictionary<short, SRE.OpCode> SREOpCodes = new Dictionary<short, SRE.OpCode>();

        static ILManipulator()
        {
            foreach (var field in typeof(SRE.OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var sreOpCode = (SRE.OpCode) field.GetValue(null);
                SREOpCodes[sreOpCode.Value] = sreOpCode;
            }
        }

        private readonly IEnumerable<CodeInstruction> codeInstructions;
        private List<MethodInfo> transpilers = new List<MethodInfo>();

        /// <summary>
        /// Initialize IL transpiler
        /// </summary>
        /// <param name="body">Body of the method to transpile</param>
        /// <param name="original">Original method. Used to resolve locals and parameters</param>
        public ILManipulator(MethodBody body, MethodBase original = null)
        {
            codeInstructions = ReadBody(body, original);
        }

        private IEnumerable<CodeInstruction> ReadBody(MethodBody body, MethodBase original = null)
        {
            var locals = original.GetMethodBody()?.LocalVariables ?? new List<LocalVariableInfo>();
            var mParams = original.GetParameters();
            var instructions = new List<CodeInstruction>(body.Instructions.Count);

            CodeInstruction ReadInstruction(Instruction ins)
            {
                var cIns = new CodeInstruction(SREOpCodes[ins.OpCode.Value]);

                switch (ins.OpCode.OperandType)
                {
                    case OperandType.InlineField:
                    case OperandType.InlineMethod:
                    case OperandType.InlineType:
                    case OperandType.InlineTok:
                        cIns.ilOperand = ((MemberReference) ins.Operand).ResolveReflection();
                        break;
                    case OperandType.InlineArg:
                        break;
                    case OperandType.InlineVar:
                    case OperandType.ShortInlineVar:
                        var varDef = (VariableDefinition) ins.Operand;
                        cIns.ilOperand = locals.FirstOrDefault(l => l.LocalIndex == varDef.Index);
                        break;
                    case OperandType.ShortInlineArg:
                        var pDef = (ParameterDefinition) ins.Operand;
                        cIns.ilOperand = mParams.First(p => p.Position == pDef.Index);
                        break;
                    case OperandType.InlineBrTarget:
                    case OperandType.ShortInlineBrTarget:
                        cIns.ilOperand = body.Instructions.IndexOf((Instruction) ins.Operand);
                        break;
                    case OperandType.InlineSwitch:
                        cIns.ilOperand = ((Instruction[]) ins.Operand)
                                         .Select(i => body.Instructions.IndexOf(i)).ToArray();
                        break;
                    default:
                        cIns.ilOperand = ins.Operand;
                        break;
                }

                return cIns;
            }

            // Pass 1: Convert IL to base abstract CodeInstructions
            instructions.AddRange(body.Instructions.Select(ReadInstruction));

            //Pass 2: Resolve CodeInstructions for branch parameters
            foreach (var cIns in instructions)
                switch (cIns.opcode.OperandType)
                {
                    case SRE.OperandType.ShortInlineBrTarget:
                    case SRE.OperandType.InlineBrTarget:
                        cIns.ilOperand = instructions[(int) cIns.operand];
                        break;
                    case SRE.OperandType.InlineSwitch:
                        cIns.ilOperand = ((int[]) cIns.operand).Select(i => instructions[i]).ToArray();
                        break;
                }

            // Pass 3: Attach exception blocks to each code instruction
            foreach (var exception in body.ExceptionHandlers)
            {
                var tryStart = instructions[body.Instructions.IndexOf(exception.TryStart)];
                var tryEnd = instructions[body.Instructions.IndexOf(exception.TryEnd)];
                var handlerStart = instructions[body.Instructions.IndexOf(exception.HandlerStart)];
                var handlerEnd = instructions[body.Instructions.IndexOf(exception.HandlerEnd)];

                tryStart.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
                handlerEnd.blocks.Add(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));

                switch (exception.HandlerType)
                {
                    case ExceptionHandlerType.Catch:
                        handlerStart.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginCatchBlock,
                                                                   exception.CatchType.ResolveReflection()));
                        break;
                    case ExceptionHandlerType.Filter:
                        var filterStart = instructions[body.Instructions.IndexOf(exception.FilterStart)];
                        filterStart.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptFilterBlock));
                        break;
                    case ExceptionHandlerType.Finally:
                        handlerStart.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginFinallyBlock));
                        break;
                    case ExceptionHandlerType.Fault:
                        handlerStart.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginFaultBlock));
                        break;
                }
            }

            return instructions;
        }

        /// <summary>
        /// Adds a transpiler method that edits the IL of the given method
        /// </summary>
        /// <param name="transpiler">Transpiler method</param>
        /// <exception cref="NotImplementedException">Currently not implemented</exception>
        public void AddTranspiler(MethodInfo transpiler)
        {
            transpilers.Add(transpiler);
        }

        private object[] GetTranpilerArguments(ILGenerator il, MethodInfo transpiler,
                                               IEnumerable<CodeInstruction> instructions, MethodBase orignal = null)
        {
            var result = new List<object>();

            foreach (var type in transpiler.GetParameters().Select(p => p.ParameterType))
                if (type.IsAssignableFrom(typeof(ILGenerator)))
                    result.Add(il);
                else if (type.IsAssignableFrom(typeof(MethodBase)) && orignal != null)
                    result.Add(orignal);
                else if (type.IsAssignableFrom(typeof(IEnumerable<CodeInstruction>)))
                    result.Add(instructions);

            return result.ToArray();
        }

        private List<CodeInstruction> ApplyTranspilers(ILGenerator il, MethodBase original = null)
        {
            var tempInstructions = codeInstructions;

            foreach (var transpiler in transpilers)
            {
                var args = GetTranpilerArguments(il, transpiler, tempInstructions, original);
                tempInstructions = transpiler.Invoke(null, args) as IEnumerable<CodeInstruction>;
            }

            return tempInstructions.ToList();
        }

        /// <summary>
        /// Processes and writes IL to the provided method body.
        /// Note that this cleans the existing method body (removes insturctions and exception handlers).
        /// </summary>
        /// <param name="body">Method body to write to.</param>
        /// <param name="original">Original method that transpiler can optionally call into</param>
        /// <exception cref="NotSupportedException">One of IL opcodes contains a CallSide (e.g. calli), which is currently not fully supported.</exception>
        /// <exception cref="ArgumentNullException">One of IL opcodes with an operand contains a null operand.</exception>
        public void WriteTo(MethodBody body, MethodBase original = null)
        {
            // Clean up the body of the target method
            body.Instructions.Clear();
            body.ExceptionHandlers.Clear();

            var il = new CecilILGenerator(body.GetILProcessor());
            var cil = il.GetProxy();

            // Step 1: Prepare labels for instructions
            foreach (var codeInstruction in codeInstructions)
            {
                // Set operand to the same as the IL operand (in most cases they are the same)
                codeInstruction.operand = codeInstruction.ilOperand;

                switch (codeInstruction.opcode.OperandType)
                {
                    case SRE.OperandType.InlineSwitch when codeInstruction.ilOperand is CodeInstruction[] targets:
                    {
                        var labels = new List<Label>();
                        foreach (var target in targets)
                        {
                            var label = il.DefineLabel();
                            target.labels.Add(label);
                            labels.Add(label);
                        }

                        codeInstruction.operand = labels.ToArray();
                    }
                        break;
                    case SRE.OperandType.ShortInlineBrTarget:
                    case SRE.OperandType.InlineBrTarget:
                    {
                        if (codeInstruction.operand is CodeInstruction target)
                        {
                            var label = il.DefineLabel();
                            target.labels.Add(label);
                            codeInstruction.operand = label;
                        }
                    }
                        break;
                }
            }

            // Step 2: Run the code instruction through transpilers
            var newInstructions = ApplyTranspilers(cil, original);

            // We don't remove trailing `ret`s because we need to do so only if prefixes/postfixes are present

            // Step 3: Emit code
            foreach (var ins in newInstructions)
            {
                ins.labels.ForEach(l => il.MarkLabel(l));
                ins.blocks.ForEach(b => il.MarkBlockBefore(b, out var _));

                // We don't replace `ret`s yet because we might not need to
                // We do that only if we add prefixes/postfixes
                // We also don't need to care for long/short forms thanks to Cecil/MonoMod

                switch (ins.opcode.OperandType)
                {
                    case SRE.OperandType.InlineNone:
                        il.Emit(ins.opcode);
                        break;
                    case SRE.OperandType.InlineSig:
                        throw new NotSupportedException(
                            "Emitting opcodes with CallSites is currently not fully implemented");
                    default:
                        if (ins.operand == null)
                            throw new ArgumentNullException(nameof(ins.operand), $"Invalid argument for {ins}");
                        il.Emit(ins.opcode, ins.operand);
                        break;
                }

                ins.blocks.ForEach(b => il.MarkBlockAfter(b));
            }

            // Note: We lose all unassigned labels here along with any way to log them
            // On the contrary, we gain better logging anyway down the line by using Cecil

            // Step 4: Run the code through raw IL manipulators (if any)
            // TODO: IL Manipulators
        }
    }
}