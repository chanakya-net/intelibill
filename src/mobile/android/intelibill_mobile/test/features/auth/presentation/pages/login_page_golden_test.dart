import 'dart:io';

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

void main() {
  setUpAll(loadAppFonts);

  testGoldens('LoginPage matches golden', (tester) async {
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
