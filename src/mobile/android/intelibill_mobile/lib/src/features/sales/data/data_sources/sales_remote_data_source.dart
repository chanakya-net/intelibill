import 'package:intelibill_mobile/src/core/network/api_client.dart';
import 'package:intelibill_mobile/src/core/utils/date_time_wire.dart';
import 'package:intelibill_mobile/src/features/sales/data/dto/void_sale_return_request_dto.dart';
import 'package:intelibill_mobile/src/features/sales/data/dto/sale_detail_dto.dart';
import 'package:intelibill_mobile/src/features/sales/data/dto/sale_return_preview_dto.dart';
import 'package:intelibill_mobile/src/features/sales/data/dto/sales_history_response_dto.dart';
import 'package:intelibill_mobile/src/features/sales/data/dto/sellable_dto.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sales_history_query.dart';

interface class SalesRemoteDataSource {
  Future<SalesHistoryResponseDto> getSalesHistory(SalesHistoryQuery query) {
    throw UnimplementedError();
  }

  Future<SaleDetailDto> getSaleDetail(String saleId) {
    throw UnimplementedError();
  }

  Future<SaleReturnPreviewResponseDto> previewSaleReturn({
    required String saleId,
    required Map<String, dynamic> request,
  }) {
    throw UnimplementedError();
  }

  Future<SaleDetailDto> recordSaleReturn({
    required String saleId,
    required Map<String, dynamic> request,
  }) {
    throw UnimplementedError();
  }

  Future<List<SellableDto>> searchSellables({
    String? searchTerm,
    String? barcode,
  }) {
    throw UnimplementedError();
  }

  Future<void> voidSaleReturn({
    required String saleReturnId,
    required String reason,
  }) {
    throw UnimplementedError();
  }
}

class SalesRemoteDataSourceImpl implements SalesRemoteDataSource {
  SalesRemoteDataSourceImpl(this._apiClient);

  final ApiClient _apiClient;

  static const String _salesEndpoint = '/sales';
  static const String _sellablesEndpoint = '/sales/sellables';

  @override
  Future<SalesHistoryResponseDto> getSalesHistory(
    SalesHistoryQuery query,
  ) async {
    final response = await _apiClient.get<Map<String, dynamic>>(
      _salesEndpoint,
      queryParameters: {
        'from': formatLocalIsoDate(query.from),
        'to': formatLocalIsoDate(query.to),
        if (query.search?.trim().isNotEmpty == true)
          'search': query.search!.trim(),
        if (query.status != null && query.status!.isNotEmpty)
          'status': query.status,
        'page': query.page,
        'pageSize': query.pageSize,
      },
    );

    return SalesHistoryResponseDto.fromJson(response.data!);
  }

  @override
  Future<SaleDetailDto> getSaleDetail(String saleId) async {
    final response = await _apiClient.get<Map<String, dynamic>>(
      '$_salesEndpoint/$saleId',
    );
    return SaleDetailDto.fromJson(response.data!);
  }

  @override
  Future<SaleReturnPreviewResponseDto> previewSaleReturn({
    required String saleId,
    required Map<String, dynamic> request,
  }) async {
    final response = await _apiClient.post<Map<String, dynamic>>(
      '$_salesEndpoint/$saleId/returns/preview',
      data: request,
    );
    return SaleReturnPreviewResponseDto.fromJson(response.data!);
  }

  @override
  Future<SaleDetailDto> recordSaleReturn({
    required String saleId,
    required Map<String, dynamic> request,
  }) async {
    final response = await _apiClient.post<Map<String, dynamic>>(
      '$_salesEndpoint/$saleId/returns',
      data: request,
    );
    return SaleDetailDto.fromJson(response.data!);
  }

  @override
  Future<List<SellableDto>> searchSellables({
    String? searchTerm,
    String? barcode,
  }) async {
    final response = await _apiClient.get<List<dynamic>>(
      _sellablesEndpoint,
      queryParameters: {
        if (searchTerm?.trim().isNotEmpty == true)
          'searchTerm': searchTerm!.trim(),
        if (barcode?.trim().isNotEmpty == true) 'barcode': barcode!.trim(),
      },
    );

    return response.data!
        .cast<Map<String, dynamic>>()
        .map(SellableDto.fromJson)
        .toList();
  }

  @override
  Future<void> voidSaleReturn({
    required String saleReturnId,
    required String reason,
  }) async {
    await _apiClient.post<void>(
      '/sales/returns/$saleReturnId/void',
      data: VoidSaleReturnRequestDto(reason: reason).toJson(),
    );
  }
}
