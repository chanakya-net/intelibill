import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/services/data/data_sources/services_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/services/data/mappers/service_mapper.dart';
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
}
