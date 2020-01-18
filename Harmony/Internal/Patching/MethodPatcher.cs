using System;
using System.Reflection;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;

namespace HarmonyLib.Internal.Patching
{
    internal abstract class MethodPatcher
    {
        public MethodBase Original { get; }

        protected MethodPatcher(MethodBase original)
        {
            Original = original;
        }

        public abstract void Apply();

        public static MethodPatcher Create(MethodBase original)
        {
            if(original.GetMethodBody() == null)
                return new NativeMethodPatcher(original);
            return new ILMethodPatcher(original);
        }
    }

    internal class ILMethodPatcher : MethodPatcher
    {
        private static readonly MethodInfo IsAppliedSetter = AccessTools.PropertySetter(typeof(ILHook), nameof(ILHook.IsApplied));
        private static readonly Action<ILHook, bool> SetIsApplied = (Action<ILHook, bool>) IsAppliedSetter.CreateDelegate<Action<ILHook, bool>>();

        private ILHook ilHook;

        public ILMethodPatcher(MethodBase original) : base(original)
        {
        }

        public override void Apply()
        {
            if (ilHook == null)
            {
                ilHook = new ILHook(Original, Manipulator, new ILHookConfig
                {
                    ManualApply = true
                });
            }

            // Reset IsApplied to force MonoMod to reapply the ILHook without removing it
            SetIsApplied(ilHook, false);
            ilHook.Apply();
        }

        private void Manipulator(ILContext ctx)
        {
            HarmonyManipulator.Manipulate(Original, Original.GetPatchInfo(), ctx);
        }
    }

    internal class NativeMethodPatcher : MethodPatcher
    {
        public NativeMethodPatcher(MethodBase original) : base(original)
        {

        }

        public override void Apply()
        {
            throw new System.NotImplementedException();
        }
    }
}