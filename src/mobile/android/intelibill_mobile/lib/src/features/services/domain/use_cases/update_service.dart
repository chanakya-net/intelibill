import 'package:intelibill_mobile/src/features/services/domain/repositories/services_repository.dart';

class UpdateService {
  const UpdateService(this._repository);

  final ServicesRepository _repository;

  Future<void> call({
    required String serviceId,
    required String name,
    String? description,
    required double price,
    String? hsnCode,
    required double taxRatePercent,
    required bool taxIncluded,
  }) {
    return _repository.updateService(
      serviceId: serviceId,
      name: name,
      description: description,
      price: price,
      hsnCode: hsnCode,
      taxRatePercent: taxRatePercent,
      taxIncluded: taxIncluded,
    );
  }
}
