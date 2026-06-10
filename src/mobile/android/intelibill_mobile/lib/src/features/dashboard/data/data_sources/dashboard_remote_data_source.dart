import 'package:intelibill_mobile/src/core/network/api_client.dart';
import 'package:intelibill_mobile/src/features/dashboard/data/dto/dashboard_dto.dart';

interface class DashboardRemoteDataSource {
  Future<DashboardDto> getDashboard({String? from, String? to}) {
    throw UnimplementedError();
  }
}

class DashboardRemoteDataSourceImpl implements DashboardRemoteDataSource {
  DashboardRemoteDataSourceImpl(this._apiClient);

  final ApiClient _apiClient;

  static const String _dashboardEndpoint = '/dashboard';

  @override
  Future<DashboardDto> getDashboard({String? from, String? to}) async {
    final queryParameters = <String, dynamic>{};
    if (from != null) {
      queryParameters['from'] = from;
    }
    if (to != null) {
      queryParameters['to'] = to;
    }

    final response = await _apiClient.get<Map<String, dynamic>>(
      _dashboardEndpoint,
      queryParameters: queryParameters.isEmpty ? null : queryParameters,
    );

    return DashboardDto.fromJson(response.data!);
  }
}
