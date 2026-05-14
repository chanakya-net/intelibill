import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';
import 'package:intelibill_mobile/src/features/auth/domain/repositories/auth_repository.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/pages/login_page.dart';
import 'package:mocktail/mocktail.dart';

class MockAuthRepository extends Mock implements AuthRepository {}

const _loginPageIdentifierFieldKey = Key('login-page-identifier');
const _loginPagePasswordFieldKey = Key('login-page-password');
const _loginPagePasswordVisibilityKey = Key('login-page-password-visibility');
const _loginPageRememberMeKey = Key('login-page-remember-me');
const _loginPageSubmitButtonKey = Key('login-page-submit');
const _loginPageForgotPasswordKey = Key('login-page-forgot-password');
const _loginPageRegisterKey = Key('login-page-register');

AuthSession _sessionFixture() => AuthSession(
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

Widget _buildPage(ProviderContainer container) {
  return UncontrolledProviderScope(
    container: container,
    child: const MaterialApp(home: LoginPage()),
  );
}

void main() {
  late MockAuthRepository repository;

  setUp(() {
    repository = MockAuthRepository();
  });

  group('LoginPage', () {
    testWidgets('renders login screen sections', (tester) async {
      when(
        () => repository.getRememberedIdentifier(),
      ).thenAnswer((_) async => null);

      final container = ProviderContainer(
        overrides: [
          authRepositoryProvider.overrideWith(
            (ref) => Future.value(repository),
          ),
        ],
      );
      await tester.pumpWidget(_buildPage(container));
      await tester.pumpAndSettle();

      expect(find.text('Intelibill'), findsOneWidget);
      expect(find.text('Login now'), findsAtLeastNWidgets(1));
      expect(find.byKey(_loginPageIdentifierFieldKey), findsOneWidget);
      expect(find.byKey(_loginPagePasswordFieldKey), findsOneWidget);
      expect(find.byKey(_loginPageSubmitButtonKey), findsOneWidget);
      expect(find.byKey(_loginPageForgotPasswordKey), findsOneWidget);
      expect(find.byKey(_loginPageRegisterKey), findsOneWidget);
    });

    testWidgets('shows inline validation messages', (tester) async {
      when(
        () => repository.getRememberedIdentifier(),
      ).thenAnswer((_) async => null);

      final container = ProviderContainer(
        overrides: [
          authRepositoryProvider.overrideWith(
            (ref) => Future.value(repository),
          ),
        ],
      );
      await tester.pumpWidget(_buildPage(container));
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(_loginPageSubmitButtonKey));
      await tester.pump();

      expect(find.text('Identifier is required.'), findsOneWidget);
      expect(find.text('Password is required.'), findsOneWidget);
    });

    testWidgets('toggles password visibility', (tester) async {
      when(
        () => repository.getRememberedIdentifier(),
      ).thenAnswer((_) async => null);
      when(
        () => repository.login(
          identifier: any(named: 'identifier'),
          password: any(named: 'password'),
          rememberMe: any(named: 'rememberMe'),
        ),
      ).thenAnswer((_) async => _sessionFixture());

      final container = ProviderContainer(
        overrides: [
          authRepositoryProvider.overrideWith(
            (ref) => Future.value(repository),
          ),
        ],
      );
      await tester.pumpWidget(_buildPage(container));
      await tester.pumpAndSettle();

      expect(find.byIcon(Icons.visibility_off_outlined), findsOneWidget);

      await tester.tap(find.byKey(_loginPagePasswordVisibilityKey));
      await tester.pump();

      expect(find.byIcon(Icons.visibility_outlined), findsOneWidget);
    });

    testWidgets('disables submit button while login loading', (tester) async {
      final loginCompleter = Completer<AuthSession>();
      when(
        () => repository.getRememberedIdentifier(),
      ).thenAnswer((_) async => null);
      when(
        () => repository.login(
          identifier: any(named: 'identifier'),
          password: any(named: 'password'),
          rememberMe: any(named: 'rememberMe'),
        ),
      ).thenAnswer((_) => loginCompleter.future);

      final container = ProviderContainer(
        overrides: [
          authRepositoryProvider.overrideWith(
            (ref) => Future.value(repository),
          ),
        ],
      );
      await tester.pumpWidget(_buildPage(container));
      await tester.pumpAndSettle();

      await tester.enterText(
        find.byKey(_loginPageIdentifierFieldKey),
        'test@example.com',
      );
      await tester.enterText(find.byKey(_loginPagePasswordFieldKey), 'secret');
      await tester.tap(find.byKey(_loginPageSubmitButtonKey));
      await tester.pump();

      final submitButton = tester.widget<FilledButton>(
        find.byKey(_loginPageSubmitButtonKey),
      );
      expect(submitButton.onPressed, isNull);
      final rememberMeTile = tester.widget<CheckboxListTile>(
        find.byKey(_loginPageRememberMeKey),
      );
      expect(rememberMeTile.onChanged, isNull);
      final forgotButton = tester.widget<TextButton>(
        find.byKey(_loginPageForgotPasswordKey),
      );
      expect(forgotButton.onPressed, isNull);
      final registerButton = tester.widget<TextButton>(
        find.byKey(_loginPageRegisterKey),
      );
      expect(registerButton.onPressed, isNull);

      loginCompleter.complete(_sessionFixture());
      await tester.pumpAndSettle();
    });

    testWidgets('shows server error banner on login failure', (tester) async {
      when(
        () => repository.getRememberedIdentifier(),
      ).thenAnswer((_) async => null);
      when(
        () => repository.login(
          identifier: any(named: 'identifier'),
          password: any(named: 'password'),
          rememberMe: any(named: 'rememberMe'),
        ),
      ).thenThrow(
        AppException(
          failure: const Failure.unauthorized(
            message: 'Auth.InvalidCredentials',
          ),
        ),
      );

      final container = ProviderContainer(
        overrides: [
          authRepositoryProvider.overrideWith(
            (ref) => Future.value(repository),
          ),
        ],
      );
      await tester.pumpWidget(_buildPage(container));
      await tester.pumpAndSettle();

      await tester.enterText(
        find.byKey(_loginPageIdentifierFieldKey),
        'user',
      );
      await tester.enterText(find.byKey(_loginPagePasswordFieldKey), 'wrong');
      await tester.tap(find.byKey(_loginPageSubmitButtonKey));
      await tester.pumpAndSettle();

      expect(find.text('Invalid identifier or password.'), findsOneWidget);
    });

    testWidgets('loads remembered identifier and toggles remember me', (
      tester,
    ) async {
      when(
        () => repository.getRememberedIdentifier(),
      ).thenAnswer(
        (_) async => 'remembered@example.com',
      );

      final container = ProviderContainer(
        overrides: [
          authRepositoryProvider.overrideWith(
            (ref) => Future.value(repository),
          ),
        ],
      );
      await tester.pumpWidget(_buildPage(container));
      await tester.pumpAndSettle();

      final identifierField = tester.widget<TextFormField>(
        find.byKey(_loginPageIdentifierFieldKey),
      );
      expect(
        identifierField.controller?.text,
        equals('remembered@example.com'),
      );

      final rememberMeTile = tester.widget<CheckboxListTile>(
        find.byKey(_loginPageRememberMeKey),
      );
      expect(rememberMeTile.value, isTrue);

      await tester.tap(find.byKey(_loginPageRememberMeKey));
      await tester.pump();
      final rememberMeTileAfterToggle = tester.widget<CheckboxListTile>(
        find.byKey(_loginPageRememberMeKey),
      );
      expect(rememberMeTileAfterToggle.value, isFalse);
    });

    testWidgets('triggers login callback with entered credentials', (
      tester,
    ) async {
      when(
        () => repository.getRememberedIdentifier(),
      ).thenAnswer((_) async => null);
      when(
        () => repository.login(
          identifier: any(named: 'identifier'),
          password: any(named: 'password'),
          rememberMe: any(named: 'rememberMe'),
        ),
      ).thenAnswer((_) async => _sessionFixture());

      final container = ProviderContainer(
        overrides: [
          authRepositoryProvider.overrideWith(
            (ref) => Future.value(repository),
          ),
        ],
      );
      await tester.pumpWidget(_buildPage(container));
      await tester.pumpAndSettle();

      await tester.enterText(
        find.byKey(_loginPageIdentifierFieldKey),
        'test@example.com',
      );
      await tester.enterText(find.byKey(_loginPagePasswordFieldKey), 'secret');
      await tester.tap(find.byKey(_loginPageSubmitButtonKey));
      await tester.pumpAndSettle();

      verify(
        () => repository.login(
          identifier: 'test@example.com',
          password: 'secret',
          rememberMe: false,
        ),
      ).called(1);
    });

    testWidgets('triggers login callback with remember me selected', (
      tester,
    ) async {
      when(
        () => repository.getRememberedIdentifier(),
      ).thenAnswer((_) async => null);
      when(
        () => repository.login(
          identifier: any(named: 'identifier'),
          password: any(named: 'password'),
          rememberMe: any(named: 'rememberMe'),
        ),
      ).thenAnswer((_) async => _sessionFixture());

      final container = ProviderContainer(
        overrides: [
          authRepositoryProvider.overrideWith(
            (ref) => Future.value(repository),
          ),
        ],
      );
      await tester.pumpWidget(_buildPage(container));
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(_loginPageRememberMeKey));
      await tester.pump();
      await tester.enterText(
        find.byKey(_loginPageIdentifierFieldKey),
        'test@example.com',
      );
      await tester.enterText(find.byKey(_loginPagePasswordFieldKey), 'secret');
      await tester.tap(find.byKey(_loginPageSubmitButtonKey));
      await tester.pumpAndSettle();

      verify(
        () => repository.login(
          identifier: 'test@example.com',
          password: 'secret',
          rememberMe: true,
        ),
      ).called(1);
    });
  });
}
