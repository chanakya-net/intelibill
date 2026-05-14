import 'package:intelibill_mobile/src/features/customers/domain/entities/customer.dart';

interface class CustomerRepository {
  Future<List<Customer>> getCustomers() {
    throw UnimplementedError();
  }

  Future<Customer> createCustomer({
    required String name,
    required String phoneNumber,
    String? address,
    required bool isActive,
  }) {
    throw UnimplementedError();
  }
}
