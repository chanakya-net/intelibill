import 'package:intelibill_mobile/src/features/suppliers/domain/entities/supplier.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/repositories/supplier_repository.dart';

class GetSuppliers {
  const GetSuppliers(this._repository);

  final SupplierRepository _repository;

  Future<List<Supplier>> call() {
    return _repository.getSuppliers();
  }
}
