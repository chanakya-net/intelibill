import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';
import 'package:intelibill_mobile/src/app/app.dart';

Future<void> _pumpUntilFound(
  WidgetTester tester,
  Finder finder, {
  Duration timeout = const Duration(seconds: 10),
}) async {
  final deadline = DateTime.now().add(timeout);
  while (finder.evaluate().isEmpty) {
    if (DateTime.now().isAfter(deadline)) {
      throw TestFailure('Timed out waiting for $finder.');
    }
    await tester.pump(const Duration(milliseconds: 100));
  }
}

void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  testWidgets('app starts at login screen for unauthenticated users', (
    tester,
  ) async {
    await tester.pumpWidget(const ProviderScope(child: IntelibillApp()));

    final identifierField = find.byKey(
      const Key('login-page-identifier'),
    );
    await _pumpUntilFound(tester, identifierField);
    await tester.pumpAndSettle();

    // Verify login page is shown
    expect(identifierField, findsOneWidget);
    expect(find.byKey(const Key('login-page-password')), findsOneWidget);
    expect(find.byKey(const Key('login-page-submit')), findsOneWidget);
  });
}
