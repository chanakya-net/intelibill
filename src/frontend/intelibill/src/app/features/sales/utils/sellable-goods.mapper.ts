import type { AvailableBatchDto } from '../../inventory/services/inventory.models';
import type { SellableGoodsDto } from '../services/sale.models';

/** A goods sellable as the batch the cart adds, keeping its unit and precision. */
export function goodsSellableToBatch(sellable: SellableGoodsDto): AvailableBatchDto {
  return {
    sellability: sellable.sellability, quarantinedQuantity: sellable.quarantinedQuantity, availableQuantity: sellable.availableQuantity, version: sellable.version,
    barcode: sellable.barcode,
    itemName: sellable.itemName,
    batchNumber: sellable.batchNumber,
    inventoryBatchId: sellable.inventoryBatchId,
    quantity: sellable.quantity,
    salesPrice: sellable.salesPrice,
    mrp: sellable.mrp,
    taxRatePercent: sellable.taxRatePercent,
    taxIncluded: sellable.taxIncluded,
    expiryDate: sellable.expiryDate,
    hsnCode: sellable.hsnCode,
    uom: sellable.uom ?? '',
    quantityScale: sellable.quantityScale ?? 0,
    documentUnit: sellable.documentUnit,
  };
}

/** A batch (online or from the offline catalog) as the goods sellable the batch picker offers. */
export function batchToGoodsSellable(batch: AvailableBatchDto): SellableGoodsDto {
  return {
    kind: 'Goods',
    sellability: batch.sellability, quarantinedQuantity: batch.quarantinedQuantity, availableQuantity: batch.availableQuantity, version: batch.version,
    inventoryBatchId: batch.inventoryBatchId,
    barcode: batch.barcode,
    itemName: batch.itemName,
    batchNumber: batch.batchNumber,
    quantity: batch.quantity,
    salesPrice: batch.salesPrice,
    mrp: batch.mrp,
    taxRatePercent: batch.taxRatePercent,
    taxIncluded: batch.taxIncluded,
    expiryDate: batch.expiryDate,
    hsnCode: batch.hsnCode,
    uom: batch.uom ?? '',
    quantityScale: batch.quantityScale ?? 0,
    documentUnit: batch.documentUnit,
  };
}
