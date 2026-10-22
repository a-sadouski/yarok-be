using System.Net;


using Microsoft.Extensions.Caching.Memory;
using MimeKit;

namespace Yarok.Api;

public sealed class ContactService(IConfiguration config, IEmailSender sender) : IDisposable
{
    private readonly MemoryCache requests = new(new MemoryCacheOptions { SizeLimit = 10000 });
    private readonly object gate = new();
    public async Task<object> Send(HttpContext ctx, CancellationToken ct)
    {
        if (!ctx.Request.HasJsonContentType()) throw new ApiException(415, "JSON content type is required");
        var body = await ctx.Request.ReadFromJsonAsync<ContactRequest>(ct) ?? throw new ApiException(400, "Body required");
        if (!string.IsNullOrWhiteSpace(body.Company)) return new { success = true };
        var name = body.Name?.Trim(); var phone = body.Phone?.Trim(); var comment = body.Comment?.Trim();
        if (string.IsNullOrEmpty(name)) throw new ApiException(400, "Введите имя");
        if (string.IsNullOrEmpty(phone)) throw new ApiException(400, "Введите телефон");
        if (string.IsNullOrEmpty(comment)) throw new ApiException(400, "Введите комментарий");
        if (name.Length > 200 || phone.Length > 100 || comment.Length > 10000)
            throw new ApiException(400, "Contact fields are too long");
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        lock (gate)
        {
            var now = DateTimeOffset.UtcNow;
            var timestamps = requests.Get<List<DateTimeOffset>>(ip)?.Where(t => now - t < TimeSpan.FromMinutes(10)).ToList() ?? [];
            if (timestamps.Count > 0 && now - timestamps[^1] < TimeSpan.FromMinutes(1)) throw new ApiException(429, "Слишком частые отправки. Попробуйте чуть позже.");
            if (timestamps.Count >= 3) throw new ApiException(429, "Превышен лимит заявок. Попробуйте позже.");
            timestamps.Add(now); requests.Set(ip, timestamps, new MemoryCacheEntryOptions { Size = 1, SlidingExpiration = TimeSpan.FromMinutes(10) });
        }
        var user = Settings.Required(config, "SMTP_USER");
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Ярок", user));
        message.To.AddRange(InternetAddressList.Parse(Settings.Required(config, "CONTACT_TO_EMAIL")));
        message.Subject = "Новая заявка с сайта";
        string Escape(string value) => WebUtility.HtmlEncode(value);
        message.Body = new BodyBuilder
        {
            TextBody = $"Новая заявка с сайта\n\nИмя: {name}\nТелефон: {phone}\nКомментарий: {comment}\nIP: {ip}",
            HtmlBody = $"<h2>Новая заявка с сайта</h2><p><strong>Имя:</strong> {Escape(name)}</p><p><strong>Телефон:</strong> {Escape(phone)}</p><p><strong>Комментарий:</strong></p><p>{Escape(comment).Replace("\n", "<br>")}</p><p><strong>IP:</strong> {Escape(ip)}</p>"
        }.ToMessageBody();
        await sender.Send(message, ct);
        return new { success = true };
    }

    public void Dispose() => requests.Dispose();
}
public record ContactRequest(string? Name, string? Phone, string? Comment, string? Company);


