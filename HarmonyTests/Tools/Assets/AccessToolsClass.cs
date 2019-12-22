namespace HarmonyLibTests.Assets
{
    public class AccessToolsClass
    {
        private class Inner
        {
        }

        public const string field1Value = "hello";
        public const string field2Value = "dummy";
        public const string field3Value = "!";

        private string field;
        private readonly string field2;
        private static string field3 = field3Value;

        private int _property;

#pragma warning disable IDE0051
        private int Property
        {
            get => _property;
            set => _property = value;
        }

        private int Property2
        {
            get => _property;
            set => _property = value;
        }
#pragma warning restore IDE0051

        public AccessToolsClass()
        {
            field = field1Value;
            field2 = field2Value;
            field3 = field3Value;
        }

        public string Method()
        {
            return field;
        }

        public string Method2()
        {
            return field2;
        }

        public void SetField(string val)
        {
            field = val;
        }

        public string Method3()
        {
            return field3;
        }
    }

    public class AccessToolsSubClass : AccessToolsClass
    {
    }
}