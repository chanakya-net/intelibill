import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/pages/change_password_page.dart';

class _StubAuthController extends AuthController {
  _StubAuthController(this._state);

  final AuthControllerState _state;

  @override
  Future<AuthControllerState> build() async {
    return _state;
  }
}

const _currentPasswordFieldKey = Key('change-password-current');
const _newPasswordFieldKey = Key('change-password-new');
const _confirmPasswordFieldKey = Key('change-password-confirm');
const _submitButtonKey = Key('change-password-submit');

AuthSession _sessionFixture() => AuthSession(
  accessToken: 'access_token',
  refreshToken: 'refresh_token',
  accessTokenExpiresAt: DateTime.utc(2026, 5, 15, 10),
  refreshTokenExpiresAt: DateTime.utc(2026, 6, 15, 10),
  user: const AuthUser(
    id: 'user-1',
    email: 'john@example.com',
    phoneNumber: '9876543210',
    firstName: 'John',
    lastName: 'Doe',
    language: 'en-IN',
  ),
  activeShopId: 'shop-1',
  shops: null,
  rememberMe: false,
);

Widget _buildPage(ProviderContainer container) {
  return UncontrolledProviderScope(
    container: container,
    child: MaterialApp(
      theme: AppTheme.lightTheme,
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      home: const ChangePasswordPage(),
    ),
  );
}

