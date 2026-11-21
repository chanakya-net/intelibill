using System.Text;

namespace Intelibill.Api.Extensions;

/// <summary>Sends the first record promptly and bounds subsequent batches before each protected handoff.</summary>
internal sealed class NdjsonResponseBuffer(HttpResponse response)
{
    private const int MaxCharacters = 16 * 1024;
    private const int MaxRecords = 50;
    private readonly StringBuilder _lines = new();
    private int _pending;
    private bool _sent;
    public bool HasFlushed => _sent;

    public async Task WriteAsync(string record, CancellationToken ct)
    {
        _lines.Append(record).Append('\n');
        if (!_sent || ++_pending == MaxRecords || _lines.Length >= MaxCharacters)
            await FlushAsync(ct);
    }

    public async Task FlushAsync(CancellationToken ct)
    {
        if (_lines.Length == 0)
            return;
        await response.WriteAsync(_lines.ToString(), ct);
        _sent = true;
        _pending = 0;
        _lines.Clear();
    }
}
