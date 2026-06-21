import 'package:intelibill_mobile/src/core/network/api_client_provider.dart';
import 'package:intelibill_mobile/src/features/sales/data/data_sources/sales_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/sales/data/repositories/sales_repository_impl.dart';
import 'package:intelibill_mobile/src/features/sales/domain/repositories/sales_repository.dart';
import 'package:intelibill_mobile/src/features/sales/domain/use_cases/preview_sale_return.dart';
import 'package:intelibill_mobile/src/features/sales/domain/use_cases/record_sale_return.dart';
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
PreviewSaleReturn previewSaleReturn(Ref ref) {
  final repository = ref.watch(salesRepositoryProvider);
  return PreviewSaleReturn(repository);
}

@riverpod
RecordSaleReturn recordSaleReturn(Ref ref) {
  final repository = ref.watch(salesRepositoryProvider);
  return RecordSaleReturn(repository);
}
