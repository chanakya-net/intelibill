import 'package:intelibill_mobile/src/features/suppliers/domain/entities/supplier.dart';

interface class SupplierRepository {
  Future<List<Supplier>> getSuppliers() {
    throw UnimplementedError();
  }

  Future<Supplier> createSupplier({
    required String name,
    String? contactPersonName,
    String? contactPersonPhone,
    required String address,
    required String city,
    required String state,
    required String pin,
    required bool isActive,
    required bool isPreferred,
  }) {
    throw UnimplementedError();
  }
}
