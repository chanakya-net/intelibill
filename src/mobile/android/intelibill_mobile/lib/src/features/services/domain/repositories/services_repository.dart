import 'package:intelibill_mobile/src/features/services/domain/entities/service.dart';

interface class ServicesRepository {
  Future<List<Service>> getServices({
    required bool includeInactive,
    String? search,
  }) {
    throw UnimplementedError();
  }
}
