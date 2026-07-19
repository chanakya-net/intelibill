import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_cancel_sheet.dart';

void main() {
  group('PurchaseOrderCancelSheet', () {
    testWidgets('accepts 1-character reason', (WidgetTester tester) async {
      var called = false;
      await tester.pumpWidget(
        MaterialApp(
          localizationsDelegates: AppLocalizations.localizationsDelegates,
          supportedLocales: AppLocalizations.supportedLocales,
          home: Scaffold(
            body: PurchaseOrderCancelSheet(
              onCancel: (reason) async {
                expect(reason, 'A');
                called = true;
              },
            ),
          ),
        ),
      );

      await tester.enterText(find.byType(TextField), 'A');
      await tester.pumpAndSettle();

      expect(find.byType(ElevatedButton), findsOneWidget);
      final button = find.byType(ElevatedButton);
      expect(tester.widget<ElevatedButton>(button).onPressed, isNotNull);

      await tester.tap(button);
      await tester.pumpAndSettle();

      expect(called, isTrue);
    });

    testWidgets('accepts 500-character reason', (WidgetTester tester) async {
      final reason500 = 'x' * 500;
      var called = false;
      await tester.pumpWidget(
        MaterialApp(
          localizationsDelegates: AppLocalizations.localizationsDelegates,
          supportedLocales: AppLocalizations.supportedLocales,
          home: Scaffold(
            body: PurchaseOrderCancelSheet(
              onCancel: (reason) async {
                expect(reason, reason500);
                called = true;
              },
            ),
          ),
        ),
      );

      await tester.enterText(find.byType(TextField), reason500);
      await tester.pumpAndSettle();

      final button = find.byType(ElevatedButton);
      expect(tester.widget<ElevatedButton>(button).onPressed, isNotNull);

      await tester.tap(button);
      await tester.pumpAndSettle();

      expect(called, isTrue);
    });

    testWidgets('disables button when reason is blank', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(
        MaterialApp(
          localizationsDelegates: AppLocalizations.localizationsDelegates,
          supportedLocales: AppLocalizations.supportedLocales,
          home: Scaffold(
            body: PurchaseOrderCancelSheet(
              onCancel: (_) async {},
            ),
          ),
        ),
      );

      final button = find.byType(ElevatedButton);
      expect(
        tester.widget<ElevatedButton>(button).onPressed,
        isNull,
      );
    });

    testWidgets('limits input to 500 chars visually', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(
        MaterialApp(
          localizationsDelegates: AppLocalizations.localizationsDelegates,
          supportedLocales: AppLocalizations.supportedLocales,
          home: Scaffold(
            body: PurchaseOrderCancelSheet(
              onCancel: (_) async {},
            ),
          ),
        ),
      );

      final reason501 = 'x' * 501;
      await tester.enterText(find.byType(TextField), reason501);
      await tester.pumpAndSettle();

      final controller = find.byType(TextField);
      final textField = tester.widget<TextField>(controller);
      expect(textField.maxLength, 500);
    });

    testWidgets('trims whitespace from reason', (WidgetTester tester) async {
      var called = false;
      await tester.pumpWidget(
        MaterialApp(
          localizationsDelegates: AppLocalizations.localizationsDelegates,
          supportedLocales: AppLocalizations.supportedLocales,
          home: Scaffold(
            body: PurchaseOrderCancelSheet(
              onCancel: (reason) async {
                expect(reason, 'No longer needed');
                called = true;
              },
            ),
          ),
        ),
      );

      await tester.enterText(find.byType(TextField), '  No longer needed  ');
      await tester.pumpAndSettle();

      await tester.tap(find.byType(ElevatedButton));
      await tester.pumpAndSettle();

      expect(called, isTrue);
    });

    testWidgets('disables button during cancel', (WidgetTester tester) async {
      final completer = Completer<void>();
      await tester.pumpWidget(
        MaterialApp(
          localizationsDelegates: AppLocalizations.localizationsDelegates,
          supportedLocales: AppLocalizations.supportedLocales,
          home: Scaffold(
            body: PurchaseOrderCancelSheet(
              onCancel: (_) => completer.future,
            ),
          ),
        ),
      );

      await tester.enterText(find.byType(TextField), 'reason');
      await tester.pumpAndSettle();

      final button = find.byType(ElevatedButton);
      await tester.tap(button);
      await tester.pump();

      expect(
        tester.widget<ElevatedButton>(button).onPressed,
        isNull,
      );

      completer.complete();
    });

    testWidgets('calls onCancel callback', (WidgetTester tester) async {
      var called = false;
      await tester.pumpWidget(
        MaterialApp(
          localizationsDelegates: AppLocalizations.localizationsDelegates,
          supportedLocales: AppLocalizations.supportedLocales,
          home: Scaffold(
            body: PurchaseOrderCancelSheet(
              onCancel: (_) async {
                called = true;
              },
            ),
          ),
        ),
      );

      await tester.enterText(find.byType(TextField), 'reason');
      await tester.pumpAndSettle();

      await tester.tap(find.byType(ElevatedButton).first);
      await tester.pumpAndSettle();

      expect(called, isTrue);
    });

    testWidgets('maps failures without rendering server diagnostics', (
      tester,
    ) async {
      const raw = 'sensitive server diagnostic';
      await tester.pumpWidget(
        MaterialApp(
          localizationsDelegates: AppLocalizations.localizationsDelegates,
          supportedLocales: AppLocalizations.supportedLocales,
          home: Scaffold(
            body: PurchaseOrderCancelSheet(
              onCancel: (_) async => throw AppException(
                failure: const Failure.server(message: raw, statusCode: 500),
              ),
            ),
          ),
        ),
      );

      await tester.enterText(find.byType(TextField), 'reason');
      await tester.pump();
      await tester.tap(find.byType(ElevatedButton));
      await tester.pumpAndSettle();

      expect(find.text(raw), findsNothing);
      expect(
        find.text('The server could not complete the request. Try again.'),
        findsOneWidget,
      );
    });
  });
}
