using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.CreditNotes.Services;

public sealed class CreditNoteCodeGenerator : ICreditNoteCodeGenerator
{
    private readonly ICreditNoteRepository _repository;
    private const int MaxRetries = 10;

    public CreditNoteCodeGenerator(ICreditNoteRepository repository)
    {
        _repository = repository;
    }

    public async Task<string> GenerateAsync(Guid shopId, CancellationToken cancellationToken = default)
    {
        for (int i = 0; i < MaxRetries; i++)
        {
            var code = GenerateCode();
            var existing = await _repository.GetByCodeAsync(shopId, code, cancellationToken);
            if (existing is null)
            {
                return code;
            }
        }

        throw new InvalidOperationException("Failed to generate unique credit note code after maximum retries.");
    }

    private static string GenerateCode()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        return $"CN-{timestamp.UtcDateTime:yyyyMMdd}-{suffix}";
    }
}
