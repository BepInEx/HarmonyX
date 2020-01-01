using System;
using System.Collections.Generic;

namespace HarmonyLibTests.Assets
{
    public class ILDasmToStringObject
    {
        public List<string> items = new List<string>{ "a", "b", "c", "d" };

        public void Test()
        {
            try
            {
                Console.WriteLine("Hello, world");
                foreach (var item in items)
                {
                    Console.WriteLine(item);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
            finally
            {
                Console.WriteLine("Done");
            }
        }
    }
}