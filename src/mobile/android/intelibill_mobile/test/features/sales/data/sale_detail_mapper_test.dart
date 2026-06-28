import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/sales/data/dto/sale_detail_dto.dart';
import 'package:intelibill_mobile/src/features/sales/data/mappers/sale_detail_mapper.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';

void main() {
  group('SaleDetailMapper', () {
    test('maps full sale detail JSON into domain model', () {
      final dto = SaleDetailDto.fromJson(_fullSaleDetailJson());
      final domain = SaleDetailMapper.toDomain(dto);

      expect(domain.saleId, 'sale-abc');
      expect(domain.invoiceNumber, 'INV-001');
      expect(
        domain.soldAt,
        DateTime.parse('2026-05-11T10:30:00.000Z').toLocal(),
      );
      expect(domain.creditNoteAppliedAmount, 50.0);
      expect(domain.warnings, ['W1']);
      expect(domain.items, hasLength(1));
      expect(domain.returns, hasLength(1));
      expect(domain.creditNoteRedemptions, hasLength(1));

      final item = domain.items.first;
      expect(item.saleItemId, 'item-1');
      expect(item.itemName, 'Widget A');
      expect(item.quantity, 2.0);
      expect(item.taxRatePercent, 10.0);
      expect(item.returnStatus, 'PartiallyReturned');

      final saleReturn = domain.returns.first;
      expect(saleReturn.saleReturnId, 'ret-1');
      expect(saleReturn.processedBy, 'user-1');
      expect(
        saleReturn.processedAt,
        DateTime.parse('2026-05-12T09:00:00.000Z').toLocal(),
      );
      expect(saleReturn.items.first.saleReturnItemId, 'retitem-1');
      expect(saleReturn.isVoided, isFalse);
      expect(saleReturn.voidedAt, isNull);
      expect(saleReturn.voidReason, isNull);

      final voided = SaleDetailMapper.toDomain(
        SaleDetailDto.fromJson(_voidedSaleDetailJson()),
      ).returns.first;
      expect(voided.isVoided, isTrue);
      expect(
        voided.voidedAt,
        DateTime.parse('2026-05-13T10:15:00.000Z').toLocal(),
      );
      expect(voided.voidReason, 'Duplicate return');

      final creditRedemption = domain.creditNoteRedemptions.first;
      expect(creditRedemption.code, 'CN-OLD');
      expect(creditRedemption.appliedAmount, 50.0);
    });

    test('makes semantic changes visible to Equatable', () {
      final warningsDiff = SaleDetail(
        saleId: 'sale-1',
        invoiceNumber: 'INV-1',
        paymentMethod: 1,
        soldAt: DateTime.parse('2026-05-11T10:30:00Z'),
        paidAmount: 500,
        dueAmount: 0,
        totalBeforeDiscount: 550,
        totalDiscountAmount: 50,
        totalAmount: 500,
        totalTaxAmount: 45,
        creditNoteAppliedAmount: 50,
        warnings: const ['warning'],
        customerName: 'John',
      );

      final creditNoteDiff = SaleDetail(
        saleId: 'sale-1',
        invoiceNumber: 'INV-1',
        paymentMethod: 1,
        soldAt: DateTime.parse('2026-05-11T10:30:00Z'),
        paidAmount: 500,
        dueAmount: 0,
        totalBeforeDiscount: 550,
        totalDiscountAmount: 50,
        totalAmount: 500,
        totalTaxAmount: 45,
        creditNoteAppliedAmount: 50,
        warnings: const ['warning'],
        returns: [
          SaleDetailReturn(
            saleReturnId: 'ret-1',
            returnNumber: 'R1',
            processedAt: DateTime.parse('2026-05-12T09:00:00Z'),
            processedBy: 'user',
            totalRefundAmount: 25,
            dueReductionAmount: 0,
            payoutAmount: 25,
            totalTaxableAmount: 20,
            totalTaxAmount: 5,
            items: const [],
          ),
        ],
        customerName: 'John',
      );

      expect(warningsDiff == creditNoteDiff, isFalse);
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

Map<String, dynamic> _voidedSaleDetailJson() => {
  'saleId': 'sale-voided',
  'invoiceNumber': 'INV-002',
  'customerId': 'cust-2',
  'customerName': 'Test User',
  'customerPhone': '+91-8888888888',
  'paymentMethod': 1,
  'soldAt': '2026-05-11T10:30:00.000Z',
  'paidAmount': 200.0,
  'dueAmount': 0.0,
  'totalBeforeDiscount': 250.0,
  'totalDiscountAmount': 50.0,
  'totalAmount': 200.0,
  'totalTaxAmount': 45.0,
  'returns': [
    {
      'saleReturnId': 'ret-2',
      'returnNumber': 'RET-002',
      'processedAt': '2026-05-13T10:00:00.000Z',
      'processedBy': 'manager-1',
      'isVoided': true,
      'voidedAt': '2026-05-13T10:15:00.000Z',
      'voidReason': 'Duplicate return',
      'notes': 'Customer dispute',
      'totalRefundAmount': 25.0,
      'dueReductionAmount': 0.0,
      'payoutAmount': 25.0,
      'payoutDestination': 'Cash',
      'totalTaxableAmount': 20.0,
      'totalTaxAmount': 5.0,
      'items': <Map<String, dynamic>>[],
    },
  ],
};
