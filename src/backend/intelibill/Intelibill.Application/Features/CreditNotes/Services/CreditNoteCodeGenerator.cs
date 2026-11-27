using System.Globalization;
using Intelibill.Application.Common.Normalization;
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

    public async Task<string> GenerateAsync(Guid shopId, DateOnly businessDate, CancellationToken cancellationToken = default)
    {
        for (int i = 0; i < MaxRetries; i++)
        {
            var code = GenerateCode(businessDate);
            var normalizedCode = CreditNoteCodeNormalizer.Normalize(code);
            var existing = await _repository.GetByCodeAsync(shopId, normalizedCode, cancellationToken);
            if (existing is null)
            {
                return code;
            }
        }

        throw new InvalidOperationException("Failed to generate unique credit note code after maximum retries.");
    }

    private static string GenerateCode(DateOnly businessDate)
    {
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        return string.Create(CultureInfo.InvariantCulture, $"CN-{businessDate:yyyyMMdd}-{suffix}");
    }
}
