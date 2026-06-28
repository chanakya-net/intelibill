import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/sales/data/dto/sale_return_preview_dto.dart';
import 'package:intelibill_mobile/src/features/sales/data/mappers/sale_return_mapper.dart';

void main() {
  group('SaleReturnMapper', () {
    test(
      'maps preview dto to domain including warnings and nested financials',
      () {
        const dto = SaleReturnPreviewResponseDto(
          saleId: 'sale-abc',
          hasFinancialAccess: true,
          lines: [
            SaleReturnPreviewLineDto(
              saleItemId: 'item-1',
              requestedQuantity: 1,
              returnedQuantity: 1,
              returnableQuantity: 2,
              condition: 1,
              willRestock: true,
              financial: SaleReturnPreviewLineFinancialDto(
                originalCostPrice: 60,
                originalSalesPrice: 100,
                originalTaxRatePercent: 18,
                originalIsPriceIncludingTax: false,
                maxRefundAmount: 100,
                approvedRefundAmount: 90,
                taxableAmount: 80,
                taxAmount: 14.4,
              ),
              itemId: 'prod-1',
              inventoryBatchId: 'batch-1',
            ),
          ],
          financial: SaleReturnPreviewFinancialDto(
            totalRefundAmount: 90,
            dueReductionAmount: 10,
            payoutAmount: 80,
            totalTaxableAmount: 80,
            totalTaxAmount: 14.4,
            customerBalanceBefore: 100,
            customerBalanceAfter: 10,
          ),
          warnings: [
            SaleReturnPreviewWarningDto(
              code: 'R01',
              message: 'Stock may need adjustment',
              severity: 'info',
            ),
            SaleReturnPreviewWarningDto(
              code: 'R02',
              message: 'High variance',
              severity: 'warn',
            ),
          ],
        );

        final model = SaleReturnMapper.toDomain(dto);

        expect(model.saleId, 'sale-abc');
        expect(model.hasFinancialAccess, isTrue);
        expect(model.financial?.totalRefundAmount, 90);
        expect(model.lines, hasLength(1));
        expect(model.lines.first.itemId, 'prod-1');
        expect(model.lines.first.inventoryBatchId, 'batch-1');
        expect(model.lines.first.willRestock, isTrue);
        expect(model.warnings, ['Stock may need adjustment', 'High variance']);
      },
    );
  });
}
