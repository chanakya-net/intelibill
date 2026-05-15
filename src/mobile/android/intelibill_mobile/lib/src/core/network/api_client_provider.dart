import 'package:intelibill_mobile/src/core/network/api_client.dart';
import 'package:intelibill_mobile/src/core/storage/secure_storage.dart';
import 'package:intelibill_mobile/src/features/auth/data/data_sources/auth_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/auth/data/dto/auth_result_dto.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'api_client_provider.g.dart';

@riverpod
ApiClient apiClient(Ref ref) {
  final secureStorage = SecureStorageImpl();
  final refreshClient = ApiClient();
  final authRemoteDataSource = AuthRemoteDataSourceImpl(refreshClient);
  AuthResultDto? refreshedAuthResult;

  return ApiClient.withAuthCallbacks(
    getAccessToken: secureStorage.getAccessToken,
    getRefreshToken: secureStorage.getRefreshToken,
    refreshTokens: () async {
      final refreshToken = await secureStorage.getRefreshToken();
      if (refreshToken == null || refreshToken.isEmpty) {
        throw StateError('No refresh token available.');
      }

      refreshedAuthResult = await authRemoteDataSource.refreshToken(
        refreshToken: refreshToken,
      );
    },
    saveRefreshedTokens: () async {
      final authResult = refreshedAuthResult;
      if (authResult == null) {
        return;
      }

      await secureStorage.saveTokens(
        accessToken: authResult.accessToken,
        refreshToken: authResult.refreshToken,
      );
      refreshedAuthResult = null;
    },
    clearAuthState: secureStorage.clearTokens,
  );
}
