using System.Text;

namespace UITKSourceGenerator
{
    public static class NamingConventions
    {
        /// <summary>
        /// Convert C# camelCase field name to UXML kebab-case name.
        /// btnLogin → btn-login, lblPlayerHP → lbl-player-hp
        /// </summary>
        public static string ToKebabCase(string camelCase)
        {
            if (string.IsNullOrEmpty(camelCase)) return camelCase;

            var sb = new StringBuilder();
            for (int i = 0; i < camelCase.Length; i++)
            {
                char c = camelCase[i];
                if (char.IsUpper(c))
                {
                    bool isAcronymPart = i > 0 && char.IsUpper(camelCase[i - 1]);
                    bool isAcronymEnd = i + 1 < camelCase.Length && char.IsLower(camelCase[i + 1]);

                    if (i > 0 && !isAcronymPart)
                    {
                        sb.Append('-');
                    }
                    else if (isAcronymPart && isAcronymEnd && i > 1)
                    {
                        sb.Append('-');
                    }
                    sb.Append(char.ToLower(c));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Extract target element name from OnClick method name.
        /// OnBtnLogin → btn-login
        /// </summary>
        public static string MethodNameToTarget(string methodName)
        {
            if (methodName.StartsWith("On") && methodName.Length > 2)
            {
                string remainder = char.ToLower(methodName[2]) + methodName.Substring(3);
                return ToKebabCase(remainder);
            }
            return ToKebabCase(methodName);
        }
    }
}
