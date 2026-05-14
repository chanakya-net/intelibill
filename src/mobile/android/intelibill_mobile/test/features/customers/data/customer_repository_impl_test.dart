import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/customers/data/data_sources/customer_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/customers/data/dto/create_customer_request_dto.dart';
import 'package:intelibill_mobile/src/features/customers/data/dto/customer_dto.dart';
import 'package:intelibill_mobile/src/features/customers/data/repositories/customer_repository_impl.dart';
import 'package:mocktail/mocktail.dart';

class MockCustomerRemoteDataSource extends Mock
    implements CustomerRemoteDataSource {}

void main() {
  setUpAll(() {
    registerFallbackValue(
      const CreateCustomerRequestDto(
        name: 'fallback',
        phoneNumber: '0000000000',
        isActive: true,
      ),
    );
  });

  late MockCustomerRemoteDataSource remoteDataSource;
  late CustomerRepositoryImpl repository;

  setUp(() {
    remoteDataSource = MockCustomerRemoteDataSource();
    repository = CustomerRepositoryImpl(remoteDataSource);
  });

  group('CustomerRepositoryImpl', () {
    test('maps remote dtos into domain entities', () async {
      when(
        () => remoteDataSource.getCustomers(),
      ).thenAnswer(
        (_) async => const [
          CustomerDto(
            customerId: 'cust-1',
            name: 'Alice Sharma',
            phoneNumber: '9876543210',
            address: '12 Main St',
            isActive: true,
            outstandingDue: 100,
          ),
        ],
      );

      final result = await repository.getCustomers();

      expect(result.length, 1);
      expect(result[0].customerId, 'cust-1');
      expect(result[0].name, 'Alice Sharma');
      expect(result[0].address, '12 Main St');
    });

    test('returns empty list when data source returns empty list', () async {
      when(
        () => remoteDataSource.getCustomers(),
      ).thenAnswer((_) async => []);

      final result = await repository.getCustomers();

      expect(result, isEmpty);
    });

    test('rethrows existing AppExceptions', () async {
      final exception = AppException(
        failure: const Failure.network(message: 'offline'),
      );
      when(() => remoteDataSource.getCustomers()).thenThrow(exception);

      expect(repository.getCustomers(), throwsA(same(exception)));
    });

    test('wraps FormatException as serialization failure', () async {
      when(
        () => remoteDataSource.getCustomers(),
      ).thenThrow(const FormatException('bad json'));

      await expectLater(
        repository.getCustomers(),
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
        () => remoteDataSource.getCustomers(),
      ).thenThrow(Exception('unexpected'));

      await expectLater(
        repository.getCustomers(),
        throwsA(
          isA<AppException>().having(
            (e) => e.failure,
            'failure',
            isA<UnknownFailure>(),
          ),
        ),
      );
    });

    test('creates customer with trimmed data and maps result', () async {
      const createdDto = CustomerDto(
        customerId: 'cust-10',
        name: 'Alice Sharma',
        phoneNumber: '9876543210',
        isActive: true,
      );

      when(
        () => remoteDataSource.createCustomer(any()),
      ).thenAnswer((_) async => createdDto);

      final result = await repository.createCustomer(
        name: '  Alice Sharma  ',
        phoneNumber: ' 9876543210 ',
        address: '   ',
        isActive: true,
      );

      expect(result.customerId, 'cust-10');
      expect(result.address, isNull);

      verify(
        () => remoteDataSource.createCustomer(
          const CreateCustomerRequestDto(
            name: 'Alice Sharma',
            phoneNumber: '9876543210',
            isActive: true,
          ),
        ),
      ).called(1);
    });

    test('rethrows AppExceptions from create', () async {
      final exception = AppException(
        failure: const Failure.validation(message: 'invalid'),
      );
      when(
        () => remoteDataSource.createCustomer(any()),
      ).thenThrow(exception);

      expect(
        repository.createCustomer(
          name: 'Alice',
          phoneNumber: '9876543210',
          address: 'Mumbai',
          isActive: true,
        ),
        throwsA(same(exception)),
      );
    });

    test(
      'wraps FormatException from create as serialization failure',
      () async {
        when(
          () => remoteDataSource.createCustomer(any()),
        ).thenThrow(const FormatException('bad json'));

        await expectLater(
          repository.createCustomer(
            name: 'Alice',
            phoneNumber: '9876543210',
            address: 'Mumbai',
            isActive: true,
          ),
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
      },
    );

    test('wraps unknown errors from create as unknown failure', () async {
      when(
        () => remoteDataSource.createCustomer(any()),
      ).thenThrow(Exception('unexpected'));

      await expectLater(
        repository.createCustomer(
          name: 'Alice',
          phoneNumber: '9876543210',
          address: 'Mumbai',
          isActive: true,
        ),
        throwsA(
          isA<AppException>().having(
            (e) => e.failure,
            'failure',
            isA<UnknownFailure>(),
          ),
        ),
      );
    });
  });
}
