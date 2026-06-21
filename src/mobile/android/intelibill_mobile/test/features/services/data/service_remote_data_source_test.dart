import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/services/data/data_sources/services_remote_data_source.dart';
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
  });
}
