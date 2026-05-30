import 'package:intelibill_mobile/src/core/network/api_client.dart';
import 'package:intelibill_mobile/src/features/app_status/data/dto/app_status_dto.dart';

interface class AppStatusRemoteDataSource {
  Future<AppStatusDto> getStatus() {
    throw UnimplementedError();
  }
}

class AppStatusRemoteDataSourceImpl implements AppStatusRemoteDataSource {
  AppStatusRemoteDataSourceImpl(
    this._apiClient, {
    DateTime Function()? clock,
    this.environment = 'development',
  }) : _clock = clock ?? DateTime.now;

  final ApiClient _apiClient;
  final DateTime Function() _clock;
  final String environment;

  @override
  Future<AppStatusDto> getStatus() async {
    // TODO(backend): Add a dedicated health/status endpoint on the backend.
    // For now, use a local/fake response to demonstrate the data flow.
    // This can be replaced with a non-invasive existing endpoint or `/health`.
    return AppStatusDto(
      statusText: 'Ready',
      apiBaseUrl: _apiClient.dio.options.baseUrl,
      timestamp: _clock().toUtc(),
      environment: environment,
    );
  }
}
