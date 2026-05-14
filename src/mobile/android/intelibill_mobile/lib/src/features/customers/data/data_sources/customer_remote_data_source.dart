import 'package:intelibill_mobile/src/core/network/api_client.dart';
import 'package:intelibill_mobile/src/features/customers/data/dto/create_customer_request_dto.dart';
import 'package:intelibill_mobile/src/features/customers/data/dto/customer_dto.dart';

interface class CustomerRemoteDataSource {
  Future<List<CustomerDto>> getCustomers() {
    throw UnimplementedError();
  }

  Future<CustomerDto> createCustomer(CreateCustomerRequestDto request) {
    throw UnimplementedError();
  }
}

class CustomerRemoteDataSourceImpl implements CustomerRemoteDataSource {
  CustomerRemoteDataSourceImpl(this._apiClient);

  final ApiClient _apiClient;

  static const String _customersEndpoint = '/customers';

  @override
  Future<List<CustomerDto>> getCustomers() async {
    final response = await _apiClient.get<List<dynamic>>(_customersEndpoint);
    return response.data!
        .cast<Map<String, dynamic>>()
        .map(CustomerDto.fromJson)
        .toList();
  }

  @override
  Future<CustomerDto> createCustomer(CreateCustomerRequestDto request) async {
    final response = await _apiClient.post<Map<String, dynamic>>(
      _customersEndpoint,
      data: request.toJson(),
    );
    return CustomerDto.fromJson(response.data!);
  }
}
