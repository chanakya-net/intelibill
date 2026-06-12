import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/users/data/data_sources/shop_user_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/users/data/dto/add_shop_user_request_dto.dart';
import 'package:intelibill_mobile/src/features/users/data/dto/edit_shop_user_request_dto.dart';
import 'package:intelibill_mobile/src/features/users/data/mappers/shop_user_mapper.dart';
import 'package:intelibill_mobile/src/features/users/domain/entities/shop_user.dart';
import 'package:intelibill_mobile/src/features/users/domain/repositories/shop_user_repository.dart';

class ShopUserRepositoryImpl implements ShopUserRepository {
  const ShopUserRepositoryImpl(this._remoteDataSource);

  final ShopUserRemoteDataSource _remoteDataSource;

  @override
  Future<List<ShopUser>> getShopUsers() async {
    try {
      final dtos = await _remoteDataSource.getShopUsers();
      return dtos.map(ShopUserMapper.toDomain).toList();
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
  Future<ShopUser> addShopUser({
    required List<String> shopIds,
    required String email,
    required String firstName,
    required String lastName,
    required String phoneNumber,
    required String password,
    required String confirmPassword,
    required String role,
  }) async {
    try {
      final request = AddShopUserRequestDto(
        shopIds: shopIds,
        email: email.trim(),
        firstName: firstName.trim(),
        lastName: lastName.trim(),
        phoneNumber: phoneNumber.trim(),
        password: password,
        confirmPassword: confirmPassword,
        role: role,
      );
      final dto = await _remoteDataSource.addShopUser(request);
      return ShopUserMapper.toDomain(dto);
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
  Future<ShopUser> editShopUser({
    required String userId,
    required String email,
    required String firstName,
    required String lastName,
    required String phoneNumber,
    required String role,
    required bool isLoginEnabled,
    required List<String> shopIds,
  }) async {
    try {
      final request = EditShopUserRequestDto(
        email: email.trim(),
        firstName: firstName.trim(),
        lastName: lastName.trim(),
        phoneNumber: phoneNumber.trim(),
        role: role,
        isLoginEnabled: isLoginEnabled,
        shopIds: shopIds,
      );
      final dto = await _remoteDataSource.editShopUser(userId, request);
      return ShopUserMapper.toDomain(dto);
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
