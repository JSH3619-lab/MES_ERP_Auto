using UnimesAutomation;
using Xunit;

public class SecretProtectorTests
{
    [Fact]
    public void Encrypt_then_Decrypt_roundtrips()
    {
        var plain = "@Fhfflzlem306";
        var enc = SecretProtector.Encrypt(plain);

        Assert.False(string.IsNullOrEmpty(enc));
        Assert.NotEqual(plain, enc);              // 평문이 그대로 노출되지 않음
        Assert.Equal(plain, SecretProtector.Decrypt(enc));
    }

    [Fact]
    public void Empty_input_returns_empty()
    {
        Assert.Equal("", SecretProtector.Encrypt(""));
        Assert.Equal("", SecretProtector.Decrypt(""));
    }

    [Fact]
    public void Decrypt_invalid_returns_empty()
    {
        Assert.Equal("", SecretProtector.Decrypt("not-a-valid-base64-or-blob!!"));
    }
}
