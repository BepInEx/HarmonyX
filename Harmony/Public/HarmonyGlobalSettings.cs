namespace HarmonyLib
{
	/// <summary>Class that holds all Global Harmony settings</summary>
	///
	public static class HarmonyGlobalSettings
	{
		/// <summary>Set to true to disallow executing UnpatchAll without specifying a harmonyId.</summary>
		/// <remarks>If set to true and UnpatchAll is called without passing a harmonyId, then said method will throw a HarmonyException</remarks>
		public static bool DisallowGlobalUnpatchAll { get; set; }
	}
}
