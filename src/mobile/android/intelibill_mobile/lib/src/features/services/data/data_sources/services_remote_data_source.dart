import 'package:intelibill_mobile/src/core/network/api_client.dart';
import 'package:intelibill_mobile/src/features/services/data/dto/create_service_request_dto.dart';
import 'package:intelibill_mobile/src/features/services/data/dto/service_dto.dart';
import 'package:intelibill_mobile/src/features/services/data/dto/update_service_request_dto.dart';

interface class ServicesRemoteDataSource {
  Future<List<ServiceDto>> getServices({
    required bool includeInactive,
    String? search,
  }) {
    throw UnimplementedError();
  }

  Future<ServiceDto> createService(CreateServiceRequestDto request) {
    throw UnimplementedError();
  }

  Future<void> updateService(
    String serviceId,
    UpdateServiceRequestDto request,
  ) {
    throw UnimplementedError();
  }

  Future<void> activateService(String serviceId) {
    throw UnimplementedError();
  }

  Future<void> deactivateService(String serviceId) {
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

  @override
  Future<ServiceDto> createService(CreateServiceRequestDto request) async {
    final response = await _apiClient.post<Map<String, dynamic>>(
      _servicesEndpoint,
      data: request.toJson(),
    );
    return ServiceDto.fromJson(response.data!);
  }

  @override
  Future<void> updateService(
    String serviceId,
    UpdateServiceRequestDto request,
  ) async {
    await _apiClient.patch<void>(
      '$_servicesEndpoint/$serviceId',
      data: request.toJson(),
    );
  }

  @override
  Future<void> activateService(String serviceId) async {
    await _apiClient.post<void>('$_servicesEndpoint/$serviceId/activate');
  }

  @override
  Future<void> deactivateService(String serviceId) async {
    await _apiClient.post<void>('$_servicesEndpoint/$serviceId/deactivate');
  }
}
