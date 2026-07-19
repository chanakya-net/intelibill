import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_line.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/shop_details.dart';
import 'package:intelibill_mobile/src/shared/documents/document_page_format.dart';
import 'package:intelibill_mobile/src/shared/documents/filename_sanitizer.dart';
import 'package:intelibill_mobile/src/shared/documents/pdf/document_font_resolver.dart';
import 'package:intelibill_mobile/src/shared/documents/pdf_document_formatters.dart';
import 'package:intelibill_mobile/src/shared/documents/pdf_document_theme.dart';
import 'package:pdf/pdf.dart';
import 'package:pdf/widgets.dart' as pw;

class PurchaseOrderPdfBuilder {
  PurchaseOrderPdfBuilder({DocumentFontResolver? fontResolver})
    : _fontResolver = fontResolver ?? DocumentFontResolver();

  final DocumentFontResolver _fontResolver;

  String filenameFor(PurchaseOrder purchaseOrder) => FilenameSanitizer.sanitize(
    'purchase-order-${purchaseOrder.purchaseOrderNumber}.pdf',
  );

  String contentFor(PurchaseOrder purchaseOrder, ShopDetails? shop) => [
    'Purchase Order',
    if (shop != null) ...[
      _shopAddress(shop),
      shop.mobileNumber,
      shop.gstNumber,
    ],
    purchaseOrder.purchaseOrderNumber,
    purchaseOrder.supplierName,
    purchaseOrder.supplierReferenceNumber,
    purchaseOrder.notes,
    ...purchaseOrder.lines.map((line) => line.description),
    'Expected total',
  ].whereType<String>().join('\n');

  Future<Uint8List> build(
    PurchaseOrder purchaseOrder,
    ShopDetails? shop, {
    Locale locale = pdfDefaultLocale,
  }) async {
    final theme = PdfDocumentTheme(await _fontResolver.resolve(locale));
    final document = pw.Document(
      title: 'Purchase Order',
      author: 'Intelibill',
      creator: 'Intelibill Mobile',
      subject: locale.toLanguageTag(),
    );
    document.addPage(
      pw.MultiPage(
        theme: theme.data,
        pageFormat: DocumentPageFormat.a4.pdfPageFormat,
        header: (_) => _header(theme, purchaseOrder, shop),
        footer: (context) => pw.Align(
          alignment: pw.Alignment.centerRight,
          child: pw.Text('Page ${context.pageNumber} of ${context.pagesCount}'),
        ),
        build: (_) => _body(theme, purchaseOrder, locale),
      ),
    );
    return document.save();
  }

  pw.Widget _header(
    PdfDocumentTheme theme,
    PurchaseOrder purchaseOrder,
    ShopDetails? shop,
  ) => pw.Column(
    crossAxisAlignment: pw.CrossAxisAlignment.start,
    children: [
      pw.Text('PURCHASE ORDER', style: theme.title),
      if (shop != null) ...[
        pw.Text(shop.name, style: theme.emphasis),
        pw.Text(_shopAddress(shop)),
        if (shop.mobileNumber != null) pw.Text('Phone: ${shop.mobileNumber}'),
        if (shop.gstNumber != null) pw.Text('GST: ${shop.gstNumber}'),
      ],
      pw.SizedBox(height: 12),
      pw.Text('Order: ${purchaseOrder.purchaseOrderNumber}'),
    ],
  );

  List<pw.Widget> _body(
    PdfDocumentTheme theme,
    PurchaseOrder order,
    Locale locale,
  ) => [
    pw.SizedBox(height: 12),
    pw.Text('Supplier: ${order.supplierName ?? 'Unavailable'}'),
    if (order.supplierReferenceNumber != null)
      pw.Text('Supplier reference: ${order.supplierReferenceNumber}'),
    if (order.supplierReference != null)
      pw.Text('Supplier account: ${order.supplierReference}'),
    pw.Text('Status: ${order.status.wireValue}'),
    pw.Text('Created: ${_date(order.createdAt, locale)}'),
    if (order.orderDate != null)
      pw.Text('Order date: ${_date(order.orderDate!, locale)}'),
    if (order.expectedDeliveryDate != null)
      pw.Text(
        'Expected delivery: ${_date(order.expectedDeliveryDate!, locale)}',
      ),
    pw.SizedBox(height: 16),
    _lineTable(theme, order.lines, locale),
    pw.SizedBox(height: 12),
    pw.Align(
      alignment: pw.Alignment.centerRight,
      child: pw.Text(
        'Expected total: ${_money(order.expectedTotal, locale)}',
        style: theme.emphasis,
      ),
    ),
    if (order.notes != null && order.notes!.trim().isNotEmpty) ...[
      pw.SizedBox(height: 16),
      theme.sectionLabel('Notes'),
      pw.Text(order.notes!),
    ],
  ];

  pw.Widget _lineTable(
    PdfDocumentTheme theme,
    List<PurchaseOrderLine> lines,
    Locale locale,
  ) => pw.Table(
    border: pw.TableBorder.all(color: PdfColor.fromInt(0xffcccccc)),
    children: [
      _row(theme, [
        'Description',
        'Expected',
        'Received',
        'Remaining',
        'Unit cost',
        'Total',
      ], bold: true),
      ...lines.map(
        (line) => _row(theme, [
          line.description,
          _decimal(line.expectedQuantity, locale),
          _decimal(line.receivedQuantity, locale),
          _decimal(line.remainingQuantity, locale),
          _money(line.unitCost, locale),
          _money(line.lineTotal, locale),
        ]),
      ),
    ],
  );

  pw.TableRow _row(
    PdfDocumentTheme theme,
    List<String> cells, {
    bool bold = false,
  }) => pw.TableRow(
    children: cells
        .map(
          (cell) => pw.Padding(
            padding: const pw.EdgeInsets.all(4),
            child: pw.Text(
              cell,
              style: bold ? theme.emphasis : null,
            ),
          ),
        )
        .toList(),
  );

  String _shopAddress(ShopDetails shop) =>
      '${shop.address}, ${shop.city}, ${shop.state} ${shop.pincode}';

  String _money(double value, Locale locale) =>
      formatPdfInr(value, locale: locale);

  String _decimal(num value, Locale locale) =>
      formatPdfDecimal(value, locale: locale, decimalDigits: 0);

  String _date(DateTime value, Locale locale) =>
      formatPdfDate(value, locale: locale);
}
