using System.Text;
using EasyTranslation.Interop;

namespace EasyTranslation.Config;

public static class SecretProtector
{
    public static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return string.Empty;
        var bytes = Dpapi.Protect(Encoding.UTF8.GetBytes(plain));
        return Convert.ToBase64String(bytes);
    }

    public static string Unprotect(string protectedBase64)
    {
        if (string.IsNullOrWhiteSpace(protectedBase64)) return string.Empty;
        try
        {
            var bytes = Convert.FromBase64String(protectedBase64);
            return Encoding.UTF8.GetString(Dpapi.Unprotect(bytes));
        }
        catch
        {
            return string.Empty;
        }
    }
}
