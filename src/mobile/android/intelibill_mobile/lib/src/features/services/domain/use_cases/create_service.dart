import 'package:intelibill_mobile/src/features/services/domain/entities/service.dart';
import 'package:intelibill_mobile/src/features/services/domain/repositories/services_repository.dart';

class CreateService {
  const CreateService(this._repository);

  final ServicesRepository _repository;

  Future<Service> call({
    required String name,
    String? description,
    required double price,
    String? hsnCode,
    required double taxRatePercent,
    required bool taxIncluded,
    required bool isActive,
  }) {
    return _repository.createService(
      name: name,
      description: description,
      price: price,
      hsnCode: hsnCode,
      taxRatePercent: taxRatePercent,
      taxIncluded: taxIncluded,
      isActive: isActive,
    );
  }
}
