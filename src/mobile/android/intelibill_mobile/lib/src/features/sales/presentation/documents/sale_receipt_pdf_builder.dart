import 'dart:typed_data';

import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';
import 'package:intelibill_mobile/src/shared/documents/document_page_format.dart';
import 'package:intelibill_mobile/src/shared/documents/filename_sanitizer.dart';
import 'package:intelibill_mobile/src/shared/documents/pdf_document_theme.dart';
import 'package:intl/intl.dart';
import 'package:pdf/widgets.dart' as pw;
import 'package:pdf/pdf.dart';

class SaleReceiptPdfBuilder {
  String filenameFor(SaleDetail sale) => FilenameSanitizer.sanitize(
    'sale-receipt-${sale.invoiceNumber}.pdf',
  );

  String contentFor(SaleDetail sale) => [
    'Receipt',
    'Invoice: ${sale.invoiceNumber}',
    'Date: ${_date(sale.soldAt)}',
    if (sale.customerName != null && sale.customerName!.trim().isNotEmpty)
      'Customer: ${sale.customerName}',
    if (sale.customerPhone != null && sale.customerPhone!.trim().isNotEmpty)
      'Phone: ${sale.customerPhone}',
    'Line items',
    ...sale.items.map(_lineItemText),
    'Totals',
    'Before discount: ${_money(sale.totalBeforeDiscount)}',
    'Discount: - ${_money(sale.totalDiscountAmount)}',
    'Tax: ${_money(sale.totalTaxAmount)}',
    'Total: ${_money(sale.totalAmount)}',
    'Paid: ${_money(sale.paidAmount)}',
    if (sale.dueAmount > 0) 'Due: ${_money(sale.dueAmount)}',
    if (sale.creditNoteAppliedAmount > 0)
      'Credit note applied: ${_money(sale.creditNoteAppliedAmount)}',
    if (sale.dueReductionAmount > 0)
      'Due reduction: ${_money(sale.dueReductionAmount)}',
    if (sale.discounts.isNotEmpty) 'Discounts',
    ...sale.discounts.map(
      (discount) =>
          '${discount.type} ${discount.value}: - ${_money(discount.amount)}',
    ),
    'Payment split',
    if (sale.settlements.isEmpty) 'No settlement records',
    ...sale.settlements.map(
      (settlement) => '${settlement.method}: ${_money(settlement.amount)}',
    ),
    if (sale.creditNoteRedemptions.isNotEmpty) 'Redemptions',
    ...sale.creditNoteRedemptions.map(
      (redemption) => '${redemption.code}: ${_money(redemption.amount)}',
    ),
  ].join('\n');

  Future<Uint8List> build(SaleDetail sale) async {
    final document = pw.Document(compress: false);
    document.addPage(
      pw.MultiPage(
        pageFormat: _receiptPageFormat(sale),
        build: (_) => [
          pw.Text('Receipt', style: PdfDocumentTheme.title),
          pw.SizedBox(height: 12),
          pw.Text('Invoice: ${sale.invoiceNumber}'),
          pw.Text('Date: ${_date(sale.soldAt)}'),
          if (sale.customerName != null && sale.customerName!.trim().isNotEmpty)
            pw.Text('Customer: ${sale.customerName}'),
          if (sale.customerPhone != null &&
              sale.customerPhone!.trim().isNotEmpty)
            pw.Text('Phone: ${sale.customerPhone}'),
          pw.SizedBox(height: 12),
          pw.Text('Line items', style: PdfDocumentTheme.emphasis),
          ..._lineItems(sale),
          pw.SizedBox(height: 12),
          pw.Text('Totals', style: PdfDocumentTheme.emphasis),
          ..._totals(sale),
          if (sale.discounts.isNotEmpty) ...[
            pw.SizedBox(height: 12),
            pw.Text('Discounts', style: PdfDocumentTheme.emphasis),
            ..._discounts(sale),
          ],
          pw.SizedBox(height: 12),
          pw.Text('Payment split', style: PdfDocumentTheme.emphasis),
          if (sale.settlements.isNotEmpty)
            ..._settlements(sale)
          else
            pw.Text('No settlement records', style: PdfDocumentTheme.emphasis),
          if (sale.creditNoteRedemptions.isNotEmpty) ...[
            pw.SizedBox(height: 12),
            pw.Text('Redemptions', style: PdfDocumentTheme.emphasis),
            ..._redemptions(sale),
          ],
        ],
      ),
    );
    return document.save();
  }

