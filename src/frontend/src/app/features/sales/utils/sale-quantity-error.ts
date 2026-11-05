import { hasProblemCode } from '../../../shared/utils/problem-code.util';

/**
 * The message for a sale the server refused because a quantity needs more decimals than its item's unit allows
 * (online record or preview), or null for any other error. The server's own text is English and names a barcode.
 */
export function saleQuantityErrorKey(error: unknown): string | null {
  return hasProblemCode(error, 'Sale.QuantityPrecisionExceeded') ? 'sales.newSale.quantityPrecisionExceeded' : null;
}
