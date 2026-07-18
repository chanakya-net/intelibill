import 'package:intelibill_mobile/src/core/network/api_client.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/purchase_order_detail_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/purchase_order_page_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_filters.dart';

interface class PurchaseOrderRemoteDataSource {
  Future<PurchaseOrderPageDto> getPurchaseOrders(PurchaseOrderFilters filters) {
    throw UnimplementedError();
  }

  Future<PurchaseOrderDetailDto> getPurchaseOrder(String purchaseOrderId) {
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
        if (filters.orderDateFrom != null)
          'order_date_from': _formatDate(filters.orderDateFrom!),
        if (filters.orderDateTo != null)
          'order_date_to': _formatDate(filters.orderDateTo!),
        'page': filters.page,
        'page_size': filters.pageSize,
      },
    );
    return PurchaseOrderPageDto.fromJson(response.data!);
  }

  String _formatDate(DateTime date) {
    return '${date.year.toString().padLeft(4, '0')}-'
        '${date.month.toString().padLeft(2, '0')}-'
        '${date.day.toString().padLeft(2, '0')}';
  }

  @override
  Future<PurchaseOrderDetailDto> getPurchaseOrder(
    String purchaseOrderId,
  ) async {
    final response = await _apiClient.get<Map<String, dynamic>>(
      '/purchase-orders/$purchaseOrderId',
    );
    return PurchaseOrderDetailDto.fromJson(response.data!);
  }
}