  PdfPageFormat _receiptPageFormat(SaleDetail sale) {
    final rowEstimate =
        7 +
        sale.items.length +
        sale.discounts.length +
        sale.settlements.length +
        sale.creditNoteRedemptions.length +
        (sale.discounts.isNotEmpty ? 2 : 0) +
        (sale.creditNoteRedemptions.isNotEmpty ? 2 : 0) +
        (sale.settlements.isNotEmpty ? 1 : 0) +
        (sale.creditNoteAppliedAmount > 0 ? 1 : 0) +
        (sale.dueReductionAmount > 0 ? 1 : 0) +
        (sale.dueAmount > 0 ? 1 : 0);
    final estimatedHeight = (rowEstimate * 16.0) + 120;
    final compactHeight = estimatedHeight.clamp(120.0, 780.0);

    return DocumentPageFormat.mm80.pdfPageFormat.copyWith(
      height: compactHeight,
    );
  }

  List<pw.Widget> _lineItems(SaleDetail sale) => sale.items
      .map<pw.Widget>(
        (item) => pw.Row(
          children: [
            pw.Expanded(child: pw.Text(item.name)),
            pw.Text(
              '${_quantity(item.quantity)} × ${_rate(item.rate)}',
              textAlign: pw.TextAlign.right,
            ),
            pw.SizedBox(width: 8),
            pw.Text(_money(item.total), textAlign: pw.TextAlign.right),
          ],
        ),
      )
      .toList();

  List<pw.Widget> _totals(SaleDetail sale) => [
    _amountLine('Before discount', _money(sale.totalBeforeDiscount)),
    _amountLine('Discount', '- ${_money(sale.totalDiscountAmount)}'),
    _amountLine('Tax', _money(sale.totalTaxAmount)),
    _amountLine('Total', _money(sale.totalAmount)),
    _amountLine('Paid', _money(sale.paidAmount)),
    if (sale.dueAmount > 0) _amountLine('Due', _money(sale.dueAmount)),
    if (sale.creditNoteAppliedAmount > 0)
      _amountLine('Credit note applied', _money(sale.creditNoteAppliedAmount)),
    if (sale.dueReductionAmount > 0)
      _amountLine('Due reduction', _money(sale.dueReductionAmount)),
  ];

  List<pw.Widget> _discounts(SaleDetail sale) => sale.discounts
      .map<pw.Widget>(
        (discount) => _amountLine(
          '${discount.type} ${discount.value}',
          '- ${_money(discount.amount)}',
        ),
      )
      .toList();

  List<pw.Widget> _settlements(SaleDetail sale) => sale.settlements
      .map<pw.Widget>(
        (settlement) =>
            _amountLine(settlement.method, _money(settlement.amount)),
      )
      .toList();

  List<pw.Widget> _redemptions(SaleDetail sale) => sale.creditNoteRedemptions
      .map<pw.Widget>(
        (redemption) => _amountLine(redemption.code, _money(redemption.amount)),
      )
      .toList();

  pw.Widget _amountLine(String label, String value) => pw.Row(
    mainAxisAlignment: pw.MainAxisAlignment.spaceBetween,
    children: [
      pw.Text(label),
      pw.Text(value),
    ],
  );

  String _money(num value) => 'Rs ${value.toStringAsFixed(0)}';

  String _rate(num value) => value.toStringAsFixed(2);

  String _lineItemText(SaleDetailItem item) =>
      '${item.name} - ${_quantity(item.quantity)} × ${_rate(item.rate)} = ${_money(item.total)}';

  String _quantity(num value) {
    final numeric = value.toDouble();
    return (numeric % 1).abs() < 0.0000001
        ? numeric.toStringAsFixed(0)
        : numeric.toStringAsFixed(2);
  }

  String _date(DateTime soldAt) {
    return DateFormat('dd MMM yyyy, h:mm a').format(soldAt.toLocal());
  }
}
