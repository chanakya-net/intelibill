import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/add_bank_account_request.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/create_shop_request.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/shop_details.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/update_shop_request.dart';
import 'package:intelibill_mobile/src/features/shops/domain/use_cases/add_bank_account_use_case.dart';
import 'package:intelibill_mobile/src/features/shops/domain/use_cases/create_shop_use_case.dart';
import 'package:intelibill_mobile/src/features/shops/domain/use_cases/get_shop_use_case.dart';
import 'package:intelibill_mobile/src/features/shops/domain/use_cases/update_shop_use_case.dart';
import 'package:intelibill_mobile/src/features/shops/presentation/controllers/shop_controller.dart';
import 'package:intelibill_mobile/src/features/shops/shops_providers.dart';
import 'package:mocktail/mocktail.dart';

class MockCreateShopUseCase extends Mock implements CreateShopUseCase {}

class MockUpdateShopUseCase extends Mock implements UpdateShopUseCase {}

class MockAddBankAccountUseCase extends Mock implements AddBankAccountUseCase {}

class MockGetShopUseCase extends Mock implements GetShopUseCase {}

class _StubAuthController extends AuthController {
  _StubAuthController(this._initialState);

  final AuthControllerState _initialState;
  int applySessionCalls = 0;
  AuthSession? appliedSession;

  @override
  Future<AuthControllerState> build() async => _initialState;

  @override
  Future<void> applySession(AuthSession session) async {
    applySessionCalls += 1;
    appliedSession = session;
    state = AsyncData(_initialState.copyWith(session: session));
  }
}

