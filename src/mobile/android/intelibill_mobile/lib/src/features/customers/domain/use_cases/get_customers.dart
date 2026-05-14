import 'package:intelibill_mobile/src/features/customers/domain/entities/customer.dart';
import 'package:intelibill_mobile/src/features/customers/domain/repositories/customer_repository.dart';

class GetCustomers {
  const GetCustomers(this._repository);

  final CustomerRepository _repository;

  Future<List<Customer>> call() {
    return _repository.getCustomers();
  }
}
