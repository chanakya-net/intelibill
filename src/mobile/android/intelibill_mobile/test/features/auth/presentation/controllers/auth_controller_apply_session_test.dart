import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/storage/secure_storage.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';
import 'package:intelibill_mobile/src/features/auth/domain/repositories/auth_repository.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:mocktail/mocktail.dart';

class MockAuthRepository extends Mock implements AuthRepository {}

class MockSecureStorage extends Mock implements SecureStorage {}

void main() {
  late MockAuthRepository repository;
  late MockSecureStorage secureStorage;

  AuthSession fixtureSession() => AuthSession(
    accessToken: 'new_access_token',
    refreshToken: 'new_refresh_token',
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

  setUp(() {
    repository = MockAuthRepository();
    secureStorage = MockSecureStorage();

    when(
      () => repository.getRememberedIdentifier(),
    ).thenAnswer((_) async => '');
    when(() => repository.getRefreshToken()).thenAnswer((_) async => null);

    when(
      () => secureStorage.saveTokens(
        accessToken: any(named: 'accessToken'),
        refreshToken: any(named: 'refreshToken'),
      ),
    ).thenAnswer((_) async => Future<void>.value());
  });

  test('applySession persists tokens and updates provider state', () async {
    final session = fixtureSession();

    final container = ProviderContainer(
      overrides: [
        authRepositoryProvider.overrideWith((ref) => Future.value(repository)),
        secureStorageProvider.overrideWith((ref) => secureStorage),
      ],
    );
    addTearDown(container.dispose);

    await container.read(authControllerProvider.future);

    final notifier = container.read(authControllerProvider.notifier);
    await notifier.applySession(session);

    final state = container.read(authControllerProvider).value!;
    expect(state.session, equals(session));
    verify(
      () => secureStorage.saveTokens(
        accessToken: session.accessToken,
        refreshToken: session.refreshToken,
      ),
    ).called(1);
  });
}
