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
import 'package:intelibill_mobile/src/features/users/presentation/widgets/edit_shop_user_sheet.dart';
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

const _editableUser = ShopUser(
  userId: 'user-1',
  firstName: 'Alice',
  lastName: 'Sharma',
  email: 'alice@example.com',
  phoneNumber: '9876543210',
  role: 'Manager',
  isLoginEnabled: true,
  shopIds: ['shop-1'],
);

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

  Widget buildSheet({required ShopUser user}) {
    return ProviderScope(
      overrides: [
        getShopUsersUseCaseProvider.overrideWithValue(getShopUsers),
        addShopUserUseCaseProvider.overrideWithValue(addShopUser),
        editShopUserUseCaseProvider.overrideWithValue(editShopUser),
        authControllerProvider.overrideWith(
          () => _StubAuthController(AuthControllerState(session: _ownerSession())),
        ),
      ],
      child: MaterialApp(
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: AppLocalizations.supportedLocales,
        home: Scaffold(body: EditShopUserSheet(user: user)),
      ),
    );
  }

  testWidgets('pre-fills form from user and validates required fields', (
    tester,
  ) async {
    when(() => getShopUsers()).thenAnswer((_) async => []);

    await tester.pumpWidget(buildSheet(user: _editableUser));
    await tester.pumpAndSettle();

    expect(find.text('Alice'), findsOneWidget);
    expect(find.text('Sharma'), findsOneWidget);
    expect(find.text('alice@example.com'), findsOneWidget);
    expect(find.text('9876543210'), findsOneWidget);

    await tester.enterText(find.byType(TextFormField).first, '');
    await tester.ensureVisible(find.byKey(EditShopUserSheet.submitButtonKey));
    await tester.tap(find.byKey(EditShopUserSheet.submitButtonKey));
    await tester.pumpAndSettle();

    expect(find.text('First name is required.'), findsOneWidget);
  });

  testWidgets('keeps sheet open and shows failure message', (tester) async {
    when(() => getShopUsers()).thenAnswer((_) async => []);
    when(
      () => editShopUser(
        userId: any(named: 'userId'),
        email: any(named: 'email'),
        firstName: any(named: 'firstName'),
        lastName: any(named: 'lastName'),
        phoneNumber: any(named: 'phoneNumber'),
        role: any(named: 'role'),
        isLoginEnabled: any(named: 'isLoginEnabled'),
        shopIds: any(named: 'shopIds'),
      ),
    ).thenThrow(
      AppException(
        failure: const Failure.validation(message: 'Cannot disable login'),
      ),
    );

    await tester.pumpWidget(buildSheet(user: _editableUser));
    await tester.pumpAndSettle();

    await tester.ensureVisible(find.byKey(EditShopUserSheet.submitButtonKey));
    await tester.tap(find.byKey(EditShopUserSheet.submitButtonKey));
    await tester.pumpAndSettle();

    expect(find.text('Cannot disable login'), findsOneWidget);
    expect(find.byKey(EditShopUserSheet.submitButtonKey), findsOneWidget);
  });

  testWidgets('submits updated user and closes sheet', (tester) async {
    when(() => getShopUsers()).thenAnswer((_) async => []);
    when(
      () => editShopUser(
        userId: any(named: 'userId'),
        email: any(named: 'email'),
        firstName: any(named: 'firstName'),
        lastName: any(named: 'lastName'),
        phoneNumber: any(named: 'phoneNumber'),
        role: any(named: 'role'),
        isLoginEnabled: any(named: 'isLoginEnabled'),
        shopIds: any(named: 'shopIds'),
      ),
    ).thenAnswer(
      (_) async => const ShopUser(
        userId: 'user-1',
        firstName: 'Alice',
        lastName: 'Sharma',
        email: 'alice@example.com',
        phoneNumber: '9876543210',
        role: 'Staff',
        isLoginEnabled: false,
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
                        builder: (context) =>
                            const EditShopUserSheet(user: _editableUser),
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

    await tester.ensureVisible(find.byType(SwitchListTile));
    await tester.tap(find.byType(SwitchListTile));
    await tester.pumpAndSettle();

    await tester.ensureVisible(find.byKey(EditShopUserSheet.submitButtonKey));
    await tester.tap(find.byKey(EditShopUserSheet.submitButtonKey));
    await tester.pumpAndSettle();

    expect(find.byKey(EditShopUserSheet.submitButtonKey), findsNothing);
    verify(
      () => editShopUser(
        userId: 'user-1',
        email: 'alice@example.com',
        firstName: 'Alice',
        lastName: 'Sharma',
        phoneNumber: '9876543210',
        role: 'Manager',
        isLoginEnabled: false,
        shopIds: ['shop-1'],
      ),
    ).called(1);
  });
}
