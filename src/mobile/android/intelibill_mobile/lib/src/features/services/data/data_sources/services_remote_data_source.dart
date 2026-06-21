import 'package:intelibill_mobile/src/core/network/api_client.dart';
import 'package:intelibill_mobile/src/features/services/data/dto/service_dto.dart';

interface class ServicesRemoteDataSource {
  Future<List<ServiceDto>> getServices({
    required bool includeInactive,
    String? search,
  }) {
    throw UnimplementedError();
  }
}

class ServicesRemoteDataSourceImpl implements ServicesRemoteDataSource {
  ServicesRemoteDataSourceImpl(this._apiClient);

  final ApiClient _apiClient;

  static const String _servicesEndpoint = '/services';

  @override
  Future<List<ServiceDto>> getServices({
    required bool includeInactive,
    String? search,
  }) async {
    final response = await _apiClient.get<List<dynamic>>(
      _servicesEndpoint,
      queryParameters: {
        'includeInactive': includeInactive,
        if (search?.trim().isNotEmpty == true) 'search': search!.trim(),
      },
    );

    return response.data!
        .cast<Map<String, dynamic>>()
        .map(ServiceDto.fromJson)
        .toList();
  }
}
