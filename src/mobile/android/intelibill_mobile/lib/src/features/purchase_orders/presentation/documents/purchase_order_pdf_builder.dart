import 'dart:typed_data';

import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_line.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/shop_details.dart';
import 'package:intelibill_mobile/src/shared/documents/document_page_format.dart';
import 'package:intelibill_mobile/src/shared/documents/filename_sanitizer.dart';
import 'package:intelibill_mobile/src/shared/documents/pdf_document_theme.dart';
import 'package:pdf/pdf.dart';
import 'package:pdf/widgets.dart' as pw;

class PurchaseOrderPdfBuilder {
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
    ShopDetails? shop,
  ) async {
    final document = pw.Document();
    document.addPage(
      pw.MultiPage(
        pageFormat: DocumentPageFormat.a4.pdfPageFormat,
        header: (_) => _header(purchaseOrder, shop),
        footer: (context) => pw.Align(
          alignment: pw.Alignment.centerRight,
          child: pw.Text('Page ${context.pageNumber} of ${context.pagesCount}'),
        ),
        build: (_) => _body(purchaseOrder),
      ),
    );
    return document.save();
  }

  pw.Widget _header(
    PurchaseOrder purchaseOrder,
    ShopDetails? shop,
  ) => pw.Column(
    crossAxisAlignment: pw.CrossAxisAlignment.start,
    children: [
      pw.Text('PURCHASE ORDER', style: PdfDocumentTheme.title),
      if (shop != null) ...[
        pw.Text(shop.name, style: PdfDocumentTheme.emphasis),
        pw.Text(_shopAddress(shop)),
        if (shop.mobileNumber != null) pw.Text('Phone: ${shop.mobileNumber}'),
        if (shop.gstNumber != null) pw.Text('GST: ${shop.gstNumber}'),
      ],
      pw.SizedBox(height: 12),
      pw.Text('Order: ${purchaseOrder.purchaseOrderNumber}'),
    ],
  );

  List<pw.Widget> _body(PurchaseOrder order) => [
    pw.SizedBox(height: 12),
    pw.Text('Supplier: ${order.supplierName ?? 'Unavailable'}'),
    if (order.supplierReferenceNumber != null)
      pw.Text('Supplier reference: ${order.supplierReferenceNumber}'),
    if (order.supplierReference != null)
      pw.Text('Supplier account: ${order.supplierReference}'),
    pw.Text('Status: ${order.status.wireValue}'),
    pw.Text('Created: ${_date(order.createdAt)}'),
    if (order.orderDate != null)
      pw.Text('Order date: ${_date(order.orderDate!)}'),
    if (order.expectedDeliveryDate != null)
      pw.Text(
        'Expected delivery: ${_date(order.expectedDeliveryDate!)}',
      ),
    pw.SizedBox(height: 16),
    _lineTable(order.lines),
    pw.SizedBox(height: 12),
    pw.Align(
      alignment: pw.Alignment.centerRight,
      child: pw.Text(
        'Expected total: ${_money(order.expectedTotal)}',
        style: PdfDocumentTheme.emphasis,
      ),
    ),
    if (order.notes != null && order.notes!.trim().isNotEmpty) ...[
      pw.SizedBox(height: 16),
      PdfDocumentTheme.sectionLabel('Notes'),
      pw.Text(order.notes!),
    ],
  ];

  pw.Widget _lineTable(List<PurchaseOrderLine> lines) => pw.Table(
    border: pw.TableBorder.all(color: PdfColor.fromInt(0xffcccccc)),
    children: [
      _row([
        'Description',
        'Expected',
        'Received',
        'Remaining',
        'Unit cost',
        'Total',
      ], bold: true),
      ...lines.map(
        (line) => _row([
          line.description,
          '${line.expectedQuantity}',
          '${line.receivedQuantity}',
          '${line.remainingQuantity}',
          _money(line.unitCost),
          _money(line.lineTotal),
        ]),
      ),
    ],
  );

  pw.TableRow _row(List<String> cells, {bool bold = false}) => pw.TableRow(
    children: cells
        .map(
          (cell) => pw.Padding(
            padding: const pw.EdgeInsets.all(4),
            child: pw.Text(
              cell,
              style: bold ? PdfDocumentTheme.emphasis : null,
            ),
          ),
        )
        .toList(),
  );

  String _shopAddress(ShopDetails shop) =>
      '${shop.address}, ${shop.city}, ${shop.state} ${shop.pincode}';

  String _money(double value) => 'INR ${value.toStringAsFixed(2)}';

  String _date(DateTime value) =>
      '${value.day.toString().padLeft(2, '0')}/${value.month.toString().padLeft(2, '0')}/${value.year}';
}
