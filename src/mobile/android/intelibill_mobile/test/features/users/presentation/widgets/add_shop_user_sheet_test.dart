import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/users/domain/entities/shop_user.dart';
import 'package:intelibill_mobile/src/features/users/domain/use_cases/add_shop_user.dart';
import 'package:intelibill_mobile/src/features/users/domain/use_cases/edit_shop_user.dart';
import 'package:intelibill_mobile/src/features/users/domain/use_cases/get_shop_users.dart';
import 'package:intelibill_mobile/src/features/users/presentation/controllers/users_controller.dart';
import 'package:intelibill_mobile/src/features/users/presentation/widgets/add_shop_user_sheet.dart';
import 'package:mocktail/mocktail.dart';

class MockGetShopUsers extends Mock implements GetShopUsers {}

class MockAddShopUser extends Mock implements AddShopUser {}

class MockEditShopUser extends Mock implements EditShopUser {}

class _StubAuthController extends AuthController {
  _StubAuthController(this._state);

  final AuthControllerState _state;

  @override
  Future<AuthControllerState> build() async => _state;
}

AuthSession _ownerSession() {
  return AuthSession(
    accessToken: 'access-token',
    refreshToken: 'refresh-token',
    accessTokenExpiresAt: DateTime.utc(2026, 5, 15, 10),
    refreshTokenExpiresAt: DateTime.utc(2026, 6, 15, 10),
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
        role: 'Owner',
        isDefault: true,
        lastUsedAt: DateTime.utc(2026, 5, 12, 10),
      ),
    ],
    rememberMe: false,
  );
}

