import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/entities/bank_account.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/entities/save_bank_account_request.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/use_cases/add_bank_account.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/use_cases/get_bank_accounts.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/presentation/controllers/bank_accounts_controller.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/presentation/pages/bank_accounts_page.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/presentation/widgets/bank_account_form.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/presentation/widgets/create_bank_account_sheet.dart';
import 'package:mocktail/mocktail.dart';

class MockGetBankAccounts extends Mock implements GetBankAccounts {}

class MockAddBankAccount extends Mock implements AddBankAccount {}

class _StubAuthController extends AuthController {
  _StubAuthController(this._state);

  final AuthControllerState _state;

  @override
  Future<AuthControllerState> build() async => _state;
}

class _TestApp extends StatelessWidget {
  const _TestApp({
    required this.getBankAccounts,
    required this.addBankAccount,
    required this.session,
  });

  final MockGetBankAccounts getBankAccounts;
  final MockAddBankAccount addBankAccount;
  final AuthSession session;

  @override
  Widget build(BuildContext context) {
    return ProviderScope(
      overrides: [
        getBankAccountsUseCaseProvider.overrideWithValue(getBankAccounts),
        addBankAccountUseCaseProvider.overrideWithValue(addBankAccount),
        authControllerProvider.overrideWith(
          () => _StubAuthController(AuthControllerState(session: session)),
        ),
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

AuthSession _session(String role) {
  return AuthSession(
    accessToken: 'access-token',
    refreshToken: 'refresh-token',
    accessTokenExpiresAt: DateTime.utc(2026, 6),
    refreshTokenExpiresAt: DateTime.utc(2026, 7),
    user: const AuthUser(
      id: 'user-1',
      email: 'owner@example.com',
      phoneNumber: null,
      firstName: 'Alex',
      lastName: 'Smith',
      language: 'en-IN',
    ),
    activeShopId: 'shop-1',
    shops: [
      UserShop(
        shopId: 'shop-1',
        shopName: 'Primary Shop',
        role: role,
        isDefault: true,
        lastUsedAt: DateTime.utc(2026, 6),
      ),
    ],
    rememberMe: false,
  );
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
  late MockAddBankAccount addBankAccount;

  setUpAll(() {
    registerFallbackValue(
      const SaveBankAccountRequest(
        bankName: 'Fallback Bank',
        accountNumber: '0',
        accountType: 'Savings',
      ),
    );
  });

  setUp(() {
    getBankAccounts = MockGetBankAccounts();
    addBankAccount = MockAddBankAccount();
  });

  testWidgets('shows loading while accounts are requested', (tester) async {
    final request = Completer<List<BankAccount>>();
    when(() => getBankAccounts()).thenAnswer((_) => request.future);

    await tester.pumpWidget(
      _TestApp(
        getBankAccounts: getBankAccounts,
        addBankAccount: addBankAccount,
        session: _session('Owner'),
      ),
    );
    await tester.pump();

    expect(find.byType(CircularProgressIndicator), findsOneWidget);
  });

  testWidgets('shows account cards with masked number and optional details', (
    tester,
  ) async {
    when(() => getBankAccounts()).thenAnswer((_) async => [_account]);

    await tester.pumpWidget(
      _TestApp(
        getBankAccounts: getBankAccounts,
        addBankAccount: addBankAccount,
        session: _session('Owner'),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Acme Bank'), findsOneWidget);
    expect(find.text('********9012'), findsOneWidget);
    expect(find.text('123456789012'), findsNothing);
    expect(find.text('IFSC: ACME0001234'), findsOneWidget);
    expect(find.text('Account holder: Alex Smith'), findsOneWidget);
  });

  testWidgets('shows empty state when no accounts exist', (tester) async {
    when(() => getBankAccounts()).thenAnswer((_) async => []);

    await tester.pumpWidget(
      _TestApp(
        getBankAccounts: getBankAccounts,
        addBankAccount: addBankAccount,
        session: _session('Owner'),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('No bank accounts found'), findsOneWidget);
  });

  testWidgets('shows retry and reloads after a failure', (tester) async {
    when(() => getBankAccounts()).thenAnswer(
      (_) async => throw AppException(
        failure: const Failure.unknown(message: 'offline'),
      ),
    );

    await tester.pumpWidget(
      _TestApp(
        getBankAccounts: getBankAccounts,
        addBankAccount: addBankAccount,
        session: _session('Owner'),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Unable to load bank accounts'), findsOneWidget);
    expect(find.text('Retry'), findsOneWidget);

    when(() => getBankAccounts()).thenAnswer((_) async => [_account]);
    await tester.tap(find.text('Retry'));
    await tester.pumpAndSettle();

    expect(find.text('Acme Bank'), findsOneWidget);
    verify(() => getBankAccounts()).called(2);
  });

  testWidgets('shows the add action only to an owner', (tester) async {
    when(() => getBankAccounts()).thenAnswer((_) async => []);

    await tester.pumpWidget(
      _TestApp(
        getBankAccounts: getBankAccounts,
        addBankAccount: addBankAccount,
        session: _session('Owner'),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.byKey(BankAccountsPage.addBankAccountFabKey), findsOneWidget);

    await tester.pumpWidget(const SizedBox.shrink());
    await tester.pumpAndSettle();
    await tester.pumpWidget(
      _TestApp(
        getBankAccounts: getBankAccounts,
        addBankAccount: addBankAccount,
        session: _session('Staff'),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.byKey(BankAccountsPage.addBankAccountFabKey), findsNothing);
  });

  testWidgets('owner creates an account and sees the refreshed card', (
    tester,
  ) async {
    var requests = 0;
    when(() => getBankAccounts()).thenAnswer((_) async {
      requests += 1;
      return requests == 1
          ? []
          : [
              const BankAccount(
                id: 'account-2',
                bankName: 'New Bank',
                accountNumber: '12345678',
                accountType: 'Savings',
              ),
            ];
    });
    when(() => addBankAccount(any())).thenAnswer((_) async {});

    await tester.pumpWidget(
      _TestApp(
        getBankAccounts: getBankAccounts,
        addBankAccount: addBankAccount,
        session: _session('Owner'),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(BankAccountsPage.addBankAccountFabKey));
    await tester.pumpAndSettle();

    await tester.enterText(
      find.byKey(BankAccountForm.bankNameFieldKey),
      ' New Bank ',
    );
    await tester.enterText(
      find.byKey(BankAccountForm.accountNumberFieldKey),
      '12345678',
    );
    await tester.tap(find.byKey(BankAccountForm.accountTypeFieldKey));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Savings').last);
    await tester.pumpAndSettle();
    await tester.ensureVisible(find.byKey(BankAccountForm.submitButtonKey));
    await tester.tap(find.byKey(BankAccountForm.submitButtonKey));
    await tester.pumpAndSettle();

    expect(find.text('Bank account created successfully.'), findsOneWidget);
    expect(find.text('New Bank'), findsOneWidget);
    expect(find.byType(CreateBankAccountSheet), findsNothing);
    verify(
      () => addBankAccount(
        const SaveBankAccountRequest(
          bankName: 'New Bank',
          accountNumber: '12345678',
          accountType: 'Savings',
        ),
      ),
    ).called(1);
    verify(() => getBankAccounts()).called(greaterThanOrEqualTo(2));
  });

  testWidgets('keeps the sheet and inputs open after a failed submission', (
    tester,
  ) async {
    when(() => getBankAccounts()).thenAnswer((_) async => []);
    when(() => addBankAccount(any())).thenThrow(
      AppException(failure: const Failure.network()),
    );

    await tester.pumpWidget(
      _TestApp(
        getBankAccounts: getBankAccounts,
        addBankAccount: addBankAccount,
        session: _session('Owner'),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(BankAccountsPage.addBankAccountFabKey));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(BankAccountForm.bankNameFieldKey),
      'Failed Bank',
    );
    await tester.enterText(
      find.byKey(BankAccountForm.accountNumberFieldKey),
      '12345678',
    );
    await tester.tap(find.byKey(BankAccountForm.accountTypeFieldKey));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Savings').last);
    await tester.pumpAndSettle();
    await tester.ensureVisible(find.byKey(BankAccountForm.submitButtonKey));
    await tester.tap(find.byKey(BankAccountForm.submitButtonKey));
    await tester.pumpAndSettle();

    expect(find.byType(CreateBankAccountSheet), findsOneWidget);
    expect(
      find.text('Unable to connect. Please check your network.'),
      findsOneWidget,
    );
    expect(find.text('Failed Bank'), findsOneWidget);
    verify(() => addBankAccount(any())).called(1);
  });
}
