import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/documents/sale_receipt_pdf_builder.dart';
import 'package:intl/intl.dart';
import 'package:pdf/pdf.dart';

void main() {
  final populated = _receipt();
  final emptySections = _receiptWithNoOptionalSections();

  test('filename follows sale-receipt-{invoiceNumber}.pdf', () {
    final builder = SaleReceiptPdfBuilder();

    expect(builder.filenameFor(populated), 'sale-receipt-INV-REC-001.pdf');
  });

  test('contentFor maps populated sale values', () {
    final builder = SaleReceiptPdfBuilder();
    final content = builder.contentFor(populated);

    expect(
      content,
      contains('Receipt'),
    );
    expect(content, contains('Invoice: INV-REC-001'));
    expect(
      content,
      contains(
        'Date: ${DateFormat('dd MMM yyyy, h:mm a').format(populated.soldAt)}',
      ),
    );
    expect(content, contains('Customer: Alice'));
    expect(content, contains('Phone: 9999999999'));
    expect(content, contains('Notebook - 2 × 50.00 = Rs 100'));
    expect(content, contains('Pen - 1.50 × 200.50 = Rs 301'));
    expect(content, contains('Before discount: Rs 560'));
    expect(content, contains('Discount: - Rs 50'));
    expect(content, contains('Tax: Rs 36'));
    expect(content, contains('Total: Rs 546'));
    expect(content, contains('Paid: Rs 300'));
    expect(content, contains('Due: Rs 12'));
    expect(content, contains('Credit note applied: Rs 36'));
    expect(content, contains('Due reduction: Rs 20'));
    expect(content, contains('Discounts'));
    expect(content, contains('Flat Rs 20: - Rs 50'));
    expect(content, contains('Payment split'));
    expect(content, contains('Cash: Rs 300'));
    expect(content, contains('Redemptions'));
    expect(content, contains('CN-LOYALTY-001: Rs 36'));
  });

  test(
    'contentFor keeps conditional sections when optional values are absent',
    () {
      final builder = SaleReceiptPdfBuilder();
      final content = builder.contentFor(emptySections);

      expect(content, contains('Payment split'));
      expect(content, contains('No settlement records'));
      expect(content, isNot(contains('Discounts')));
      expect(content, isNot(contains('Redemptions')));
      expect(content, isNot(contains('Credit note applied')));
      expect(content, isNot(contains('Due reduction')));
      expect(content, isNot(contains('Due:')));
    },
  );

  test(
    'builds 80 mm PDF bytes with compact page geometry and currency marker',
    () async {
      final builder = SaleReceiptPdfBuilder();
      final bytes = await builder.build(populated);
      final payload = latin1.decode(bytes, allowInvalid: true);
      final mediaBox = _extractMediaBox(payload);

      expect(bytes, isNotEmpty);
      expect(bytes.take(4), orderedEquals('%PDF'.codeUnits));
      expect(payload, isNot(contains('₹')));
      expect(mediaBox.width, closeTo(PdfPageFormat.roll80.width, 0.5));
      expect(mediaBox.height, greaterThan(0));
      expect(mediaBox.height, lessThan(PdfPageFormat.a4.height));
      expect(payload, contains('INV-REC-001'));
      expect(
        _containsTokenSequence(payload, const [
          'Notebook',
          '2',
          '×',
          '50.00',
          'Rs',
          '100',
        ]),
        isTrue,
      );
      expect(
        _containsTokenSequence(payload, const ['Payment', 'split']),
        isTrue,
      );
      expect(
        _containsTokenSequence(payload, const [
          'Discounts',
          'Flat',
          'Rs',
          '20',
          'Rs',
          '50',
        ]),
        isTrue,
      );
      expect(payload, isNot(contains('No settlement records')));
    },
  );
}

bool _containsTokenSequence(String text, List<String> tokens) {
  var index = -1;
  for (final token in tokens) {
    index = text.indexOf(token, index + 1);
    if (index == -1) {
      return false;
    }
  }

  return true;
}

_SaleReceiptMediaBox _extractMediaBox(String payload) {
  final match = RegExp(r'/MediaBox\s*\[(.*?)\]').firstMatch(payload);
  expect(match, isNotNull, reason: 'PDF payload should include a MediaBox');

  final boxValues = match!.group(1)!.trim().split(RegExp(r'\s+'));
  expect(boxValues, hasLength(4));

  final values = boxValues.map<double>(double.parse).toList();
  return _SaleReceiptMediaBox(
    left: values[0],
    bottom: values[1],
    right: values[2],
    top: values[3],
  );
}

