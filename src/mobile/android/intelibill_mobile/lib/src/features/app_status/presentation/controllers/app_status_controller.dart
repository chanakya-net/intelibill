import 'package:intelibill_mobile/src/core/network/api_client_provider.dart';
import 'package:intelibill_mobile/src/features/app_status/data/data_sources/app_status_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/app_status/data/repositories/app_status_repository_impl.dart';
import 'package:intelibill_mobile/src/features/app_status/domain/entities/app_status.dart';
import 'package:intelibill_mobile/src/features/app_status/domain/repositories/app_status_repository.dart';
import 'package:intelibill_mobile/src/features/app_status/domain/use_cases/get_app_status.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'app_status_controller.g.dart';

@riverpod
AppStatusRemoteDataSource appStatusRemoteDataSource(Ref ref) {
  final apiClient = ref.watch(apiClientProvider);
  return AppStatusRemoteDataSourceImpl(apiClient);
}

@riverpod
AppStatusRepository appStatusRepository(Ref ref) {
  final remoteDataSource = ref.watch(appStatusRemoteDataSourceProvider);
  return AppStatusRepositoryImpl(remoteDataSource);
}

@riverpod
GetAppStatus getAppStatusUseCase(Ref ref) {
  final repository = ref.watch(appStatusRepositoryProvider);
  return GetAppStatus(repository);
}

@riverpod
class AppStatusController extends _$AppStatusController {
  @override
  Future<AppStatus> build() async {
    final useCase = ref.watch(getAppStatusUseCaseProvider);
    return useCase();
  }

  Future<void> refresh() async {
    state = const AsyncLoading();
    state = await AsyncValue.guard(
      () => ref.read(getAppStatusUseCaseProvider)(),
    );
  }
}
