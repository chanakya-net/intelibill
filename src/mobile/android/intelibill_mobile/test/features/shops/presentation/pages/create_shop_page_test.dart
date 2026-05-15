import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/add_bank_account_request.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/create_shop_request.dart';
import 'package:intelibill_mobile/src/features/shops/domain/use_cases/add_bank_account_use_case.dart';
import 'package:intelibill_mobile/src/features/shops/domain/use_cases/create_shop_use_case.dart';
import 'package:intelibill_mobile/src/features/shops/presentation/pages/create_shop_page.dart';
import 'package:intelibill_mobile/src/features/shops/presentation/widgets/bank_details_form.dart';
import 'package:intelibill_mobile/src/features/shops/presentation/widgets/shop_info_form.dart';
import 'package:intelibill_mobile/src/features/shops/shops_providers.dart';
import 'package:mocktail/mocktail.dart';

class MockCreateShopUseCase extends Mock implements CreateShopUseCase {}

class MockAddBankAccountUseCase extends Mock implements AddBankAccountUseCase {}

class _StubAuthController extends AuthController {
  _StubAuthController(this._initialState);

  final AuthControllerState _initialState;

  @override
  Future<AuthControllerState> build() async => _initialState;

  @override
  Future<void> applySession(AuthSession session) async {
    state = AsyncData(_initialState.copyWith(session: session));
  }
}

