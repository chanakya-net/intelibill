namespace Intelibill.Application.Features.Items.Barcodes;

public interface IBarcodeLabelRepository
{
    Task<IReadOnlyList<BarcodeLabelPrintRow>> GetRowsAsync(
        Guid activeShopId,
        IReadOnlyList<PrintBarcodeLabelItemRequest> items,
        CancellationToken cancellationToken);
}
