import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/sales/data/data_sources/sales_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/sales/data/dto/sale_detail_dto.dart';
import 'package:intelibill_mobile/src/features/sales/data/dto/sale_preview_dto.dart';
import 'package:intelibill_mobile/src/features/sales/data/dto/sale_return_preview_dto.dart';
import 'package:intelibill_mobile/src/features/sales/data/repositories/sales_repository_impl.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_preview.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_return.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/record_sale.dart';
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

    test('maps sale return preview response to domain model', () async {
      when(
        () => remoteDataSource.previewSaleReturn(
          saleId: 'sale-abc',
          request: any(named: 'request'),
        ),
      ).thenAnswer(
        (_) async => SaleReturnPreviewResponseDto(
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
            ),
          ],
          financial: const SaleReturnPreviewFinancialDto(
            totalRefundAmount: 90,
            dueReductionAmount: 0,
            payoutAmount: 90,
            totalTaxableAmount: 80,
            totalTaxAmount: 14.4,
          ),
          warnings: const [
            SaleReturnPreviewWarningDto(
              code: 'R01',
              message: 'Stock may need adjustment',
              severity: 'info',
            ),
          ],
        ),
      );

      final result = await repository.previewSaleReturn(
        saleId: 'sale-abc',
        request: const PreviewSaleReturnRequest(
          dueReductionOverrideAmount: 5,
          items: [
            SaleReturnLineDraft(
              saleItemId: 'item-1',
              selected: true,
              quantity: 1,
              condition: 1,
              approvedRefundAmount: 90,
            ),
          ],
        ),
      );

      expect(result.saleId, 'sale-abc');
      expect(result.financial?.payoutAmount, 90);
      expect(result.warnings, ['Stock may need adjustment']);
    });

    test('maps sale preview response to domain model', () async {
      when(
        () => remoteDataSource.previewSale(
          request: any(named: 'request'),
        ),
      ).thenAnswer(
        (_) async => SalePreviewResponseDto.fromJson(_previewResponseJson()),
      );

      final result = await repository.previewSale(
        request: const PreviewSaleRequest(
          saleDiscountType: 0,
          saleDiscountValue: 0,
          items: [
            PreviewSaleItemRequest(
              inventoryBatchId: 'batch-1',
              barcode: 'BAR-1',
              batchNumber: 'BN-1',
              itemName: 'Rice',
              quantity: 2,
              costPrice: 48,
              salesPrice: 60,
              mrp: 62,
              taxRatePercent: 12,
              isPriceIncludingTax: false,
              itemDiscountType: 0,
              itemDiscountValue: 0,
              clientLineKey: 'line-1',
              lineType: 'Goods',
            ),
          ],
        ),
      );

      expect(result.totalAmount, 236);
      expect(result.configuredSaleRule?.ruleType, 'SalePercentage');
      expect(result.lines, hasLength(2));
      expect(result.infos, hasLength(1));
      expect(result.warnings, hasLength(1));
    });

    test('maps record sale return response to sale detail', () async {
      when(
        () => remoteDataSource.recordSaleReturn(
          saleId: 'sale-abc',
          request: any(named: 'request'),
        ),
      ).thenAnswer(
        (_) async => SaleDetailDto.fromJson(_minimalSaleDetailJson()),
      );

      final result = await repository.recordSaleReturn(
        saleId: 'sale-abc',
        request: const RecordSaleReturnRequest(
          payoutDestination: 2,
          dueReductionOverrideAmount: 5,
          items: [
            SaleReturnLineDraft(
              saleItemId: 'item-1',
              selected: true,
              quantity: 1,
              approvedRefundAmount: 90,
            ),
          ],
        ),
      );

      expect(result.saleId, 'sale-abc');
      verify(
        () => remoteDataSource.recordSaleReturn(
          saleId: 'sale-abc',
          request: any(named: 'request'),
        ),
      ).called(1);
    });

    test('forwards void sale return request to remote data source', () async {
      when(
        () => remoteDataSource.voidSaleReturn(
          saleReturnId: 'return-1',
          reason: 'Damaged',
        ),
      ).thenAnswer((_) async {});

      await repository.voidSaleReturn(
        saleReturnId: 'return-1',
        reason: 'Damaged',
      );

      verify(
        () => remoteDataSource.voidSaleReturn(
          saleReturnId: 'return-1',
          reason: 'Damaged',
        ),
      ).called(1);
    });

    test(
      'maps recorded sale response and forwards payload to remote source',
      () async {
        late Map<String, dynamic> sentRequest;

        when(
          () => remoteDataSource.recordSale(
            request: any<Map<String, dynamic>>(named: 'request'),
          ),
        ).thenAnswer(
          (invocation) async {
            sentRequest =
                invocation.namedArguments[#request] as Map<String, dynamic>;
            return SaleDetailDto.fromJson(_minimalSaleDetailJson());
          },
        );

        final sale = await repository.recordSale(request: _recordSaleRequest());

        expect(sale.saleId, 'sale-abc');
        expect(sentRequest['paymentMethod'], 1);
        expect(sentRequest['idempotencyKey'], 'new-sale-record-001');
        expect(sentRequest['customerName'], 'John Doe');
        expect(
          sentRequest['items'],
          isA<List>(),
        );
        final items = sentRequest['items'] as List<dynamic>;
        expect(items, hasLength(2));
        expect(items[0]['lineType'], 'Goods');
        expect(items[1]['lineType'], 'Service');
        expect(items[1]['serviceId'], 'svc-1');
        expect(sale.totalAmount, 500.0);
      },
    );
  });
}

