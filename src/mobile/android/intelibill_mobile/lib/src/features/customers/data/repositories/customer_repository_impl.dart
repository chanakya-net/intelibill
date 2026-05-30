import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/customers/data/data_sources/customer_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/customers/data/dto/create_customer_request_dto.dart';
import 'package:intelibill_mobile/src/features/customers/data/mappers/customer_mapper.dart';
import 'package:intelibill_mobile/src/features/customers/domain/entities/customer.dart';
import 'package:intelibill_mobile/src/features/customers/domain/repositories/customer_repository.dart';

class CustomerRepositoryImpl implements CustomerRepository {
  const CustomerRepositoryImpl(this._remoteDataSource);

  final CustomerRemoteDataSource _remoteDataSource;

  @override
  Future<List<Customer>> getCustomers() async {
    try {
      final dtos = await _remoteDataSource.getCustomers();
      return dtos.map(CustomerMapper.toDomain).toList();
    } on AppException {
      rethrow;
    } on FormatException catch (error) {
      throw AppException(
        failure: Failure.serialization(message: error.message),
      );
    } catch (error) {
      throw AppException(failure: Failure.unknown(message: error.toString()));
    }
  }

  @override
  Future<Customer> createCustomer({
    required String name,
    required String phoneNumber,
    String? address,
    required bool isActive,
  }) async {
    try {
      final normalizedName = name.trim();
      final normalizedPhone = phoneNumber.trim();
      final normalizedAddress = address?.trim();
      final request = CreateCustomerRequestDto(
        name: normalizedName,
        phoneNumber: normalizedPhone,
        address: (normalizedAddress == null || normalizedAddress.isEmpty)
            ? null
            : normalizedAddress,
        isActive: isActive,
      );
      final dto = await _remoteDataSource.createCustomer(request);
      return CustomerMapper.toDomain(dto);
    } on AppException {
      rethrow;
    } on FormatException catch (error) {
      throw AppException(
        failure: Failure.serialization(message: error.message),
      );
    } catch (error) {
      throw AppException(failure: Failure.unknown(message: error.toString()));
    }
  }
}
