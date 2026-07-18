import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/data_sources/purchase_order_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/close_purchase_order_request_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/purchase_order_detail_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/repositories/purchase_order_repository_impl.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:mocktail/mocktail.dart';

class MockPurchaseOrderRemoteDataSource extends Mock
    implements PurchaseOrderRemoteDataSource {}

void main() {
  late MockPurchaseOrderRemoteDataSource dataSource;
  late PurchaseOrderRepositoryImpl repository;

  setUpAll(() {
    registerFallbackValue(const ClosePurchaseOrderRequestDto(reason: ''));
  });

  setUp(() {
    dataSource = MockPurchaseOrderRemoteDataSource();
    repository = PurchaseOrderRepositoryImpl(dataSource);
  });

  test('trims reason, sends it, and maps the closed detail', () async {
    when(() => dataSource.close(any(), any())).thenAnswer((_) async => _dto());

    final result = await repository.close('po-1', '  Discontinued  ');

    final captured =
        verify(
              () => dataSource.close(
                'po-1',
                captureAny<ClosePurchaseOrderRequestDto>(),
              ),
            ).captured.single
            as ClosePurchaseOrderRequestDto;
    expect(captured.reason, 'Discontinued');
    expect(result.status, PurchaseOrderStatus.closed);
    expect(result.closeReason, 'Server reason');
  });

  test('retains AppException failures', () async {
    when(
      () => dataSource.close(any(), any()),
    ).thenThrow(AppException(failure: const Failure.server(statusCode: 409)));

    expect(
      () => repository.close('po-1', 'Discontinued'),
      throwsA(isA<AppException>()),
    );
  });

  test('rejects invalid reasons before reaching the data source', () async {
    expect(() => repository.close('po-1', ' '), throwsA(isA<AppException>()));
    expect(
      () => repository.close('po-1', 'x' * 501),
      throwsA(isA<AppException>()),
    );
    verifyNever(() => dataSource.close(any(), any()));
  });
}

PurchaseOrderDetailDto _dto() => PurchaseOrderDetailDto(
  purchaseOrderId: 'po-1',
  purchaseOrderNumber: 'PO-001',
  status: 'Closed',
  lines: const [],
  expectedTotal: 0,
  createdAt: DateTime.utc(2026, 7, 1),
  receivedQuantity: 5,
  closedAt: '2026-07-15T10:30:00Z',
  closedBy: 'user-1',
  closeReason: 'Server reason',
);
