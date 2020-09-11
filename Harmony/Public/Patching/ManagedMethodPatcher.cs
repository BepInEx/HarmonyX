using System;
using System.Reflection;
using HarmonyLib.Internal.Patching;
using HarmonyLib.Internal.Util;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;

namespace HarmonyLib.Public.Patching
{
	/// <summary>
	/// Method patcher for normal managed methods that have IL body attached to them.
	/// Uses <see cref="MonoMod.RuntimeDetour.ILHook"/> in order to apply hooks in a way compatible with MonoMod's own
	/// hooking system.
	/// </summary>
	///
	public class ManagedMethodPatcher : MethodPatcher
	{
		private static readonly MethodInfo IsAppliedSetter =
			AccessTools.PropertySetter(typeof(ILHook), nameof(ILHook.IsApplied));

		private static readonly Action<ILHook, bool> SetIsApplied =
			(Action<ILHook, bool>) IsAppliedSetter.CreateDelegate<Action<ILHook, bool>>();

		private ILHook ilHook;

		/// <inheritdoc />
		public ManagedMethodPatcher(MethodBase original) : base(original) { }

		/// <inheritdoc />
		public override DynamicMethodDefinition PrepareOriginal()
		{
			return null;
		}

		/// <inheritdoc />
		public override MethodBase DetourTo(MethodBase replacement)
		{
			ilHook ??= new ILHook(Original, Manipulator, new ILHookConfig {ManualApply = true});
			// Reset IsApplied to force MonoMod to reapply the ILHook without removing it
			SetIsApplied(ilHook, false);
			ilHook.Apply();
			return ilHook.GetCurrentTarget();
		}

		private void Manipulator(ILContext ctx)
		{
			HarmonyManipulator.Manipulate(Original, Original.GetPatchInfo(), ctx);
		}

		public static void TryResolve(object sender, PatchManager.PatcherResolverEeventArgs args)
		{
			if (args.Original.GetMethodBody() != null)
				args.MethodPatcher = new ManagedMethodPatcher(args.Original);
		}
	}
}
