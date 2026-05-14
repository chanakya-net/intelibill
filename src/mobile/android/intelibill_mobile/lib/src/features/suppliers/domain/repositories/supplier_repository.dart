import 'package:intelibill_mobile/src/features/suppliers/domain/entities/supplier.dart';

interface class SupplierRepository {
  Future<List<Supplier>> getSuppliers() {
    throw UnimplementedError();
  }
}
