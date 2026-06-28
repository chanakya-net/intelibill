import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/services/data/data_sources/services_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/services/data/dto/create_service_request_dto.dart';
import 'package:intelibill_mobile/src/features/services/data/dto/service_dto.dart';
import 'package:intelibill_mobile/src/features/services/data/dto/update_service_request_dto.dart';
import 'package:intelibill_mobile/src/features/services/data/repositories/services_repository_impl.dart';
import 'package:mocktail/mocktail.dart';

class MockServicesRemoteDataSource extends Mock
    implements ServicesRemoteDataSource {}

void main() {
  late MockServicesRemoteDataSource remoteDataSource;
  late ServicesRepositoryImpl repository;

  setUpAll(() {
    registerFallbackValue(
      const CreateServiceRequestDto(
        name: 'Fallback Service',
        price: 1,
        taxRatePercent: 0,
        taxIncluded: false,
        isActive: true,
      ),
    );
    registerFallbackValue(
      const UpdateServiceRequestDto(
        name: 'Fallback Service',
        price: 1,
        taxRatePercent: 0,
        taxIncluded: false,
      ),
    );
  });

  setUp(() {
    remoteDataSource = MockServicesRemoteDataSource();
    repository = ServicesRepositoryImpl(remoteDataSource);
  });

  group('ServicesRepositoryImpl', () {
    test('maps remote dtos into domain entities', () async {
      when(
        () => remoteDataSource.getServices(includeInactive: true),
      ).thenAnswer(
        (_) async => const [
          ServiceDto(
            serviceId: 'svc-1',
            code: 'SRV-001',
            name: 'Repair',
            description: 'Phone repair service',
            price: 499.9,
            hsnCode: '9987',
            taxRatePercent: 18,
            taxIncluded: true,
            isActive: true,
          ),
        ],
      );

      final result = await repository.getServices(includeInactive: true);

      expect(result, hasLength(1));
      expect(result.first.serviceId, 'svc-1');
      expect(result.first.code, 'SRV-001');
      expect(result.first.taxIncluded, isTrue);
    });

    test('rethrows existing AppExceptions', () async {
      final exception = AppException(
        failure: const Failure.network(message: 'offline'),
      );
      when(
        () => remoteDataSource.getServices(includeInactive: true),
      ).thenThrow(exception);

      expect(
        repository.getServices(includeInactive: true),
        throwsA(same(exception)),
      );
    });

    test('wraps FormatException as serialization failure', () async {
      when(
        () => remoteDataSource.getServices(includeInactive: true),
      ).thenThrow(const FormatException('bad json'));

      await expectLater(
        repository.getServices(includeInactive: true),
        throwsA(
          isA<AppException>().having(
            (e) => e.failure,
            'failure',
            isA<SerializationFailure>().having(
              (f) => f.message,
              'message',
              'bad json',
            ),
          ),
        ),
      );
    });

    test('wraps unknown errors as unknown failure', () async {
      when(
        () => remoteDataSource.getServices(includeInactive: true),
      ).thenThrow(Exception('unexpected'));

      await expectLater(
        repository.getServices(includeInactive: true),
        throwsA(
          isA<AppException>().having(
            (e) => e.failure,
            'failure',
            isA<UnknownFailure>(),
          ),
        ),
      );
    });

    test(
      'creates service with trimmed and null-normalized request data',
      () async {
        const createdDto = ServiceDto(
          serviceId: 'svc-new',
          code: 'SRV-010',
          name: 'Repair',
          description: 'Phone repair service',
          price: 499.9,
          hsnCode: '9987',
          taxRatePercent: 18,
          taxIncluded: true,
          isActive: true,
        );
        when(
          () => remoteDataSource.createService(any()),
        ).thenAnswer((_) async => createdDto);

        final result = await repository.createService(
          name: '  Repair  ',
          description: '  Phone repair service  ',
          price: 499.9,
          hsnCode: ' 9987 ',
          taxRatePercent: 18,
          taxIncluded: true,
          isActive: true,
        );

        expect(result.serviceId, 'svc-new');
        verify(
          () => remoteDataSource.createService(
            const CreateServiceRequestDto(
              name: 'Repair',
              description: 'Phone repair service',
              price: 499.9,
              hsnCode: '9987',
              taxRatePercent: 18,
              taxIncluded: true,
              isActive: true,
            ),
          ),
        ).called(1);
      },
    );

    test(
      'updates service with trimmed and null-normalized request data',
      () async {
        when(
          () => remoteDataSource.updateService(any(), any()),
        ).thenAnswer((_) async {});

        await repository.updateService(
          serviceId: 'svc-1',
          name: '  Repair Updated  ',
          description: '   ',
          price: 599,
          hsnCode: ' 9988 ',
          taxRatePercent: 12,
          taxIncluded: false,
        );

        verify(
          () => remoteDataSource.updateService(
            'svc-1',
            const UpdateServiceRequestDto(
              name: 'Repair Updated',
              price: 599,
              hsnCode: '9988',
              taxRatePercent: 12,
              taxIncluded: false,
            ),
          ),
        ).called(1);
      },
    );

    test(
      'activates and deactivates services through the remote source',
      () async {
        when(
          () => remoteDataSource.activateService('svc-1'),
        ).thenAnswer((_) async {});
        when(
          () => remoteDataSource.deactivateService('svc-1'),
        ).thenAnswer((_) async {});

        await repository.activateService('svc-1');
        await repository.deactivateService('svc-1');

        verify(() => remoteDataSource.activateService('svc-1')).called(1);
        verify(() => remoteDataSource.deactivateService('svc-1')).called(1);
      },
    );
  });
}
