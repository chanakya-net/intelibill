import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/shared/barcode_scanner/barcode_scan_result.dart';
import 'package:intelibill_mobile/src/shared/barcode_scanner/barcode_scanner_page.dart';

Future<BarcodeScanResult?> showBarcodeScanner(BuildContext context) {
  return Navigator.of(context).push<BarcodeScanResult?>(
    MaterialPageRoute<BarcodeScanResult?>(
      fullscreenDialog: true,
      builder: (_) => const BarcodeScannerPage(),
    ),
  );
}
