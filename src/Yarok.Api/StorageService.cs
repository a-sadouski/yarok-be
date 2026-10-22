using System.Net.Http.Headers;

namespace Yarok.Api;

public sealed class StorageService(HttpClient client, IConfiguration config)
{
    private static readonly Dictionary<string, string> Extensions = new() { ["image/png"] = ".png", ["image/jpeg"] = ".jpg", ["image/webp"] = ".webp", ["image/gif"] = ".gif" };
    public void Validate(IFormFile image)
    {
        if (image.Length > 10 * 1024 * 1024 || !Extensions.ContainsKey(image.ContentType)) throw new ApiException(400, "Image must be PNG, JPEG, WebP or GIF, up to 10 MB");
        using var stream = image.OpenReadStream();
        Span<byte> header = stackalloc byte[12];
        var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        var bytes = header[..read];
        var valid = image.ContentType switch
        {
            "image/png" => bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "image/jpeg" => bytes.StartsWith(new byte[] { 255, 216, 255 }),
            "image/gif" => bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8),
            "image/webp" => bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8),
            _ => false
        };
        if (!valid) throw new ApiException(400, "Image content does not match its type");
    }
    public async Task<string> Upload(long id, IFormFile image, CancellationToken ct)
    {
        Validate(image);
        // A failed database write must never replace an image used by the live catalog.
        var path = $"products/{id}/{Guid.NewGuid():N}{Extensions[image.ContentType]}";
        var key = Settings.Required(config, "SUPABASE_SERVICE_KEY");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Settings.Required(config, "SUPABASE_URL").TrimEnd('/')}/storage/v1/object/products-images/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.Add("apikey", key);
        await using var stream = image.OpenReadStream();
        request.Content = new StreamContent(stream); request.Content.Headers.ContentType = new MediaTypeHeaderValue(image.ContentType);
        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return path;
    }
}
