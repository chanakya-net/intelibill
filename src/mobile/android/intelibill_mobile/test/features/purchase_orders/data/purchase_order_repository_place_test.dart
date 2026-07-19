import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/data_sources/purchase_order_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/purchase_order_detail_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/repositories/purchase_order_repository_impl.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:mocktail/mocktail.dart';

class MockPurchaseOrderRemoteDataSource extends Mock
    implements PurchaseOrderRemoteDataSource {}

void main() {
  late MockPurchaseOrderRemoteDataSource mockDataSource;
  late PurchaseOrderRepositoryImpl repository;

  setUp(() {
    mockDataSource = MockPurchaseOrderRemoteDataSource();
    repository = PurchaseOrderRepositoryImpl(mockDataSource);
  });

  group('PurchaseOrderRepositoryImpl.place', () {
    test('maps placed DTO to domain PurchaseOrder', () async {
      final dto = _placedDto();
      when(
        () => mockDataSource.place(any()),
      ).thenAnswer((_) async => dto);

      final result = await repository.place('po-1');

      expect(result.purchaseOrderId, 'po-1');
      expect(result.status, PurchaseOrderStatus.placed);
    });

    test('wraps AppException from data source', () async {
      when(
        () => mockDataSource.place(any()),
      ).thenThrow(AppException(failure: const Failure.notFound()));

      expect(
        () => repository.place('po-1'),
        throwsA(isA<AppException>()),
      );
    });

    test('wraps unknown error', () async {
      when(
        () => mockDataSource.place(any()),
      ).thenThrow(Exception('network error'));

      expect(
        () => repository.place('po-1'),
        throwsA(isA<AppException>()),
      );
    });
  });
}

PurchaseOrderDetailDto _placedDto() {
  return PurchaseOrderDetailDto(
    purchaseOrderId: 'po-1',
    purchaseOrderNumber: 'PO-001',
    status: 'Placed',
    lines: const [],
    expectedTotal: 100,
    createdAt: DateTime.utc(2026, 7),
    receivedQuantity: 0,
  );
}
