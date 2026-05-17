import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:golden_toolkit/golden_toolkit.dart';
import 'package:intelibill_mobile/src/features/app_status/domain/entities/app_status.dart';
import 'package:intelibill_mobile/src/features/app_status/presentation/controllers/app_status_controller.dart';
import 'package:intelibill_mobile/src/features/app_status/presentation/pages/app_status_page.dart';

class GoldenAppStatusController extends AppStatusController {
  @override
  Future<AppStatus> build() async {
    return AppStatus(
      statusText: 'Ready',
      apiBaseUrl: 'https://api.example.com',
      timestamp: DateTime.utc(2026, 5, 14, 10),
      environment: 'test',
    );
  }
}

void main() {
  setUpAll(loadAppFonts);

  testGoldens('AppStatusPage matches golden', (tester) async {
    await tester.pumpWidgetBuilder(
      ProviderScope(
        overrides: [
          appStatusControllerProvider.overrideWith(
            GoldenAppStatusController.new,
          ),
        ],
        child: const MaterialApp(home: AppStatusPage()),
      ),
    );
    await tester.pumpAndSettle();

    final goldenName = Platform.isLinux
        ? 'app_status_page_linux'
        : 'app_status_page';

    await screenMatchesGolden(tester, goldenName);
  });
}
