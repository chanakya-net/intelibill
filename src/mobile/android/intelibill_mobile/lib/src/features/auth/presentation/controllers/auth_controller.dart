import 'package:intelibill_mobile/src/core/storage/preferences_storage.dart';
import 'package:intelibill_mobile/src/core/storage/secure_storage.dart';
import 'package:intelibill_mobile/src/features/app_status/presentation/controllers/app_status_controller.dart';
import 'package:intelibill_mobile/src/features/auth/data/data_sources/auth_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/auth/data/repositories/auth_repository_impl.dart';
import 'package:intelibill_mobile/src/features/auth/domain/repositories/auth_repository.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';
import 'package:shared_preferences/shared_preferences.dart';

part 'auth_controller.g.dart';

@riverpod
SecureStorage secureStorage(Ref ref) {
  return SecureStorageImpl();
}

@riverpod
Future<PreferencesStorage> preferencesStorage(Ref ref) async {
  final prefs = await SharedPreferences.getInstance();
  return PreferencesStorageImpl(prefs);
}

@riverpod
AuthRemoteDataSource authRemoteDataSource(Ref ref) {
  final apiClient = ref.watch(apiClientProvider);
  return AuthRemoteDataSourceImpl(apiClient);
}

@riverpod
Future<AuthRepository> authRepository(Ref ref) async {
  final remoteDataSource = ref.watch(authRemoteDataSourceProvider);
  final secureStorage = ref.watch(secureStorageProvider);
  final preferencesStorage = await ref.watch(preferencesStorageProvider.future);

  return AuthRepositoryImpl(
    remoteDataSource: remoteDataSource,
    secureStorage: secureStorage,
    preferencesStorage: preferencesStorage,
  );
}
