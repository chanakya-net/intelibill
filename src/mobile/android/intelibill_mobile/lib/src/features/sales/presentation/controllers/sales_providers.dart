import 'package:intelibill_mobile/src/core/network/api_client_provider.dart';
import 'package:intelibill_mobile/src/features/sales/data/data_sources/sales_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/sales/data/repositories/sales_repository_impl.dart';
import 'package:intelibill_mobile/src/features/sales/domain/repositories/sales_repository.dart';
import 'package:intelibill_mobile/src/features/sales/domain/use_cases/get_sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/domain/use_cases/get_sales_history.dart';
import 'package:intelibill_mobile/src/features/sales/domain/use_cases/void_sale_return.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'sales_providers.g.dart';

@riverpod
SalesRemoteDataSource salesRemoteDataSource(Ref ref) {
  final apiClient = ref.watch(apiClientProvider);
  return SalesRemoteDataSourceImpl(apiClient);
}

@riverpod
SalesRepository salesRepository(Ref ref) {
  final remoteDataSource = ref.watch(salesRemoteDataSourceProvider);
  return SalesRepositoryImpl(remoteDataSource);
}

@riverpod
GetSalesHistory getSalesHistoryUseCase(Ref ref) {
  return GetSalesHistory(ref.watch(salesRepositoryProvider));
}

@riverpod
GetSaleDetail getSaleDetailUseCase(Ref ref) {
  return GetSaleDetail(ref.watch(salesRepositoryProvider));
}

@riverpod
VoidSaleReturn voidSaleReturnUseCase(Ref ref) {
  return VoidSaleReturn(ref.watch(salesRepositoryProvider));
}
