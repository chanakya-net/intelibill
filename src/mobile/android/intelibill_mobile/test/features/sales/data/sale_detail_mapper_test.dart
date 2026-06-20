import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/sales/data/dto/sale_detail_dto.dart';
import 'package:intelibill_mobile/src/features/sales/data/mappers/sale_detail_mapper.dart';

void main() {
  group('SaleDetailDtoX.toDomain', () {
    test('maps complete sale detail payload', () {
      final dto = SaleDetailDto.fromJson({
        'saleId': 'sale-1',
        'invoiceNumber': 'INV-2026-001',
        'customerId': null,
        'customerName': 'John Doe',
        'customerPhone': '9999999999',
        'paymentMethod': 1,
        'soldAt': '2026-05-11T10:00:00.000Z',
        'paidAmount': 500.0,
        'dueAmount': 0.0,
        'totalBeforeDiscount': 550.0,
        'totalDiscountAmount': 50.0,
        'totalAmount': 500.0,
        'totalTaxAmount': 18.0,
        'refundAmount': 22.0,
        'dueReductionAmount': 5.0,
        'items': [
          {
            'saleItemId': 'item-1',
            'itemName': 'Notebook',
            'quantity': 2.0,
            'salesPrice': 100.0,
            'taxRatePercent': 18.0,
            'totalAmount': 236.0,
          },
        ],
        'settlements': [
          {
            'settlementId': 'settlement-1',
            'method': 'Cash',
            'amount': 200.0,
            'settledAt': '2026-05-11T11:00:00.000Z',
          },
        ],
        'discounts': [
          {
            'discountId': 'discount-1',
            'type': 'Promo',
            'value': '10%',
            'amount': 20.0,
          },
        ],
        'returns': [
          {
            'saleReturnId': 'return-1',
            'returnNumber': 'RET-1',
            'items': [
              {
                'saleItemId': 'item-1',
                'itemName': 'Notebook',
                'quantity': 1.0,
                'approvedRefundAmount': 50.0,
              },
            ],
            'totalRefundAmount': 50.0,
            'processedAt': '2026-05-12T09:00:00.000Z',
          },
        ],
        'creditNoteRedemptions': [
          {
            'creditNoteId': 'redemption-1',
            'code': 'CN-001',
            'appliedAmount': 30.0,
          },
        ],
        'warnings': ['Stock low'],
        'status': 'partiallyPaid',
      });

      final detail = dto.toDomain();

      expect(detail.saleId, 'sale-1');
      expect(detail.invoiceNumber, 'INV-2026-001');
      expect(detail.customerName, 'John Doe');
      expect(detail.customerPhone, '9999999999');
      expect(detail.paymentMethod, 1);
      expect(detail.paidAmount, 500.0);
      expect(detail.dueAmount, 0.0);
      expect(detail.totalBeforeDiscount, 550.0);
      expect(detail.totalDiscountAmount, 50.0);
      expect(detail.totalAmount, 500.0);
      expect(detail.totalTaxAmount, 18.0);
      expect(detail.refundAmount, 22.0);
      expect(detail.dueReductionAmount, 5.0);
      expect(detail.status, 'partiallyPaid');
      expect(detail.warnings, ['Stock low']);

      expect(detail.items, hasLength(1));
      expect(detail.items.first.itemId, 'item-1');
      expect(detail.items.first.name, 'Notebook');
      expect(detail.items.first.quantity, 2.0);
      expect(detail.items.first.rate, 100.0);
      expect(detail.items.first.tax, 18.0);
      expect(detail.items.first.total, 236.0);

      expect(detail.settlements, hasLength(1));
      expect(detail.settlements.first.settlementId, 'settlement-1');
      expect(detail.settlements.first.method, 'Cash');
      expect(detail.settlements.first.amount, 200.0);

      expect(detail.discounts, hasLength(1));
      expect(detail.discounts.first.discountId, 'discount-1');
      expect(detail.discounts.first.type, 'Promo');
      expect(detail.discounts.first.value, '10%');
      expect(detail.discounts.first.amount, 20.0);

      expect(detail.returns, hasLength(1));
      expect(detail.returns.first.returnId, 'return-1');
      expect(detail.returns.first.returnNumber, 'RET-1');
      expect(detail.returns.first.amount, 50.0);
      expect(detail.returns.first.returnedAt.year, 2026);
      expect(detail.returns.first.items, hasLength(1));
      expect(detail.returns.first.items.first.itemId, 'item-1');
      expect(detail.returns.first.items.first.itemName, 'Notebook');
      expect(detail.returns.first.items.first.quantity, 1.0);
      expect(detail.returns.first.items.first.amount, 50.0);

      expect(detail.redemptions, hasLength(1));
      expect(detail.redemptions.first.redemptionId, 'redemption-1');
      expect(detail.redemptions.first.code, 'CN-001');
      expect(detail.redemptions.first.amount, 30.0);
    });

    test('maps null status as null in domain', () {
      final dto = SaleDetailDto.fromJson({
        'saleId': 'sale-2',
        'invoiceNumber': 'INV-2026-002',
        'paymentMethod': 1,
        'soldAt': '2026-05-11T10:00:00.000Z',
        'paidAmount': 500.0,
        'dueAmount': 0.0,
        'totalBeforeDiscount': 500.0,
        'totalDiscountAmount': 0.0,
        'totalAmount': 500.0,
        'totalTaxAmount': 18.0,
      });

      final detail = dto.toDomain();

      expect(detail.status, isNull);
    });
  });
}
