namespace HarmonyLibTests.Assets
{
    internal static class TranspliersClasses
    {
        internal static int TestStaticField = 0;

        internal static void TestStaticMethod()
        {
            int i = int.MaxValue;
            var b = i.CompareTo(i);
        }
    }
}