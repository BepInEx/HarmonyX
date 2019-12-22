using System.Linq;

namespace HarmonyLibTests.Assets
{
    public class TraverseMethods_Instance
    {
        public bool Method1_called;

#pragma warning disable IDE0051
        private void Method1()
        {
            Method1_called = true;
        }

        private string Method2(string arg1)
        {
            return arg1 + arg1;
        }
#pragma warning restore IDE0051
    }

    public static class TraverseMethods_Static
    {
#pragma warning disable IDE0051
        private static int StaticMethod(int a, int b)
        {
            return a * b;
        }
#pragma warning restore IDE0051
    }

    public static class TraverseMethods_VarArgs
    {
#pragma warning disable IDE0051
        private static int Test1(int a, int b)
        {
            return a + b;
        }

        private static int Test2(int a, int b, int c)
        {
            return a + b + c;
        }

        private static int Test3(int multiplier, params int[] n)
        {
            return n.Aggregate(0, (acc, x) => acc + x) * multiplier;
        }
#pragma warning restore IDE0051
    }

    public static class TraverseMethods_Parameter
    {
#pragma warning disable IDE0051
        private static string WithRefParameter(ref string refParameter)
        {
            refParameter = "hello";
            return "ok";
        }

        private static string WithOutParameter(out string refParameter)
        {
            refParameter = "hello";
            return "ok";
        }

        private static T WithGenericParameter<T>(T refParameter)
        {
            return refParameter;
        }
#pragma warning restore IDE0051
    }
}