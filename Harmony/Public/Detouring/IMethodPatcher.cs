using System.Reflection;
using MonoMod.Utils;

namespace HarmonyLib.Public.Detouring
{
	/// <summary>
	/// A general method patcher interface for implementing custom Harmony patcher backends.
	/// </summary>
	///
	public interface IMethodPatcher
	{
		/// <summary>
		/// Original method to patch.
		/// </summary>
		///
		MethodBase Original { get; set; }

		/// <summary>
		/// Prepares method body for the unpatched <see cref="DynamicMethodDefinition"/> that simply calls
		/// <see cref="Original"/> function.
		/// </summary>
		/// <returns>A <see cref="DynamicMethodDefinition"/> that contains</returns>
		///
		DynamicMethodDefinition PrepareOriginal();

		/// <summary>
		/// Detours <see cref="Original"/> to the provided replacement function. If called multiple times,
		/// <see cref="Original"/> is re-detoured to the new method.
		/// </summary>
		/// <param name="replacement">Target method to detour the function to.</param>
		///
		void DetourTo(MethodBase replacement);
	}
}
