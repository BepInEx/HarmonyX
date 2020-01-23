using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using MonoMod.Utils;

namespace HarmonyLib
{
    /// <summary>
    /// An exception thrown when a patch argument in a Harmony patch is invalid.
    /// </summary>
    public class InvalidHarmonyPatchArgumentException : Exception
    {
        public MethodBase Original { get; }
        public MethodInfo Patch { get; }

        /// <inheritdoc />
        public InvalidHarmonyPatchArgumentException(string message, MethodBase original, MethodInfo patch) : base(message)
        {
            Original = original;
            Patch = patch;
        }

        /// <inheritdoc />
        public override string Message => $"({Patch.GetID()}): {base.Message}";
    }
}
