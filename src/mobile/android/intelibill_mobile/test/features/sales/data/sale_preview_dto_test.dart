import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/sales/data/dto/sale_preview_dto.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_preview.dart';

void main() {
  group('SalePreviewDto', () {
    test('parses preview response with totals, rule, infos, and warnings', () {
      final dto = SalePreviewResponseDto.fromJson(_previewResponseJson());

      expect(dto.totalAmount, 236.0);
      expect(dto.totalTaxableAmount, 200.0);
      expect(dto.totalTaxAmount, 36.0);
      expect(dto.totalDiscountAmount, 14.0);
      expect(dto.saleLevelEligibleSubtotal, 120.0);
      expect(dto.configuredSaleRule, isNotNull);
      expect(dto.configuredSaleRule!.ruleType, 'SalePercentage');
      expect(dto.lines, hasLength(2));
      expect(dto.lines.first.lineType, 'Goods');
      expect(dto.lines.first.hasClientPriceMismatch, isTrue);
      expect(dto.lines.last.lineType, 'Service');
      expect(dto.infos, hasLength(1));
      expect(dto.infos.first.code, 'sale_preview.info.configured_rule_applied');
      expect(dto.warnings, hasLength(2));
      expect(dto.warnings.first.severity, 'warning');
    });

    test('serializes mixed goods and service preview request', () {
      final request = SalePreviewRequestDto.fromDomain(
        const PreviewSaleRequest(
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
            PreviewSaleItemRequest(
              inventoryBatchId: '00000000-0000-0000-0000-000000000000',
              barcode: 'SRV-1',
              batchNumber: '',
              itemName: 'Installation',
              quantity: 1,
              costPrice: 0,
              salesPrice: 150,
              mrp: 150,
              taxRatePercent: 18,
              isPriceIncludingTax: false,
              itemDiscountType: 0,
              itemDiscountValue: 0,
              clientLineKey: 'line-2',
              lineType: 'Service',
              serviceId: 'svc-1',
            ),
          ],
        ),
      );

      expect(request.toJson(), {
        'saleDiscount': {'type': 0, 'value': 0},
        'items': [
          {
            'inventoryBatchId': 'batch-1',
            'barcode': 'BAR-1',
            'batchNumber': 'BN-1',
            'itemName': 'Rice',
            'quantity': 2.0,
            'costPrice': 48.0,
            'salesPrice': 60.0,
            'mrp': 62.0,
            'taxRatePercent': 12.0,
            'isPriceIncludingTax': false,
            'itemDiscount': {'type': 0, 'value': 0},
            'clientLineKey': 'line-1',
            'lineType': 'Goods',
          },
          {
            'inventoryBatchId': '00000000-0000-0000-0000-000000000000',
            'barcode': 'SRV-1',
            'batchNumber': '',
            'itemName': 'Installation',
            'quantity': 1.0,
            'costPrice': 0.0,
            'salesPrice': 150.0,
            'mrp': 150.0,
            'taxRatePercent': 18.0,
            'isPriceIncludingTax': false,
            'itemDiscount': {'type': 0, 'value': 0},
            'clientLineKey': 'line-2',
            'lineType': 'Service',
            'serviceId': 'svc-1',
          },
        ],
      });
    });
  });
}

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
    {
      'code': 'sale_preview.warning.validation',
      'message': 'Sale-level discount is limited by configured rule.',
      'severity': 'info',
      'inventoryBatchId': null,
      'clientLineKey': null,
    },
  ],
};
