import 'package:intelibill_mobile/src/features/customers/data/dto/customer_dto.dart';
import 'package:intelibill_mobile/src/features/customers/domain/entities/customer.dart';

class CustomerMapper {
  static Customer toDomain(CustomerDto dto) {
    return Customer(
      customerId: dto.customerId,
      name: dto.name,
      phoneNumber: dto.phoneNumber,
      address: dto.address,
      isActive: dto.isActive,
      outstandingDue: dto.outstandingDue,
    );
  }
}
