import 'package:intelibill_mobile/src/features/services/domain/entities/service.dart';

interface class ServicesRepository {
  Future<List<Service>> getServices({
    required bool includeInactive,
    String? search,
  }) {
    throw UnimplementedError();
  }

  Future<Service> createService({
    required String name,
    String? description,
    required double price,
    String? hsnCode,
    required double taxRatePercent,
    required bool taxIncluded,
    required bool isActive,
  }) {
    throw UnimplementedError();
  }

  Future<void> updateService({
    required String serviceId,
    required String name,
    String? description,
    required double price,
    String? hsnCode,
    required double taxRatePercent,
    required bool taxIncluded,
  }) {
    throw UnimplementedError();
  }

  Future<void> activateService(String serviceId) {
    throw UnimplementedError();
  }

  Future<void> deactivateService(String serviceId) {
    throw UnimplementedError();
  }
}
