using System;
using System.Reflection.Emit;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;

namespace HarmonyLib.Internal.RuntimeFixes
{
    /// <summary>
    /// Fix to enable DynamicMethod having ref return.
    ///
    /// AccessTools.FieldRefAccess generates a dynamic method with ref returns.
    /// While working on Cecil and MethodBuilder, DynamicMethod by default does not allow ref returns.
    /// This fix manually sets the return type of a DynamicMethod when DynamicMethodDefinition uses DynamicMethod backend.
    /// </summary>
    internal static class DynamicMethodByRefReturnFix
    {
        private static bool _applied;

        public static void Install()
        {
            if (_applied)
                return;

            new ILHook(AccessTools.Method(typeof(DMDEmitDynamicMethodGenerator), "_Generate"), EditDMGenerator).Apply();

            _applied = true;
        }

        private delegate DynamicMethod ReplaceDMGen(string name, Type returnType, Type[] paramTypes, Type owner,
                                                    bool skipVis);

        private static void EditDMGenerator(ILContext ctx)
        {
            var cursor = new ILCursor(ctx);
            cursor.GotoNext(i => i.MatchNewobj<DynamicMethod>());
            cursor.Remove();
            cursor.EmitDelegate((ReplaceDMGen) ((name, type, types, owner, vis) =>
            {
                var returnType = type;
                if (type.IsByRef)
                    returnType = type.GetElementType();
                var dm = new DynamicMethod(name, returnType, types, owner, vis);

                if (type.IsByRef)
                {
                    var trv = Traverse.Create(dm);
                    trv.Field("returnType").SetValue(type);
                    trv.Field("m_returnType").SetValue(type);
                }

                return dm;
            }));
        }
    }
}