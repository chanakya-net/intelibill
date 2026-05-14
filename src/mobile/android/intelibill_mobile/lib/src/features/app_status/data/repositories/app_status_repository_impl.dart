import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/app_status/data/data_sources/app_status_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/app_status/data/mappers/app_status_mapper.dart';
import 'package:intelibill_mobile/src/features/app_status/domain/entities/app_status.dart';
import 'package:intelibill_mobile/src/features/app_status/domain/repositories/app_status_repository.dart';

class AppStatusRepositoryImpl implements AppStatusRepository {
  const AppStatusRepositoryImpl(this._remoteDataSource);

  final AppStatusRemoteDataSource _remoteDataSource;

  @override
  Future<AppStatus> getStatus() async {
    try {
      final dto = await _remoteDataSource.getStatus();
      return AppStatusMapper.toDomain(dto);
    } on AppException {
      rethrow;
    } on FormatException catch (error) {
      throw AppException(
        failure: Failure.serialization(message: error.message),
      );
    } catch (error) {
      throw AppException(
        failure: Failure.unknown(message: error.toString()),
      );
    }
  }
}
