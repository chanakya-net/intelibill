import 'dart:io';
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:golden_toolkit/golden_toolkit.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/pages/login_page.dart';

class GoldenAuthController extends AuthController {
  @override
  Future<AuthControllerState> build() async {
    return const AuthControllerState();
  }
}

class _TolerantGoldenFileComparator extends LocalFileComparator {
  _TolerantGoldenFileComparator(
    super.testFile, {
    required double precisionTolerance,
  }) : _precisionTolerance = precisionTolerance;

  final double _precisionTolerance;

  @override
  Future<bool> compare(Uint8List imageBytes, Uri golden) async {
    final result = await GoldenFileComparator.compareLists(
      imageBytes,
      await getGoldenBytes(golden),
    );

    final passed = result.passed || result.diffPercent <= _precisionTolerance;
    if (passed) {
      result.dispose();
      return true;
    }

    final error = await generateFailureOutput(result, golden, basedir);
    result.dispose();
    throw FlutterError(error);
  }
}

void main() {
  setUpAll(loadAppFonts);

  testGoldens('LoginPage matches golden', (tester) async {
    final previousGoldenFileComparator = goldenFileComparator;
    goldenFileComparator = _TolerantGoldenFileComparator(
      Uri.file(
        '${Directory.current.path}/test/features/auth/presentation/'
        'pages/login_page_golden_test.dart',
      ),
      precisionTolerance: Platform.isLinux ? 0.015 : 0,
    );
    addTearDown(() {
      goldenFileComparator = previousGoldenFileComparator;
    });

    await tester.pumpWidgetBuilder(
      ProviderScope(
        overrides: [
          authControllerProvider.overrideWith(GoldenAuthController.new),
        ],
        child: MaterialApp(
          theme: AppTheme.lightTheme,
          home: const LoginPage(),
        ),
      ),
    );
    await tester.pumpAndSettle();

    final goldenName = Platform.isLinux ? 'login_page_linux' : 'login_page';

    await screenMatchesGolden(tester, goldenName);
  });
}
