using System.Reflection;
using HarmonyLib.Internal.Patching;
using MonoMod.Utils;

namespace HarmonyLib.Public.Patching
{
	/// <summary>
	/// A general method patcher for implementing custom Harmony patcher backends.
	/// </summary>
	///
	public abstract class MethodPatcher
	{
		/// <summary>
		/// Constructs a method patcher
		/// </summary>
		/// <param name="original">Original method to patch</param>
		///
		protected MethodPatcher(MethodBase original)
		{
			Original = original;
		}

		/// <summary>
		/// Original method to patch.
		/// </summary>
		///
		public MethodBase Original { get; }

		/// <summary>
		/// Prepares method body for the unpatched <see cref="DynamicMethodDefinition"/> that simply calls
		/// <see cref="Original"/> function.
		/// </summary>
		/// <returns>
		/// A <see cref="DynamicMethodDefinition"/> that contains a call to
		/// the original method to pass to the IL manipulator.
		/// If <b>null</b>, Harmony patches must be manually applied to the original via <see cref="HarmonyManipulator.Manipulate"/>.
		/// </returns>
		///
		public abstract DynamicMethodDefinition PrepareOriginal();

		/// <summary>
		/// Detours <see cref="Original"/> to the provided replacement function. If called multiple times,
		/// <see cref="Original"/> is re-detoured to the new method.
		/// </summary>
		/// <param name="replacement">
		/// Result of <see cref="HarmonyManipulator.Manipulate"/>
		/// if <see cref="PrepareOriginal"/> returned non-<b>null</b>.
		/// Otherwise, this will be <b>null</b>, in which case you must manually generate Harmony-patched method
		/// with <see cref="HarmonyManipulator.Manipulate"/>.
		/// </param>
		///
		public abstract void DetourTo(MethodBase replacement);
	}
}
