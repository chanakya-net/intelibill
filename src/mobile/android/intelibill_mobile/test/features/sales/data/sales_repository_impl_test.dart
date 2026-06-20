import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/sales/data/data_sources/sales_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/sales/data/dto/sale_detail_dto.dart';
import 'package:intelibill_mobile/src/features/sales/data/repositories/sales_repository_impl.dart';
import 'package:mocktail/mocktail.dart';

class MockSalesRemoteDataSource extends Mock implements SalesRemoteDataSource {}

void main() {
  late MockSalesRemoteDataSource remoteDataSource;
  late SalesRepositoryImpl repository;

  setUp(() {
    remoteDataSource = MockSalesRemoteDataSource();
    repository = SalesRepositoryImpl(remoteDataSource);
  });

  group('SalesRepositoryImpl', () {
    test('maps full sale detail response to domain entity', () async {
      when(() => remoteDataSource.getSaleDetail('sale-abc')).thenAnswer(
        (_) async => SaleDetailDto.fromJson(_fullSaleDetailJson()),
      );

      final result = await repository.getSaleDetail('sale-abc');

      expect(result.warnings, ['W1']);
      expect(result.items, hasLength(1));
      expect(result.items.first.itemId, 'prod-1');
      expect(result.returns, hasLength(1));
      expect(result.returns.first.creditNote, isNotNull);
      expect(result.returns.first.creditNote!.availableBalance, 100.0);
      expect(result.creditNoteRedemptions.first.appliedAmount, 50.0);

      verify(() => remoteDataSource.getSaleDetail('sale-abc')).called(1);
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
