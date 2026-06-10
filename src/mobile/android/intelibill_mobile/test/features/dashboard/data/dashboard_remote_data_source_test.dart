import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/dashboard/data/data_sources/dashboard_remote_data_source.dart';
import 'package:mocktail/mocktail.dart';

import '../../../mocks/mock_api_client.dart';
import '../dashboard_test_fixtures.dart';

void main() {
  late MockApiClient mockApiClient;
  late DashboardRemoteDataSourceImpl remoteDataSource;

  setUp(() {
    mockApiClient = MockApiClient();
    remoteDataSource = DashboardRemoteDataSourceImpl(mockApiClient);
  });

  group('DashboardRemoteDataSourceImpl', () {
    test('calls /dashboard without query params by default', () async {
      when(
        () => mockApiClient.get<Map<String, dynamic>>(
          any<String>(),
          queryParameters: any(named: 'queryParameters'),
        ),
      ).thenAnswer(
        (_) async => Response(
          data: dashboardJson,
          statusCode: 200,
          requestOptions: RequestOptions(path: '/dashboard'),
        ),
      );

      final dto = await remoteDataSource.getDashboard();

      expect(dto.salesCount, 5);
      verify(
        () => mockApiClient.get<Map<String, dynamic>>('/dashboard'),
      ).called(1);
    });

    test('passes from and to query params when provided', () async {
      when(
        () => mockApiClient.get<Map<String, dynamic>>(
          any<String>(),
          queryParameters: any(named: 'queryParameters'),
        ),
      ).thenAnswer(
        (_) async => Response(
          data: dashboardJson,
          statusCode: 200,
          requestOptions: RequestOptions(path: '/dashboard'),
        ),
      );

      await remoteDataSource.getDashboard(
        from: '2026-05-01',
        to: '2026-05-31',
      );

      verify(
        () => mockApiClient.get<Map<String, dynamic>>(
          '/dashboard',
          queryParameters: {'from': '2026-05-01', 'to': '2026-05-31'},
        ),
      ).called(1);
    });
  });
}