void main() {
  late MockCreateShopUseCase createShopUseCase;
  late MockAddBankAccountUseCase addBankAccountUseCase;

  setUpAll(() {
    registerFallbackValue(const CreateShopRequest(
      name: 'name',
      address: 'address',
      city: 'city',
      state: 'state',
      pincode: '123456',
    ));
    registerFallbackValue(const AddBankAccountRequest(
      bankName: 'Bank',
      accountNumber: '123',
      accountType: 'Savings',
      ifscCode: 'ABCD0123456',
      accountHolderName: 'John',
    ));
  });

  setUp(() {
    createShopUseCase = MockCreateShopUseCase();
    addBankAccountUseCase = MockAddBankAccountUseCase();
  });

  AuthSession fixtureSession() {
    return AuthSession(
      accessToken: 'access_token',
      refreshToken: 'refresh_token',
      accessTokenExpiresAt: DateTime.utc(2026, 1, 1),
      refreshTokenExpiresAt: DateTime.utc(2026, 1, 1),
      user: AuthUser(
        id: 'user-1',
        email: 'test@example.com',
        phoneNumber: null,
        firstName: 'John',
        lastName: 'Doe',
        language: 'en',
      ),
      activeShopId: 'shop-1',
      shops: null,
      rememberMe: false,
    );
  }

  Widget buildPage() {
    return ProviderScope(
      overrides: [
        authControllerProvider.overrideWith(
          () => _StubAuthController(const AuthControllerState()),
        ),
        createShopUseCaseProvider.overrideWith((ref) => createShopUseCase),
        addBankAccountUseCaseProvider.overrideWith((ref) => addBankAccountUseCase),
      ],
      child: MaterialApp(
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: AppLocalizations.supportedLocales,
        home: const Scaffold(
          body: CreateShopPage(),
        ),
      ),
    );
  }

  Future<void> fillShopInfo(WidgetTester tester) async {
    await tester.enterText(
      find.byKey(ShopInfoForm.shopNameFieldKey),
      'Acme Store',
    );
    await tester.enterText(
      find.byKey(ShopInfoForm.addressFieldKey),
      '12 Industrial Area',
    );
    await tester.enterText(find.byKey(ShopInfoForm.cityFieldKey), 'City');
    await tester.enterText(find.byKey(ShopInfoForm.stateFieldKey), 'State');
    await tester.enterText(find.byKey(ShopInfoForm.pincodeFieldKey), '123456');
    await tester.enterText(find.byKey(ShopInfoForm.contactPersonFieldKey), 'Ali');
    await tester.enterText(find.byKey(ShopInfoForm.mobileNumberFieldKey), '9876543210');
    await tester.enterText(find.byKey(ShopInfoForm.gstNumberFieldKey), '12ABCDE1234F1Z1');
  }

  Future<void> fillBankDetails(WidgetTester tester) async {
    await tester.enterText(
      find.byKey(BankDetailsForm.bankNameFieldKey),
      'State Bank',
    );
    await tester.enterText(
      find.byKey(BankDetailsForm.accountNumberFieldKey),
      '1234567890',
    );
    await tester.tap(find.byKey(BankDetailsForm.accountTypeFieldKey));
    await tester.pumpAndSettle();
    await tester.tap(find.text(BankDetailsForm.accountTypeSavings).last);
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(BankDetailsForm.ifscCodeFieldKey), 'ABCD0123456');
    await tester.enterText(
      find.byKey(BankDetailsForm.accountHolderNameFieldKey),
      'Store Owner',
    );
  }

  testWidgets('creates shop and bank account then moves to success step', (tester) async {
    when(() => createShopUseCase(any())).thenAnswer((_) async => fixtureSession());
    when(() => addBankAccountUseCase(any())).thenAnswer((_) async {});

    await tester.pumpWidget(buildPage());
    await tester.pumpAndSettle();

    await fillShopInfo(tester);
    await tester.tap(find.byKey(CreateShopPage.nextButtonKey));
    await tester.pumpAndSettle();

    await fillBankDetails(tester);
    await tester.tap(find.byKey(CreateShopPage.nextButtonKey));
    await tester.pumpAndSettle();

    expect(find.text('Your shop "Acme Store" is ready.'), findsOneWidget);
    expect(find.byKey(CreateShopPage.doneButtonKey), findsOneWidget);

    verify(() => createShopUseCase(any())).called(1);
    verify(
      () => addBankAccountUseCase(
        const AddBankAccountRequest(
          bankName: 'State Bank',
          accountNumber: '1234567890',
          accountType: 'Savings',
          ifscCode: 'ABCD0123456',
          accountHolderName: 'Store Owner',
        ),
      ),
    ).called(1);
  });

  testWidgets('skips bank details step without API call', (tester) async {
    when(() => createShopUseCase(any())).thenAnswer((_) async => fixtureSession());

    await tester.pumpWidget(buildPage());
    await tester.pumpAndSettle();

    await fillShopInfo(tester);
    await tester.tap(find.byKey(CreateShopPage.nextButtonKey));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(CreateShopPage.skipButtonKey));
    await tester.pumpAndSettle();

    expect(find.text('Your shop "Acme Store" is ready.'), findsOneWidget);
    verifyNever(() => addBankAccountUseCase(any()));
  });

  testWidgets('shows loading indicator while creating shop', (tester) async {
    final completer = Completer<AuthSession>();
    when(() => createShopUseCase(any())).thenAnswer((_) => completer.future);

    await tester.pumpWidget(buildPage());
    await tester.pumpAndSettle();

    await fillShopInfo(tester);
    await tester.tap(find.byKey(CreateShopPage.nextButtonKey));

    await tester.pump();
    expect(find.byType(CircularProgressIndicator), findsOneWidget);

    final nextButton =
        tester.widget<FilledButton>(find.byKey(CreateShopPage.nextButtonKey));
    expect(nextButton.onPressed, isNull);

    completer.complete(fixtureSession());
    await tester.pumpAndSettle();
  });

  testWidgets('shows snackbar on create failure', (tester) async {
    when(() => createShopUseCase(any())).thenThrow(
      AppException(failure: const Failure.validation(message: 'Invalid payload')),
    );

    await tester.pumpWidget(buildPage());
    await tester.pumpAndSettle();

    await fillShopInfo(tester);
    await tester.tap(find.byKey(CreateShopPage.nextButtonKey));
    await tester.pumpAndSettle();

    expect(find.text('Invalid payload'), findsOneWidget);
    expect(find.byKey(CreateShopPage.nextButtonKey), findsOneWidget);
  });
}
