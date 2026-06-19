namespace Intelibill.Application.Features.CreditNotes.Services;

public interface ICreditNoteCodeGenerator
{
    Task<string> GenerateAsync(Guid shopId, CancellationToken cancellationToken = default);
}
