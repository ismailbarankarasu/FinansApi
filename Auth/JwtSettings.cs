namespace FinansApi.Auth;

public sealed class JwtSettings
{
    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "FinansKurs";
    public string Audience { get; set; } = "FinansKursStudents";
    public int ExpiresMinutes { get; set; } = 480;

    public void Validate()
    {
        if (System.Text.Encoding.UTF8.GetByteCount(Key) < 32)
        {
            throw new InvalidOperationException("Jwt:Key için en az 32 baytlık bir environment veya user-secrets değeri tanımlayın.");
        }

        if (string.IsNullOrWhiteSpace(Issuer) || string.IsNullOrWhiteSpace(Audience) || ExpiresMinutes is < 1 or > 10080)
        {
            throw new InvalidOperationException("JWT issuer, audience ve süre ayarlarını kontrol edin.");
        }
    }

}
