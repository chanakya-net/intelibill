import 'dart:async';

import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/shared/barcode_scanner/barcode_scan_result.dart';
import 'package:mobile_scanner/mobile_scanner.dart';

/// Supported barcode formats for the shared scanner.
const List<BarcodeFormat> _supportedFormats = [
  BarcodeFormat.code128,
  BarcodeFormat.code39,
  BarcodeFormat.ean13,
  BarcodeFormat.ean8,
  BarcodeFormat.qrCode,
  BarcodeFormat.upcA,
  BarcodeFormat.upcE,
];

/// Duration to debounce duplicate accepted scan values.
const _deduplicateWindow = Duration(milliseconds: 1200);

/// Duration to show success feedback before popping.
const _successFeedbackDuration = Duration(milliseconds: 600);

/// A builder that can produce a scanner surface widget.
///
/// The [onDetect] callback must be called with a [BarcodeCapture] when a
/// barcode is detected.
typedef ScannerSurfaceBuilder =
    Widget Function(
      BuildContext context,
      void Function(BarcodeCapture) onDetect,
    );

/// Full-screen barcode scanner page.
///
/// Returns a [BarcodeScanResult] via [Navigator.pop] when a valid barcode is
/// detected, or `null` when the user closes the scanner.
///
/// The optional [scannerSurfaceBuilder] parameter is intended for widget tests
/// to inject a hardware-free fake scanner surface.
class BarcodeScannerPage extends StatefulWidget {
  const BarcodeScannerPage({super.key, this.scannerSurfaceBuilder});

  /// Test injection seam: when provided this builder is used instead of the
  /// real [MobileScanner].
  final ScannerSurfaceBuilder? scannerSurfaceBuilder;

  @override
  State<BarcodeScannerPage> createState() => _BarcodeScannerPageState();
}

enum _ScannerStatus { loading, ready, success, error }

class _BarcodeScannerPageState extends State<BarcodeScannerPage> {
  MobileScannerController? _controller;
  _ScannerStatus _status = _ScannerStatus.loading;
  String? _errorMessage;
  String? _lastAcceptedValue;
  Timer? _deduplicateTimer;
  bool _popping = false;

  @override
  void initState() {
    super.initState();
    if (widget.scannerSurfaceBuilder == null) {
      _controller = MobileScannerController(formats: _supportedFormats);
      _controller!.addListener(_onControllerStateChanged);
      _status = _ScannerStatus.loading;
    } else {
      // Test mode: surface builder handles detections; no real controller.
      _status = _ScannerStatus.ready;
    }
  }

  void _onControllerStateChanged() {
    final state = _controller?.value;
    if (state == null) return;
    if (!mounted) return;
    if (state.isInitialized && _status == _ScannerStatus.loading) {
      setState(() => _status = _ScannerStatus.ready);
    }
  }

  void _handleDetection(BarcodeCapture capture) {
    if (_popping) return;
    final barcodes = capture.barcodes;
    for (final barcode in barcodes) {
      final rawValue = barcode.rawValue;
      if (rawValue == null || rawValue.isEmpty) continue;
      if (rawValue == _lastAcceptedValue) continue;

      _lastAcceptedValue = rawValue;
      _deduplicateTimer?.cancel();
      _deduplicateTimer = Timer(_deduplicateWindow, () {
        _lastAcceptedValue = null;
      });

      unawaited(
        _acceptScan(
          BarcodeScanResult(
            value: rawValue,
            format: barcode.format == BarcodeFormat.unknown
                ? null
                : barcode.format.name,
          ),
        ),
      );
      break;
    }
  }

  Future<void> _acceptScan(BarcodeScanResult result) async {
    if (_popping || !mounted) return;
    setState(() => _status = _ScannerStatus.success);
    await _controller?.stop();
    await Future<void>.delayed(_successFeedbackDuration);
    if (!mounted) return;
    _popping = true;
    Navigator.of(context).pop(result);
  }

  void _handleError(Object error, StackTrace stackTrace) {
    if (!mounted) return;
    final l10n = AppLocalizations.of(context)!;
    final String message;
    if (error is MobileScannerException &&
        error.errorCode == MobileScannerErrorCode.permissionDenied) {
      message = l10n.barcodeScannerPermissionDenied;
    } else {
      message = l10n.barcodeScannerUnavailable;
    }
    setState(() {
      _status = _ScannerStatus.error;
      _errorMessage = message;
    });
  }

  Future<void> _close() async {
    if (_popping) return;
    _popping = true;
    await _controller?.stop();
    if (mounted) Navigator.of(context).pop();
  }

