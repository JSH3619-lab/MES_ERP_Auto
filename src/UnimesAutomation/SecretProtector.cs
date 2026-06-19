using System.Security.Cryptography;
using System.Text;

namespace UnimesAutomation;

// Windows DPAPI(CurrentUser)로 비밀번호를 암호화/복호화한다. 평문은 어떤 파일에도 저장하지 않는다.
public static class SecretProtector
{
    public static string Encrypt(string plain)
    {
        if (string.IsNullOrEmpty(plain))
        {
            return "";
        }

        var bytes = Encoding.UTF8.GetBytes(plain);
        var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    public static string Decrypt(string encrypted)
    {
        if (string.IsNullOrEmpty(encrypted))
        {
            return "";
        }

        try
        {
            var protectedBytes = Convert.FromBase64String(encrypted);
            var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception)
        {
            // 다른 계정/PC에서 저장된 값이거나 손상된 값이면 복호화 불가 → 빈 값.
            return "";
        }
    }
}
