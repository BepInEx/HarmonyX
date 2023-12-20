using System;
using System.Reflection;
using HarmonyLib.Internal.Patching;
using HarmonyLib.Internal.Util;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using MethodBody = Mono.Cecil.Cil.MethodBody;

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
		private MethodBody hookBody;

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
			ilHook ??= new ILHook(Original, Manipulator, applyByDefault: false);
			try
			{
				ilHook.Undo();
				ilHook.Apply();
			}
			catch (Exception e)
			{
				throw HarmonyException.Create(e, hookBody);
			}
			return ilHook.Method;
		}

		/// <inheritdoc />
		public override DynamicMethodDefinition CopyOriginal()
		{
			return new DynamicMethodDefinition(Original);
		}

		private void Manipulator(ILContext ctx)
		{
			hookBody = ctx.Body;
			HarmonyManipulator.Manipulate(Original, ctx);
		}

		/// <summary>
		/// A handler for <see cref="PatchManager.ResolvePatcher"/> that checks if a method is a normal Managed method.
		/// </summary>
		/// <param name="sender">Not used</param>
		/// <param name="args">Patch resolver arguments</param>
		///
		public static void TryResolve(object sender, PatchManager.PatcherResolverEventArgs args)
		{
			if (args.Original.GetMethodBody() != null)
				args.MethodPatcher = new ManagedMethodPatcher(args.Original);
		}
	}
}
