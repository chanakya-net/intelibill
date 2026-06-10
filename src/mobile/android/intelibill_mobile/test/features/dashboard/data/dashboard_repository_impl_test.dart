import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/dashboard/data/data_sources/dashboard_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/dashboard/data/repositories/dashboard_repository_impl.dart';
import 'package:mocktail/mocktail.dart';

import '../dashboard_test_fixtures.dart';

class MockDashboardRemoteDataSource extends Mock
    implements DashboardRemoteDataSource {}

void main() {
  late MockDashboardRemoteDataSource remoteDataSource;
  late DashboardRepositoryImpl repository;

  setUp(() {
    remoteDataSource = MockDashboardRemoteDataSource();
    repository = DashboardRepositoryImpl(remoteDataSource);
  });

  group('DashboardRepositoryImpl', () {
    test('maps remote dto into domain entity', () async {
      when(
        () => remoteDataSource.getDashboard(
          from: any(named: 'from'),
          to: any(named: 'to'),
        ),
      ).thenAnswer((_) async => dashboardDto);

      final result = await repository.getDashboard(
        from: DateTime(2026, 5, 1),
        to: DateTime(2026, 5, 31),
      );

      expect(result.salesCount, 5);
      expect(result.latestSales.first.invoiceNumber, 'INV-001');
      verify(
        () => remoteDataSource.getDashboard(
          from: '2026-05-01',
          to: '2026-05-31',
        ),
      ).called(1);
    });

    test('rethrows existing AppExceptions', () async {
      final exception = AppException(
        failure: const Failure.network(message: 'offline'),
      );
      when(
        () => remoteDataSource.getDashboard(
          from: any(named: 'from'),
          to: any(named: 'to'),
        ),
      ).thenThrow(exception);

      expect(
        repository.getDashboard(
          from: DateTime(2026, 5, 1),
          to: DateTime(2026, 5, 31),
        ),
        throwsA(same(exception)),
      );
    });

    test('wraps unexpected errors in AppException', () async {
      when(
        () => remoteDataSource.getDashboard(
          from: any(named: 'from'),
          to: any(named: 'to'),
        ),
      ).thenThrow(Exception('boom'));

      expect(
        repository.getDashboard(),
        throwsA(
          isA<AppException>().having(
            (error) => error.failure,
            'failure',
            isA<UnknownFailure>(),
          ),
        ),
      );
    });
  });
}
