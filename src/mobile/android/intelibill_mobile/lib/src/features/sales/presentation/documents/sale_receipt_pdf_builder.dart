import 'dart:typed_data';

import 'package:intelibill_mobile/src/core/formatting/currency_formatter.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';
import 'package:intelibill_mobile/src/shared/documents/document_page_format.dart';
import 'package:intelibill_mobile/src/shared/documents/filename_sanitizer.dart';
import 'package:intelibill_mobile/src/shared/documents/pdf_document_theme.dart';
import 'package:intl/intl.dart';
import 'package:pdf/pdf.dart';
import 'package:pdf/widgets.dart' as pw;

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
    'Line items:',
    ...sale.items.map(
      (item) =>
          '${item.name} - ${_quantity(item.quantity)} × ${_money(item.rate)} = ${_money(item.total)}',
    ),
    'Totals:',
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
    if (sale.discounts.isNotEmpty) 'Discounts:',
    ...sale.discounts.map(
      (discount) =>
          '${discount.type} • ${discount.value}: - ${_money(discount.amount)}',
    ),
    if (sale.settlements.isEmpty) 'No settlement records',
    if (sale.settlements.isNotEmpty) 'Payment split:',
    ...sale.settlements.map(
      (settlement) => '${settlement.method}: ${_money(settlement.amount)}',
    ),
    if (sale.creditNoteRedemptions.isNotEmpty) 'Redemptions:',
    ...sale.creditNoteRedemptions.map(
      (redemption) => '${redemption.code}: ${_money(redemption.amount)}',
    ),
  ].join('\n');

  Future<Uint8List> build(SaleDetail sale) async {
    final document = pw.Document();
    document.addPage(
      pw.MultiPage(
        pageFormat: DocumentPageFormat.mm80.pdfPageFormat.copyWith(
          height: PdfPageFormat.a4.height,
        ),
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

  List<pw.Widget> _lineItems(SaleDetail sale) => sale.items
      .map<pw.Widget>(
        (item) => pw.Row(
          children: [
            pw.Expanded(child: pw.Text(item.name)),
            pw.Text(
              '${_quantity(item.quantity)} × ${_money(item.rate)}',
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

  String _money(num value) => formatInr(value);

  String _quantity(num value) {
    final numeric = value.toDouble();
    return numeric % 1 == 0
        ? numeric.toStringAsFixed(0)
        : numeric.toStringAsFixed(2);
  }

  String _date(DateTime soldAt) {
    return DateFormat('dd MMM yyyy, h:mm a').format(soldAt.toLocal());
  }
}
