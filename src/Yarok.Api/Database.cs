using Npgsql;

namespace Yarok.Api;

public sealed class Database(IConfiguration config)
{
    public static string ConnectionString(string raw)
    {
        if (raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(raw);
            var credentials = uri.UserInfo.Split(':', 2);
            var cs = new NpgsqlConnectionStringBuilder { Host = uri.Host, Port = uri.Port > 0 ? uri.Port : 5432, Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')), Username = Uri.UnescapeDataString(credentials[0]), Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : "", MaxAutoPrepare = 0 };
            foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                if (parts[0] == "sslmode" && parts.Length == 2) cs.SslMode = Enum.Parse<SslMode>(Uri.UnescapeDataString(parts[1]).Replace("-", ""), true);
                if (parts[0] == "connect_timeout" && parts.Length == 2) cs.Timeout = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            }
            raw = cs.ConnectionString;
        }
        return raw;
    }

    public async Task<NpgsqlConnection> Open(CancellationToken ct)
    {
        var conn = new NpgsqlConnection(ConnectionString(Settings.Required(config, "DATABASE_URL")));
        try { await conn.OpenAsync(ct); return conn; } catch { await conn.DisposeAsync(); throw; }
    }
    public async Task<List<Dictionary<string, object?>>> Query(string sql, CancellationToken ct)
    {
        await using var conn = await Open(ct);
        return await Read(new NpgsqlCommand(sql, conn), ct);
    }
    public static async Task<List<Dictionary<string, object?>>> Read(NpgsqlCommand cmd, CancellationToken ct)
    {
        using (cmd)
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var rows = new List<Dictionary<string, object?>>();
            while (await reader.ReadAsync(ct))
            {
                var row = new Dictionary<string, object?>();
                for (var i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rows.Add(row);
            }
            return rows;
        }
    }
}
