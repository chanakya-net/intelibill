import 'package:intelibill_mobile/src/core/network/api_client.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/purchase_order_page_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_filters.dart';

interface class PurchaseOrderRemoteDataSource {
  Future<PurchaseOrderPageDto> getPurchaseOrders(PurchaseOrderFilters filters) {
    throw UnimplementedError();
  }
}

class PurchaseOrderRemoteDataSourceImpl
    implements PurchaseOrderRemoteDataSource {
  PurchaseOrderRemoteDataSourceImpl(this._apiClient);

  final ApiClient _apiClient;

  @override
  Future<PurchaseOrderPageDto> getPurchaseOrders(
    PurchaseOrderFilters filters,
  ) async {
    final response = await _apiClient.get<Map<String, dynamic>>(
      '/purchase-orders',
      queryParameters: {
        if (filters.search?.trim().isNotEmpty == true)
          'search': filters.search!.trim(),
        if (filters.status != null) 'status': filters.status!.wireValue,
        'page': filters.page,
        'page_size': filters.pageSize,
      },
    );
    return PurchaseOrderPageDto.fromJson(response.data!);
  }
}
