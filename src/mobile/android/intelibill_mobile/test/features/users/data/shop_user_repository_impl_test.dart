import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/users/data/data_sources/shop_user_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/users/data/dto/add_shop_user_request_dto.dart';
import 'package:intelibill_mobile/src/features/users/data/dto/edit_shop_user_request_dto.dart';
import 'package:intelibill_mobile/src/features/users/data/dto/shop_user_dto.dart';
import 'package:intelibill_mobile/src/features/users/data/repositories/shop_user_repository_impl.dart';
import 'package:mocktail/mocktail.dart';

class MockShopUserRemoteDataSource extends Mock
    implements ShopUserRemoteDataSource {}

void main() {
  setUpAll(() {
    registerFallbackValue(
      const AddShopUserRequestDto(
        shopIds: ['shop-1'],
        email: 'fallback@example.com',
        firstName: 'Fallback',
        lastName: 'User',
        phoneNumber: '0000000000',
        password: 'password1',
        confirmPassword: 'password1',
        role: 'Staff',
      ),
    );
    registerFallbackValue(
      const EditShopUserRequestDto(
        email: 'fallback@example.com',
        firstName: 'Fallback',
        lastName: 'User',
        phoneNumber: '0000000000',
        role: 'Staff',
        isLoginEnabled: true,
        shopIds: ['shop-1'],
      ),
    );
  });

  late MockShopUserRemoteDataSource remoteDataSource;
  late ShopUserRepositoryImpl repository;

  setUp(() {
    remoteDataSource = MockShopUserRemoteDataSource();
    repository = ShopUserRepositoryImpl(remoteDataSource);
  });

  group('ShopUserRepositoryImpl', () {
    test('maps remote dtos into domain entities', () async {
      when(() => remoteDataSource.getShopUsers()).thenAnswer(
        (_) async => const [
          ShopUserDto(
            userId: 'user-1',
            firstName: 'Alice',
            lastName: 'Sharma',
            email: 'alice@example.com',
            phoneNumber: '9876543210',
            role: 'Manager',
            isLoginEnabled: true,
            shopIds: ['shop-1'],
          ),
        ],
      );

      final result = await repository.getShopUsers();

      expect(result.length, 1);
      expect(result[0].userId, 'user-1');
      expect(result[0].fullName, 'Alice Sharma');
      expect(result[0].isOwner, false);
    });

    test('rethrows existing AppExceptions from getShopUsers', () async {
      final exception = AppException(
        failure: const Failure.network(message: 'offline'),
      );
      when(() => remoteDataSource.getShopUsers()).thenThrow(exception);

      expect(repository.getShopUsers(), throwsA(same(exception)));
    });

    test('wraps FormatException as serialization failure', () async {
      when(
        () => remoteDataSource.getShopUsers(),
      ).thenThrow(const FormatException('bad json'));

      await expectLater(
        repository.getShopUsers(),
        throwsA(
          isA<AppException>().having(
            (e) => e.failure,
            'failure',
            isA<SerializationFailure>().having(
              (f) => f.message,
              'message',
              'bad json',
            ),
          ),
        ),
      );
    });

    test('adds shop user with trimmed data and maps result', () async {
      const createdDto = ShopUserDto(
        userId: 'user-10',
        firstName: 'New',
        lastName: 'User',
        email: 'new@example.com',
        phoneNumber: '9000000000',
        role: 'Staff',
        isLoginEnabled: true,
        shopIds: ['shop-1'],
      );

      when(
        () => remoteDataSource.addShopUser(any()),
      ).thenAnswer((_) async => createdDto);

      final result = await repository.addShopUser(
        shopIds: const ['shop-1'],
        email: '  new@example.com ',
        firstName: '  New ',
        lastName: ' User ',
        phoneNumber: ' 9000000000 ',
        password: 'password1',
        confirmPassword: 'password1',
        role: 'Staff',
      );

      expect(result.userId, 'user-10');
      expect(result.role, 'Staff');

      verify(
        () => remoteDataSource.addShopUser(
          const AddShopUserRequestDto(
            shopIds: ['shop-1'],
            email: 'new@example.com',
            firstName: 'New',
            lastName: 'User',
            phoneNumber: '9000000000',
            password: 'password1',
            confirmPassword: 'password1',
            role: 'Staff',
          ),
        ),
      ).called(1);
    });

    test('edits shop user with trimmed data and maps result', () async {
      const updatedDto = ShopUserDto(
        userId: 'user-1',
        firstName: 'Alice',
        lastName: 'Sharma',
        email: 'alice@example.com',
        phoneNumber: '9876543210',
        role: 'Manager',
        isLoginEnabled: false,
        shopIds: ['shop-1', 'shop-2'],
      );

      when(
        () => remoteDataSource.editShopUser(any(), any()),
      ).thenAnswer((_) async => updatedDto);

      final result = await repository.editShopUser(
        userId: 'user-1',
        email: ' alice@example.com ',
        firstName: ' Alice ',
        lastName: ' Sharma ',
        phoneNumber: ' 9876543210 ',
        role: 'Manager',
        isLoginEnabled: false,
        shopIds: const ['shop-1', 'shop-2'],
      );

      expect(result.isLoginEnabled, false);
      expect(result.shopIds, ['shop-1', 'shop-2']);

      verify(
        () => remoteDataSource.editShopUser(
          'user-1',
          const EditShopUserRequestDto(
            email: 'alice@example.com',
            firstName: 'Alice',
            lastName: 'Sharma',
            phoneNumber: '9876543210',
            role: 'Manager',
            isLoginEnabled: false,
            shopIds: ['shop-1', 'shop-2'],
          ),
        ),
      ).called(1);
    });

    test('rethrows AppExceptions from addShopUser', () async {
      final exception = AppException(
        failure: const Failure.validation(message: 'invalid'),
      );
      when(() => remoteDataSource.addShopUser(any())).thenThrow(exception);

      expect(
        repository.addShopUser(
          shopIds: const ['shop-1'],
          email: 'new@example.com',
          firstName: 'New',
          lastName: 'User',
          phoneNumber: '9000000000',
          password: 'password1',
          confirmPassword: 'password1',
          role: 'Staff',
        ),
        throwsA(same(exception)),
      );
    });

    test('wraps unknown errors from editShopUser as unknown failure', () async {
      when(
        () => remoteDataSource.editShopUser(any(), any()),
      ).thenThrow(Exception('unexpected'));

      await expectLater(
        repository.editShopUser(
          userId: 'user-1',
          email: 'alice@example.com',
          firstName: 'Alice',
          lastName: 'Sharma',
          phoneNumber: '9876543210',
          role: 'Manager',
          isLoginEnabled: true,
          shopIds: const ['shop-1'],
        ),
        throwsA(
          isA<AppException>().having(
            (e) => e.failure,
            'failure',
            isA<UnknownFailure>(),
          ),
        ),
      );
    });
  });
}
