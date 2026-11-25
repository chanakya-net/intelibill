using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using ErrorOr;
using Intelibill.Licensing.Application.Documents;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace Intelibill.Licensing.Api.Documents;

internal static class BillingDocumentMultipart
{
    internal const int CommandLimit = 2048;
    private const int ContentLimit = BillingDocumentMetadataValidator.MaxBytes;
    private const int RequestLimit = ContentLimit + 16 * 1024;

    private static readonly JsonSerializerOptions Strict = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.Strict,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
    };

    internal sealed record UploadParts(UploadBillingDocument Command, string? FileName, byte[] Content)
    {
        public override string ToString() => "[REDACTED]";
    }

    internal static async Task<ErrorOr<UploadParts>> ReadAsync(HttpContext context)
    {
        if (context.Request.ContentLength > RequestLimit) return BillingDocumentErrors.TooLarge;
        if (!MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var media)
            || !media.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            return BillingDocumentErrors.MediaType;
        var boundary = HeaderUtilities.RemoveQuotes(media.Boundary);
        if (boundary.Length is < 1 or > 70 || boundary.Value!.Any(c => c < ' ' || c > '~'))
            return BillingDocumentErrors.Invalid;
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
            limit.MaxRequestBodySize = RequestLimit;

        try
        {
            var reader = new MultipartReader(boundary.Value!, context.Request.Body) { HeadersCountLimit = 8, HeadersLengthLimit = 4096 };
            return await ReadPartsAsync(reader, context.RequestAborted);
        }
        catch (BadHttpRequestException error) when (error.StatusCode == 413)
        {
            return BillingDocumentErrors.TooLarge;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException)
        {
            context.RequestAborted.ThrowIfCancellationRequested();
            return BillingDocumentErrors.Invalid;
        }
    }

    private static async Task<ErrorOr<UploadParts>> ReadPartsAsync(MultipartReader reader, CancellationToken ct)
    {
        var metadata = await reader.ReadNextSectionAsync(ct);
        if (Disposition(metadata, "command") is null || !HasMediaType(metadata!, "application/json"))
            return BillingDocumentErrors.Invalid;
        var json = await ReadBoundedAsync(metadata!.Body, CommandLimit, ct);
        var command = json is null ? null : JsonSerializer.Deserialize<UploadBillingDocument>(json, Strict);
        if (command is null) return BillingDocumentErrors.Invalid;

        var file = await reader.ReadNextSectionAsync(ct);
        var disposition = Disposition(file, "content");
        if (disposition is null) return BillingDocumentErrors.Invalid;
        if (!HasMediaType(file!, BillingDocumentMetadataValidator.PdfMediaType)) return BillingDocumentErrors.MediaType;

        var bytes = await ReadBoundedAsync(file!.Body, ContentLimit, ct);
        if (bytes is null) return BillingDocumentErrors.TooLarge;
        if (await reader.ReadNextSectionAsync(ct) is not null) return BillingDocumentErrors.Invalid;
        var name = disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName;
        return new UploadParts(command, HeaderUtilities.RemoveQuotes(name).Value, bytes);
    }

    private static ContentDispositionHeaderValue? Disposition(MultipartSection? part, string name) =>
        ContentDispositionHeaderValue.TryParse(part?.ContentDisposition, out var disposition)
        && disposition.DispositionType.Equals("form-data", StringComparison.OrdinalIgnoreCase)
        && HeaderUtilities.RemoveQuotes(disposition.Name).Equals(name, StringComparison.Ordinal)
            ? disposition : null;

    private static bool HasMediaType(MultipartSection part, string mime) =>
        MediaTypeHeaderValue.TryParse(part.ContentType, out var media) && media.MediaType.Equals(mime, StringComparison.OrdinalIgnoreCase);

    private static async Task<byte[]?> ReadBoundedAsync(Stream stream, int limit, CancellationToken ct)
    {
        using var body = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            while (true)
            {
                var remaining = limit - (int)body.Length;
                var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining + 1)), ct);
                if (read == 0) return body.ToArray();
                if (read > remaining) return null;
                body.Write(buffer, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }
}
