import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/customers/data/data_sources/customer_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/customers/data/dto/create_customer_request_dto.dart';
import 'package:mocktail/mocktail.dart';

import '../../../mocks/mock_api_client.dart';

void main() {
  late MockApiClient mockApiClient;
  late CustomerRemoteDataSourceImpl remoteDataSource;

  setUp(() {
    mockApiClient = MockApiClient();
    remoteDataSource = CustomerRemoteDataSourceImpl(mockApiClient);
  });

  group('CustomerRemoteDataSourceImpl', () {
    test('calls /customers endpoint and returns parsed DTOs', () async {
      final responseData = [
        {
          'customerId': 'cust-1',
          'name': 'Alice Sharma',
          'phoneNumber': '9876543210',
          'address': '12 Main St',
          'isActive': true,
          'outstandingDue': 100.0,
        },
        {
          'customerId': 'cust-2',
          'name': 'Bob Kumar',
          'phoneNumber': '9123456789',
          'isActive': false,
          'outstandingDue': 0.0,
        },
      ];

      when(() => mockApiClient.get<List<dynamic>>(any<String>())).thenAnswer(
        (_) async => Response(
          data: responseData,
          statusCode: 200,
          requestOptions: RequestOptions(path: '/customers'),
        ),
      );

      final dtos = await remoteDataSource.getCustomers();

      expect(dtos.length, 2);
      expect(dtos[0].customerId, 'cust-1');
      expect(dtos[0].name, 'Alice Sharma');
      expect(dtos[1].customerId, 'cust-2');
      expect(dtos[1].address, isNull);

      verify(() => mockApiClient.get<List<dynamic>>('/customers')).called(1);
    });

    test('returns empty list when API returns empty array', () async {
      when(() => mockApiClient.get<List<dynamic>>(any<String>())).thenAnswer(
        (_) async => Response(
          data: <dynamic>[],
          statusCode: 200,
          requestOptions: RequestOptions(path: '/customers'),
        ),
      );

      final dtos = await remoteDataSource.getCustomers();

      expect(dtos, isEmpty);
    });

    test('posts to /customers and returns created customer', () async {
      const request = CreateCustomerRequestDto(
        name: 'Alice Sharma',
        phoneNumber: '9876543210',
        address: '12 Main St',
        isActive: true,
      );
      final responseData = {
        'customerId': 'cust-3',
        'name': 'Alice Sharma',
        'phoneNumber': '9876543210',
        'address': '12 Main St',
        'isActive': true,
        'outstandingDue': 0.0,
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
          requestOptions: RequestOptions(path: '/customers'),
        ),
      );

      final dto = await remoteDataSource.createCustomer(request);

      expect(dto.customerId, 'cust-3');
      expect(dto.name, 'Alice Sharma');
      expect(dto.phoneNumber, '9876543210');

      verify(
        () => mockApiClient.post<Map<String, dynamic>>(
          '/customers',
          data: request.toJson(),
        ),
      ).called(1);
    });
  });
}
