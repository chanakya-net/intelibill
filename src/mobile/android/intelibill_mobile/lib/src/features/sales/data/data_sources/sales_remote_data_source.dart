import 'package:intelibill_mobile/src/core/network/api_client.dart';
import 'package:intelibill_mobile/src/core/utils/date_time_wire.dart';
import 'package:intelibill_mobile/src/features/sales/data/dto/sellable_dto.dart';
import 'package:intelibill_mobile/src/features/sales/data/dto/sales_history_response_dto.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sales_history_query.dart';

interface class SalesRemoteDataSource {
  Future<SalesHistoryResponseDto> getSalesHistory(SalesHistoryQuery query) {
    throw UnimplementedError();
  }

  Future<List<SellableDto>> searchSellables({
    String? searchTerm,
    String? barcode,
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
}
