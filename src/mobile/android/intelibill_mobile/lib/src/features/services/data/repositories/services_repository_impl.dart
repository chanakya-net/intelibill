import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/services/data/data_sources/services_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/services/data/dto/create_service_request_dto.dart';
import 'package:intelibill_mobile/src/features/services/data/mappers/service_mapper.dart';
import 'package:intelibill_mobile/src/features/services/data/dto/update_service_request_dto.dart';
import 'package:intelibill_mobile/src/features/services/domain/entities/service.dart';
import 'package:intelibill_mobile/src/features/services/domain/repositories/services_repository.dart';

class ServicesRepositoryImpl implements ServicesRepository {
  const ServicesRepositoryImpl(this._remoteDataSource);

  final ServicesRemoteDataSource _remoteDataSource;

  @override
  Future<List<Service>> getServices({
    required bool includeInactive,
    String? search,
  }) async {
    try {
      final dtos = await _remoteDataSource.getServices(
        includeInactive: includeInactive,
        search: search,
      );
      return dtos.map(ServiceMapper.toDomain).toList();
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

  @override
  Future<Service> createService({
    required String name,
    String? description,
    required double price,
    String? hsnCode,
    required double taxRatePercent,
    required bool taxIncluded,
    required bool isActive,
  }) async {
    try {
      final request = CreateServiceRequestDto(
        name: name.trim(),
        description: _normalizeOptional(description),
        price: price,
        hsnCode: _normalizeOptional(hsnCode),
        taxRatePercent: taxRatePercent,
        taxIncluded: taxIncluded,
        isActive: isActive,
      );
      final dto = await _remoteDataSource.createService(request);
      return ServiceMapper.toDomain(dto);
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

  @override
  Future<void> updateService({
    required String serviceId,
    required String name,
    String? description,
    required double price,
    String? hsnCode,
    required double taxRatePercent,
    required bool taxIncluded,
  }) async {
    try {
      final request = UpdateServiceRequestDto(
        name: name.trim(),
        description: _normalizeOptional(description),
        price: price,
        hsnCode: _normalizeOptional(hsnCode),
        taxRatePercent: taxRatePercent,
        taxIncluded: taxIncluded,
      );
      await _remoteDataSource.updateService(serviceId, request);
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

  @override
  Future<void> activateService(String serviceId) async {
    try {
      await _remoteDataSource.activateService(serviceId);
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

  @override
  Future<void> deactivateService(String serviceId) async {
    try {
      await _remoteDataSource.deactivateService(serviceId);
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

  String? _normalizeOptional(String? value) {
    final trimmed = value?.trim();
    if (trimmed == null || trimmed.isEmpty) {
      return null;
    }
    return trimmed;
  }
}
