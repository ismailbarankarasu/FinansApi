namespace FinansApi.Infrastructure.Errors;

public sealed class DomainException(int status, string message, string? field = null) : Exception(message)
{
    public int Status { get; } = status;
    public string? Field { get; } = field;

    public static DomainException Invalid(string message, string? field = null) => new(400, message, field);

    public static DomainException Missing() => new(404, "Kayıt bulunamadı.");

    public static DomainException Conflict(string message) => new(409, message);

}