class _SaleReceiptMediaBox {
  _SaleReceiptMediaBox({
    required this.left,
    required this.bottom,
    required this.right,
    required this.top,
  });

  final double left;
  final double bottom;
  final double right;
  final double top;

  double get width => right - left;
  double get height => top - bottom;
}

SaleDetail _receipt() {
  return SaleDetail(
    saleId: 'sale-100',
    invoiceNumber: 'INV-REC-001',
    customerId: 'cust-1',
    paymentMethod: 1,
    soldAt: DateTime(2026, 6, 10, 10),
    paidAmount: 300,
    dueAmount: 12,
    totalBeforeDiscount: 560,
    totalDiscountAmount: 50,
    totalAmount: 546,
    totalTaxAmount: 36,
    creditNoteAppliedAmount: 36,
    dueReductionAmount: 20,
    customerName: 'Alice',
    customerPhone: '9999999999',
    items: const [
      SaleDetailItem(
        saleItemId: 'item-1',
        lineType: 'Goods',
        lineCode: 'NB-1',
        itemName: 'Notebook',
        quantity: 2,
        salesPrice: 50,
        originalSalesPrice: 50,
        finalSalesPrice: 50,
        preTaxAmountBeforeDiscount: 100,
        itemDiscountAmount: 0,
        saleDiscountAmount: 0,
        taxableAmount: 100,
        taxAmount: 0,
        totalAmount: 100,
        savingsAmount: 0,
        taxRatePercent: 0,
        isPriceIncludingTax: false,
        hasPriceMismatch: false,
        returnedQuantity: 0,
        returnableQuantity: 0,
        returnStatus: 'none',
      ),
      SaleDetailItem(
        saleItemId: 'item-2',
        lineType: 'Goods',
        lineCode: 'PN-2',
        itemName: 'Pen',
        quantity: 1.5,
        salesPrice: 200.5,
        originalSalesPrice: 200.5,
        finalSalesPrice: 200.5,
        preTaxAmountBeforeDiscount: 300.75,
        itemDiscountAmount: 0,
        saleDiscountAmount: 0,
        taxableAmount: 300.75,
        taxAmount: 0,
        totalAmount: 300.75,
        savingsAmount: 0,
        taxRatePercent: 0,
        isPriceIncludingTax: false,
        hasPriceMismatch: false,
        returnedQuantity: 0,
        returnableQuantity: 0,
        returnStatus: 'none',
      ),
    ],
    settlements: [
      SaleDetailSettlement(
        settlementId: 'settlement-1',
        method: 'Cash',
        amount: 300,
        settledAt: DateTime.utc(2026, 6, 10, 11),
      ),
    ],
    discounts: const [
      SaleDetailDiscount(
        discountId: 'discount-1',
        type: 'Flat',
        value: '₹20',
        amount: 50,
      ),
    ],
    creditNoteRedemptions: const [
      SaleDetailCreditNoteRedemption(
        creditNoteId: 'cn-1',
        code: 'CN-LOYALTY-001',
        appliedAmount: 36,
      ),
    ],
    status: 'paid',
  );
}

SaleDetail _receiptWithNoOptionalSections() {
  return SaleDetail(
    saleId: 'sale-200',
    invoiceNumber: 'INV-REC-002',
    customerId: 'cust-2',
    paymentMethod: 1,
    soldAt: DateTime(2026, 6, 10, 10),
    paidAmount: 100,
    dueAmount: 0,
    totalBeforeDiscount: 100,
    totalDiscountAmount: 0,
    totalAmount: 100,
    totalTaxAmount: 0,
    customerName: 'Bob',
    customerPhone: '8888888888',
    items: const [
      SaleDetailItem(
        saleItemId: 'item-3',
        lineType: 'Goods',
        lineCode: 'NB-2',
        itemName: 'Pencil',
        quantity: 1,
        salesPrice: 100,
        originalSalesPrice: 100,
        finalSalesPrice: 100,
        preTaxAmountBeforeDiscount: 100,
        itemDiscountAmount: 0,
        saleDiscountAmount: 0,
        taxableAmount: 100,
        taxAmount: 0,
        totalAmount: 100,
        savingsAmount: 0,
        taxRatePercent: 0,
        isPriceIncludingTax: false,
        hasPriceMismatch: false,
        returnedQuantity: 0,
        returnableQuantity: 0,
        returnStatus: 'none',
      ),
    ],
    status: 'paid',
  );
}
