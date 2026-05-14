import 'package:intelibill_mobile/src/features/customers/domain/entities/customer.dart';
import 'package:intelibill_mobile/src/features/customers/domain/repositories/customer_repository.dart';

class CreateCustomer {
  const CreateCustomer(this._repository);

  final CustomerRepository _repository;

  Future<Customer> call({
    required String name,
    required String phoneNumber,
    String? address,
    required bool isActive,
  }) {
    return _repository.createCustomer(
      name: name,
      phoneNumber: phoneNumber,
      address: address,
      isActive: isActive,
    );
  }
}
