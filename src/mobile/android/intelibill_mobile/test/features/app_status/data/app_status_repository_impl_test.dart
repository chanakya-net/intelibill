import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/app_status/data/data_sources/app_status_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/app_status/data/dto/app_status_dto.dart';
import 'package:intelibill_mobile/src/features/app_status/data/repositories/app_status_repository_impl.dart';
import 'package:mocktail/mocktail.dart';

class MockAppStatusRemoteDataSource extends Mock
    implements AppStatusRemoteDataSource {}

void main() {
  late MockAppStatusRemoteDataSource remoteDataSource;
  late AppStatusRepositoryImpl repository;

  setUp(() {
    remoteDataSource = MockAppStatusRemoteDataSource();
    repository = AppStatusRepositoryImpl(remoteDataSource);
  });

  group('AppStatusRepositoryImpl', () {
    test('maps remote dto into domain entity', () async {
      when(
        () => remoteDataSource.getStatus(),
      ).thenAnswer(
        (_) async => AppStatusDto(
          statusText: 'Ready',
          apiBaseUrl: 'https://api.example.com',
          timestamp: DateTime.utc(2026, 5, 14, 10),
          environment: 'test',
        ),
      );

      final result = await repository.getStatus();

      expect(result.statusText, 'Ready');
      expect(result.apiBaseUrl, 'https://api.example.com');
      expect(result.timestamp, DateTime.utc(2026, 5, 14, 10));
      expect(result.environment, 'test');
    });

    test('rethrows existing app exceptions', () async {
      final exception = AppException(
        failure: const Failure.network(message: 'offline'),
      );
      when(() => remoteDataSource.getStatus()).thenThrow(exception);

      expect(repository.getStatus(), throwsA(same(exception)));
    });

    test('wraps format exceptions as serialization failures', () async {
      when(
        () => remoteDataSource.getStatus(),
      ).thenThrow(const FormatException('bad payload'));

      await expectLater(
        repository.getStatus(),
        throwsA(
          isA<AppException>().having(
            (error) => error.failure,
            'failure',
            isA<SerializationFailure>().having(
              (failure) => failure.message,
              'message',
              'bad payload',
            ),
          ),
        ),
      );
    });
  });
}
