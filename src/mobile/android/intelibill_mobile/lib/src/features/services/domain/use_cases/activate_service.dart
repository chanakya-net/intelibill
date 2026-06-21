import 'package:intelibill_mobile/src/features/services/domain/repositories/services_repository.dart';

class ActivateService {
  const ActivateService(this._repository);

  final ServicesRepository _repository;

  Future<void> call(String serviceId) {
    return _repository.activateService(serviceId);
  }
}
