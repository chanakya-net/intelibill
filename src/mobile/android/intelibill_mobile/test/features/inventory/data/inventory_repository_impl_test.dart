import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/inventory/data/data_sources/inventory_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/inventory/data/dto/generate_item_barcode_response_dto.dart';
import 'package:intelibill_mobile/src/features/inventory/data/repositories/inventory_repository_impl.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/generated_item_barcode.dart';
import 'package:mocktail/mocktail.dart';

class MockInventoryRemoteDataSource extends Mock
    implements InventoryRemoteDataSource {}

void main() {
  late MockInventoryRemoteDataSource remoteDataSource;
  late InventoryRepositoryImpl repository;

  setUp(() {
    remoteDataSource = MockInventoryRemoteDataSource();
    repository = InventoryRepositoryImpl(remoteDataSource);
  });

  group('InventoryRepositoryImpl', () {
    test('maps generated barcode DTO into typed domain result', () async {
      when(() => remoteDataSource.generateItemBarcode()).thenAnswer(
        (_) async => const GenerateItemBarcodeResponseDto(barcode: 'IB-000001'),
      );

      final generated = await repository.generateItemBarcode();

      expect(generated, isA<GeneratedItemBarcode>());
      expect(generated.barcode, 'IB-000001');
      verify(() => remoteDataSource.generateItemBarcode()).called(1);
    });

    test('rethrows AppExceptions from remote data source', () async {
      final exception = AppException(
        failure: const Failure.network(message: 'offline'),
      );
      when(() => remoteDataSource.generateItemBarcode()).thenThrow(exception);

      expect(repository.generateItemBarcode(), throwsA(same(exception)));
    });

    test('wraps malformed JSON as serialization failure', () async {
      when(
        () => remoteDataSource.generateItemBarcode(),
      ).thenThrow(const FormatException('missing barcode'));

      await expectLater(
        repository.generateItemBarcode(),
        throwsA(
          isA<AppException>().having(
            (error) => error.failure,
            'failure',
            isA<SerializationFailure>().having(
              (failure) => failure.message,
              'message',
              'missing barcode',
            ),
          ),
        ),
      );
    });

    test('wraps unknown exceptions as unknown failure', () async {
      when(
        () => remoteDataSource.generateItemBarcode(),
      ).thenThrow(Exception('boom'));

      await expectLater(
        repository.generateItemBarcode(),
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
