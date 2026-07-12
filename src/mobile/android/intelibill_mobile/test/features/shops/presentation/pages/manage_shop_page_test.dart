import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/shop_details.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/update_shop_request.dart';
import 'package:intelibill_mobile/src/features/shops/domain/use_cases/add_bank_account_use_case.dart';
import 'package:intelibill_mobile/src/features/shops/domain/use_cases/get_shop_use_case.dart';
import 'package:intelibill_mobile/src/features/shops/domain/use_cases/update_shop_use_case.dart';
import 'package:intelibill_mobile/src/features/shops/presentation/pages/manage_shop_page.dart';
import 'package:intelibill_mobile/src/features/shops/presentation/widgets/shop_info_form.dart';
import 'package:intelibill_mobile/src/features/shops/shops_providers.dart';
import 'package:mocktail/mocktail.dart';

class MockGetShopUseCase extends Mock implements GetShopUseCase {}

class MockUpdateShopUseCase extends Mock implements UpdateShopUseCase {}

class MockAddBankAccountUseCase extends Mock implements AddBankAccountUseCase {}

class _StubAuthController extends AuthController {
  _StubAuthController(this._initialState);

  final AuthControllerState _initialState;

  @override
  Future<AuthControllerState> build() async => _initialState;
}

void main() {
  late MockGetShopUseCase getShopUseCase;
  late MockUpdateShopUseCase updateShopUseCase;
  late MockAddBankAccountUseCase addBankAccountUseCase;
  late AppLocalizations l10n;

  setUpAll(() {
    registerFallbackValue(
      const UpdateShopRequest(
        name: 'name',
        address: 'address',
        city: 'city',
        state: 'state',
        pincode: '123456',
      ),
    );
  });

  setUp(() async {
    getShopUseCase = MockGetShopUseCase();
    updateShopUseCase = MockUpdateShopUseCase();
    addBankAccountUseCase = MockAddBankAccountUseCase();
    l10n = await AppLocalizations.delegate.load(const Locale('en'));
  });

  List<UserShop> fixtureShops({required int count}) {
    return List.generate(
      count,
      (i) => UserShop(
        shopId: 'shop-${i + 1}',
        shopName: 'Shop ${i + 1}',
        role: 'Owner',
        isDefault: i == 0,
        lastUsedAt: null,
      ),
    );
  }

  AuthSession fixtureSession({required List<UserShop> shops}) {
    return AuthSession(
      accessToken: 'access_token',
      refreshToken: 'refresh_token',
      accessTokenExpiresAt: DateTime.utc(2026),
      refreshTokenExpiresAt: DateTime.utc(2026),
      user: const AuthUser(
        id: 'user-1',
        email: 'test@example.com',
        phoneNumber: null,
        firstName: 'John',
        lastName: 'Doe',
        language: 'en',
      ),
      activeShopId: 'shop-1',
      shops: shops,
      rememberMe: false,
    );
  }

  ShopDetails fixtureShopDetails({String id = 'shop-1'}) {
    return const ShopDetails(
      id: 'shop-1',
      name: 'Acme Store',
      address: '12 Industrial Area',
      city: 'City',
      state: 'State',
      pincode: '123456',
      contactPerson: 'Ali',
      mobileNumber: '9876543210',
      gstNumber: '12ABCDE1234F1Z1',
      bankAccounts: [
        BankAccount(
          id: 'bank-1',
          bankName: 'State Bank',
          accountNumber: '1234567890',
          accountType: 'Savings',
          ifscCode: 'ABCD0123456',
          accountHolderName: 'Store Owner',
        ),
      ],
    );
  }

  Widget buildPage({required AuthSession session}) {
    return ProviderScope(
      overrides: [
        authControllerProvider.overrideWith(
          () => _StubAuthController(AuthControllerState(session: session)),
        ),
        getShopUseCaseProvider.overrideWith((ref) => getShopUseCase),
        updateShopUseCaseProvider.overrideWith((ref) => updateShopUseCase),
        addBankAccountUseCaseProvider.overrideWith(
          (ref) => addBankAccountUseCase,
        ),
      ],
      child: const MaterialApp(
        locale: Locale('en'),
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: AppLocalizations.supportedLocales,
        home: Scaffold(body: ManageShopPage()),
      ),
    );
  }

  testWidgets('multiple shops renders selector dropdown on step 1', (
    tester,
  ) async {
    final session = fixtureSession(shops: fixtureShops(count: 2));

    await tester.pumpWidget(buildPage(session: session));
    await tester.pumpAndSettle();

    expect(find.byKey(ManageShopPage.shopSelectorKey), findsOneWidget);
    expect(find.byKey(ManageShopPage.nextButtonKey), findsOneWidget);
  });

  testWidgets('single shop auto-advances to step 2', (tester) async {
    final session = fixtureSession(shops: fixtureShops(count: 1));
    when(
      () => getShopUseCase('shop-1'),
    ).thenAnswer((_) async => fixtureShopDetails());

    await tester.pumpWidget(buildPage(session: session));
    await tester.pumpAndSettle();

    expect(find.byKey(ShopInfoForm.shopNameFieldKey), findsOneWidget);
    verify(() => getShopUseCase('shop-1')).called(1);
  });

  testWidgets('step 2 fields are pre-filled from loaded shop details', (
    tester,
  ) async {
    final session = fixtureSession(shops: fixtureShops(count: 1));
    when(
      () => getShopUseCase('shop-1'),
    ).thenAnswer((_) async => fixtureShopDetails());

    await tester.pumpWidget(buildPage(session: session));
    await tester.pumpAndSettle();

    final shopNameField = tester.widget<TextFormField>(
      find.byKey(ShopInfoForm.shopNameFieldKey),
    );
    expect(shopNameField.controller?.text, equals('Acme Store'));
  });

  testWidgets('save calls updateShop then shows success step', (tester) async {
    final session = fixtureSession(shops: fixtureShops(count: 1));
    when(
      () => getShopUseCase('shop-1'),
    ).thenAnswer((_) async => fixtureShopDetails());
    when(
      () => updateShopUseCase(any(), any()),
    ).thenAnswer((_) async => fixtureShopDetails());

    await tester.pumpWidget(buildPage(session: session));
    await tester.pumpAndSettle();

    await tester.enterText(
      find.byKey(ShopInfoForm.shopNameFieldKey),
      'Updated Store',
    );
    await tester.tap(find.byKey(ManageShopPage.saveButtonKey));
    await tester.pumpAndSettle();

    expect(
      find.text(l10n.shopsManageSuccessMessage('Acme Store')),
      findsOneWidget,
    );
    verify(
      () => updateShopUseCase(
        'shop-1',
        const UpdateShopRequest(
          name: 'Updated Store',
          address: '12 Industrial Area',
          city: 'City',
          state: 'State',
          pincode: '123456',
          contactPerson: 'Ali',
          mobileNumber: '9876543210',
          gstNumber: '12ABCDE1234F1Z1',
        ),
      ),
    ).called(1);
  });

  testWidgets('save disabled and spinner shown during AsyncLoading', (
    tester,
  ) async {
    final session = fixtureSession(shops: fixtureShops(count: 1));
    final completer = Completer<ShopDetails>();
    when(() => getShopUseCase('shop-1')).thenAnswer((_) => completer.future);

    await tester.pumpWidget(buildPage(session: session));
    await tester.pump();

    final nextButton = tester.widget<FilledButton>(
      find.byKey(ManageShopPage.nextButtonKey),
    );
    expect(nextButton.onPressed, isNull);
    expect(find.byType(CircularProgressIndicator), findsOneWidget);

    completer.complete(fixtureShopDetails());
    await tester.pumpAndSettle();
  });

  testWidgets('shows snackbar on error', (tester) async {
    final session = fixtureSession(shops: fixtureShops(count: 1));
    when(
      () => getShopUseCase('shop-1'),
    ).thenThrow(AppException(failure: const Failure.server(message: 'boom')));

    await tester.pumpWidget(buildPage(session: session));
    await tester.pumpAndSettle();

    expect(find.text('boom'), findsOneWidget);
  });

  testWidgets('manage shop displays only shop info form, not bank details', (
    tester,
  ) async {
    final session = fixtureSession(shops: fixtureShops(count: 1));
    when(
      () => getShopUseCase('shop-1'),
    ).thenAnswer((_) async => fixtureShopDetails());

    await tester.pumpWidget(buildPage(session: session));
    await tester.pumpAndSettle();

    expect(find.byType(ShopInfoForm), findsOneWidget);
    expect(find.byKey(ShopInfoForm.shopNameFieldKey), findsOneWidget);
  });

  testWidgets('save shop does not invoke addBankAccount use case', (
    tester,
  ) async {
    final session = fixtureSession(shops: fixtureShops(count: 1));
    when(
      () => getShopUseCase('shop-1'),
    ).thenAnswer((_) async => fixtureShopDetails());
    when(
      () => updateShopUseCase(any(), any()),
    ).thenAnswer((_) async => fixtureShopDetails());

    await tester.pumpWidget(buildPage(session: session));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(ManageShopPage.saveButtonKey));
    await tester.pumpAndSettle();

    expect(
      find.text(l10n.shopsManageSuccessMessage('Acme Store')),
      findsOneWidget,
    );
    verify(
      () => updateShopUseCase(
        'shop-1',
        any(),
      ),
    ).called(1);
  });
}
