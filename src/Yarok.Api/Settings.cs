namespace Yarok.Api;

public static class Settings
{
    public static string Required(IConfiguration c, string key) => !string.IsNullOrWhiteSpace(c[key]) ? c[key]! : throw new InvalidOperationException($"Configure {key}");

    public static void Validate(IConfiguration config)
    {
        string[] required = ["DATABASE_URL", "SUPABASE_URL", "SUPABASE_SERVICE_KEY", "ADMIN_LOGIN", "ADMIN_PASSWORD", "ADMIN_SESSION_SECRET", "SMTP_HOST", "SMTP_USER", "SMTP_PASSWORD", "CONTACT_TO_EMAIL"];
        var missing = required.Where(key => string.IsNullOrWhiteSpace(config[key])).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Missing configuration: {string.Join(", ", missing)}. Use scripts/Start-Dev.ps1 -EnvFile ../yarok/.env, environment variables, or dotnet user-secrets in Development. dotnet run does not load .env files.");
        if (!Uri.TryCreate(config["SUPABASE_URL"], UriKind.Absolute, out var url) || (url.Scheme != "https" && url.Scheme != "http"))
            throw new InvalidOperationException("SUPABASE_URL must be an absolute HTTP(S) URL.");
        if (!int.TryParse(config["SMTP_PORT"] ?? "587", out var port) || port is < 1 or > 65535)
            throw new InvalidOperationException("SMTP_PORT must be between 1 and 65535.");
        try { _ = new Npgsql.NpgsqlConnectionStringBuilder(Database.ConnectionString(config["DATABASE_URL"]!)); }
        catch (Exception e) when (e is ArgumentException or FormatException or UriFormatException)
        {
            // Do not include parser exceptions: they may contain credentials from the input.
            throw new InvalidOperationException("DATABASE_URL must be a valid PostgreSQL URI or Npgsql connection string.");
        }
    }
}

