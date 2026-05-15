import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/auth/data/mappers/auth_mapper.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';
import 'package:intelibill_mobile/src/features/shops/data/data_sources/shop_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/shops/data/mappers/shop_mapper.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/add_bank_account_request.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/create_shop_request.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/shop_details.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/update_shop_request.dart';
import 'package:intelibill_mobile/src/features/shops/domain/repositories/shop_repository.dart';

class ShopRepositoryImpl implements ShopRepository {
  const ShopRepositoryImpl(this._remoteDataSource);

  final ShopRemoteDataSource _remoteDataSource;

  @override
  Future<ShopDetails> getShop(String shopId) async {
    try {
      final dto = await _remoteDataSource.getShop(shopId);
      return ShopMapper.toDomain(dto);
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

  @override
  Future<AuthSession> createShop(CreateShopRequest request) async {
    try {
      final requestDto = ShopMapper.toCreateDto(request);
      final dto = await _remoteDataSource.createShop(requestDto);
      return AuthMapper.toDomain(dto);
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

  @override
  Future<ShopDetails> updateShop(
    String shopId,
    UpdateShopRequest request,
  ) async {
    try {
      final requestDto = ShopMapper.toUpdateDto(request);
      final dto = await _remoteDataSource.updateShop(shopId, requestDto);
      return ShopMapper.toDomain(dto);
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

  @override
  Future<void> addBankAccount(
    AddBankAccountRequest request,
  ) async {
    try {
      final requestDto = ShopMapper.toAddBankAccountDto(request);
      await _remoteDataSource.addBankAccount(requestDto);
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
