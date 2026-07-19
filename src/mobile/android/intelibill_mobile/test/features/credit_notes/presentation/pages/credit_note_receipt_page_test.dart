import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/entities/credit_note_print.dart';
import 'package:intelibill_mobile/src/features/credit_notes/presentation/controllers/credit_notes_controller.dart';
import 'package:intelibill_mobile/src/features/credit_notes/presentation/pages/credit_note_receipt_page.dart';
import 'package:intelibill_mobile/src/shared/documents/document_preview_scaffold.dart';

void main() {
  group('CreditNoteReceiptPage', () {
    testWidgets('displays loading state', (WidgetTester tester) async {
      final delayCompleter = Completer<CreditNotePrint>();

      final container = ProviderContainer(
        overrides: [
          creditNotePrintByCodeProvider.overrideWith(
            (ref, _) => delayCompleter.future,
          ),
        ],
      );
      addTearDown(container.dispose);

      await tester.pumpWidget(
        UncontrolledProviderScope(
          container: container,
          child: const MaterialApp(
            locale: Locale('en', 'IN'),
            supportedLocales: [Locale('en', 'IN')],
            localizationsDelegates: [
              AppLocalizations.delegate,
              ...AppLocalizations.localizationsDelegates,
            ],
            home: CreditNoteReceiptPage(code: 'CN-REC-001'),
          ),
        ),
      );
      await tester.pump();

      expect(find.byType(CircularProgressIndicator), findsOneWidget);
      expect(find.byType(AppBar), findsOneWidget);

      delayCompleter.complete(_creditNotePrint());
    });

    testWidgets('displays error state with retry button', (
      WidgetTester tester,
    ) async {
      final container = ProviderContainer(
        overrides: [
          creditNotePrintByCodeProvider.overrideWith(
            (ref, _) => Future<CreditNotePrint>.error(Exception('Load failed')),
          ),
        ],
      );
      addTearDown(container.dispose);

      await tester.pumpWidget(
        UncontrolledProviderScope(
          container: container,
          child: const MaterialApp(
            locale: Locale('en', 'IN'),
            supportedLocales: [Locale('en', 'IN')],
            localizationsDelegates: [
              AppLocalizations.delegate,
              ...AppLocalizations.localizationsDelegates,
            ],
            home: CreditNoteReceiptPage(code: 'CN-REC-001'),
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.byIcon(Icons.error_outline), findsOneWidget);
      expect(find.byType(TextButton), findsOneWidget);
    });

    testWidgets('retries the page code after a failed load', (
      WidgetTester tester,
    ) async {
      final requestedCodes = <String>[];
      var allowSuccess = false;

      final container = ProviderContainer(
        overrides: [
          creditNotePrintByCodeProvider.overrideWith(
            (ref, code) {
              requestedCodes.add(code);
              if (!allowSuccess) {
                return Future<CreditNotePrint>.error(
                  Exception('Load failed'),
                );
              }
              return Future.value(_creditNotePrint(code: code));
            },
          ),
        ],
      );
      addTearDown(container.dispose);

      await tester.pumpWidget(_receiptApp(container, 'CN-REC-001'));
      await tester.pumpAndSettle();

      expect(find.byType(TextButton), findsOneWidget);
      allowSuccess = true;
      await tester.tap(find.byType(TextButton));
      await tester.pump();
      await tester.pump();

      expect(requestedCodes, everyElement('CN-REC-001'));
      expect(requestedCodes.length, greaterThanOrEqualTo(2));
      expect(find.byType(DocumentPreviewScaffold), findsOneWidget);
    });

    testWidgets('builds DocumentPreviewScaffold on data', (
      WidgetTester tester,
    ) async {
      final container = ProviderContainer(
        overrides: [
          creditNotePrintByCodeProvider.overrideWith(
            (ref, _) => Future.value(_creditNotePrint()),
          ),
        ],
      );
      addTearDown(container.dispose);

      await tester.pumpWidget(
        UncontrolledProviderScope(
          container: container,
          child: const MaterialApp(
            locale: Locale('en', 'IN'),
            supportedLocales: [Locale('en', 'IN')],
            localizationsDelegates: [
              AppLocalizations.delegate,
              ...AppLocalizations.localizationsDelegates,
            ],
            home: CreditNoteReceiptPage(code: 'CN-REC-001'),
          ),
        ),
      );
      await tester.pump();

      expect(find.byType(DocumentPreviewScaffold), findsOneWidget);
    });
  });
}

Widget _receiptApp(ProviderContainer container, String code) =>
    UncontrolledProviderScope(
      container: container,
      child: MaterialApp(
        locale: const Locale('en', 'IN'),
        supportedLocales: const [Locale('en', 'IN')],
        localizationsDelegates: [
          AppLocalizations.delegate,
          ...AppLocalizations.localizationsDelegates,
        ],
        home: CreditNoteReceiptPage(code: code),
      ),
    );

CreditNotePrint _creditNotePrint({String? code}) {
  return CreditNotePrint(
    creditNoteId: 'cn-1',
    code: code ?? 'CN-REC-001',
    status: 'active',
    isUsable: true,
    originalAmount: 1000,
    availableBalance: 800,
    issuedAt: DateTime.utc(2026, 6, 10, 10),
    expiresAt: DateTime.utc(2026, 7, 1, 10),
    saleId: 'sale-1',
    invoiceNumber: 'INV-001',
    saleReturnId: 'ret-1',
    returnNumber: 'RET-001',
    customerDisplayName: 'Alice',
    reason: 'Defective item',
    voidReason: null,
  );
}
