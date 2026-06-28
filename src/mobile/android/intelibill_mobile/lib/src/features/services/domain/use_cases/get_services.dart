import 'package:intelibill_mobile/src/features/services/domain/entities/service.dart';
import 'package:intelibill_mobile/src/features/services/domain/repositories/services_repository.dart';

class GetServices {
  const GetServices(this._repository);

  final ServicesRepository _repository;

  Future<List<Service>> call({
    required bool includeInactive,
    String? search,
  }) {
    return _repository.getServices(
      includeInactive: includeInactive,
      search: search,
    );
  }
}
