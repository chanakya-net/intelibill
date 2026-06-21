import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/services/data/data_sources/services_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/services/data/dto/create_service_request_dto.dart';
import 'package:intelibill_mobile/src/features/services/data/dto/update_service_request_dto.dart';
import 'package:mocktail/mocktail.dart';

import '../../../mocks/mock_api_client.dart';

void main() {
  late MockApiClient mockApiClient;
  late ServicesRemoteDataSourceImpl remoteDataSource;

  setUp(() {
    mockApiClient = MockApiClient();
    remoteDataSource = ServicesRemoteDataSourceImpl(mockApiClient);
  });

  group('ServicesRemoteDataSourceImpl', () {
    test(
      'calls /services with includeInactive false and search query',
      () async {
        final responseData = [
          {
            'serviceId': 'svc-1',
            'code': 'SRV-001',
            'name': 'Repair',
            'description': 'Phone repair',
            'price': 300,
            'taxRatePercent': 18,
            'taxIncluded': true,
            'isActive': true,
          },
        ];

        when(
          () => mockApiClient.get<List<dynamic>>(
            any<String>(),
            queryParameters: any<Map<String, dynamic>>(
              named: 'queryParameters',
            ),
          ),
        ).thenAnswer(
          (_) async => Response(
            data: responseData,
            statusCode: 200,
            requestOptions: RequestOptions(path: '/services'),
          ),
        );

        final dtos = await remoteDataSource.getServices(
          includeInactive: false,
          search: ' repair ',
        );

        expect(dtos.length, 1);
        expect(dtos[0].name, 'Repair');
        verify(
          () => mockApiClient.get<List<dynamic>>(
            '/services',
            queryParameters: {
              'includeInactive': false,
              'search': 'repair',
            },
          ),
        ).called(1);
      },
    );

    test('calls /services with includeInactive true and no search', () async {
      when(
        () => mockApiClient.get<List<dynamic>>(
          any<String>(),
          queryParameters: any<Map<String, dynamic>>(named: 'queryParameters'),
        ),
      ).thenAnswer(
        (_) async => Response(
          data: <dynamic>[],
          statusCode: 200,
          requestOptions: RequestOptions(path: '/services'),
        ),
      );

      await remoteDataSource.getServices(includeInactive: true);

      verify(
        () => mockApiClient.get<List<dynamic>>(
          '/services',
          queryParameters: {'includeInactive': true},
        ),
      ).called(1);
    });

    test('posts create service payload to /services', () async {
      const request = CreateServiceRequestDto(
        name: 'Repair',
        description: 'Phone repair service',
        price: 499.9,
        hsnCode: '9987',
        taxRatePercent: 18,
        taxIncluded: true,
        isActive: true,
      );
      final responseData = {
        'serviceId': 'svc-1',
        'code': 'SRV-001',
        'name': 'Repair',
        'description': 'Phone repair service',
        'price': 499.9,
        'hsnCode': '9987',
        'taxRatePercent': 18,
        'taxIncluded': true,
        'isActive': true,
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
          requestOptions: RequestOptions(path: '/services'),
        ),
      );

      final dto = await remoteDataSource.createService(request);

      expect(dto.serviceId, 'svc-1');
      expect(dto.code, 'SRV-001');
      verify(
        () => mockApiClient.post<Map<String, dynamic>>(
          '/services',
          data: request.toJson(),
        ),
      ).called(1);
    });

    test('patches update service payload to /services/{id}', () async {
      const request = UpdateServiceRequestDto(
        name: 'Repair Updated',
        description: 'Updated description',
        price: 599.0,
        hsnCode: '9988',
        taxRatePercent: 12,
        taxIncluded: false,
      );

      when(
        () => mockApiClient.patch<void>(
          any<String>(),
          data: any<Map<String, dynamic>>(named: 'data'),
        ),
      ).thenAnswer(
        (_) async => Response<void>(
          data: null,
          statusCode: 204,
          requestOptions: RequestOptions(path: '/services/svc-1'),
        ),
      );

      await remoteDataSource.updateService('svc-1', request);

      verify(
        () => mockApiClient.patch<void>(
          '/services/svc-1',
          data: request.toJson(),
        ),
      ).called(1);
    });

    test('posts activate request to /services/{id}/activate', () async {
      when(() => mockApiClient.post<void>(any<String>())).thenAnswer(
        (_) async => Response<void>(
          data: null,
          statusCode: 204,
          requestOptions: RequestOptions(path: '/services/svc-1/activate'),
        ),
      );

      await remoteDataSource.activateService('svc-1');

      verify(
        () => mockApiClient.post<void>('/services/svc-1/activate'),
      ).called(1);
    });

    test('posts deactivate request to /services/{id}/deactivate', () async {
      when(() => mockApiClient.post<void>(any<String>())).thenAnswer(
        (_) async => Response<void>(
          data: null,
          statusCode: 204,
          requestOptions: RequestOptions(path: '/services/svc-1/deactivate'),
        ),
      );

      await remoteDataSource.deactivateService('svc-1');

      verify(
        () => mockApiClient.post<void>('/services/svc-1/deactivate'),
      ).called(1);
    });
  });
}
