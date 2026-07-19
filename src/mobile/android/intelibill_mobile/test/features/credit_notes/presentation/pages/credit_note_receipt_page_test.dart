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

    test('retry invalidates provider', () {
      var callCount = 0;

      final container = ProviderContainer(
        overrides: [
          creditNotePrintByCodeProvider.overrideWith(
            (ref, code) {
              callCount++;
              if (callCount == 1) {
                return Future<CreditNotePrint>.error(Exception('Load failed'));
              }
              return Future.value(_creditNotePrint());
            },
          ),
        ],
      );
      addTearDown(container.dispose);

      container.refresh(creditNotePrintByCodeProvider('CN-REC-001'));
      expect(callCount, equals(1));

      container.refresh(creditNotePrintByCodeProvider('CN-REC-001'));
      expect(callCount, equals(2));
    });

    test('passes code argument to creditNotePrintByCodeProvider', () {
      String? capturedCode;

      final container = ProviderContainer(
        overrides: [
          creditNotePrintByCodeProvider.overrideWith(
            (ref, code) {
              capturedCode = code;
              return Future.value(_creditNotePrint(code: code));
            },
          ),
        ],
      );
      addTearDown(container.dispose);

      final fakeCode = 'TEST-CODE-123';
      container.refresh(creditNotePrintByCodeProvider(fakeCode));

      expect(capturedCode, equals(fakeCode));
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
