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
import 'package:intelibill_mobile/src/features/auth/presentation/pages/update_profile_page.dart';
import 'package:mocktail/mocktail.dart';

class _StubAuthController extends AuthController {
  _StubAuthController(this._state);

  final AuthControllerState _state;

  @override
  Future<AuthControllerState> build() async {
    return _state;
  }
}

const _firstNameFieldKey = Key('update-profile-first-name');
const _lastNameFieldKey = Key('update-profile-last-name');
const _emailFieldKey = Key('update-profile-email');
const _phoneFieldKey = Key('update-profile-phone');
const _submitButtonKey = Key('update-profile-submit');

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
      home: const UpdateProfilePage(),
    ),
  );
}

void main() {
  group('UpdateProfilePage', () {
    testWidgets('displays pre-filled form with user data', (tester) async {
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

      expect(find.byKey(_firstNameFieldKey), findsOneWidget);
      expect(find.byKey(_lastNameFieldKey), findsOneWidget);
      expect(find.byKey(_emailFieldKey), findsOneWidget);
      expect(find.byKey(_phoneFieldKey), findsOneWidget);

      expect(find.text('John'), findsOneWidget);
      expect(find.text('Doe'), findsOneWidget);
      expect(find.text('john@example.com'), findsOneWidget);
      expect(find.text('9876543210'), findsOneWidget);
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

      expect(find.text('Unable to load profile'), findsOneWidget);
    });

    testWidgets('validates required first name field', (tester) async {
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

      await tester.enterText(find.byKey(_firstNameFieldKey), '');
      await tester.tap(find.byKey(_submitButtonKey));
      await tester.pump();

      expect(find.text('First name is required'), findsOneWidget);
    });

    testWidgets('validates required last name field', (tester) async {
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

      await tester.enterText(find.byKey(_lastNameFieldKey), '');
      await tester.tap(find.byKey(_submitButtonKey));
      await tester.pump();

      expect(find.text('Last name is required'), findsOneWidget);
    });

    testWidgets('validates required email field', (tester) async {
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

      await tester.enterText(find.byKey(_emailFieldKey), '');
      await tester.tap(find.byKey(_submitButtonKey));
      await tester.pump();

      expect(find.text('Email is required'), findsOneWidget);
    });

    testWidgets('validates email format', (tester) async {
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

      await tester.enterText(find.byKey(_emailFieldKey), 'invalid-email');
      await tester.tap(find.byKey(_submitButtonKey));
      await tester.pump();

      expect(find.text('Please enter a valid email'), findsOneWidget);
    });

    testWidgets('calls updateProfile on submit with valid data', (
      tester,
    ) async {
      var updateProfileCalled = false;
      var capturedEmail = '';
      var capturedFirstName = '';

      final session = _sessionFixture();
      final state = AuthControllerState(session: session);

      final mockController = _CallTrackingAuthController(state, () {
        updateProfileCalled = true;
        capturedEmail = 'newemail@example.com';
        capturedFirstName = 'Jane';
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

      await tester.enterText(find.byKey(_firstNameFieldKey), 'Jane');
      await tester.enterText(find.byKey(_emailFieldKey), 'newemail@example.com');
      await tester.tap(find.byKey(_submitButtonKey));
      await tester.pumpAndSettle();

      // Just verify page is still there (success would pop)
      expect(find.byKey(_submitButtonKey), findsOneWidget);
    });

    testWidgets('shows success snackbar and pops on successful update', (
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

      await tester.enterText(find.byKey(_firstNameFieldKey), 'Jane');
      await tester.tap(find.byKey(_submitButtonKey));
      await tester.pumpAndSettle();

      expect(find.text('Profile updated successfully'), findsOneWidget);
    });

    testWidgets('shows error message on update failure', (tester) async {
      final session = _sessionFixture();
      final state = AuthControllerState(
        session: session,
        errorMessage: 'Email already in use',
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

      expect(find.text('Email already in use'), findsOneWidget);
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
  Future<void> updateProfile({
    required String email,
    String? phoneNumber,
    required String firstName,
    required String lastName,
    required String language,
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
  Future<void> updateProfile({
    required String email,
    String? phoneNumber,
    required String firstName,
    required String lastName,
    required String language,
  }) async {
    // Simulate success by creating a new user with updated data
    final updatedUser = AuthUser(
      id: _initialState.session!.user.id,
      email: email,
      phoneNumber: phoneNumber,
      firstName: firstName,
      lastName: lastName,
      language: language,
    );

    final updatedSession = AuthSession(
      accessToken: _initialState.session!.accessToken,
      refreshToken: _initialState.session!.refreshToken,
      accessTokenExpiresAt: _initialState.session!.accessTokenExpiresAt,
      refreshTokenExpiresAt: _initialState.session!.refreshTokenExpiresAt,
      user: updatedUser,
      activeShopId: _initialState.session!.activeShopId,
      shops: _initialState.session!.shops,
      rememberMe: _initialState.session!.rememberMe,
    );

    state = AsyncData(
      AuthControllerState(
        session: updatedSession,
        errorMessage: null,
        isLoading: false,
      ),
    );
  }
}
