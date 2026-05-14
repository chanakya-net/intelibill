import 'package:intelibill_mobile/src/features/suppliers/domain/entities/supplier.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/repositories/supplier_repository.dart';

class CreateSupplier {
  const CreateSupplier(this._repository);

  final SupplierRepository _repository;

  Future<Supplier> call({
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
    return _repository.createSupplier(
      name: name,
      contactPersonName: contactPersonName,
      contactPersonPhone: contactPersonPhone,
      address: address,
      city: city,
      state: state,
      pin: pin,
      isActive: isActive,
      isPreferred: isPreferred,
    );
  }
}
