import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/shops/data/data_sources/shop_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/shops/data/dtos/add_bank_account_request_dto.dart';
import 'package:intelibill_mobile/src/features/shops/data/dtos/create_shop_request_dto.dart';
import 'package:intelibill_mobile/src/features/shops/data/dtos/update_shop_request_dto.dart';
import 'package:mocktail/mocktail.dart';

import '../../../../mocks/mock_api_client.dart';

void main() {
  late MockApiClient mockApiClient;
  late ShopRemoteDataSourceImpl remoteDataSource;

  setUp(() {
    mockApiClient = MockApiClient();
    remoteDataSource = ShopRemoteDataSourceImpl(mockApiClient);
  });

  group('ShopRemoteDataSourceImpl', () {
    test('calls GET /shops/{shopId} and returns parsed DTO', () async {
      final responseData = {
        'shopId': 'shop-1',
        'name': 'My Shop',
        'address': '12 Main Street',
        'city': 'Mumbai',
        'state': 'Maharashtra',
        'pincode': '400001',
        'contactPerson': 'John',
        'mobileNumber': '9999999999',
        'gstNumber': 'GST123',
        'bankName': 'HDFC',
        'bankAccountNumber': '123456',
        'bankAccountType': 'Savings',
        'ifscCode': 'HDFC0001',
        'accountHolderName': 'John Doe',
      };

      when(
        () => mockApiClient.get<Map<String, dynamic>>(any<String>()),
      ).thenAnswer(
        (_) async => Response(
          data: responseData,
          statusCode: 200,
          requestOptions: RequestOptions(path: '/shops/shop-1'),
        ),
      );

      final dto = await remoteDataSource.getShop('shop-1');

      expect(dto.shopId, 'shop-1');
      expect(dto.name, 'My Shop');
      expect(dto.bankName, 'HDFC');

      verify(
        () => mockApiClient.get<Map<String, dynamic>>('/shops/shop-1'),
      ).called(1);
    });

    test('calls POST /shops with payload and returns parsed AuthResultDto',
        () async {
      const request = CreateShopRequestDto(
        name: 'My Shop',
        address: 'Addr',
        city: 'City',
        state: 'State',
        pincode: '000000',
        contactPerson: 'John',
        mobileNumber: '999',
        gstNumber: 'GST',
      );

      final responseData = {
        'accessToken': 'access',
        'refreshToken': 'refresh',
        'accessTokenExpiresAt': '2030-01-01T00:00:00.000Z',
        'refreshTokenExpiresAt': '2030-01-02T00:00:00.000Z',
        'user': {
          'id': 'user-1',
          'email': 'a@b.com',
          'phoneNumber': null,
          'firstName': 'A',
          'lastName': 'B',
          'language': 'en-IN',
        },
        'activeShopId': 'shop-1',
        'shops': [
          {
            'shopId': 'shop-1',
            'shopName': 'My Shop',
            'role': 'Owner',
            'isDefault': true,
            'lastUsedAt': null,
          },
        ],
      };

      when(
        () => mockApiClient.post<Map<String, dynamic>>(
          any<String>(),
          data: any<Map<String, dynamic>>(named: 'data'),
        ),
      ).thenAnswer(
        (_) async => Response(
          data: responseData,
          statusCode: 200,
          requestOptions: RequestOptions(path: '/shops'),
        ),
      );

      final dto = await remoteDataSource.createShop(request);

      expect(dto.accessToken, 'access');
      expect(dto.activeShopId, 'shop-1');

      verify(
        () => mockApiClient.post<Map<String, dynamic>>(
          '/shops',
          data: request.toJson(),
        ),
      ).called(1);
    });

    test('calls PUT /shops/{shopId} with payload and returns parsed DTO',
        () async {
      const request = UpdateShopRequestDto(
        name: 'My Shop',
        address: 'Addr',
        city: 'City',
        state: 'State',
        pincode: '000000',
      );

      final responseData = {
        'shopId': 'shop-1',
        'name': 'My Shop',
        'address': 'Addr',
        'city': 'City',
        'state': 'State',
        'pincode': '000000',
        'bankName': null,
        'bankAccountNumber': null,
        'bankAccountType': null,
        'ifscCode': null,
        'accountHolderName': null,
      };

      when(
        () => mockApiClient.put<Map<String, dynamic>>(
          any<String>(),
          data: any<Map<String, dynamic>>(named: 'data'),
        ),
      ).thenAnswer(
        (_) async => Response(
          data: responseData,
          statusCode: 200,
          requestOptions: RequestOptions(path: '/shops/shop-1'),
        ),
      );

      final dto = await remoteDataSource.updateShop('shop-1', request);

      expect(dto.shopId, 'shop-1');
      expect(dto.pincode, '000000');

      verify(
        () => mockApiClient.put<Map<String, dynamic>>(
          '/shops/shop-1',
          data: request.toJson(),
        ),
      ).called(1);
    });

    test('calls POST /bank-accounts with payload and returns parsed DTO',
        () async {
      const request = AddBankAccountRequestDto(
        bankName: 'HDFC',
        accountNumber: '123',
        accountType: 'Savings',
        ifscCode: 'HDFC0001',
        accountHolderName: 'John Doe',
      );

      final responseData = {
        'id': 'ba-1',
        'bankName': 'HDFC',
        'accountNumber': '123',
        'accountType': 'Savings',
        'ifscCode': 'HDFC0001',
        'accountHolderName': 'John Doe',
      };

      when(
        () => mockApiClient.post<Map<String, dynamic>>(
          any<String>(),
          data: any<Map<String, dynamic>>(named: 'data'),
        ),
      ).thenAnswer(
        (_) async => Response(
          data: responseData,
          statusCode: 200,
          requestOptions: RequestOptions(path: '/bank-accounts'),
        ),
      );

      final dto = await remoteDataSource.addBankAccount(request);

      expect(dto.id, 'ba-1');
      expect(dto.bankName, 'HDFC');

      verify(
        () => mockApiClient.post<Map<String, dynamic>>(
          '/bank-accounts',
          data: request.toJson(),
        ),
      ).called(1);
    });

    test('propagates AppException from ApiClient', () async {
      final exception = AppException(
        failure: const Failure.network(message: 'offline'),
      );
      when(
        () => mockApiClient.get<Map<String, dynamic>>(any<String>()),
      ).thenThrow(exception);

      expect(
        () => remoteDataSource.getShop('shop-1'),
        throwsA(same(exception)),
      );
    });
  });
}