void main() {
  late MockCreateShopUseCase createShopUseCase;
  late MockUpdateShopUseCase updateShopUseCase;
  late MockAddBankAccountUseCase addBankAccountUseCase;
  late MockGetShopUseCase getShopUseCase;

  AuthSession fixtureSession() => AuthSession(
        accessToken: 'access_token',
        refreshToken: 'refresh_token',
        accessTokenExpiresAt: DateTime.utc(2026, 5, 15, 10),
        refreshTokenExpiresAt: DateTime.utc(2026, 6, 15, 10),
        user: const AuthUser(
          id: 'user-1',
          email: 'test@example.com',
          phoneNumber: null,
          firstName: 'John',
          lastName: 'Doe',
          language: 'en-IN',
        ),
        activeShopId: null,
        shops: null,
        rememberMe: false,
      );

  ShopDetails fixtureShopDetails() => const ShopDetails(
        id: 'shop-1',
        name: 'Shop',
        address: 'Addr',
        city: 'City',
        state: 'State',
        pincode: '123456',
        bankAccounts: [],
      );

  setUp(() {
    createShopUseCase = MockCreateShopUseCase();
    updateShopUseCase = MockUpdateShopUseCase();
    addBankAccountUseCase = MockAddBankAccountUseCase();
    getShopUseCase = MockGetShopUseCase();
  });

  test('createShop success applies returned session', () async {
    const request = CreateShopRequest(
      name: 'Shop',
      address: 'Addr',
      city: 'City',
      state: 'State',
      pincode: '123456',
    );
    final session = fixtureSession();
    when(() => createShopUseCase(request)).thenAnswer((_) async => session);

    final authController = _StubAuthController(const AuthControllerState());
    final container = ProviderContainer(
      overrides: [
        authControllerProvider.overrideWith(() => authController),
        createShopUseCaseProvider.overrideWith((ref) => createShopUseCase),
      ],
    );
    addTearDown(container.dispose);

    await container.read(shopControllerProvider.future);
    await container.read(shopControllerProvider.notifier).createShop(request);

    expect(container.read(shopControllerProvider).hasError, isFalse);
    expect(authController.applySessionCalls, equals(1));
    expect(authController.appliedSession, equals(session));
  });

  test('createShop failure transitions to AsyncError', () async {
    const request = CreateShopRequest(
      name: 'Shop',
      address: 'Addr',
      city: 'City',
      state: 'State',
      pincode: '123456',
    );
    when(() => createShopUseCase(request)).thenThrow(
      AppException(failure: const Failure.server(message: 'boom')),
    );

    final authController = _StubAuthController(const AuthControllerState());
    final container = ProviderContainer(
      overrides: [
        authControllerProvider.overrideWith(() => authController),
        createShopUseCaseProvider.overrideWith((ref) => createShopUseCase),
      ],
    );
    addTearDown(container.dispose);

    await container.read(shopControllerProvider.future);
    await container.read(shopControllerProvider.notifier).createShop(request);

    expect(container.read(shopControllerProvider), isA<AsyncError<void>>());
    expect(authController.applySessionCalls, equals(0));
  });

  test('updateShop success completes with AsyncData', () async {
    const shopId = 'shop-1';
    const request = UpdateShopRequest(
      name: 'Shop',
      address: 'Addr',
      city: 'City',
      state: 'State',
      pincode: '123456',
    );
    when(
      () => updateShopUseCase(shopId, request),
    ).thenAnswer((_) async => fixtureShopDetails());

    final container = ProviderContainer(
      overrides: [
        updateShopUseCaseProvider.overrideWith((ref) => updateShopUseCase),
      ],
    );
    addTearDown(container.dispose);

    await container.read(shopControllerProvider.future);
    await container
        .read(shopControllerProvider.notifier)
        .updateShop(shopId, request);

    expect(container.read(shopControllerProvider).hasError, isFalse);
  });

  test('updateShop failure transitions to AsyncError', () async {
    const shopId = 'shop-1';
    const request = UpdateShopRequest(
      name: 'Shop',
      address: 'Addr',
      city: 'City',
      state: 'State',
      pincode: '123456',
    );
    when(() => updateShopUseCase(shopId, request)).thenThrow(
      AppException(failure: const Failure.unknown(message: 'boom')),
    );

    final container = ProviderContainer(
      overrides: [
        updateShopUseCaseProvider.overrideWith((ref) => updateShopUseCase),
      ],
    );
    addTearDown(container.dispose);

    await container.read(shopControllerProvider.future);
    await container
        .read(shopControllerProvider.notifier)
        .updateShop(shopId, request);

    expect(container.read(shopControllerProvider), isA<AsyncError<void>>());
  });

  test('addBankAccount success completes with AsyncData', () async {
    const request = AddBankAccountRequest(
      bankName: 'Bank',
      accountNumber: '123',
      accountType: 'Savings',
      ifscCode: 'IFSC0001',
      accountHolderName: 'John',
    );
    when(() => addBankAccountUseCase(request)).thenAnswer(
      (_) async => Future<void>.value(),
    );

    final container = ProviderContainer(
      overrides: [
        addBankAccountUseCaseProvider.overrideWith(
          (ref) => addBankAccountUseCase,
        ),
      ],
    );
    addTearDown(container.dispose);

    await container.read(shopControllerProvider.future);
    await container
        .read(shopControllerProvider.notifier)
        .addBankAccount(request);

    expect(container.read(shopControllerProvider).hasError, isFalse);
  });

  test('addBankAccount failure transitions to AsyncError', () async {
    const request = AddBankAccountRequest(
      bankName: 'Bank',
      accountNumber: '123',
      accountType: 'Savings',
      ifscCode: 'IFSC0001',
      accountHolderName: 'John',
    );
  when(() => addBankAccountUseCase(request)).thenThrow(
      AppException(failure: const Failure.forbidden(message: 'nope')),
    );

    final container = ProviderContainer(
      overrides: [
        addBankAccountUseCaseProvider.overrideWith(
          (ref) => addBankAccountUseCase,
        ),
      ],
    );
    addTearDown(container.dispose);

    await container.read(shopControllerProvider.future);
    await container
        .read(shopControllerProvider.notifier)
        .addBankAccount(request);

    expect(container.read(shopControllerProvider), isA<AsyncError<void>>());
  });

  test('loadShop success returns shop details', () async {
    const shopId = 'shop-1';
    final details = fixtureShopDetails();
    when(() => getShopUseCase(shopId)).thenAnswer((_) async => details);

    final container = ProviderContainer(
      overrides: [
        getShopUseCaseProvider.overrideWith((ref) => getShopUseCase),
      ],
    );
    addTearDown(container.dispose);

    await container.read(shopControllerProvider.future);
    final result =
        await container.read(shopControllerProvider.notifier).loadShop(shopId);

    expect(container.read(shopControllerProvider).hasError, isFalse);
    expect(result, equals(details));
  });

  test('loadShop failure transitions to AsyncError and returns null', () async {
    const shopId = 'shop-1';
    when(() => getShopUseCase(shopId)).thenThrow(
      AppException(failure: const Failure.server(message: 'boom')),
    );

    final container = ProviderContainer(
      overrides: [
        getShopUseCaseProvider.overrideWith((ref) => getShopUseCase),
      ],
    );
    addTearDown(container.dispose);

    await container.read(shopControllerProvider.future);
    final result =
        await container.read(shopControllerProvider.notifier).loadShop(shopId);

    expect(container.read(shopControllerProvider), isA<AsyncError<void>>());
    expect(result, isNull);
  });
}
