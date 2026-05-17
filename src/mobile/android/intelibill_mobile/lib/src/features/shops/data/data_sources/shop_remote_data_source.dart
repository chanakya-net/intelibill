import 'package:intelibill_mobile/src/core/network/api_client.dart';
import 'package:intelibill_mobile/src/features/auth/data/dto/auth_result_dto.dart';
import 'package:intelibill_mobile/src/features/shops/data/dtos/add_bank_account_request_dto.dart';
import 'package:intelibill_mobile/src/features/shops/data/dtos/bank_account_dto.dart';
import 'package:intelibill_mobile/src/features/shops/data/dtos/create_shop_request_dto.dart';
import 'package:intelibill_mobile/src/features/shops/data/dtos/shop_details_dto.dart';
import 'package:intelibill_mobile/src/features/shops/data/dtos/update_shop_request_dto.dart';

interface class ShopRemoteDataSource {
  Future<ShopDetailsDto> getShop(String shopId) {
    throw UnimplementedError();
  }

  Future<AuthResultDto> createShop(CreateShopRequestDto request) {
    throw UnimplementedError();
  }

  Future<ShopDetailsDto> updateShop(
    String shopId,
    UpdateShopRequestDto request,
  ) {
    throw UnimplementedError();
  }

  Future<BankAccountDto> addBankAccount(AddBankAccountRequestDto request) {
    throw UnimplementedError();
  }
}

class ShopRemoteDataSourceImpl implements ShopRemoteDataSource {
  ShopRemoteDataSourceImpl(this._apiClient);

  final ApiClient _apiClient;

  static const String _shopsEndpoint = '/shops';
  static const String _bankAccountsEndpoint = '/bank-accounts';

  @override
  Future<ShopDetailsDto> getShop(String shopId) async {
    final response = await _apiClient.get<Map<String, dynamic>>(
      '$_shopsEndpoint/$shopId',
    );
    return ShopDetailsDto.fromJson(response.data!);
  }

  @override
  Future<AuthResultDto> createShop(CreateShopRequestDto request) async {
    final response = await _apiClient.post<Map<String, dynamic>>(
      _shopsEndpoint,
      data: request.toJson(),
    );
    return AuthResultDto.fromJson(response.data!);
  }

  @override
  Future<ShopDetailsDto> updateShop(
    String shopId,
    UpdateShopRequestDto request,
  ) async {
    final response = await _apiClient.put<Map<String, dynamic>>(
      '$_shopsEndpoint/$shopId',
      data: request.toJson(),
    );
    return ShopDetailsDto.fromJson(response.data!);
  }

  @override
  Future<BankAccountDto> addBankAccount(
    AddBankAccountRequestDto request,
  ) async {
    final response = await _apiClient.post<Map<String, dynamic>>(
      _bankAccountsEndpoint,
      data: request.toJson(),
    );
    return BankAccountDto.fromJson(response.data!);
  }
}
