import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';
import 'package:intelibill_mobile/src/app/app.dart';

void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  testWidgets('app starts at login screen for unauthenticated users', (
    tester,
  ) async {
    await tester.pumpWidget(
      const ProviderScope(
        child: IntelibillApp(),
      ),
    );
    await tester.pumpAndSettle();

    // Verify login page is shown
    expect(find.byKey(const Key('login-page-identifier')), findsOneWidget);
    expect(find.byKey(const Key('login-page-password')), findsOneWidget);
    expect(find.byKey(const Key('login-page-submit')), findsOneWidget);
  });
}
