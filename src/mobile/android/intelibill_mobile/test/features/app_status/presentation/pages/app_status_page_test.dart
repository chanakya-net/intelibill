import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/app_status/domain/entities/app_status.dart';
import 'package:intelibill_mobile/src/features/app_status/presentation/controllers/app_status_controller.dart';
import 'package:intelibill_mobile/src/features/app_status/presentation/pages/app_status_page.dart';

class LoadedAppStatusController extends AppStatusController {
  LoadedAppStatusController(this.status);

  final AppStatus status;

  @override
  Future<AppStatus> build() async => status;
}

class FailingAppStatusController extends AppStatusController {
  @override
  Future<AppStatus> build() async => throw StateError('failed');
}

void main() {
  group('AppStatusPage', () {
    testWidgets('renders loaded status details', (tester) async {
      final status = AppStatus(
        statusText: 'Ready',
        apiBaseUrl: 'https://api.example.com',
        timestamp: DateTime.utc(2026, 5, 14, 10),
        environment: 'test',
      );

      await tester.pumpWidget(
        ProviderScope(
          overrides: [
            appStatusControllerProvider.overrideWith(
              () => LoadedAppStatusController(status),
            ),
          ],
          child: const MaterialApp(home: AppStatusPage()),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Intelibill status'), findsOneWidget);
      expect(find.text('Mobile clean architecture sample'), findsOneWidget);
      expect(find.text('Ready'), findsOneWidget);
      expect(find.text('https://api.example.com'), findsOneWidget);
      expect(find.text('test'), findsOneWidget);
      expect(find.text('2026-05-14 10:00:00 UTC'), findsOneWidget);
    });

    testWidgets('renders retry state on failure', (tester) async {
      await tester.pumpWidget(
        ProviderScope(
          overrides: [
            appStatusControllerProvider.overrideWith(
              FailingAppStatusController.new,
            ),
          ],
          child: const MaterialApp(home: AppStatusPage()),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Unable to load app status'), findsOneWidget);
      expect(find.textContaining('failed'), findsOneWidget);
      expect(find.widgetWithText(FilledButton, 'Retry'), findsOneWidget);
    });
  });
}
