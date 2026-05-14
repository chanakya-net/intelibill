import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/app_status/domain/entities/app_status.dart';
import 'package:intelibill_mobile/src/features/app_status/domain/repositories/app_status_repository.dart';
import 'package:intelibill_mobile/src/features/app_status/domain/use_cases/get_app_status.dart';
import 'package:mocktail/mocktail.dart';

class MockAppStatusRepository extends Mock implements AppStatusRepository {}

void main() {
  late MockAppStatusRepository repository;
  late GetAppStatus useCase;

  setUp(() {
    repository = MockAppStatusRepository();
    useCase = GetAppStatus(repository);
  });

  group('GetAppStatus', () {
    test('returns status from repository', () async {
      final expectedStatus = AppStatus(
        statusText: 'Ready',
        apiBaseUrl: 'https://api.example.com',
        timestamp: DateTime.utc(2026, 5, 14, 10),
        environment: 'test',
      );
      when(
        () => repository.getStatus(),
      ).thenAnswer((_) async => expectedStatus);

      final result = await useCase();

      expect(result, expectedStatus);
      verify(() => repository.getStatus()).called(1);
    });

    test('propagates repository failure', () async {
      final error = StateError('boom');
      when(
        () => repository.getStatus(),
      ).thenAnswer((_) => Future<AppStatus>.error(error));

      expect(useCase(), throwsA(same(error)));
      verify(() => repository.getStatus()).called(1);
    });
  });
}
