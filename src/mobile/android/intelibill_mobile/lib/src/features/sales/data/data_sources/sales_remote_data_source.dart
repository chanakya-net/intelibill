import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/network/api_client.dart';
import 'package:intelibill_mobile/src/core/utils/date_time_wire.dart';
import 'package:intelibill_mobile/src/features/sales/data/dto/sales_history_response_dto.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sales_history_query.dart';

interface class SalesRemoteDataSource {
  Future<SalesHistoryResponseDto> getSalesHistory(SalesHistoryQuery query) {
    throw UnimplementedError();
  }

  Future<Map<String, dynamic>> getSaleDetail(String saleId) {
    throw UnimplementedError();
  }
}

class SalesRemoteDataSourceImpl implements SalesRemoteDataSource {
  SalesRemoteDataSourceImpl(this._apiClient);

  final ApiClient _apiClient;

  static const String _salesEndpoint = '/sales';

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

    final data = response.data;
    if (data == null) {
      throw AppException(
        failure: const Failure.unknown(message: 'Empty response body'),
      );
    }

    return SalesHistoryResponseDto.fromJson(data);
  }

  @override
  Future<Map<String, dynamic>> getSaleDetail(String saleId) async {
    final response = await _apiClient.get<Map<String, dynamic>>(
      '$_salesEndpoint/$saleId',
    );
    final data = response.data;
    if (data == null) {
      throw AppException(
        failure: const Failure.unknown(message: 'Empty response body'),
      );
    }
    return data;
  }
}
