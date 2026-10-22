using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using MimeKit;
using Npgsql;
using Yarok.Api;

var checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    checks++;
}
async Task Reject(Func<Task> action, int status)
{
    try { await action(); throw new Exception($"Expected HTTP {status}"); }
    catch (ApiException e) { Check(e.Status == status, $"Expected {status}, got {e.Status}"); }
}
IConfiguration Config(Dictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
var settings = new Dictionary<string, string?>
{
    ["ADMIN_LOGIN"] = "test-admin", ["ADMIN_PASSWORD"] = "test-password",
    ["ADMIN_SESSION_SECRET"] = "test-only-secret", ["SUPABASE_URL"] = "https://storage.invalid",
    ["SUPABASE_SERVICE_KEY"] = "test-only-key", ["SMTP_USER"] = "test@example.invalid",
    ["CONTACT_TO_EMAIL"] = "receiver@example.invalid"
};
var config = Config(settings);
var parsed = new NpgsqlConnectionStringBuilder(Database.ConnectionString("postgres://test:p%40ss@localhost/catalog%20test?sslmode=verify-full&connect_timeout=3"));
Check(parsed.Password == "p@ss" && parsed.Database == "catalog test" && parsed.SslMode == SslMode.VerifyFull && parsed.Timeout == 3, "PostgreSQL URI parsing");
try { Settings.Validate(Config([])); throw new Exception("Missing settings accepted"); }
catch (InvalidOperationException e) { Check(e.Message.Contains("DATABASE_URL") && e.Message.Contains("Start-Dev"), "Actionable startup configuration error"); }

var signed = Session.Sign(config);
var authenticated = new DefaultHttpContext();
authenticated.Request.Headers.Cookie = $"admin_session={signed}";
Session.Require(authenticated, config);
Check(true, "Signed session accepted");
foreach (var cookie in new[] { signed + "x", "admin.1.invalid", "admin.invalid.invalid", "admin" })
{
    var ctx = new DefaultHttpContext(); ctx.Request.Headers.Cookie = $"admin_session={cookie}";
    await Reject(() => { Session.Require(ctx, config); return Task.CompletedTask; }, 401);
}

var email = new CapturingEmailSender();
using var contact = new ContactService(config, email);
HttpContext Contact(object body)
{
    var ctx = new DefaultHttpContext();
    var bytes = JsonSerializer.SerializeToUtf8Bytes(body);
    ctx.Request.Body = new MemoryStream(bytes);
    ctx.Request.ContentType = "application/json"; ctx.Request.ContentLength = bytes.Length;
    ctx.Connection.RemoteIpAddress = IPAddress.Loopback;
    return ctx;
}
await contact.Send(Contact(new { company = "bot" }), default);
Check(email.Message is null, "Honeypot sends no email");
await Reject(() => contact.Send(Contact(new { name = "test", phone = "123" }), default), 400);
await Reject(() => contact.Send(Contact(new { name = new string('a', 201), phone = "123", comment = "x" }), default), 400);
await contact.Send(Contact(new { name = "<script>", phone = "123", comment = "A&B\nhello" }), default);
Check(email.Message!.HtmlBody!.Contains("&lt;script&gt;") && email.Message.HtmlBody.Contains("A&amp;B<br>hello"), "Email HTML escaping and newline conversion");
Check(email.Message.TextBody!.Contains("<script>"), "Plain-text email preserves input");
await Reject(() => contact.Send(Contact(new { name = "test", phone = "123", comment = "x" }), default), 429);

var http = new StorageHandler();
using var client = new HttpClient(http);
var storage = new StorageService(client, config);
IFormFile Image(bool valid = true)
{
    byte[] data = valid ? Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aZ1cAAAAASUVORK5CYII=") : Encoding.UTF8.GetBytes("this is not an image");
    return new FormFile(new MemoryStream(data), 0, data.Length, "image", "pixel.png") { Headers = new HeaderDictionary(), ContentType = "image/png" };
}
await Reject(() => { storage.Validate(Image(false)); return Task.CompletedTask; }, 400);
var firstPath = await storage.Upload(42, Image(), default);
var secondPath = await storage.Upload(42, Image(), default);
Check(firstPath != secondPath && !http.Upsert, "Uploads use unique paths and never overwrite an existing image");

if (args.Contains("--database"))
{
    var raw = Environment.GetEnvironmentVariable("DATABASE_URL") ?? throw new Exception("DATABASE_URL required for isolated database checks");
    var adminString = Database.ConnectionString(raw);
    var schema = "yarok_test_" + Guid.NewGuid().ToString("N");
    await using var admin = new NpgsqlConnection(adminString);
    await admin.OpenAsync();
    await using (var create = new NpgsqlCommand($"create schema {schema}", admin)) await create.ExecuteNonQueryAsync();
    try
    {
        var isolated = new NpgsqlConnectionStringBuilder(adminString) { SearchPath = schema, Pooling = false };
        settings["DATABASE_URL"] = isolated.ConnectionString;
        config = Config(settings);
        var db = new Database(config);
        await using (var conn = await db.Open(default))
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "database/001_schema.sql"))) root = root.Parent;
            var sql = await File.ReadAllTextAsync(Path.Combine(root?.FullName ?? throw new Exception("Repository root not found"), "database/001_schema.sql"));
            await using var setup = new NpgsqlCommand(sql + " insert into categories(name,slug) values('Test','test'); insert into colors(name,slug) values('Red','red'),('Blue','blue');", conn);
            await setup.ExecuteNonQueryAsync();
        }
        storage = new StorageService(client, config);
        var products = new ProductService(db, storage, config);
        HttpRequest Product(Dictionary<string, StringValues>? overrides = null, bool image = false)
        {
            var fields = new Dictionary<string, StringValues>
            {
                ["name"] = "Isolated test", ["slug"] = "test", ["categoryId"] = "1", ["colorId"] = "1", ["price"] = "12.50"
            };
            if (overrides is not null) foreach (var item in overrides) fields[item.Key] = item.Value;
            var ctx = new DefaultHttpContext(); ctx.Request.ContentType = "multipart/form-data; boundary=test";
            var files = new FormFileCollection(); if (image) files.Add(Image());
            ctx.Request.Form = new FormCollection(fields, files);
            return ctx.Request;
        }
        await products.Save(Product(image: true), null, default);
        var list = JsonSerializer.SerializeToElement(await products.List(default));
        Check(list.GetArrayLength() == 1 && list[0].GetProperty("variants")[0].GetProperty("price").GetString() == "12.50", "Create and catalog price contract");
        Check(list[0].GetProperty("previewImage").GetString()!.Contains("/storage/v1/object/public/products-images/"), "Public image URL contract");
        await using (var conn = await db.Open(default))
        {
            await using var extra = new NpgsqlCommand("insert into product_images(product_id,image_path,sort_order) values(1,'second.png',0)", conn);
            await extra.ExecuteNonQueryAsync();
        }
        await products.Save(Product(new() { ["name"] = "Updated", ["isVariable"] = "true", ["variants"] = "[{\"colorId\":1,\"price\":\"20\"},{\"colorId\":2,\"price\":\"30\"}]" }, true), 1, default);
        list = JsonSerializer.SerializeToElement(await products.List(default));
        Check(list[0].GetProperty("name").GetString() == "Updated" && list[0].GetProperty("variants").GetArrayLength() == 2, "Update replaces variants atomically");
        Check(list[0].GetProperty("imagePath").GetString() != "second.png", "Updating main image preserves ordering");
        try { await products.Save(Product(), null, default); throw new Exception("Duplicate slug accepted"); }
        catch (PostgresException e) { Check(e.SqlState == "23505", "Duplicate slug rejected"); }
        await Reject(() => products.Save(Product(new() { ["categoryId"] = "999" }), 1, default), 400);
        await Reject(() => products.Save(Product(), 999, default), 404);
        await Reject(() => products.Save(Product(new() { ["price"] = "1.234" }), 1, default), 400);
        http.Fail = true;
        try { await products.Save(Product(new() { ["name"] = "Must roll back" }, true), 1, default); throw new Exception("Storage failure accepted"); }
        catch (HttpRequestException) { Check(true, "Storage failure surfaced"); }
        http.Fail = false;
        list = JsonSerializer.SerializeToElement(await products.List(default));
        Check(list[0].GetProperty("name").GetString() == "Updated" && list[0].GetProperty("variants").GetArrayLength() == 2, "Storage failure rolls back product and variants");
        await products.Save(Product(new() { ["isActive"] = "false", ["colorId"] = "" }), 1, default);
        list = JsonSerializer.SerializeToElement(await products.List(default));
        Check(list.GetArrayLength() == 0, "Inactive products excluded; nullable color accepted on update");
    }
    finally
    {
        await using var cleanup = new NpgsqlCommand($"drop schema {schema} cascade", admin);
        await cleanup.ExecuteNonQueryAsync();
    }
}
Console.WriteLine($"PASS: {checks} checks; email captured in memory, Storage mocked" + (args.Contains("--database") ? ", isolated PostgreSQL schema removed." : ". Database checks skipped (pass --database)."));

sealed class CapturingEmailSender : IEmailSender
{
    public MimeMessage? Message { get; private set; }
    public Task Send(MimeMessage message, CancellationToken ct) { Message = message; return Task.CompletedTask; }
}
sealed class StorageHandler : HttpMessageHandler
{
    public bool Fail { get; set; }
    public bool Upsert { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Upsert |= request.Headers.Contains("x-upsert");
        return Task.FromResult(new HttpResponseMessage(Fail ? HttpStatusCode.BadGateway : HttpStatusCode.OK));
    }
}
