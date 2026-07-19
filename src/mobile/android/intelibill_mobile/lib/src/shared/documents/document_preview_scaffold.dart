import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/shared/documents/document_descriptor.dart';
import 'package:pdf/pdf.dart';
import 'package:printing/printing.dart';

typedef DocumentBytesBuilder = Future<Uint8List> Function(PdfPageFormat format);
typedef DocumentActionCallback = Future<void> Function(Uint8List bytes);
typedef DocumentFailureCallback = void Function(String message);

class DocumentPreviewScaffold extends StatefulWidget {
  const DocumentPreviewScaffold({
    required this.descriptor,
    required this.onBuild,
    this.onPrint,
    this.onShare,
    this.onFailure,
    super.key,
  });

  final DocumentDescriptor descriptor;
  final DocumentBytesBuilder onBuild;
  final DocumentActionCallback? onPrint;
  final DocumentActionCallback? onShare;
  final DocumentFailureCallback? onFailure;

  @override
  State<DocumentPreviewScaffold> createState() =>
      _DocumentPreviewScaffoldState();
}

class _DocumentPreviewScaffoldState extends State<DocumentPreviewScaffold> {
  bool _isPrinting = false;
  bool _isSharing = false;
  Uint8List? _cachedBytes;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: Text(widget.descriptor.title),
        actions: [
          if (widget.onPrint != null)
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 8),
              child: IconButton(
                icon: const Icon(Icons.print),
                onPressed: _isPrinting ? null : _handlePrint,
                tooltip: 'Print',
              ),
            ),
          if (widget.onShare != null)
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 8),
              child: IconButton(
                icon: const Icon(Icons.share),
                onPressed: _isSharing ? null : _handleShare,
                tooltip: 'Share',
              ),
            ),
        ],
      ),
      body: PdfPreview(
        build: (_) => widget.onBuild(widget.descriptor.pdfPageFormat),
        initialPageFormat: widget.descriptor.pdfPageFormat,
        pdfFileName: widget.descriptor.filename,
        canChangePageFormat: false,
      ),
    );
  }

  Future<void> _handlePrint() async {
    setState(() => _isPrinting = true);
    try {
      final bytes = _cachedBytes ??
          await widget.onBuild(widget.descriptor.pdfPageFormat);
      _cachedBytes = bytes;
      await widget.onPrint!(bytes);
    } catch (e) {
      widget.onFailure?.call('Print failed: ${e.toString()}');
    } finally {
      if (mounted) {
        setState(() => _isPrinting = false);
      }
    }
  }

  Future<void> _handleShare() async {
    setState(() => _isSharing = true);
    try {
      final bytes = _cachedBytes ??
          await widget.onBuild(widget.descriptor.pdfPageFormat);
      _cachedBytes = bytes;
      await widget.onShare!(bytes);
    } catch (e) {
      widget.onFailure?.call('Share failed: ${e.toString()}');
    } finally {
      if (mounted) {
        setState(() => _isSharing = false);
      }
    }
  }
}
