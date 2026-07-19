import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/data_sources/purchase_order_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/repositories/purchase_order_repository_impl.dart';
import 'package:mocktail/mocktail.dart';

class _MockPurchaseOrderRemoteDataSource extends Mock
    implements PurchaseOrderRemoteDataSource {}

void main() {
  late _MockPurchaseOrderRemoteDataSource remote;
  late PurchaseOrderRepositoryImpl repository;

  setUp(() {
    remote = _MockPurchaseOrderRemoteDataSource();
    repository = PurchaseOrderRepositoryImpl(remote);
  });

  group('PurchaseOrderRepositoryImpl.deleteDraft', () {
    test('delegates deletion to the remote data source', () async {
      when(() => remote.deleteDraft('po-1')).thenAnswer((_) async {});

      await repository.deleteDraft('po-1');

      verify(() => remote.deleteDraft('po-1')).called(1);
    });

    test('preserves mapped API failures', () async {
      when(() => remote.deleteDraft('po-1')).thenThrow(
        AppException(failure: const Failure.server(statusCode: 409)),
      );

      expect(
        () => repository.deleteDraft('po-1'),
        throwsA(
          isA<AppException>().having(
            (error) => error.failure,
            'failure',
            const Failure.server(statusCode: 409),
          ),
        ),
      );
    });

    test('maps unexpected failures to unknown', () async {
      when(() => remote.deleteDraft('po-1')).thenThrow(Exception('offline'));

      expect(
        () => repository.deleteDraft('po-1'),
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
