import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/users/domain/entities/shop_user.dart';
import 'package:intelibill_mobile/src/features/users/domain/use_cases/add_shop_user.dart';
import 'package:intelibill_mobile/src/features/users/domain/use_cases/edit_shop_user.dart';
import 'package:intelibill_mobile/src/features/users/domain/use_cases/get_shop_users.dart';
import 'package:intelibill_mobile/src/features/users/presentation/controllers/users_controller.dart';
import 'package:intelibill_mobile/src/features/users/presentation/pages/users_page.dart';
import 'package:intelibill_mobile/src/features/users/presentation/widgets/add_shop_user_sheet.dart';
import 'package:mocktail/mocktail.dart';

class _StubUsersController extends UsersController {
  _StubUsersController(this._state);

  final UsersState _state;

  @override
  UsersState build() => _state;
}

class _StubAuthController extends AuthController {
  _StubAuthController(this._state);

  final AuthControllerState _state;

  @override
  Future<AuthControllerState> build() async => _state;
}

class MockGetShopUsers extends Mock implements GetShopUsers {}

class MockAddShopUser extends Mock implements AddShopUser {}

class MockEditShopUser extends Mock implements EditShopUser {}

const _loadedState = UsersState(
  users: [
    ShopUser(
      userId: 'user-1',
      firstName: 'Alice',
      lastName: 'Sharma',
      email: 'alice@example.com',
      phoneNumber: '9876543210',
      role: 'Manager',
      isLoginEnabled: true,
      shopIds: ['shop-1'],
    ),
    ShopUser(
      userId: 'user-2',
      firstName: 'Bob',
      lastName: 'Kumar',
      email: 'bob@example.com',
      phoneNumber: '9123456789',
      role: 'Owner',
      isLoginEnabled: true,
      shopIds: ['shop-1'],
    ),
    ShopUser(
      userId: 'user-3',
      firstName: 'Carol',
      lastName: 'Das',
      email: 'carol@example.com',
      phoneNumber: '9000000001',
      role: 'Staff',
      isLoginEnabled: false,
      shopIds: ['shop-1'],
    ),
  ],
);

Widget _buildApp({
  required UsersState usersState,
  AuthSession? session,
}) {
  return ProviderScope(
    overrides: [
      usersControllerProvider.overrideWith(
        () => _StubUsersController(usersState),
      ),
      authControllerProvider.overrideWith(
        () => _StubAuthController(AuthControllerState(session: session)),
      ),
    ],
    child: MaterialApp(
      theme: AppTheme.lightTheme,
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      home: const UsersPage(),
    ),
  );
}

Widget _buildAppWithOverrides({
  required AuthSession session,
  required MockGetShopUsers getShopUsers,
  required MockAddShopUser addShopUser,
  required MockEditShopUser editShopUser,
}) {
  return ProviderScope(
    overrides: [
      getShopUsersUseCaseProvider.overrideWithValue(getShopUsers),
      addShopUserUseCaseProvider.overrideWithValue(addShopUser),
      editShopUserUseCaseProvider.overrideWithValue(editShopUser),
      authControllerProvider.overrideWith(
        () => _StubAuthController(AuthControllerState(session: session)),
      ),
    ],
    child: MaterialApp(
      theme: AppTheme.lightTheme,
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      home: const UsersPage(),
    ),
  );
}

AuthSession _ownerSession({String role = 'Owner'}) {
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
        role: role,
        isDefault: true,
        lastUsedAt: DateTime.utc(2026, 5, 12, 10),
      ),
    ],
    rememberMe: false,
  );
}

