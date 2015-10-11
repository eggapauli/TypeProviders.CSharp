namespace TypeProviders.CSharp
{
    public static class StringHelper
    {
        public static string ToPublicIdentifier(this string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }
            return char.ToUpper(name[0]) + name.Substring(1);
        }

        public static string ToVariableIdentifier(this string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }
            return char.ToLower(name[0]) + name.Substring(1);
        }
    }
}