  @override
  void dispose() {
    _deduplicateTimer?.cancel();
    _controller?.removeListener(_onControllerStateChanged);
    unawaited(_controller?.dispose() ?? Future<void>.value());
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final theme = Theme.of(context);
    const primaryOrange = Color(0xFFF97316);

    return Scaffold(
      backgroundColor: Colors.black,
      appBar: AppBar(
        backgroundColor: Colors.black,
        foregroundColor: Colors.white,
        centerTitle: true,
        title: Text(
          l10n.barcodeScannerTitle,
          style: const TextStyle(
            color: Colors.white,
            fontWeight: FontWeight.w700,
          ),
        ),
        leading: IconButton(
          key: const Key('barcode_scanner_close'),
          icon: const Icon(Icons.close, color: Colors.white),
          tooltip: l10n.commonClose,
          onPressed: _close,
        ),
        // Intelibill branded accent line
        bottom: PreferredSize(
          preferredSize: const Size.fromHeight(2),
          child: Container(height: 2, color: primaryOrange),
        ),
      ),
      body: Stack(
        fit: StackFit.expand,
        children: [
          _buildScannerSurface(),
          _buildScanGuideOverlay(theme, primaryOrange),
          _buildStatusOverlay(l10n, theme, primaryOrange),
        ],
      ),
    );
  }

  Widget _buildScannerSurface() {
    if (widget.scannerSurfaceBuilder != null) {
      return widget.scannerSurfaceBuilder!(context, _handleDetection);
    }
    return MobileScanner(
      controller: _controller,
      onDetect: _handleDetection,
      onDetectError: _handleError,
    );
  }

  Widget _buildScanGuideOverlay(ThemeData theme, Color accentColor) {
    return Center(
      child: IgnorePointer(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            SizedBox(
              width: 240,
              height: 240,
              child: CustomPaint(
                painter: _ScanGuideCornerPainter(color: accentColor),
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildStatusOverlay(
    AppLocalizations l10n,
    ThemeData theme,
    Color accentColor,
  ) {
    return Positioned(
      bottom: 48,
      left: 24,
      right: 24,
      child: Center(child: _buildStatusContent(l10n, accentColor)),
    );
  }

  Widget _buildStatusContent(AppLocalizations l10n, Color accentColor) {
    switch (_status) {
      case _ScannerStatus.loading:
        return Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const CircularProgressIndicator(color: Colors.white),
            const SizedBox(height: 12),
            Text(
              l10n.barcodeScannerSearching,
              style: const TextStyle(color: Colors.white, fontSize: 14),
              textAlign: TextAlign.center,
            ),
          ],
        );
      case _ScannerStatus.ready:
        return Text(
          l10n.barcodeScannerHint,
          key: const Key('barcode_scanner_hint'),
          style: const TextStyle(color: Colors.white, fontSize: 14),
          textAlign: TextAlign.center,
        );
      case _ScannerStatus.success:
        return Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(Icons.check_circle, color: accentColor),
            const SizedBox(width: 8),
            Text(
              l10n.barcodeScannerSuccess,
              key: const Key('barcode_scanner_success'),
              style: TextStyle(
                color: accentColor,
                fontSize: 16,
                fontWeight: FontWeight.w700,
              ),
            ),
          ],
        );
      case _ScannerStatus.error:
        return Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Icon(Icons.error_outline, color: Colors.red),
            const SizedBox(height: 8),
            Text(
              _errorMessage ?? l10n.barcodeScannerUnavailable,
              key: const Key('barcode_scanner_error'),
              style: const TextStyle(color: Colors.red, fontSize: 14),
              textAlign: TextAlign.center,
            ),
          ],
        );
    }
  }
}

/// Paints corner brackets for the scan guide rectangle.
class _ScanGuideCornerPainter extends CustomPainter {
  const _ScanGuideCornerPainter({required this.color});

  final Color color;

  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()
      ..color = color
      ..strokeWidth = 4
      ..style = PaintingStyle.stroke
      ..strokeCap = StrokeCap.round;

    const cornerLen = 28.0;

    // Top-left
    canvas
      ..drawLine(Offset.zero, const Offset(cornerLen, 0), paint)
      ..drawLine(Offset.zero, const Offset(0, cornerLen), paint)
      // Top-right
      ..drawLine(
        Offset(size.width, 0),
        Offset(size.width - cornerLen, 0),
        paint,
      )
      ..drawLine(Offset(size.width, 0), Offset(size.width, cornerLen), paint)
      // Bottom-left
      ..drawLine(Offset(0, size.height), Offset(cornerLen, size.height), paint)
      ..drawLine(
        Offset(0, size.height),
        Offset(0, size.height - cornerLen),
        paint,
      )
      // Bottom-right
      ..drawLine(
        Offset(size.width, size.height),
        Offset(size.width - cornerLen, size.height),
        paint,
      )
      ..drawLine(
        Offset(size.width, size.height),
        Offset(size.width, size.height - cornerLen),
        paint,
      );
  }

  @override
  bool shouldRepaint(_ScanGuideCornerPainter oldDelegate) =>
      oldDelegate.color != color;
}
