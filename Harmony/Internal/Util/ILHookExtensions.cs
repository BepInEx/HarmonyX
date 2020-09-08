using System.Reflection;
using MonoMod.RuntimeDetour;

namespace HarmonyLib.Internal.Util
{
    internal static class ILHookExtensions
    {
        private static readonly MethodInfo IsAppliedSetter = AccessTools.PropertySetter(typeof(ILHook), nameof(ILHook.IsApplied));

        public static ILHook MarkApply(this ILHook hook, bool apply)
        {
            if (hook == null)
                return null;

            // By manually resetting IsApplied we make it possible to rerun the manipulator
            IsAppliedSetter.Invoke(hook, new object[] { !apply });
            return hook;
        }
    }
}