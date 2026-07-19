import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_line.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/localization/purchase_order_messages.dart';
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

  String contentFor(
    PurchaseOrder purchaseOrder,
    ShopDetails? shop,
    AppLocalizations l10n,
  ) => [
    l10n.purchaseOrderDocumentTitle,
    if (shop != null) ...[
      _shopAddress(shop),
      shop.mobileNumber,
      shop.gstNumber,
    ],
    purchaseOrder.purchaseOrderNumber,
    purchaseOrder.supplierName,
    purchaseOrder.supplierReferenceNumber,
    purchaseOrder.notes,
    purchaseOrderStatusMessage(l10n, purchaseOrder.status),
    ...purchaseOrder.lines.map((line) => line.description),
    l10n.purchaseOrderDocumentExpectedTotal(''),
  ].whereType<String>().join('\n');

  Future<Uint8List> build(
    PurchaseOrder purchaseOrder,
    ShopDetails? shop,
    AppLocalizations l10n, {
    Locale locale = pdfDefaultLocale,
  }) async {
    final theme = PdfDocumentTheme(await _fontResolver.resolve(locale));
    final document = pw.Document(
      title: l10n.purchaseOrderDocumentTitle,
      author: 'Intelibill',
      creator: 'Intelibill Mobile',
      subject: locale.toLanguageTag(),
    );
    document.addPage(
      pw.MultiPage(
        theme: theme.data,
        pageFormat: DocumentPageFormat.a4.pdfPageFormat,
        header: (_) => _header(theme, purchaseOrder, shop, l10n),
        footer: (context) => pw.Align(
          alignment: pw.Alignment.centerRight,
          child: pw.Text(
            l10n.purchaseOrderDocumentPageCount(
              context.pageNumber,
              context.pagesCount,
            ),
          ),
        ),
        build: (_) => _body(theme, purchaseOrder, locale, l10n),
      ),
    );
    return document.save();
  }

  pw.Widget _header(
    PdfDocumentTheme theme,
    PurchaseOrder purchaseOrder,
    ShopDetails? shop,
    AppLocalizations l10n,
  ) => pw.Column(
    crossAxisAlignment: pw.CrossAxisAlignment.start,
    children: [
      pw.Text(l10n.purchaseOrderDocumentHeading, style: theme.title),
      if (shop != null) ...[
        pw.Text(shop.name, style: theme.emphasis),
        pw.Text(_shopAddress(shop)),
        if (shop.mobileNumber != null)
          pw.Text(l10n.purchaseOrderDocumentPhone(shop.mobileNumber!)),
        if (shop.gstNumber != null)
          pw.Text(l10n.purchaseOrderDocumentGst(shop.gstNumber!)),
      ],
      pw.SizedBox(height: 12),
      pw.Text(
        l10n.purchaseOrderDocumentOrderNumber(
          purchaseOrder.purchaseOrderNumber,
        ),
      ),
    ],
  );

  List<pw.Widget> _body(
    PdfDocumentTheme theme,
    PurchaseOrder order,
    Locale locale,
    AppLocalizations l10n,
  ) => [
    pw.SizedBox(height: 12),
    pw.Text(
      l10n.purchaseOrderDocumentSupplier(
        order.supplierName ?? l10n.purchaseOrderDocumentUnavailable,
      ),
    ),
    if (order.supplierReferenceNumber != null)
      pw.Text(
        l10n.purchaseOrderDocumentSupplierReference(
          order.supplierReferenceNumber!,
        ),
      ),
    if (order.supplierReference != null)
      pw.Text(
        l10n.purchaseOrderDocumentSupplierAccount(order.supplierReference!),
      ),
    pw.Text(
      l10n.purchaseOrderDocumentStatus(
        purchaseOrderStatusMessage(l10n, order.status),
      ),
    ),
    pw.Text(
      l10n.purchaseOrderDocumentCreated(_date(order.createdAt, locale)),
    ),
    if (order.orderDate != null)
      pw.Text(
        l10n.purchaseOrderDocumentOrderDate(_date(order.orderDate!, locale)),
      ),
    if (order.expectedDeliveryDate != null)
      pw.Text(
        l10n.purchaseOrderDocumentExpectedDelivery(
          _date(order.expectedDeliveryDate!, locale),
        ),
      ),
    pw.SizedBox(height: 16),
    _lineTable(theme, order.lines, locale, l10n),
    pw.SizedBox(height: 12),
    pw.Align(
      alignment: pw.Alignment.centerRight,
      child: pw.Text(
        l10n.purchaseOrderDocumentExpectedTotal(
          _money(order.expectedTotal, locale),
        ),
        style: theme.emphasis,
      ),
    ),
    if (order.notes != null && order.notes!.trim().isNotEmpty) ...[
      pw.SizedBox(height: 16),
      theme.sectionLabel(l10n.purchaseOrderDocumentNotes),
      pw.Text(order.notes!),
    ],
  ];

  pw.Widget _lineTable(
    PdfDocumentTheme theme,
    List<PurchaseOrderLine> lines,
    Locale locale,
    AppLocalizations l10n,
  ) => pw.Table(
    border: pw.TableBorder.all(color: const PdfColor.fromInt(0xffcccccc)),
    children: [
      _row(theme, [
        l10n.purchaseOrderDocumentDescription,
        l10n.purchaseOrderDocumentExpected,
        l10n.purchaseOrderDocumentReceived,
        l10n.purchaseOrderDocumentRemaining,
        l10n.purchaseOrderDocumentUnitCost,
        l10n.purchaseOrderDocumentTotal,
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