void main() {
  late MockGetShopUsers getShopUsers;
  late MockAddShopUser addShopUser;
  late MockEditShopUser editShopUser;

  setUp(() {
    getShopUsers = MockGetShopUsers();
    addShopUser = MockAddShopUser();
    editShopUser = MockEditShopUser();
  });

  Widget buildSheet() {
    return ProviderScope(
      overrides: [
        getShopUsersUseCaseProvider.overrideWithValue(getShopUsers),
        addShopUserUseCaseProvider.overrideWithValue(addShopUser),
        editShopUserUseCaseProvider.overrideWithValue(editShopUser),
        authControllerProvider.overrideWith(
          () => _StubAuthController(AuthControllerState(session: _ownerSession())),
        ),
      ],
      child: const MaterialApp(
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: AppLocalizations.supportedLocales,
        home: Scaffold(body: AddShopUserSheet()),
      ),
    );
  }

  testWidgets('validates required fields, email, phone, and password', (
    tester,
  ) async {
    when(() => getShopUsers()).thenAnswer((_) async => []);

    await tester.pumpWidget(buildSheet());
    await tester.pumpAndSettle();

    await tester.ensureVisible(find.byKey(AddShopUserSheet.submitButtonKey));
    await tester.tap(find.byKey(AddShopUserSheet.submitButtonKey));
    await tester.pumpAndSettle();

    expect(find.text('First name is required.'), findsOneWidget);
    expect(find.text('Last name is required.'), findsOneWidget);
    expect(find.text('Email is required.'), findsOneWidget);
    expect(find.text('Mobile number is required.'), findsOneWidget);
    expect(find.text('Password is required.'), findsOneWidget);

    await tester.enterText(
      find.byKey(AddShopUserSheet.emailFieldKey),
      'not-an-email',
    );
    await tester.enterText(
      find.byKey(AddShopUserSheet.phoneFieldKey),
      '123',
    );
    await tester.enterText(
      find.byKey(AddShopUserSheet.passwordFieldKey),
      'short',
    );
    await tester.enterText(
      find.byKey(AddShopUserSheet.confirmPasswordFieldKey),
      'different',
    );
    await tester.ensureVisible(find.byKey(AddShopUserSheet.submitButtonKey));
    await tester.tap(find.byKey(AddShopUserSheet.submitButtonKey));
    await tester.pumpAndSettle();

    expect(find.text('Enter a valid email address.'), findsOneWidget);
    expect(find.text('Enter a valid phone number.'), findsOneWidget);
    expect(
      find.text('Password must be at least 8 characters.'),
      findsOneWidget,
    );
    expect(
      find.text('Password and confirm password must match.'),
      findsOneWidget,
    );
  });

  testWidgets('keeps sheet open and shows failure message', (tester) async {
    when(() => getShopUsers()).thenAnswer((_) async => []);
    when(
      () => addShopUser(
        shopIds: any(named: 'shopIds'),
        email: any(named: 'email'),
        firstName: any(named: 'firstName'),
        lastName: any(named: 'lastName'),
        phoneNumber: any(named: 'phoneNumber'),
        password: any(named: 'password'),
        confirmPassword: any(named: 'confirmPassword'),
        role: any(named: 'role'),
      ),
    ).thenThrow(
      AppException(
        failure: const Failure.validation(message: 'Invalid payload'),
      ),
    );

    await tester.pumpWidget(buildSheet());
    await tester.pumpAndSettle();

    await tester.enterText(
      find.byKey(AddShopUserSheet.firstNameFieldKey),
      'Alice',
    );
    await tester.enterText(
      find.byKey(AddShopUserSheet.lastNameFieldKey),
      'Sharma',
    );
    await tester.enterText(
      find.byKey(AddShopUserSheet.emailFieldKey),
      'alice@example.com',
    );
    await tester.enterText(
      find.byKey(AddShopUserSheet.phoneFieldKey),
      '+919876543210',
    );
    await tester.enterText(
      find.byKey(AddShopUserSheet.passwordFieldKey),
      'password1',
    );
    await tester.enterText(
      find.byKey(AddShopUserSheet.confirmPasswordFieldKey),
      'password1',
    );
    await tester.ensureVisible(find.byKey(AddShopUserSheet.submitButtonKey));
    await tester.tap(find.byKey(AddShopUserSheet.submitButtonKey));
    await tester.pumpAndSettle();

    expect(find.text('Invalid payload'), findsOneWidget);
    expect(find.byKey(AddShopUserSheet.firstNameFieldKey), findsOneWidget);
  });

  testWidgets('submits valid form and closes sheet', (tester) async {
    when(() => getShopUsers()).thenAnswer((_) async => []);
    when(
      () => addShopUser(
        shopIds: any(named: 'shopIds'),
        email: any(named: 'email'),
        firstName: any(named: 'firstName'),
        lastName: any(named: 'lastName'),
        phoneNumber: any(named: 'phoneNumber'),
        password: any(named: 'password'),
        confirmPassword: any(named: 'confirmPassword'),
        role: any(named: 'role'),
      ),
    ).thenAnswer(
      (_) async => const ShopUser(
        userId: 'user-10',
        firstName: 'Alice',
        lastName: 'Sharma',
        email: 'alice@example.com',
        phoneNumber: '+919876543210',
        role: 'Manager',
        isLoginEnabled: true,
        shopIds: ['shop-1'],
      ),
    );

    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          getShopUsersUseCaseProvider.overrideWithValue(getShopUsers),
          addShopUserUseCaseProvider.overrideWithValue(addShopUser),
          editShopUserUseCaseProvider.overrideWithValue(editShopUser),
          authControllerProvider.overrideWith(
            () => _StubAuthController(
              AuthControllerState(session: _ownerSession()),
            ),
          ),
        ],
        child: MaterialApp(
          localizationsDelegates: AppLocalizations.localizationsDelegates,
          supportedLocales: AppLocalizations.supportedLocales,
          home: Builder(
            builder: (context) {
              return Scaffold(
                body: Center(
                  child: ElevatedButton(
                    onPressed: () {
                      showModalBottomSheet<bool>(
                        context: context,
                        isScrollControlled: true,
                        builder: (context) => const AddShopUserSheet(),
                      );
                    },
                    child: const Text('Open'),
                  ),
                ),
              );
            },
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.text('Open'));
    await tester.pumpAndSettle();

    await tester.enterText(
      find.byKey(AddShopUserSheet.firstNameFieldKey),
      'Alice',
    );
    await tester.enterText(
      find.byKey(AddShopUserSheet.lastNameFieldKey),
      'Sharma',
    );
    await tester.enterText(
      find.byKey(AddShopUserSheet.emailFieldKey),
      'alice@example.com',
    );
    await tester.enterText(
      find.byKey(AddShopUserSheet.phoneFieldKey),
      '+919876543210',
    );
    await tester.enterText(
      find.byKey(AddShopUserSheet.passwordFieldKey),
      'password1',
    );
    await tester.enterText(
      find.byKey(AddShopUserSheet.confirmPasswordFieldKey),
      'password1',
    );
    await tester.ensureVisible(find.byKey(AddShopUserSheet.submitButtonKey));
    await tester.tap(find.byKey(AddShopUserSheet.submitButtonKey));
    await tester.pumpAndSettle();

    expect(find.byKey(AddShopUserSheet.firstNameFieldKey), findsNothing);
    verify(
      () => addShopUser(
        shopIds: ['shop-1'],
        email: 'alice@example.com',
        firstName: 'Alice',
        lastName: 'Sharma',
        phoneNumber: '+919876543210',
        password: 'password1',
        confirmPassword: 'password1',
        role: 'Manager',
      ),
    ).called(1);
  });
}
