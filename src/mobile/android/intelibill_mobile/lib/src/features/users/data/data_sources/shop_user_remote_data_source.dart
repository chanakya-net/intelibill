import 'package:intelibill_mobile/src/core/network/api_client.dart';
import 'package:intelibill_mobile/src/features/users/data/dto/add_shop_user_request_dto.dart';
import 'package:intelibill_mobile/src/features/users/data/dto/edit_shop_user_request_dto.dart';
import 'package:intelibill_mobile/src/features/users/data/dto/shop_user_dto.dart';

interface class ShopUserRemoteDataSource {
  Future<List<ShopUserDto>> getShopUsers() {
    throw UnimplementedError();
  }

  Future<ShopUserDto> addShopUser(AddShopUserRequestDto request) {
    throw UnimplementedError();
  }

  Future<ShopUserDto> editShopUser(
    String userId,
    EditShopUserRequestDto request,
  ) {
    throw UnimplementedError();
  }
}

class ShopUserRemoteDataSourceImpl implements ShopUserRemoteDataSource {
  ShopUserRemoteDataSourceImpl(this._apiClient);

  final ApiClient _apiClient;

  static const String _usersEndpoint = '/users';

  @override
  Future<List<ShopUserDto>> getShopUsers() async {
    final response = await _apiClient.get<List<dynamic>>(_usersEndpoint);
    return response.data!
        .cast<Map<String, dynamic>>()
        .map(ShopUserDto.fromJson)
        .toList();
  }

  @override
  Future<ShopUserDto> addShopUser(AddShopUserRequestDto request) async {
    final response = await _apiClient.post<Map<String, dynamic>>(
      _usersEndpoint,
      data: request.toJson(),
    );
    return ShopUserDto.fromJson(response.data!);
  }

  @override
  Future<ShopUserDto> editShopUser(
    String userId,
    EditShopUserRequestDto request,
  ) async {
    final response = await _apiClient.put<Map<String, dynamic>>(
      '$_usersEndpoint/$userId',
      data: request.toJson(),
    );
    return ShopUserDto.fromJson(response.data!);
  }
}
