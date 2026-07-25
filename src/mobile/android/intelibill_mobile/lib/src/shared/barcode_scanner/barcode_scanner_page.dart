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
    final colorScheme = theme.colorScheme;

    return Scaffold(
      backgroundColor: Colors.black,
      appBar: AppBar(
        backgroundColor: Colors.black,
        foregroundColor: Colors.white,
        centerTitle: true,
        title: Text(
          l10n.barcodeScannerTitle,
          style: theme.textTheme.titleLarge?.copyWith(
            color: Colors.white,
            fontWeight: FontWeight.w700,
          ),
        ),
        leading: IconButton(
          key: const Key('barcode_scanner_close'),
          style: IconButton.styleFrom(
            backgroundColor: Colors.white.withValues(alpha: 0.12),
            foregroundColor: Colors.white,
          ),
          icon: const Icon(Icons.close),
          tooltip: l10n.commonClose,
          onPressed: _close,
        ),
        bottom: PreferredSize(
          preferredSize: const Size.fromHeight(2),
          child: Container(height: 2, color: colorScheme.primary),
        ),
      ),
      body: Stack(
        fit: StackFit.expand,
        children: [
          _buildScannerSurface(),
          _ScanFrameOverlay(frameColor: colorScheme.primary),
          _buildStatusOverlay(l10n, theme),
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

  Widget _buildStatusOverlay(AppLocalizations l10n, ThemeData theme) {
    final colorScheme = theme.colorScheme;

    return Positioned(
      bottom: 48,
      left: 24,
      right: 24,
      child: Center(
        child: DecoratedBox(
          decoration: BoxDecoration(
            color: Colors.black.withValues(alpha: 0.62),
            borderRadius: BorderRadius.circular(14),
            border: Border.all(
              color: colorScheme.primary.withValues(alpha: 0.35),
            ),
          ),
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 14),
            child: _buildStatusContent(l10n, theme),
          ),
        ),
      ),
    );
  }

  Widget _buildStatusContent(AppLocalizations l10n, ThemeData theme) {
    final colorScheme = theme.colorScheme;
    const statusTextStyle = TextStyle(color: Colors.white, fontSize: 14);

    switch (_status) {
      case _ScannerStatus.loading:
        return Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            CircularProgressIndicator(color: colorScheme.primary),
            const SizedBox(height: 12),
            Text(
              l10n.barcodeScannerSearching,
              style: statusTextStyle,
              textAlign: TextAlign.center,
            ),
          ],
        );
      case _ScannerStatus.ready:
        return Text(
          l10n.barcodeScannerHint,
          key: const Key('barcode_scanner_hint'),
          style: statusTextStyle,
          textAlign: TextAlign.center,
        );
      case _ScannerStatus.success:
        return Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(Icons.check_circle, color: colorScheme.primary),
            const SizedBox(width: 8),
            Text(
              l10n.barcodeScannerSuccess,
              key: const Key('barcode_scanner_success'),
              style: TextStyle(
                color: colorScheme.primary,
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
            Icon(Icons.error_outline, color: colorScheme.error),
            const SizedBox(height: 8),
            Text(
              _errorMessage ?? l10n.barcodeScannerUnavailable,
              key: const Key('barcode_scanner_error'),
              style: TextStyle(color: colorScheme.error, fontSize: 14),
              textAlign: TextAlign.center,
            ),
          ],
        );
    }
  }
}

/// Dimmed overlay with a transparent scan window and themed corner frame.
class _ScanFrameOverlay extends StatelessWidget {
  const _ScanFrameOverlay({required this.frameColor});

  final Color frameColor;

  static const _cornerRadius = 16.0;
  static const _frameAspectRatio = 1.0;
  static const _maxFrameSize = 280.0;
  static const _minFrameSize = 220.0;

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, constraints) {
        final frameSize = (constraints.maxWidth * 0.72).clamp(
          _minFrameSize,
          _maxFrameSize,
        );
        final frameRect = Rect.fromCenter(
          center: Offset(constraints.maxWidth / 2, constraints.maxHeight / 2),
          width: frameSize,
          height: frameSize * _frameAspectRatio,
        );

        return IgnorePointer(
          child: CustomPaint(
            painter: _ScanFrameOverlayPainter(
              frameColor: frameColor,
              scanRect: frameRect,
              cornerRadius: _cornerRadius,
            ),
            size: Size(constraints.maxWidth, constraints.maxHeight),
          ),
        );
      },
    );
  }
}

/// Paints a dimmed overlay, scan-window border, and corner brackets.
class _ScanFrameOverlayPainter extends CustomPainter {
  const _ScanFrameOverlayPainter({
    required this.frameColor,
    required this.scanRect,
    required this.cornerRadius,
  });

  final Color frameColor;
  final Rect scanRect;
  final double cornerRadius;

  static const _overlayOpacity = 0.58;
  static const _cornerLength = 28.0;
  static const _strokeWidth = 3.5;

  @override
  void paint(Canvas canvas, Size size) {
    final overlayPaint = Paint()
      ..color = Colors.black.withValues(alpha: _overlayOpacity);
    final fullRect = Offset.zero & size;
    final cutout = RRect.fromRectAndRadius(
      scanRect,
      Radius.circular(cornerRadius),
    );
    final overlayPath = Path()
      ..addRect(fullRect)
      ..addRRect(cutout);
    overlayPath.fillType = PathFillType.evenOdd;
    canvas.drawPath(overlayPath, overlayPaint);

    final framePaint = Paint()
      ..color = frameColor
      ..strokeWidth = _strokeWidth
      ..style = PaintingStyle.stroke
      ..strokeCap = StrokeCap.round;

    canvas.drawRRect(cutout, framePaint);

    final cornerPaint = Paint()
      ..color = frameColor
      ..strokeWidth = _strokeWidth + 0.5
      ..style = PaintingStyle.stroke
      ..strokeCap = StrokeCap.round;

    _drawCornerBrackets(canvas, scanRect, cornerPaint);
  }

  void _drawCornerBrackets(Canvas canvas, Rect rect, Paint paint) {
    final left = rect.left;
    final right = rect.right;
    final top = rect.top;
    final bottom = rect.bottom;
    const len = _cornerLength;

    canvas
      ..drawLine(
        Offset(left, top + cornerRadius),
        Offset(left, top + len),
        paint,
      )
      ..drawLine(
        Offset(left, top + cornerRadius),
        Offset(left + len, top),
        paint,
      )
      ..drawLine(
        Offset(right, top + cornerRadius),
        Offset(right, top + len),
        paint,
      )
      ..drawLine(
        Offset(right, top + cornerRadius),
        Offset(right - len, top),
        paint,
      )
      ..drawLine(
        Offset(left, bottom - cornerRadius),
        Offset(left, bottom - len),
        paint,
      )
      ..drawLine(
        Offset(left, bottom - cornerRadius),
        Offset(left + len, bottom),
        paint,
      )
      ..drawLine(
        Offset(right, bottom - cornerRadius),
        Offset(right, bottom - len),
        paint,
      )
      ..drawLine(
        Offset(right, bottom - cornerRadius),
        Offset(right - len, bottom),
        paint,
      );
  }

  @override
  bool shouldRepaint(_ScanFrameOverlayPainter oldDelegate) =>
      oldDelegate.frameColor != frameColor ||
      oldDelegate.scanRect != scanRect ||
      oldDelegate.cornerRadius != cornerRadius;
}
