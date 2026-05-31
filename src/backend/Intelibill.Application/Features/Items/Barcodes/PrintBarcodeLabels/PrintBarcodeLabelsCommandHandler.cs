using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Items.Barcodes;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Items.Barcodes.PrintBarcodeLabels;

public sealed class PrintBarcodeLabelsCommandHandler(
    IUserRepository userRepository,
    IBarcodeLabelRepository barcodeLabelRepository,
    IBarcodeLabelPdfRenderer barcodeLabelPdfRenderer)
{
    public async Task<ErrorOr<BarcodeLabelPrintResult>> HandleAsync(
        PrintBarcodeLabelsCommand command,
        CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var actorMembership = actor.ShopMemberships.FirstOrDefault(membership => membership.ShopId == command.ActiveShopId);
        if (actorMembership is null)
            return Errors.Shop.MembershipNotFound;

        if (actorMembership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Item.UserIsNotOwnerOrManager;

        var distinctItems = command.Items
            .DistinctBy(item => new LabelRequestKey(item.ItemId, item.InventoryBatchId))
            .ToArray();
        var rows = await barcodeLabelRepository.GetRowsAsync(command.ActiveShopId, distinctItems, cancellationToken);
        var rowLookup = rows.ToDictionary(
            row => new LabelRequestKey(row.ItemId, row.InventoryBatchId),
            row => row);

        var errors = new List<Error>();
        var expandedRows = new List<BarcodeLabelPrintRow>(command.Items.Sum(item => item.Quantity));

        foreach (var item in command.Items)
        {
            var lookupKey = new LabelRequestKey(item.ItemId, item.InventoryBatchId);
            if (!rowLookup.TryGetValue(lookupKey, out var row))
            {
                errors.Add(item.InventoryBatchId is null
                    ? Errors.Item.BarcodeLabelItemNotFound(item.ItemId)
                    : Errors.Item.BarcodeLabelBatchNotFound(item.ItemId, item.InventoryBatchId.Value));
                continue;
            }

            for (var quantity = 0; quantity < item.Quantity; quantity++)
            {
                expandedRows.Add(row);
            }
        }

        if (errors.Count > 0)
            return errors;

        var dataset = new BarcodeLabelPrintDataset(expandedRows);
        return await barcodeLabelPdfRenderer.RenderAsync(dataset, cancellationToken);
    }

    private readonly record struct LabelRequestKey(Guid ItemId, Guid? InventoryBatchId);
}
