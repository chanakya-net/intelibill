import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/network/api_client.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/cancel_purchase_order_request_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/close_purchase_order_request_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/create_purchase_order_draft_request_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/receive_purchase_order_request_dto.dart';
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

  Future<PurchaseOrderDetailDto> createDraft(
    CreatePurchaseOrderDraftRequestDto request,
  ) {
    throw UnimplementedError();
  }

  Future<PurchaseOrderDetailDto> updateDraft(
    String purchaseOrderId,
    CreatePurchaseOrderDraftRequestDto request,
  ) {
    throw UnimplementedError();
  }

  Future<PurchaseOrderDetailDto> cancel(
    String purchaseOrderId,
    CancelPurchaseOrderRequestDto request,
  ) {
    throw UnimplementedError();
  }

  Future<PurchaseOrderDetailDto> close(
    String purchaseOrderId,
    ClosePurchaseOrderRequestDto request,
  ) {
    throw UnimplementedError();
  }

  Future<PurchaseOrderDetailDto> receive(
    String purchaseOrderId,
    ReceivePurchaseOrderRequestDto request,
  ) {
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

  @override
  Future<PurchaseOrderDetailDto> createDraft(
    CreatePurchaseOrderDraftRequestDto request,
  ) async {
    final response = await _apiClient.post<Map<String, dynamic>>(
      '/purchase-orders',
      data: request.toJson(),
    );
    return PurchaseOrderDetailDto.fromJson(response.data!);
  }

  @override
  Future<PurchaseOrderDetailDto> updateDraft(
    String purchaseOrderId,
    CreatePurchaseOrderDraftRequestDto request,
  ) async {
    final response = await _apiClient.put<Map<String, dynamic>>(
      '/purchase-orders/$purchaseOrderId',
      data: request.toJson(),
    );
    return PurchaseOrderDetailDto.fromJson(response.data!);
  }

  @override
  Future<PurchaseOrderDetailDto> cancel(
    String purchaseOrderId,
    CancelPurchaseOrderRequestDto request,
  ) async {
    final trimmed = request.reason.trim();
    if (trimmed.isEmpty || trimmed.length > 500) {
      throw AppException(
        failure: const Failure.validation(
          message: 'Reason must be between 1 and 500 characters.',
        ),
      );
    }
    final response = await _apiClient.post<Map<String, dynamic>>(
      '/purchase-orders/$purchaseOrderId/cancel',
      data: CancelPurchaseOrderRequestDto(reason: trimmed).toJson(),
    );
    return PurchaseOrderDetailDto.fromJson(response.data!);
  }

  @override
  Future<PurchaseOrderDetailDto> close(
    String purchaseOrderId,
    ClosePurchaseOrderRequestDto request,
  ) async {
    final trimmed = request.reason.trim();
    if (trimmed.isEmpty || trimmed.length > 500) {
      throw AppException(
        failure: const Failure.validation(
          message: 'Reason must be between 1 and 500 characters.',
        ),
      );
    }
    final response = await _apiClient.post<Map<String, dynamic>>(
      '/purchase-orders/$purchaseOrderId/close',
      data: ClosePurchaseOrderRequestDto(reason: trimmed).toJson(),
    );
    return PurchaseOrderDetailDto.fromJson(response.data!);
  }

  @override
  Future<PurchaseOrderDetailDto> receive(
    String purchaseOrderId,
    ReceivePurchaseOrderRequestDto request,
  ) async {
    final response = await _apiClient.post<Map<String, dynamic>>(
      '/purchase-orders/$purchaseOrderId/receipts',
      data: request.toJson(),
    );
    return PurchaseOrderDetailDto.fromJson(response.data!);
  }
}
