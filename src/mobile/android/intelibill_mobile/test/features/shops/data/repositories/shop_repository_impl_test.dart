import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/auth/data/dto/auth_result_dto.dart';
import 'package:intelibill_mobile/src/features/auth/data/dto/auth_user_dto.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';
import 'package:intelibill_mobile/src/features/shops/data/data_sources/shop_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/shops/data/dtos/add_bank_account_request_dto.dart';
import 'package:intelibill_mobile/src/features/shops/data/dtos/bank_account_dto.dart';
import 'package:intelibill_mobile/src/features/shops/data/dtos/create_shop_request_dto.dart';
import 'package:intelibill_mobile/src/features/shops/data/dtos/shop_details_dto.dart';
import 'package:intelibill_mobile/src/features/shops/data/dtos/update_shop_request_dto.dart';
import 'package:intelibill_mobile/src/features/shops/data/repositories/shop_repository_impl.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/add_bank_account_request.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/create_shop_request.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/update_shop_request.dart';
import 'package:mocktail/mocktail.dart';

class MockShopRemoteDataSource extends Mock implements ShopRemoteDataSource {}

void main() {
  late MockShopRemoteDataSource remoteDataSource;
  late ShopRepositoryImpl repository;

  setUpAll(() {
    registerFallbackValue(
      const CreateShopRequestDto(
        name: 'Fallback Shop',
        address: 'Fallback Address',
        city: 'Fallback City',
        state: 'Fallback State',
        pincode: '000000',
      ),
    );
    registerFallbackValue(
      const UpdateShopRequestDto(
        name: 'Fallback Shop',
        address: 'Fallback Address',
        city: 'Fallback City',
        state: 'Fallback State',
        pincode: '000000',
      ),
    );
    registerFallbackValue(
      const AddBankAccountRequestDto(
        bankName: 'Fallback Bank',
        accountNumber: '000',
        accountType: 'Savings',
        ifscCode: 'IFSC',
        accountHolderName: 'Holder',
      ),
    );
  });

  setUp(() {
    remoteDataSource = MockShopRemoteDataSource();
    repository = ShopRepositoryImpl(remoteDataSource);
  });

  group('ShopRepositoryImpl', () {
    test('maps getShop dto into domain entity', () async {
      when(
        () => remoteDataSource.getShop('shop-1'),
      ).thenAnswer(
        (_) async => const ShopDetailsDto(
          shopId: 'shop-1',
          name: 'My Shop',
          address: 'Addr',
          city: 'City',
          state: 'State',
          pincode: '000000',
          bankName: 'HDFC',
          bankAccountNumber: '123',
          bankAccountType: 'Savings',
          ifscCode: 'HDFC0001',
          accountHolderName: 'John Doe',
        ),
      );

      final result = await repository.getShop('shop-1');

      expect(result.id, 'shop-1');
      expect(result.bankAccounts, hasLength(1));
      expect(result.bankAccounts.first.bankName, 'HDFC');
      expect(result.bankAccounts.first.accountNumber, '123');
    });

    test('maps createShop result dto into AuthSession', () async {
      const request = CreateShopRequest(
        name: 'My Shop',
        address: 'Addr',
        city: 'City',
        state: 'State',
        pincode: '000000',
      );

      final authResult = AuthResultDto(
        accessToken: 'access',
        refreshToken: 'refresh',
        accessTokenExpiresAt: DateTime(2030),
        refreshTokenExpiresAt: DateTime(2030, 1, 2),
        user: const AuthUserDto(
          id: 'user-1',
          email: 'a@b.com',
          firstName: 'A',
          lastName: 'B',
          language: 'en-IN',
        ),
        activeShopId: 'shop-1',
      );

      when(
        () => remoteDataSource.createShop(any()),
      ).thenAnswer((_) async => authResult);

      final session = await repository.createShop(request);

      expect(session, isA<AuthSession>());
      expect(session.accessToken, 'access');
      expect(session.activeShopId, 'shop-1');
      verify(
        () => remoteDataSource.createShop(
          const CreateShopRequestDto(
            name: 'My Shop',
            address: 'Addr',
            city: 'City',
            state: 'State',
            pincode: '000000',
          ),
        ),
      ).called(1);
    });

    test('maps updateShop dto into domain entity', () async {
      const request = UpdateShopRequest(
        name: 'My Shop',
        address: 'Addr',
        city: 'City',
        state: 'State',
        pincode: '000000',
      );

      when(
        () => remoteDataSource.updateShop(any(), any()),
      ).thenAnswer(
        (_) async => const ShopDetailsDto(
          shopId: 'shop-1',
          name: 'My Shop',
          address: 'Addr',
          city: 'City',
          state: 'State',
          pincode: '000000',
        ),
      );

      final shop = await repository.updateShop('shop-1', request);

      expect(shop.id, 'shop-1');
      expect(shop.name, 'My Shop');
      verify(
        () => remoteDataSource.updateShop(
          'shop-1',
          const UpdateShopRequestDto(
            name: 'My Shop',
            address: 'Addr',
            city: 'City',
            state: 'State',
            pincode: '000000',
          ),
        ),
      ).called(1);
    });

    test('delegates addBankAccount to remote data source', () async {
      const request = AddBankAccountRequest(
        bankName: 'HDFC',
        accountNumber: '123',
        accountType: 'Savings',
        ifscCode: 'HDFC0001',
        accountHolderName: 'John Doe',
      );

      when(
        () => remoteDataSource.addBankAccount(any()),
      ).thenAnswer(
        (_) async => const BankAccountDto(
          id: 'ba-1',
          bankName: 'HDFC',
          accountNumber: '123',
          accountType: 'Savings',
          ifscCode: 'HDFC0001',
          accountHolderName: 'John Doe',
        ),
      );

      await repository.addBankAccount(request);

      verify(
        () => remoteDataSource.addBankAccount(
          const AddBankAccountRequestDto(
            bankName: 'HDFC',
            accountNumber: '123',
            accountType: 'Savings',
            ifscCode: 'HDFC0001',
            accountHolderName: 'John Doe',
          ),
        ),
      ).called(1);
    });

    test('rethrows existing AppExceptions', () async {
      final exception = AppException(
        failure: const Failure.network(message: 'offline'),
      );
      when(() => remoteDataSource.getShop(any())).thenThrow(exception);

      expect(repository.getShop('shop-1'), throwsA(same(exception)));
    });

    test('wraps FormatException as serialization failure', () async {
      when(
        () => remoteDataSource.getShop(any()),
      ).thenThrow(const FormatException('bad json'));

      await expectLater(
        repository.getShop('shop-1'),
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

    test('wraps unknown errors as unknown failure', () async {
      when(
        () => remoteDataSource.getShop(any()),
      ).thenThrow(Exception('unexpected'));

      await expectLater(
        repository.getShop('shop-1'),
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
