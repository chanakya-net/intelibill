import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/suppliers/data/data_sources/supplier_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/suppliers/data/dto/create_supplier_request_dto.dart';
import 'package:intelibill_mobile/src/features/suppliers/data/mappers/supplier_mapper.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/entities/supplier.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/repositories/supplier_repository.dart';

class SupplierRepositoryImpl implements SupplierRepository {
  const SupplierRepositoryImpl(this._remoteDataSource);

  final SupplierRemoteDataSource _remoteDataSource;

  @override
  Future<List<Supplier>> getSuppliers() async {
    try {
      final dtos = await _remoteDataSource.getSuppliers();
      return dtos.map(SupplierMapper.toDomain).toList();
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
  }) async {
    try {
      String? normalizeOptional(String? value) {
        final trimmed = value?.trim();
        if (trimmed == null || trimmed.isEmpty) {
          return null;
        }
        return trimmed;
      }

      final request = CreateSupplierRequestDto(
        name: name.trim(),
        contactPersonName: normalizeOptional(contactPersonName),
        contactPersonPhone: normalizeOptional(contactPersonPhone),
        address: address.trim(),
        city: city.trim(),
        state: state.trim(),
        pin: pin.trim(),
        isActive: isActive,
        isPreferred: isPreferred,
      );
      final dto = await _remoteDataSource.createSupplier(request);
      return SupplierMapper.toDomain(dto);
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
