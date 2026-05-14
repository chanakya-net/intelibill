import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';
import 'package:intelibill_mobile/src/features/auth/domain/repositories/auth_repository.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:mocktail/mocktail.dart';

class MockAuthRepository extends Mock implements AuthRepository {}

void main() {
  late MockAuthRepository repository;

  setUp(() {
    repository = MockAuthRepository();
  });

  AuthSession userFixtureSession({bool rememberMe = false}) => AuthSession(
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
    rememberMe: rememberMe,
  );

  group('AuthController', () {
    test('loads remembered identifier from repository', () async {
      when(
        () => repository.getRememberedIdentifier(),
      ).thenAnswer((_) async => 'remembered@example.com');

      final container = ProviderContainer(
        overrides: [
          authRepositoryProvider.overrideWith(
            (ref) => Future.value(repository),
          ),
        ],
      );
      addTearDown(container.dispose);

      final state = await container.read(authControllerProvider.future);

      expect(state.rememberedIdentifier, equals('remembered@example.com'));
      expect(state.rememberMe, isTrue);
    });

    test(
      'login success updates authenticated session and loading states',
      () async {
        when(
          () => repository.getRememberedIdentifier(),
        ).thenAnswer((_) async => null);
        when(
          () => repository.login(
            identifier: any(named: 'identifier'),
            password: any(named: 'password'),
            rememberMe: any(named: 'rememberMe'),
          ),
        ).thenAnswer((_) async => userFixtureSession());

        final container = ProviderContainer(
          overrides: [
            authRepositoryProvider.overrideWith(
              (ref) => Future.value(repository),
            ),
          ],
        );
        addTearDown(container.dispose);

        await container.read(authControllerProvider.future);

        final notifier = container.read(authControllerProvider.notifier);
        await notifier.login(
          identifier: 'test@example.com',
          password: 'password123',
          rememberMe: true,
        );

        final state = container.read(authControllerProvider).value;
        expect(state, isNotNull);
        expect(state!.isLoading, isFalse);
        expect(state.session, equals(userFixtureSession()));
        expect(state.errorMessage, isNull);
        verify(
          () => repository.login(
            identifier: 'test@example.com',
            password: 'password123',
            rememberMe: true,
          ),
        ).called(1);
      },
    );

    test('maps invalid credentials failure', () async {
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
      addTearDown(container.dispose);

      await container.read(authControllerProvider.future);
      await container
          .read(authControllerProvider.notifier)
          .login(identifier: 'user', password: 'pass', rememberMe: false);

      final state = container.read(authControllerProvider).value!;
      expect(state.errorMessage, equals('Invalid identifier or password.'));
      expect(state.isLoading, isFalse);
    });

    test('maps disabled account failure', () async {
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
          failure: const Failure.forbidden(message: 'Auth.UserLoginDisabled'),
        ),
      );

      final container = ProviderContainer(
        overrides: [
          authRepositoryProvider.overrideWith(
            (ref) => Future.value(repository),
          ),
        ],
      );
      addTearDown(container.dispose);

      await container.read(authControllerProvider.future);
      await container
          .read(authControllerProvider.notifier)
          .login(identifier: 'user', password: 'pass', rememberMe: false);

      final state = container.read(authControllerProvider).value!;
      expect(state.errorMessage, equals('This account has been disabled.'));
      expect(state.isLoading, isFalse);
    });

    test('maps network error failure', () async {
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
        AppException(failure: const Failure.network(message: 'No internet')),
      );

      final container = ProviderContainer(
        overrides: [
          authRepositoryProvider.overrideWith(
            (ref) => Future.value(repository),
          ),
        ],
      );
      addTearDown(container.dispose);

      await container.read(authControllerProvider.future);
      await container
          .read(authControllerProvider.notifier)
          .login(identifier: 'user', password: 'pass', rememberMe: false);

      final state = container.read(authControllerProvider).value!;
      expect(
        state.errorMessage,
        equals('Unable to connect. Please check your network.'),
      );
    });

    test('maps timeout failure', () async {
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
        AppException(failure: const Failure.timeout(message: 'Timeout')),
      );

      final container = ProviderContainer(
        overrides: [
          authRepositoryProvider.overrideWith(
            (ref) => Future.value(repository),
          ),
        ],
      );
      addTearDown(container.dispose);

      await container.read(authControllerProvider.future);
      await container
          .read(authControllerProvider.notifier)
          .login(identifier: 'user', password: 'pass', rememberMe: false);

      final state = container.read(authControllerProvider).value!;
      expect(state.errorMessage, equals('Login timed out. Please try again.'));
    });

    test('maps unknown error to generic sign-in error message', () async {
      when(
        () => repository.getRememberedIdentifier(),
      ).thenAnswer((_) async => null);
      when(
        () => repository.login(
          identifier: any(named: 'identifier'),
          password: any(named: 'password'),
          rememberMe: any(named: 'rememberMe'),
        ),
      ).thenThrow(Exception('boom'));

      final container = ProviderContainer(
        overrides: [
          authRepositoryProvider.overrideWith(
            (ref) => Future.value(repository),
          ),
        ],
      );
      addTearDown(container.dispose);

      await container.read(authControllerProvider.future);
      await container
          .read(authControllerProvider.notifier)
          .login(identifier: 'user', password: 'pass', rememberMe: false);

      final state = container.read(authControllerProvider).value!;
      expect(
        state.errorMessage,
        equals('Unable to sign in. Please try again.'),
      );
    });

    test(
      'preserves remember-me state across logins',
      () async {
        when(
          () => repository.getRememberedIdentifier(),
        ).thenAnswer((_) async => null);
        when(
          () => repository.login(
            identifier: any(named: 'identifier'),
            password: any(named: 'password'),
            rememberMe: any(named: 'rememberMe'),
          ),
        ).thenAnswer((invocation) async {
          final rememberMe = invocation.namedArguments[#rememberMe] as bool;
          return userFixtureSession(rememberMe: rememberMe);
        });

        final container = ProviderContainer(
          overrides: [
            authRepositoryProvider.overrideWith(
              (ref) => Future.value(repository),
            ),
          ],
        );
        addTearDown(container.dispose);

        await container.read(authControllerProvider.future);
        final notifier = container.read(authControllerProvider.notifier);

        await notifier.login(
          identifier: 'user',
          password: 'pass',
          rememberMe: true,
        );
        expect(
          container.read(authControllerProvider).value?.rememberMe,
          isTrue,
        );

        await notifier.login(
          identifier: 'user',
          password: 'pass',
          rememberMe: false,
        );
        expect(
          container.read(authControllerProvider).value?.rememberMe,
          isFalse,
        );
      },
    );

    test('loading state transitions to false when login succeeds', () async {
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
      addTearDown(container.dispose);

      final observedLoadingStates = <bool>[];
      final subscription = container.listen<AsyncValue<AuthControllerState>>(
        authControllerProvider,
        (_, next) {
          final value = next.value;
          if (value != null) {
            observedLoadingStates.add(value.isLoading);
          }
        },
      );
      addTearDown(subscription.close);

      await container.read(authControllerProvider.future);

      final notifier = container.read(authControllerProvider.notifier);
      final loginFuture = notifier.login(
        identifier: 'user',
        password: 'pass',
        rememberMe: false,
      );

      await Future<void>.delayed(const Duration(milliseconds: 5));
      expect(observedLoadingStates, contains(true));

      loginCompleter.complete(userFixtureSession());
      await loginFuture;
      expect(observedLoadingStates.last, isFalse);
    });

    test('clear error resets visible server message', () async {
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
      addTearDown(container.dispose);

      await container.read(authControllerProvider.future);
      await container
          .read(authControllerProvider.notifier)
          .login(identifier: 'user', password: 'pass', rememberMe: false);

      final notifier = container.read(authControllerProvider.notifier);
      expect(
        container.read(authControllerProvider).value?.errorMessage,
        isNotNull,
      );

      notifier.clearError();
      expect(
        container.read(authControllerProvider).value?.errorMessage,
        isNull,
      );
    });

    test('sign out clears session and error', () async {
      when(
        () => repository.getRememberedIdentifier(),
      ).thenAnswer((_) async => null);
      when(
        () => repository.login(
          identifier: any(named: 'identifier'),
          password: any(named: 'password'),
          rememberMe: any(named: 'rememberMe'),
        ),
      ).thenAnswer((_) async => userFixtureSession());
      when(
        () => repository.clearTokens(),
      ).thenAnswer((_) async => Future<void>.value());

      final container = ProviderContainer(
        overrides: [
          authRepositoryProvider.overrideWith(
            (ref) => Future.value(repository),
          ),
        ],
      );
      addTearDown(container.dispose);

      await container.read(authControllerProvider.future);
      await container
          .read(authControllerProvider.notifier)
          .login(identifier: 'user', password: 'pass', rememberMe: false);
      expect(container.read(authControllerProvider).value?.session, isNotNull);

      await container.read(authControllerProvider.notifier).signOut();

      final postSignOut = container.read(authControllerProvider).value!;
      expect(postSignOut.session, isNull);
      expect(postSignOut.isLoading, isFalse);
      verify(() => repository.clearTokens()).called(1);
    });
  });
}
