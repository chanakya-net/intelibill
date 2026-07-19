import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/data_sources/purchase_order_remote_data_source.dart';
import 'package:mocktail/mocktail.dart';

import '../../../mocks/mock_api_client.dart';

void main() {
  group('PurchaseOrderRemoteDataSource.deleteDraft', () {
    test(
      'DELETEs the draft route with no body or query and accepts 204',
      () async {
        final apiClient = MockApiClient();
        final source = PurchaseOrderRemoteDataSourceImpl(apiClient);
        when(() => apiClient.delete<void>(any<String>())).thenAnswer(
          (_) async => Response<void>(
            statusCode: 204,
            requestOptions: RequestOptions(path: '/purchase-orders/po-1'),
          ),
        );

        await source.deleteDraft('po-1');

        verify(
          () => apiClient.delete<void>('/purchase-orders/po-1'),
        ).called(1);
      },
    );

    test('propagates mapped API failures', () async {
      final apiClient = MockApiClient();
      final source = PurchaseOrderRemoteDataSourceImpl(apiClient);
      when(() => apiClient.delete<void>(any<String>())).thenThrow(
        AppException(failure: const Failure.forbidden()),
      );

      expect(
        () => source.deleteDraft('po-1'),
        throwsA(
          isA<AppException>().having(
            (error) => error.failure,
            'failure',
            const Failure.forbidden(),
          ),
        ),
      );
    });
  });
}
