using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Npgsql;
using Yarok.Api;

var builder = WebApplication.CreateBuilder(args);
Settings.Validate(builder.Configuration);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 10 * 1024 * 1024);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 11 * 1024 * 1024);
builder.Services.AddSingleton<Database>();
builder.Services.AddSingleton<ContactService>();
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddHttpClient<StorageService>();
builder.Services.AddScoped<ProductService>();
var app = builder.Build();
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (Exception ex)
    {
        var (status, message) = ex switch
        {
            ApiException e => (e.Status, e.Message),
            BadHttpRequestException e => (e.StatusCode, "Invalid request"),
            InvalidDataException => (400, "Invalid form data"),
            JsonException => (400, "Invalid JSON"),
            PostgresException { SqlState: "23505" } => (409, "Товар с таким slug уже существует"),
            PostgresException { SqlState: "23503" } => (400, "Unknown category or color"),
            _ => (500, "Internal Server Error")
        };
        if (ctx.RequestAborted.IsCancellationRequested) return;
        if (status == 500) app.Logger.LogError(ex, "Request failed: {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
        if (ctx.Response.HasStarted) throw;
        ctx.Response.Clear();
        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(new { statusCode = status, statusMessage = message, message });
    }
});
app.MapGet("/health", () => new { status = "ok" });
app.MapPost("/api/admin/login", async (HttpContext ctx, IConfiguration config) =>
{
    if (!ctx.Request.HasJsonContentType()) throw new ApiException(415, "JSON content type is required");
    var body = await ctx.Request.ReadFromJsonAsync<LoginRequest>() ?? throw new ApiException(400, "Body required");
    if (!Session.Equal(body.Login, Settings.Required(config, "ADMIN_LOGIN")) || !Session.Equal(body.Password, Settings.Required(config, "ADMIN_PASSWORD")))
        throw new ApiException(401, "Неверный логин или пароль");
    ctx.Response.Cookies.Append("admin_session", Session.Sign(config), new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = !app.Environment.IsDevelopment(), Path = "/", MaxAge = TimeSpan.FromHours(12) });
    return Results.Ok(new { success = true });
});
app.MapPost("/api/admin/logout", (HttpContext ctx) =>
{
    ctx.Response.Cookies.Delete("admin_session", new CookieOptions { Path = "/" });
    return new { success = true };
});
app.MapGet("/api/admin/me", (HttpContext ctx, IConfiguration config) =>
{
    Session.Require(ctx, config);
    return new { authenticated = true };
});
app.MapGet("/api/categories", (Database db, CancellationToken ct) => db.Query("select id, name, slug from categories order by id", ct));
app.MapGet("/api/colors", (Database db, CancellationToken ct) => db.Query("select id, name, slug, hex from colors order by id", ct));
app.MapGet("/api/products", (ProductService service, CancellationToken ct) => service.List(ct));
app.MapPost("/api/products/create", (HttpContext ctx, ProductService service, IConfiguration config, CancellationToken ct) =>
{
    Session.Require(ctx, config);
    return service.Save(ctx.Request, null, ct);
});
app.MapPut("/api/products/{id}", (string id, HttpContext ctx, ProductService service, IConfiguration config, CancellationToken ct) =>
{
    Session.Require(ctx, config);
    if (!long.TryParse(id, out var value) || value <= 0)
        throw new ApiException(400, "Valid product id is required");
    return service.Save(ctx.Request, value, ct);
});
app.MapPost("/api/contact", (HttpContext ctx, ContactService service, CancellationToken ct) => service.Send(ctx, ct));
app.Run();


