import 'package:intelibill_mobile/src/features/services/domain/repositories/services_repository.dart';

class DeactivateService {
  const DeactivateService(this._repository);

  final ServicesRepository _repository;

  Future<void> call(String serviceId) {
    return _repository.deactivateService(serviceId);
  }
}
