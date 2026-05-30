import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/auth/data/data_sources/auth_remote_data_source.dart';
import 'package:mocktail/mocktail.dart';

import '../../../mocks/mock_api_client.dart';

void main() {
  late MockApiClient mockApiClient;
  late AuthRemoteDataSourceImpl remoteDataSource;

  setUp(() {
    mockApiClient = MockApiClient();
    remoteDataSource = AuthRemoteDataSourceImpl(mockApiClient);
  });

  group('AuthRemoteDataSourceImpl', () {
    test('login sends correct request body to /auth/login endpoint', () async {
      final responseData = {
        'accessToken': 'token123',
        'refreshToken': 'refresh123',
        'accessTokenExpiresAt': '2026-05-15T10:00:00Z',
        'refreshTokenExpiresAt': '2026-06-14T10:00:00Z',
        'user': {'id': 'user-1', 'firstName': 'John', 'lastName': 'Doe'},
      };

      when(
        () => mockApiClient.post<Map<String, dynamic>>(
          any<String>(),
          data: any<Map<String, dynamic>>(named: 'data'),
        ),
      ).thenAnswer((_) async {
        return Response(
          data: responseData,
          statusCode: 200,
          requestOptions: RequestOptions(path: '/auth/login'),
        );
      });

      final result = await remoteDataSource.login(
        identifier: 'test@example.com',
        password: 'password123',
      );

      expect(result.accessToken, 'token123');

      final captured =
          verify(
                () => mockApiClient.post<Map<String, dynamic>>(
                  '/auth/login',
                  data: captureAny<Map<String, dynamic>>(named: 'data'),
                ),
              ).captured.single
              as Map<String, dynamic>;

      expect(captured, {
        'identifier': 'test@example.com',
        'password': 'password123',
      });
    });

    test(
      'refreshToken sends correct request to /auth/token/refresh endpoint',
      () async {
        final responseData = {
          'accessToken': 'new_token',
          'refreshToken': 'new_refresh',
          'accessTokenExpiresAt': '2026-05-15T10:00:00Z',
          'refreshTokenExpiresAt': '2026-06-14T10:00:00Z',
          'user': {'id': 'user-1', 'firstName': 'John', 'lastName': 'Doe'},
        };

        when(
          () => mockApiClient.post<Map<String, dynamic>>(
            any<String>(),
            data: any<Map<String, dynamic>>(named: 'data'),
          ),
        ).thenAnswer((_) async {
          return Response(
            data: responseData,
            statusCode: 200,
            requestOptions: RequestOptions(path: '/auth/token/refresh'),
          );
        });

        final result = await remoteDataSource.refreshToken(
          refreshToken: 'old_refresh',
        );

        expect(result.accessToken, 'new_token');

        final captured =
            verify(
                  () => mockApiClient.post<Map<String, dynamic>>(
                    '/auth/token/refresh',
                    data: captureAny<Map<String, dynamic>>(named: 'data'),
                  ),
                ).captured.single
                as Map<String, dynamic>;

        expect(captured, {'refreshToken': 'old_refresh'});
      },
    );

    test('revokeToken calls /auth/token/revoke endpoint', () async {
      when(
        () => mockApiClient.post<void>(
          any<String>(),
          data: any<Map<String, dynamic>>(named: 'data'),
        ),
      ).thenAnswer((_) async {
        return Response(
          statusCode: 204,
          requestOptions: RequestOptions(path: '/auth/token/revoke'),
        );
      });

      await remoteDataSource.revokeToken(refreshToken: 'token_to_revoke');

      final captured =
          verify(
                () => mockApiClient.post<void>(
                  '/auth/token/revoke',
                  data: captureAny<Map<String, dynamic>>(named: 'data'),
                ),
              ).captured.single
              as Map<String, dynamic>;

      expect(captured, {'refreshToken': 'token_to_revoke'});
    });
  });
}