void main() {
  group('ChangePasswordPage', () {
    testWidgets('displays form with password fields', (tester) async {
      final session = _sessionFixture();
      final state = AuthControllerState(session: session);
      final container = ProviderContainer(
        overrides: [
          authControllerProvider.overrideWith(
            () => _StubAuthController(state),
          ),
        ],
      );

      await tester.pumpWidget(_buildPage(container));
      await tester.pumpAndSettle();

      expect(find.byKey(_currentPasswordFieldKey), findsOneWidget);
      expect(find.byKey(_newPasswordFieldKey), findsOneWidget);
      expect(find.byKey(_confirmPasswordFieldKey), findsOneWidget);
      expect(find.byKey(_submitButtonKey), findsOneWidget);
    });

    testWidgets('shows error message when user not authenticated', (
      tester,
    ) async {
      final state = const AuthControllerState();
      final container = ProviderContainer(
        overrides: [
          authControllerProvider.overrideWith(
            () => _StubAuthController(state),
          ),
        ],
      );

      await tester.pumpWidget(_buildPage(container));
      await tester.pumpAndSettle();

      expect(find.text('Unable to initialize password screen'), findsOneWidget);
    });

    testWidgets('validates required current password', (tester) async {
      final session = _sessionFixture();
      final state = AuthControllerState(session: session);
      final container = ProviderContainer(
        overrides: [
          authControllerProvider.overrideWith(
            () => _StubAuthController(state),
          ),
        ],
      );

      await tester.pumpWidget(_buildPage(container));
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(_submitButtonKey));
      await tester.pump();

      expect(find.text('Current password is required'), findsOneWidget);
    });

    testWidgets('validates required new password', (tester) async {
      final session = _sessionFixture();
      final state = AuthControllerState(session: session);
      final container = ProviderContainer(
        overrides: [
          authControllerProvider.overrideWith(
            () => _StubAuthController(state),
          ),
        ],
      );

      await tester.pumpWidget(_buildPage(container));
      await tester.pumpAndSettle();

      await tester.enterText(find.byKey(_currentPasswordFieldKey), 'oldpass');
      await tester.tap(find.byKey(_submitButtonKey));
      await tester.pump();

      expect(find.text('New password is required'), findsOneWidget);
    });

    testWidgets('validates required confirm password', (tester) async {
      final session = _sessionFixture();
      final state = AuthControllerState(session: session);
      final container = ProviderContainer(
        overrides: [
          authControllerProvider.overrideWith(
            () => _StubAuthController(state),
          ),
        ],
      );

      await tester.pumpWidget(_buildPage(container));
      await tester.pumpAndSettle();

      await tester.enterText(find.byKey(_currentPasswordFieldKey), 'oldpass');
      await tester.enterText(find.byKey(_newPasswordFieldKey), 'newpass123');
      await tester.tap(find.byKey(_submitButtonKey));
      await tester.pump();

      expect(find.text('Please confirm your password'), findsOneWidget);
    });

    testWidgets('validates confirm password matches new password', (
      tester,
    ) async {
      final session = _sessionFixture();
      final state = AuthControllerState(session: session);
      final container = ProviderContainer(
        overrides: [
          authControllerProvider.overrideWith(
            () => _StubAuthController(state),
          ),
        ],
      );

      await tester.pumpWidget(_buildPage(container));
      await tester.pumpAndSettle();

      await tester.enterText(find.byKey(_currentPasswordFieldKey), 'oldpass');
      await tester.enterText(find.byKey(_newPasswordFieldKey), 'newpass123');
      await tester.enterText(
        find.byKey(_confirmPasswordFieldKey),
        'newpass456',
      );
      await tester.tap(find.byKey(_submitButtonKey));
      await tester.pump();

      expect(find.text('Passwords do not match'), findsOneWidget);
    });

    testWidgets('calls changePassword on submit with valid data', (
      tester,
    ) async {
      var changePasswordCalled = false;
      final session = _sessionFixture();
      final state = AuthControllerState(session: session);

      final mockController = _CallTrackingAuthController(state, () {
        changePasswordCalled = true;
      });

      final container = ProviderContainer(
        overrides: [
          authControllerProvider.overrideWith(
            () => mockController,
          ),
        ],
      );

      await tester.pumpWidget(_buildPage(container));
      await tester.pumpAndSettle();

      await tester.enterText(find.byKey(_currentPasswordFieldKey), 'oldpass');
      await tester.enterText(find.byKey(_newPasswordFieldKey), 'newpass123');
      await tester.enterText(
        find.byKey(_confirmPasswordFieldKey),
        'newpass123',
      );
      await tester.tap(find.byKey(_submitButtonKey));
      await tester.pumpAndSettle();

      expect(changePasswordCalled, true);
    });

    testWidgets(
      'shows success snackbar and pops on successful password change',
      (
        tester,
      ) async {
        final session = _sessionFixture();
        final state = AuthControllerState(session: session);

        final mockController = _SuccessAuthController(state);

        final container = ProviderContainer(
          overrides: [
            authControllerProvider.overrideWith(
              () => mockController,
            ),
          ],
        );

        await tester.pumpWidget(_buildPage(container));
        await tester.pumpAndSettle();

        await tester.enterText(find.byKey(_currentPasswordFieldKey), 'oldpass');
        await tester.enterText(find.byKey(_newPasswordFieldKey), 'newpass123');
        await tester.enterText(
          find.byKey(_confirmPasswordFieldKey),
          'newpass123',
        );
        await tester.tap(find.byKey(_submitButtonKey));
        await tester.pumpAndSettle();

        expect(find.text('Password changed successfully'), findsOneWidget);
      },
    );

    testWidgets('shows error message on password change failure', (
      tester,
    ) async {
      final session = _sessionFixture();
      final state = AuthControllerState(
        session: session,
        errorMessage: 'Current password is incorrect',
      );

      final container = ProviderContainer(
        overrides: [
          authControllerProvider.overrideWith(
            () => _StubAuthController(state),
          ),
        ],
      );

      await tester.pumpWidget(_buildPage(container));
      await tester.pumpAndSettle();

      expect(find.text('Current password is incorrect'), findsOneWidget);
    });
  });
}

class _CallTrackingAuthController extends AuthController {
  _CallTrackingAuthController(this._state, this._onCall);

  final AuthControllerState _state;
  final VoidCallback _onCall;

  @override
  Future<AuthControllerState> build() async {
    return _state;
  }

  @override
  Future<void> changePassword({
    required String currentPassword,
    required String newPassword,
  }) async {
    _onCall();
  }
}

class _SuccessAuthController extends AuthController {
  _SuccessAuthController(this._initialState);

  final AuthControllerState _initialState;
  late AsyncValue<AuthControllerState> _state;

  @override
  Future<AuthControllerState> build() async {
    return _initialState;
  }

  @override
  Future<void> changePassword({
    required String currentPassword,
    required String newPassword,
  }) async {
    // Simulate success
    state = AsyncData(
      AuthControllerState(
        session: _initialState.session,
        errorMessage: null,
        isLoading: false,
      ),
    );
  }
}
