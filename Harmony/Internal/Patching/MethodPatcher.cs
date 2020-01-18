using System.Reflection;

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

        public static MethodPatcher Create(MethodInfo original)
        {
            if(original.GetMethodBody() == null)
                return new NativeMethodPatcher(original);
            return new ILMethodPatcher(original);
        }
    }

    internal class ILMethodPatcher : MethodPatcher
    {
        public ILMethodPatcher(MethodBase original) : base(original)
        {

        }

        public override void Apply()
        {
            throw new System.NotImplementedException();
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