import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/documents/sale_receipt_pdf_builder.dart';
import 'package:intl/intl.dart';

void main() {
  final detail = _receipt();

  test('filename follows sale-receipt-{invoiceNumber}.pdf', () {
    final builder = SaleReceiptPdfBuilder();
    expect(builder.filenameFor(detail), 'sale-receipt-INV-REC-001.pdf');
  });

  test('contentFor includes every receipt value', () {
    final builder = SaleReceiptPdfBuilder();
    final content = builder.contentFor(detail);

    expect(content, contains('Receipt'));
    expect(content, contains('INV-REC-001'));
    expect(
      content,
      contains(
        'Date: ${DateFormat('dd MMM yyyy, h:mm a').format(detail.soldAt)}',
      ),
    );
    expect(content, contains('Alice'));
    expect(content, contains('9999999999'));
    expect(content, contains('Notebook'));
    expect(content, contains('2 × ₹50'));
    expect(content, contains('Cash'));
    expect(content, contains('₹300'));
    expect(content, contains('₹560'));
    expect(content, contains('₹36'));
    expect(content, contains('₹546'));
    expect(content, contains('Discount'));
    expect(content, contains('Flat • 50'));
    expect(content, contains('Credit note applied'));
    expect(content, contains('₹36'));
    expect(content, contains('CN-LOYALTY-001'));
    expect(content, contains('Due: ₹12'));
    expect(content, contains('Before discount'));
    expect(content, contains('Cash'));
  });

  test('builds 80 mm PDF bytes with complete content', () async {
    final builder = SaleReceiptPdfBuilder();
    final bytes = await builder.build(detail);

    expect(bytes, isNotEmpty);
    expect(bytes.take(4), orderedEquals('%PDF'.codeUnits));
    expect(builder.contentFor(detail), contains('Pen'));
  });
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
        value: '50',
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
    creditNoteAppliedAmount: 36,
  );
}