void main() {
  group('UsersPage', () {
    testWidgets('shows loading indicator when isLoading is true', (
      tester,
    ) async {
      await tester.pumpWidget(
        _buildApp(
          usersState: const UsersState(isLoading: true),
          session: _ownerSession(),
        ),
      );

      expect(find.byType(CircularProgressIndicator), findsOneWidget);
    });

    testWidgets('shows empty state when user list is empty', (tester) async {
      await tester.pumpWidget(
        _buildApp(usersState: const UsersState(), session: _ownerSession()),
      );
      await tester.pumpAndSettle();

      expect(find.text('No users found'), findsOneWidget);
      expect(
        find.text(
          'Start by adding a manager or sales person for this shop.',
        ),
        findsOneWidget,
      );
    });

    testWidgets('shows error state with retry button when error is set', (
      tester,
    ) async {
      await tester.pumpWidget(
        _buildApp(
          usersState: const UsersState(failure: NetworkFailure()),
          session: _ownerSession(),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Unable to load users'), findsOneWidget);
      expect(
        find.text('Unable to connect. Please check your network.'),
        findsOneWidget,
      );
      expect(find.widgetWithText(FilledButton, 'Retry'), findsOneWidget);
    });

    testWidgets('shows user cards when users are loaded', (tester) async {
      await tester.pumpWidget(
        _buildApp(usersState: _loadedState, session: _ownerSession()),
      );
      await tester.pumpAndSettle();

      expect(find.text('Alice Sharma'), findsOneWidget);
      expect(find.text('9876543210'), findsOneWidget);
      expect(find.text('alice@example.com'), findsOneWidget);
      expect(find.text('Bob Kumar'), findsOneWidget);
    });

    testWidgets('shows role and login status chips', (tester) async {
      await tester.pumpWidget(
        _buildApp(usersState: _loadedState, session: _ownerSession()),
      );
      await tester.pumpAndSettle();

      expect(find.text('Manager'), findsOneWidget);
      expect(find.text('Owner'), findsOneWidget);
      expect(find.text('Staff'), findsOneWidget);
      expect(find.text('Enabled'), findsNWidgets(2));
      expect(find.text('Disabled'), findsOneWidget);
    });

    testWidgets('shows add FAB for owner and edit for non-owner users only', (
      tester,
    ) async {
      await tester.pumpWidget(
        _buildApp(usersState: _loadedState, session: _ownerSession()),
      );
      await tester.pumpAndSettle();

      expect(find.byKey(UsersPage.addUserFabKey), findsOneWidget);
      expect(find.byIcon(Icons.edit_outlined), findsNWidgets(2));
    });

    testWidgets('hides add FAB and edit buttons for non-owner', (tester) async {
      await tester.pumpWidget(
        _buildApp(
          usersState: _loadedState,
          session: _ownerSession(role: 'Manager'),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.byKey(UsersPage.addUserFabKey), findsNothing);
      expect(find.byIcon(Icons.edit_outlined), findsNothing);
    });

    testWidgets('shows search field', (tester) async {
      await tester.pumpWidget(
        _buildApp(usersState: _loadedState, session: _ownerSession()),
      );
      await tester.pumpAndSettle();

      expect(find.byType(TextField), findsOneWidget);
    });

    testWidgets('shows RefreshIndicator in loaded state', (tester) async {
      await tester.pumpWidget(
        _buildApp(usersState: _loadedState, session: _ownerSession()),
      );
      await tester.pumpAndSettle();

      expect(find.byType(RefreshIndicator), findsOneWidget);
    });

    testWidgets('pull-to-refresh calls refresh on controller', (tester) async {
      var refreshCount = 0;

      final controller = _CountingRefreshController(_loadedState, () {
        refreshCount++;
      });

      await tester.pumpWidget(
        ProviderScope(
          overrides: [
            usersControllerProvider.overrideWith(() => controller),
            authControllerProvider.overrideWith(
              () => _StubAuthController(
                AuthControllerState(session: _ownerSession()),
              ),
            ),
          ],
          child: const MaterialApp(
            localizationsDelegates: AppLocalizations.localizationsDelegates,
            supportedLocales: AppLocalizations.supportedLocales,
            home: UsersPage(),
          ),
        ),
      );
      await tester.pumpAndSettle();

      await tester.fling(find.byType(ListView), const Offset(0, 300), 1000);
      await tester.pumpAndSettle();

      expect(refreshCount, greaterThanOrEqualTo(1));
    });

    testWidgets('filters shown users based on search query', (tester) async {
      final searchState = UsersState(
        users: _loadedState.users,
        searchQuery: 'Alice',
      );

      await tester.pumpWidget(
        _buildApp(usersState: searchState, session: _ownerSession()),
      );
      await tester.pumpAndSettle();

      expect(find.text('Alice Sharma'), findsOneWidget);
      expect(find.text('Bob Kumar'), findsNothing);
    });

    testWidgets('adds user and shows success snackbar', (tester) async {
      final getShopUsers = MockGetShopUsers();
      final addShopUser = MockAddShopUser();
      final editShopUser = MockEditShopUser();

      when(getShopUsers.call).thenAnswer((_) async => _loadedState.users);
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
          firstName: 'New',
          lastName: 'User',
          email: 'new@example.com',
          phoneNumber: '9000000000',
          role: 'Staff',
          isLoginEnabled: true,
          shopIds: ['shop-1'],
        ),
      );

      await tester.pumpWidget(
        _buildAppWithOverrides(
          session: _ownerSession(),
          getShopUsers: getShopUsers,
          addShopUser: addShopUser,
          editShopUser: editShopUser,
        ),
      );
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(UsersPage.addUserFabKey));
      await tester.pumpAndSettle();

      await tester.enterText(
        find.byKey(AddShopUserSheet.firstNameFieldKey),
        'New',
      );
      await tester.enterText(
        find.byKey(AddShopUserSheet.lastNameFieldKey),
        'User',
      );
      await tester.enterText(
        find.byKey(AddShopUserSheet.emailFieldKey),
        'new@example.com',
      );
      await tester.enterText(
        find.byKey(AddShopUserSheet.phoneFieldKey),
        '9000000000',
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

      expect(find.text('User added successfully.'), findsOneWidget);
      expect(find.byKey(AddShopUserSheet.firstNameFieldKey), findsNothing);
      verify(getShopUsers.call).called(greaterThanOrEqualTo(2));
    });
  });
}

class _CountingRefreshController extends UsersController {
  _CountingRefreshController(this._state, this._onRefresh);

  final UsersState _state;
  final VoidCallback _onRefresh;

  @override
  UsersState build() => _state;

  @override
  Future<void> refresh() async {
    _onRefresh();
  }
}
