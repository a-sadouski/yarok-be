using System.Security.Cryptography;
using System.Text;

namespace Yarok.Api;

public static class Session
{
    public static bool Equal(string? a, string? b) => a is not null && b is not null && CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(a)), SHA256.HashData(Encoding.UTF8.GetBytes(b)));
    private static string Signature(string payload, IConfiguration c) => Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Settings.Required(c, "ADMIN_SESSION_SECRET")), Encoding.UTF8.GetBytes(payload)));
    public static string Sign(IConfiguration c)
    {
        var payload = $"admin.{DateTimeOffset.UtcNow.AddHours(12).ToUnixTimeSeconds()}";
        return $"{payload}.{Signature(payload, c)}";
    }
    public static void Require(HttpContext ctx, IConfiguration c)
    {
        var parts = ctx.Request.Cookies["admin_session"]?.Split('.');
        if (parts is not { Length: 3 } || parts[0] != "admin" || !long.TryParse(parts[1], out var expiry) || expiry <= DateTimeOffset.UtcNow.ToUnixTimeSeconds() || !Equal(parts[2], Signature($"{parts[0]}.{parts[1]}", c)))
            throw new ApiException(401, "Unauthorized");
    }
}

