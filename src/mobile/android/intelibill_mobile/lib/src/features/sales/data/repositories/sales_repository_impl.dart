import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/sales/data/data_sources/sales_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/sales/data/mappers/sale_list_item_mapper.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sales_history_query.dart';
import 'package:intelibill_mobile/src/features/sales/domain/repositories/sales_repository.dart';

class SalesRepositoryImpl implements SalesRepository {
  const SalesRepositoryImpl(this._remoteDataSource);

  final SalesRemoteDataSource _remoteDataSource;

  @override
  Future<SalesHistoryResult> getSalesHistory(SalesHistoryQuery query) async {
    try {
      final dto = await _remoteDataSource.getSalesHistory(query);
      return SaleListItemMapper.resultToDomain(dto);
    } on AppException {
      rethrow;
    } on FormatException catch (error) {
      throw AppException(
        failure: Failure.serialization(message: error.message),
      );
    } catch (error) {
      throw AppException(failure: Failure.unknown(message: error.toString()));
    }
  }
}
