import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/sales/data/dto/sales_history_response_dto.dart';

void main() {
  group('SalesHistoryResponseDto', () {
    test('parses payment method from string enum wire format', () {
      final dto = SalesHistoryResponseDto.fromJson({
        'items': [
          {
            'saleId': 'sale-1',
            'invoiceNumber': 'INV-001',
            'customerId': null,
            'paymentMethod': 'Cash',
            'soldAt': '2026-05-11T10:30:00.000Z',
            'paidAmount': 500,
            'dueAmount': 0,
            'totalBeforeDiscount': 500,
            'totalDiscountAmount': 0,
            'totalAmount': 500,
            'totalTaxAmount': 50,
            'customerName': 'John',
            'customerPhone': null,
            'itemCount': 2,
            'returnNumbers': <String>[],
            'status': 'paid',
            'refundAmount': 0,
            'dueReductionAmount': 0,
          },
        ],
        'totalCount': 1,
        'pageNumber': 1,
        'pageSize': 20,
        'summary': {
          'periodSales': 500,
          'invoiceCount': 1,
          'refundAmount': 0,
        },
      });

      expect(dto.items.first.paymentMethod, 1);
    });

    test('parses paginated sales history response', () {
      final dto = SalesHistoryResponseDto.fromJson({
        'items': [
          {
            'saleId': 'sale-1',
            'invoiceNumber': 'INV-001',
            'customerId': null,
            'paymentMethod': 1,
            'soldAt': '2026-05-11T10:30:00.000Z',
            'paidAmount': 500,
            'dueAmount': 0,
            'totalBeforeDiscount': 500,
            'totalDiscountAmount': 0,
            'totalAmount': 500,
            'totalTaxAmount': 50,
            'customerName': 'John',
            'customerPhone': null,
            'itemCount': 2,
            'returnNumbers': <String>[],
            'status': 'paid',
            'refundAmount': 0,
            'dueReductionAmount': 0,
          },
        ],
        'totalCount': 1,
        'pageNumber': 1,
        'pageSize': 20,
        'summary': {
          'periodSales': 500,
          'invoiceCount': 1,
          'refundAmount': 0,
        },
      });

      expect(dto.items.length, 1);
      expect(dto.items.first.invoiceNumber, 'INV-001');
      expect(dto.summary.periodSales, 500);
    });
  });
}
