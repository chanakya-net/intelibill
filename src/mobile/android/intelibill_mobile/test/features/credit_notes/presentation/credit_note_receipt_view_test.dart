import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/formatting/currency_formatter.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/entities/credit_note_print.dart';
import 'package:intelibill_mobile/src/features/credit_notes/presentation/widgets/credit_note_receipt_view.dart';
import 'package:intl/intl.dart';

void main() {
  group('CreditNoteReceiptView', () {
    testWidgets('renders active credit note details', (tester) async {
      await tester.pumpWidget(_buildReceiptApp(note: _activeReceipt));
      await tester.pumpAndSettle();

      expect(find.text(_activeReceipt.code), findsNWidgets(2));
      expect(find.text(_activeReceipt.invoiceNumber), findsOneWidget);
      expect(find.text(_activeReceipt.returnNumber), findsOneWidget);
      expect(find.text(_activeReceipt.customerDisplayName), findsOneWidget);
      expect(find.text(_activeReceipt.reason), findsOneWidget);
      expect(
        find.text(formatInr(_activeReceipt.originalAmount)),
        findsOneWidget,
      );
      expect(
        find.text(formatInr(_activeReceipt.availableBalance)),
        findsOneWidget,
      );
      expect(find.text('Active'), findsOneWidget);
    });

    testWidgets('renders expired status with expiry date', (tester) async {
      await tester.pumpWidget(_buildReceiptApp(note: _expiredReceipt));
      await tester.pumpAndSettle();

      expect(find.text('Expired'), findsOneWidget);
      expect(
        find.text(
          DateFormat('dd MMM yyyy, h:mm a').format(_expiredReceipt.expiresAt!),
        ),
        findsOneWidget,
      );
    });

    testWidgets('renders void status and void reason', (tester) async {
      await tester.pumpWidget(_buildReceiptApp(note: _voidedReceipt));
      await tester.pumpAndSettle();

      expect(find.text('Voided'), findsOneWidget);
      expect(find.text(_voidedReceipt.voidReason!), findsOneWidget);
    });
  });
}

Widget _buildReceiptApp({required CreditNotePrint note}) {
  return ProviderScope(
    child: MaterialApp(
      theme: AppTheme.lightTheme,
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: const [Locale('en', 'IN')],
      home: CreditNoteReceiptView(note: note),
    ),
  );
}

CreditNotePrint _activeReceipt = CreditNotePrint(
  creditNoteId: 'cn-1',
  code: 'CN-ACTIVE',
  status: 'active',
  isUsable: true,
  originalAmount: 1000,
  availableBalance: 800,
  issuedAt: DateTime(2026, 6, 10, 10),
  expiresAt: DateTime(2026, 7, 10, 10),
  saleId: 'sale-1',
  invoiceNumber: 'INV-001',
  saleReturnId: 'ret-1',
  returnNumber: 'RET-001',
  customerDisplayName: 'John',
  reason: 'Damaged item',
  voidReason: null,
);

CreditNotePrint _expiredReceipt = CreditNotePrint(
  creditNoteId: 'cn-2',
  code: 'CN-EXPIRED',
  status: 'expired',
  isUsable: false,
  originalAmount: 1000,
  availableBalance: 0,
  issuedAt: DateTime(2026, 5, 1, 10),
  expiresAt: DateTime(2026, 6, 1, 10),
  saleId: 'sale-2',
  invoiceNumber: 'INV-002',
  saleReturnId: 'ret-2',
  returnNumber: 'RET-002',
  customerDisplayName: 'Jane',
  reason: 'Wrong item',
  voidReason: null,
);

CreditNotePrint _voidedReceipt = CreditNotePrint(
  creditNoteId: 'cn-3',
  code: 'CN-VOID',
  status: 'voided',
  isUsable: false,
  originalAmount: 1200,
  availableBalance: 1200,
  issuedAt: DateTime(2026, 6, 1, 10),
  expiresAt: DateTime(2026, 7, 1, 10),
  saleId: 'sale-3',
  invoiceNumber: 'INV-003',
  saleReturnId: 'ret-3',
  returnNumber: 'RET-003',
  customerDisplayName: 'Alan',
  reason: 'Customer complaint',
  voidReason: 'Invalid return',
);
