import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/entities/bank_account.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/use_cases/get_bank_accounts.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/presentation/controllers/bank_accounts_controller.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/presentation/pages/bank_accounts_page.dart';
import 'package:mocktail/mocktail.dart';

class MockGetBankAccounts extends Mock implements GetBankAccounts {}

class _TestApp extends StatelessWidget {
  const _TestApp({required this.getBankAccounts});

  final MockGetBankAccounts getBankAccounts;

  @override
  Widget build(BuildContext context) {
    return ProviderScope(
      overrides: [
        getBankAccountsUseCaseProvider.overrideWithValue(getBankAccounts),
      ],
      child: MaterialApp(
        theme: AppTheme.lightTheme,
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: AppLocalizations.supportedLocales,
        home: const BankAccountsPage(),
      ),
    );
  }
}

const _account = BankAccount(
  id: 'account-1',
  bankName: 'Acme Bank',
  accountNumber: '123456789012',
  accountType: 'Current',
  ifscCode: 'ACME0001234',
  accountHolderName: 'Alex Smith',
);

void main() {
  late MockGetBankAccounts getBankAccounts;

  setUp(() {
    getBankAccounts = MockGetBankAccounts();
  });

  testWidgets('shows loading while accounts are requested', (tester) async {
    final request = Completer<List<BankAccount>>();
    when(() => getBankAccounts()).thenAnswer((_) => request.future);

    await tester.pumpWidget(_TestApp(getBankAccounts: getBankAccounts));
    await tester.pump();

    expect(find.byType(CircularProgressIndicator), findsOneWidget);
  });

  testWidgets('shows account cards with masked number and optional details', (
    tester,
  ) async {
    when(() => getBankAccounts()).thenAnswer((_) async => [_account]);

    await tester.pumpWidget(_TestApp(getBankAccounts: getBankAccounts));
    await tester.pumpAndSettle();

    expect(find.text('Acme Bank'), findsOneWidget);
    expect(find.text('********9012'), findsOneWidget);
    expect(find.text('123456789012'), findsNothing);
    expect(find.text('IFSC: ACME0001234'), findsOneWidget);
    expect(find.text('Account holder: Alex Smith'), findsOneWidget);
  });

  testWidgets('shows empty state when no accounts exist', (tester) async {
    when(() => getBankAccounts()).thenAnswer((_) async => []);

    await tester.pumpWidget(_TestApp(getBankAccounts: getBankAccounts));
    await tester.pumpAndSettle();

    expect(find.text('No bank accounts found'), findsOneWidget);
  });

  testWidgets('shows retry and reloads after a failure', (tester) async {
    when(() => getBankAccounts()).thenAnswer(
      (_) async => throw AppException(
        failure: const Failure.unknown(message: 'offline'),
      ),
    );

    await tester.pumpWidget(_TestApp(getBankAccounts: getBankAccounts));
    await tester.pumpAndSettle();

    expect(find.text('Unable to load bank accounts'), findsOneWidget);
    expect(find.text('Retry'), findsOneWidget);

    when(() => getBankAccounts()).thenAnswer((_) async => [_account]);
    await tester.tap(find.text('Retry'));
    await tester.pumpAndSettle();

    expect(find.text('Acme Bank'), findsOneWidget);
    verify(() => getBankAccounts()).called(2);
  });
}
