import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/users/data/data_sources/shop_user_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/users/data/dto/add_shop_user_request_dto.dart';
import 'package:intelibill_mobile/src/features/users/data/dto/edit_shop_user_request_dto.dart';
import 'package:mocktail/mocktail.dart';

import '../../../mocks/mock_api_client.dart';

void main() {
  late MockApiClient mockApiClient;
  late ShopUserRemoteDataSourceImpl remoteDataSource;

  setUp(() {
    mockApiClient = MockApiClient();
    remoteDataSource = ShopUserRemoteDataSourceImpl(mockApiClient);
  });

  group('ShopUserRemoteDataSourceImpl', () {
    test('calls /users endpoint and returns parsed DTOs', () async {
      final responseData = [
        {
          'userId': 'user-1',
          'firstName': 'Alice',
          'lastName': 'Sharma',
          'email': 'alice@example.com',
          'phoneNumber': '9876543210',
          'role': 'Manager',
          'isLoginEnabled': true,
          'shopIds': ['shop-1'],
        },
      ];

      when(() => mockApiClient.get<List<dynamic>>(any<String>())).thenAnswer(
        (_) async => Response(
          data: responseData,
          statusCode: 200,
          requestOptions: RequestOptions(path: '/users'),
        ),
      );

      final dtos = await remoteDataSource.getShopUsers();

      expect(dtos.length, 1);
      expect(dtos[0].userId, 'user-1');
      expect(dtos[0].role, 'Manager');
      verify(() => mockApiClient.get<List<dynamic>>('/users')).called(1);
    });

    test('posts to /users and returns created user', () async {
      const request = AddShopUserRequestDto(
        shopIds: ['shop-1'],
        email: 'bob@example.com',
        firstName: 'Bob',
        lastName: 'Kumar',
        phoneNumber: '9123456789',
        password: 'password1',
        confirmPassword: 'password1',
        role: 'Staff',
      );
      final responseData = {
        'userId': 'user-2',
        'firstName': 'Bob',
        'lastName': 'Kumar',
        'email': 'bob@example.com',
        'phoneNumber': '9123456789',
        'role': 'Staff',
        'isLoginEnabled': true,
        'shopIds': ['shop-1'],
      };

      when(
        () => mockApiClient.post<Map<String, dynamic>>(
          any<String>(),
          data: any<Map<String, dynamic>>(named: 'data'),
        ),
      ).thenAnswer(
        (_) async => Response(
          data: responseData,
          statusCode: 201,
          requestOptions: RequestOptions(path: '/users'),
        ),
      );

      final dto = await remoteDataSource.addShopUser(request);

      expect(dto.userId, 'user-2');
      expect(dto.role, 'Staff');
      verify(
        () => mockApiClient.post<Map<String, dynamic>>(
          '/users',
          data: request.toJson(),
        ),
      ).called(1);
    });

    test('puts to /users/{id} and returns updated user', () async {
      const request = EditShopUserRequestDto(
        email: 'alice@example.com',
        firstName: 'Alice',
        lastName: 'Sharma',
        phoneNumber: '9876543210',
        role: 'Manager',
        isLoginEnabled: false,
        shopIds: ['shop-1', 'shop-2'],
      );
      final responseData = {
        'userId': 'user-1',
        'firstName': 'Alice',
        'lastName': 'Sharma',
        'email': 'alice@example.com',
        'phoneNumber': '9876543210',
        'role': 'Manager',
        'isLoginEnabled': false,
        'shopIds': ['shop-1', 'shop-2'],
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
          requestOptions: RequestOptions(path: '/users/user-1'),
        ),
      );

      final dto = await remoteDataSource.editShopUser('user-1', request);

      expect(dto.isLoginEnabled, false);
      expect(dto.shopIds, ['shop-1', 'shop-2']);
      verify(
        () => mockApiClient.put<Map<String, dynamic>>(
          '/users/user-1',
          data: request.toJson(),
        ),
      ).called(1);
    });
  });
}
