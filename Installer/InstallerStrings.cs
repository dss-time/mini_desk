using System.Globalization;
using System.Resources;

namespace MiniDesk.Setup;

internal static class InstallerStrings
{
    private static readonly ResourceManager Manager = new("MiniDesk.Setup.Resources.Strings", typeof(InstallerStrings).Assembly);
    internal static string Get(string key) => Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
    internal static string Format(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
