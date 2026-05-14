import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/suppliers/data/data_sources/supplier_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/suppliers/data/dto/create_supplier_request_dto.dart';
import 'package:intelibill_mobile/src/features/suppliers/data/dto/supplier_dto.dart';
import 'package:intelibill_mobile/src/features/suppliers/data/repositories/supplier_repository_impl.dart';
import 'package:mocktail/mocktail.dart';

class MockSupplierRemoteDataSource extends Mock
    implements SupplierRemoteDataSource {}

void main() {
  late MockSupplierRemoteDataSource remoteDataSource;
  late SupplierRepositoryImpl repository;

  setUpAll(() {
    registerFallbackValue(
      const CreateSupplierRequestDto(
        name: 'Fallback Supplier',
        address: 'Fallback Address',
        city: 'Fallback City',
        state: 'Fallback State',
        pin: '000000',
        isActive: true,
        isPreferred: false,
      ),
    );
  });

  setUp(() {
    remoteDataSource = MockSupplierRemoteDataSource();
    repository = SupplierRepositoryImpl(remoteDataSource);
  });

  group('SupplierRepositoryImpl', () {
    test('maps remote dtos into domain entities', () async {
      when(
        () => remoteDataSource.getSuppliers(),
      ).thenAnswer(
        (_) async => const [
          SupplierDto(
            supplierId: 'sup-1',
            name: 'ABC Traders',
            contactPersonName: 'John Doe',
            contactPersonPhone: '9876543210',
            address: '12 Main St',
            city: 'Mumbai',
            state: 'Maharashtra',
            pin: '400001',
            isSystem: false,
            isActive: true,
            isPreferred: true,
            balanceDue: 1500.50,
          ),
        ],
      );

      final result = await repository.getSuppliers();

      expect(result.length, 1);
      expect(result[0].supplierId, 'sup-1');
      expect(result[0].name, 'ABC Traders');
      expect(result[0].city, 'Mumbai');
      expect(result[0].balanceDue, 1500.50);
    });

    test('returns empty list when data source returns empty list', () async {
      when(
        () => remoteDataSource.getSuppliers(),
      ).thenAnswer((_) async => []);

      final result = await repository.getSuppliers();

      expect(result, isEmpty);
    });

    test('rethrows existing AppExceptions', () async {
      final exception = AppException(
        failure: const Failure.network(message: 'offline'),
      );
      when(() => remoteDataSource.getSuppliers()).thenThrow(exception);

      expect(repository.getSuppliers(), throwsA(same(exception)));
    });

    test('wraps FormatException as serialization failure', () async {
      when(
        () => remoteDataSource.getSuppliers(),
      ).thenThrow(const FormatException('bad json'));

      await expectLater(
        repository.getSuppliers(),
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
        () => remoteDataSource.getSuppliers(),
      ).thenThrow(Exception('unexpected'));

      await expectLater(
        repository.getSuppliers(),
        throwsA(
          isA<AppException>().having(
            (e) => e.failure,
            'failure',
            isA<UnknownFailure>(),
          ),
        ),
      );
    });

    test('maps multiple suppliers correctly', () async {
      when(
        () => remoteDataSource.getSuppliers(),
      ).thenAnswer(
        (_) async => const [
          SupplierDto(
            supplierId: 'sup-1',
            name: 'ABC Traders',
            isSystem: false,
            isActive: true,
            isPreferred: false,
          ),
          SupplierDto(
            supplierId: 'sup-2',
            name: 'XYZ Suppliers',
            isSystem: false,
            isActive: true,
            isPreferred: true,
            balanceDue: 500,
          ),
        ],
      );

      final result = await repository.getSuppliers();

      expect(result.length, 2);
      expect(result[0].name, 'ABC Traders');
      expect(result[1].name, 'XYZ Suppliers');
      expect(result[1].isPreferred, true);
    });

    test('preserves system supplier flag through mapping', () async {
      when(
        () => remoteDataSource.getSuppliers(),
      ).thenAnswer(
        (_) async => const [
          SupplierDto(
            supplierId: 'sup-sys',
            name: 'System Supplier',
            isSystem: true,
            isActive: true,
            isPreferred: false,
          ),
        ],
      );

      final result = await repository.getSuppliers();

      expect(result[0].isSystem, true);
    });

    test('maps null contact fields safely', () async {
      when(
        () => remoteDataSource.getSuppliers(),
      ).thenAnswer(
        (_) async => const [
          SupplierDto(
            supplierId: 'sup-3',
            name: 'DEF Corp',
            isSystem: false,
            isActive: false,
            isPreferred: false,
          ),
        ],
      );

      final result = await repository.getSuppliers();

      expect(result[0].contactPersonName, isNull);
      expect(result[0].contactPersonPhone, isNull);
      expect(result[0].address, isNull);
    });

    test('defaults balanceDue to 0.0 in mapped entity', () async {
      when(
        () => remoteDataSource.getSuppliers(),
      ).thenAnswer(
        (_) async => const [
          SupplierDto(
            supplierId: 'sup-4',
            name: 'GHI Ltd',
            isSystem: false,
            isActive: true,
            isPreferred: false,
          ),
        ],
      );

      final result = await repository.getSuppliers();

      expect(result[0].balanceDue, 0.0);
    });

    test(
      'creates supplier with trimmed and null-normalized request data',
      () async {
        const createdDto = SupplierDto(
          supplierId: 'sup-new',
          name: 'ABC Traders',
          address: '12 Main Street',
          city: 'Mumbai',
          state: 'Maharashtra',
          pin: '400001',
          isSystem: false,
          isActive: true,
          isPreferred: true,
        );
        when(
          () => remoteDataSource.createSupplier(any()),
        ).thenAnswer((_) async => createdDto);

        final result = await repository.createSupplier(
          name: '  ABC Traders  ',
          contactPersonName: '   ',
          contactPersonPhone: '   ',
          address: ' 12 Main Street ',
          city: ' Mumbai ',
          state: ' Maharashtra ',
          pin: ' 400001 ',
          isActive: true,
          isPreferred: true,
        );

        expect(result.name, 'ABC Traders');
        verify(
          () => remoteDataSource.createSupplier(
            const CreateSupplierRequestDto(
              name: 'ABC Traders',
              address: '12 Main Street',
              city: 'Mumbai',
              state: 'Maharashtra',
              pin: '400001',
              isActive: true,
              isPreferred: true,
            ),
          ),
        ).called(1);
      },
    );
  });
}
