import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/sales/data/dto/sale_detail_dto.dart';

void main() {
  group('SaleDetailDto', () {
    test(
      'parses full sale detail with items, returns, and credit note redemptions',
      () {
        final dto = SaleDetailDto.fromJson(_fullSaleDetailJson());

        expect(dto.saleId, 'sale-abc');
        expect(dto.invoiceNumber, 'INV-001');
        expect(dto.customerId, 'cust-1');
        expect(dto.customerName, 'John Doe');
        expect(dto.paymentMethod, 1);
        expect(dto.paidAmount, 500.0);
        expect(dto.creditNoteAppliedAmount, 50.0);
        expect(dto.warnings, ['W1']);
      },
    );

    test('parses sale detail items with all price fields', () {
      final dto = SaleDetailDto.fromJson(_fullSaleDetailJson());
      final item = dto.items.first;

      expect(item.saleItemId, 'item-1');
      expect(item.lineType, 'Goods');
      expect(item.itemName, 'Widget A');
      expect(item.quantity, 2.0);
      expect(item.salesPrice, 100.0);
      expect(item.taxRatePercent, 10.0);
      expect(item.returnedQuantity, 1.0);
      expect(item.returnStatus, 'PartiallyReturned');
    });

    test('parses sale return with items and credit note', () {
      final dto = SaleDetailDto.fromJson(_fullSaleDetailJson());
      final ret = dto.returns.first;

      expect(ret.saleReturnId, 'ret-1');
      expect(ret.returnNumber, 'RET-001');
      expect(ret.totalRefundAmount, 100.0);
      expect(ret.creditNote, isNotNull);
      expect(ret.creditNote!.code, 'CN-001');
      expect(ret.items, hasLength(1));
      expect(ret.items.first.saleItemId, 'item-1');
    });

    test('parses credit note redemptions', () {
      final dto = SaleDetailDto.fromJson(_fullSaleDetailJson());

      expect(dto.creditNoteRedemptions, hasLength(1));
      expect(dto.creditNoteRedemptions.first.code, 'CN-OLD');
      expect(dto.creditNoteRedemptions.first.appliedAmount, 50.0);
    });

    test('defaults empty lists when returns and redemptions are absent', () {
      final dto = SaleDetailDto.fromJson({
        'saleId': 'sale-min',
        'invoiceNumber': 'INV-002',
        'paymentMethod': 1,
        'soldAt': '2026-05-11T10:30:00.000Z',
        'paidAmount': 200.0,
        'dueAmount': 0.0,
        'totalBeforeDiscount': 200.0,
        'totalDiscountAmount': 0.0,
        'totalAmount': 200.0,
        'totalTaxAmount': 20.0,
      });

      expect(dto.items, isEmpty);
      expect(dto.returns, isEmpty);
      expect(dto.creditNoteRedemptions, isEmpty);
      expect(dto.warnings, isEmpty);
      expect(dto.creditNoteAppliedAmount, 0.0);
    });

    test('parses paymentMethod from string enum', () {
      final dto = SaleDetailDto.fromJson({
        'saleId': 's',
        'invoiceNumber': 'I',
        'paymentMethod': 'Cash',
        'soldAt': '2026-05-11T10:30:00.000Z',
        'paidAmount': 0.0,
        'dueAmount': 0.0,
        'totalBeforeDiscount': 0.0,
        'totalDiscountAmount': 0.0,
        'totalAmount': 0.0,
        'totalTaxAmount': 0.0,
      });

      expect(dto.paymentMethod, 1);
    });
  });
}

Map<String, dynamic> _fullSaleDetailJson() => {
  'saleId': 'sale-abc',
  'invoiceNumber': 'INV-001',
  'customerId': 'cust-1',
  'customerName': 'John Doe',
  'customerPhone': '+91-9999999999',
  'paymentMethod': 1,
  'soldAt': '2026-05-11T10:30:00.000Z',
  'paidAmount': 500.0,
  'dueAmount': 0.0,
  'totalBeforeDiscount': 550.0,
  'totalDiscountAmount': 50.0,
  'totalAmount': 500.0,
  'totalTaxAmount': 45.0,
  'creditNoteAppliedAmount': 50.0,
  'warnings': ['W1'],
  'items': [
    {
      'saleItemId': 'item-1',
      'lineType': 'Goods',
      'itemId': 'prod-1',
      'inventoryBatchId': null,
      'serviceId': null,
      'lineCode': 'LC-01',
      'itemName': 'Widget A',
      'quantity': 2.0,
      'salesPrice': 100.0,
      'originalSalesPrice': 110.0,
      'finalSalesPrice': 100.0,
      'preTaxAmountBeforeDiscount': 220.0,
      'itemDiscountAmount': 20.0,
      'saleDiscountAmount': 0.0,
      'taxableAmount': 200.0,
      'taxAmount': 20.0,
      'totalAmount': 220.0,
      'savingsAmount': 20.0,
      'taxRatePercent': 10.0,
      'isPriceIncludingTax': false,
      'hasPriceMismatch': false,
      'hsnCode': '1234',
      'returnedQuantity': 1.0,
      'returnableQuantity': 1.0,
      'returnStatus': 'PartiallyReturned',
    },
  ],
  'returns': [
    {
      'saleReturnId': 'ret-1',
      'returnNumber': 'RET-001',
      'processedAt': '2026-05-12T09:00:00.000Z',
      'processedBy': 'user-1',
      'notes': 'Damaged',
      'totalRefundAmount': 100.0,
      'dueReductionAmount': 0.0,
      'payoutAmount': 100.0,
      'payoutDestination': 'Cash',
      'totalTaxableAmount': 90.0,
      'totalTaxAmount': 10.0,
      'creditNote': {
        'creditNoteId': 'cn-1',
        'code': 'CN-001',
        'originalAmount': 100.0,
        'availableBalance': 100.0,
        'expiresAt': null,
        'reason': 'Return',
      },
      'items': [
        {
          'saleReturnItemId': 'retitem-1',
          'saleItemId': 'item-1',
          'quantity': 1.0,
          'condition': 'Good',
          'approvedRefundAmount': 100.0,
          'taxableAmount': 90.0,
          'taxAmount': 10.0,
          'notes': null,
        },
      ],
    },
  ],
  'creditNoteRedemptions': [
    {
      'creditNoteId': 'cn-old-1',
      'code': 'CN-OLD',
      'appliedAmount': 50.0,
    },
  ],
};
