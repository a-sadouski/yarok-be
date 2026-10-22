namespace Yarok.Api;

public sealed class ApiException(int status, string message) : Exception(message)
{
    public int Status => status;
}

public record LoginRequest(string? Login, string? Password);
