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
  bool _isReady = false;
  Uint8List? _cachedBytes;
  Future<Uint8List>? _buildFuture;
  var _buildGeneration = 0;

  Future<Uint8List> _build(PdfPageFormat format) {
    return _buildFuture ??= _runBuild(
      format,
      _buildGeneration,
      widget.onBuild,
    );
  }

  Future<Uint8List> _runBuild(
    PdfPageFormat format,
    int generation,
    DocumentBytesBuilder onBuild,
  ) async {
    try {
      final bytes = await onBuild(format);
      if (generation != _buildGeneration) {
        return bytes;
      }

      _cachedBytes = bytes;
      if (mounted) {
        setState(() => _isReady = true);
      }
      return bytes;
    } catch (e) {
      if (generation == _buildGeneration) {
        _buildFuture = null;
      }
      rethrow;
    }
  }

  @override
  void didUpdateWidget(covariant DocumentPreviewScaffold oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.onBuild == widget.onBuild &&
        oldWidget.descriptor == widget.descriptor) {
      return;
    }

    _buildGeneration++;
    _buildFuture = null;
    _cachedBytes = null;
    _isReady = false;
  }

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
                onPressed: (_isReady && !_isPrinting) ? _handlePrint : null,
                tooltip: 'Print',
              ),
            ),
          if (widget.onShare != null)
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 8),
              child: IconButton(
                icon: const Icon(Icons.share),
                onPressed: (_isReady && !_isSharing) ? _handleShare : null,
                tooltip: 'Share',
              ),
            ),
        ],
      ),
      body: PdfPreview(
        key: ValueKey(_buildGeneration),
        build: (_) => _build(widget.descriptor.pdfPageFormat),
        initialPageFormat: widget.descriptor.pdfPageFormat,
        pdfFileName: widget.descriptor.filename,
        canChangePageFormat: false,
        useActions: false,
      ),
    );
  }

  Future<void> _handlePrint() async {
    setState(() => _isPrinting = true);
    try {
      final bytes =
          _cachedBytes ?? await _build(widget.descriptor.pdfPageFormat);
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
      final bytes =
          _cachedBytes ?? await _build(widget.descriptor.pdfPageFormat);
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
