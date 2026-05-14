import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/suppliers/data/data_sources/supplier_remote_data_source.dart';
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
      throw AppException(
        failure: Failure.unknown(message: error.toString()),
      );
    }
  }
}
