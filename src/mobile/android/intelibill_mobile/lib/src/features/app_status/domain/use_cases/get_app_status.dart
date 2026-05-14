import 'package:intelibill_mobile/src/features/app_status/domain/entities/app_status.dart';
import 'package:intelibill_mobile/src/features/app_status/domain/repositories/app_status_repository.dart';

class GetAppStatus {
  const GetAppStatus(this._repository);

  final AppStatusRepository _repository;

  Future<AppStatus> call() {
    return _repository.getStatus();
  }
}
