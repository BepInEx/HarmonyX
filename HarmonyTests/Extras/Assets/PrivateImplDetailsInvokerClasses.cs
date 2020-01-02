using System;
using System.Runtime.CompilerServices;

namespace HarmonyLibTests.Assets
{
    public class PrivateImplDetailsInvokerObjectPatch
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Prefix(string value)
        {
            Console.WriteLine(value);
        }
    }

    public class PrivateImplDetailsInvokerObject
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Something(string val)
        {

        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Test(string value)
        {
            switch (value)
            {
                case "A":
                    Something("A branch");
                    break;

                case "B":
                    Something("B branch");
                    break;

                case "C":
                    Something("C branch");
                    break;

                case "D":
                    Something("D branch");
                    break;

                case "E":
                    Something("E branch");
                    break;

                case "F":
                    Something("F branch");
                    break;

                case "G":
                    Something("G branch");
                    break;

                case "H":
                    Something("H branch");
                    break;

                case "I":
                    Something("I branch");
                    break;

                case "J":
                    Something("J branch");
                    break;

                case "K":
                    Something("K branch");
                    break;

                case "L":
                    Something("L branch");
                    break;

                case "M":
                    Something("M branch");
                    break;

                default:
                    Something("default branch");
                    break;
            }

            Something("Complete");
        }
    }
}