RecordSaleRequest _recordSaleRequest() {
  return RecordSaleRequest(
    idempotencyKey: 'new-sale-record-001',
    customerId: 'cust-1',
    customerName: 'John Doe',
    customerPhone: '+91-9999999999',
    paymentMethod: 1,
    paidAmount: 500.0,
    dueAmount: 0.0,
    items: const [
      RecordSaleLineRequest(
        barcode: 'BAR-1',
        batchNumber: 'BN-1',
        itemName: 'Rice',
        quantity: 2.0,
        costPrice: 48.0,
        salesPrice: 60.0,
        mrp: 62.0,
        taxRatePercent: 12.0,
        isPriceIncludingTax: false,
        inventoryBatchId: 'batch-1',
        clientLineKey: 'line-1',
        lineType: 'Goods',
        itemDiscount: RecordSaleLineDiscountRequest(type: 0, value: 0),
      ),
      RecordSaleLineRequest(
        barcode: 'SRV-1',
        batchNumber: '',
        itemName: 'Installation',
        quantity: 1.0,
        costPrice: 0.0,
        salesPrice: 150.0,
        mrp: 150.0,
        taxRatePercent: 18.0,
        isPriceIncludingTax: false,
        inventoryBatchId: '00000000-0000-0000-0000-000000000000',
        clientLineKey: 'line-2',
        lineType: 'Service',
        itemDiscount: RecordSaleLineDiscountRequest(type: 0, value: 0),
        serviceId: 'svc-1',
      ),
    ],
    saleDiscount: const RecordSaleLineDiscountRequest(type: 0, value: 0),
  );
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

Map<String, dynamic> _minimalSaleDetailJson() => {
  'saleId': 'sale-abc',
  'invoiceNumber': 'INV-001',
  'paymentMethod': 1,
  'soldAt': '2026-05-11T10:30:00.000Z',
  'paidAmount': 0.0,
  'dueAmount': 0.0,
  'totalBeforeDiscount': 0.0,
  'totalDiscountAmount': 0.0,
  'totalAmount': 500.0,
  'totalTaxAmount': 0.0,
};

Map<String, dynamic> _previewResponseJson() => {
  'totalAmount': 236.0,
  'totalTaxableAmount': 200.0,
  'totalTaxAmount': 36.0,
  'totalDiscountAmount': 14.0,
  'saleLevelEligibleSubtotal': 120.0,
  'configuredSaleRule': {
    'ruleId': 'rule-1',
    'ruleType': 'SalePercentage',
    'percentage': 10.0,
    'thresholdAmount': 100.0,
  },
  'lines': [
    {
      'lineType': 'Goods',
      'itemId': 'item-1',
      'serviceId': null,
      'barcode': 'BAR-1',
      'itemName': 'Rice',
      'inventoryBatchId': 'batch-1',
      'batchNumber': 'BN-1',
      'quantity': 2.0,
      'costPrice': 48.0,
      'salesPrice': 60.0,
      'mrp': 62.0,
      'taxRatePercent': 12.0,
      'isPriceIncludingTax': false,
      'preTaxAmountBeforeDiscount': 120.0,
      'itemDiscountAmount': 0.0,
      'saleDiscountAmount': 4.0,
      'taxableAmount': 116.0,
      'taxAmount': 13.92,
      'lineTotalAmount': 129.92,
      'maxAllowedItemDiscountFlat': 12.0,
      'maxAllowedItemDiscountPercent': 10.0,
      'configuredBatchRuleId': 'batch-rule-1',
      'configuredBatchRulePercentage': 5.0,
      'hasClientPriceMismatch': true,
      'clientLineKey': 'line-1',
    },
    {
      'lineType': 'Service',
      'itemId': null,
      'serviceId': 'svc-1',
      'barcode': 'SRV-1',
      'itemName': 'Installation',
      'inventoryBatchId': null,
      'batchNumber': null,
      'quantity': 1.0,
      'costPrice': 0.0,
      'salesPrice': 150.0,
      'mrp': 150.0,
      'taxRatePercent': 18.0,
      'isPriceIncludingTax': false,
      'preTaxAmountBeforeDiscount': 150.0,
      'itemDiscountAmount': 0.0,
      'saleDiscountAmount': 10.0,
      'taxableAmount': 140.0,
      'taxAmount': 25.2,
      'lineTotalAmount': 165.2,
      'maxAllowedItemDiscountFlat': 0.0,
      'maxAllowedItemDiscountPercent': 0.0,
      'configuredBatchRuleId': null,
      'configuredBatchRulePercentage': null,
      'hasClientPriceMismatch': false,
      'clientLineKey': 'line-2',
    },
  ],
  'infos': [
    {
      'code': 'sale_preview.info.configured_rule_applied',
      'message': 'Configured sale rule applied.',
    },
  ],
  'warnings': [
    {
      'code': 'sale_preview.warning.client_price_mismatch',
      'message': 'Client pricing differs from latest batch pricing.',
      'severity': 'warning',
      'inventoryBatchId': 'batch-1',
      'clientLineKey': 'line-1',
    },
  ],
};
