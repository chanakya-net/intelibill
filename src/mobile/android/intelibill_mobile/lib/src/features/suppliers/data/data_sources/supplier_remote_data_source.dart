import 'package:intelibill_mobile/src/core/network/api_client.dart';
import 'package:intelibill_mobile/src/features/suppliers/data/dto/supplier_dto.dart';

interface class SupplierRemoteDataSource {
  Future<List<SupplierDto>> getSuppliers() {
    throw UnimplementedError();
  }
}

class SupplierRemoteDataSourceImpl implements SupplierRemoteDataSource {
  SupplierRemoteDataSourceImpl(this._apiClient);

  final ApiClient _apiClient;

  static const String _suppliersEndpoint = '/suppliers';

  @override
  Future<List<SupplierDto>> getSuppliers() async {
    final response = await _apiClient.get<List<dynamic>>(_suppliersEndpoint);
    return response.data!
        .cast<Map<String, dynamic>>()
        .map(SupplierDto.fromJson)
        .toList();
  }
}
