import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/sales/data/data_sources/sales_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/sales/data/mappers/sale_detail_mapper.dart';
import 'package:intelibill_mobile/src/features/sales/data/dto/sale_preview_dto.dart';
import 'package:intelibill_mobile/src/features/sales/data/mappers/sale_preview_mapper.dart';
import 'package:intelibill_mobile/src/features/sales/data/mappers/sale_return_mapper.dart';
import 'package:intelibill_mobile/src/features/sales/data/mappers/sale_list_item_mapper.dart';
import 'package:intelibill_mobile/src/features/sales/data/mappers/sellable_mapper.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_preview.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_return.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sales_history_query.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sellable.dart';
import 'package:intelibill_mobile/src/features/sales/domain/repositories/sales_repository.dart';

class SalesRepositoryImpl implements SalesRepository {
  const SalesRepositoryImpl(this._remoteDataSource);

  final SalesRemoteDataSource _remoteDataSource;

  @override
  Future<SalesHistoryResult> getSalesHistory(SalesHistoryQuery query) async {
    try {
      final dto = await _remoteDataSource.getSalesHistory(query);
      return SaleListItemMapper.resultToDomain(dto);
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
  Future<SaleDetail> getSaleDetail(String saleId) async {
    try {
      final dto = await _remoteDataSource.getSaleDetail(saleId);
      return SaleDetailMapper.toDomain(dto);
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
  Future<SalePreview> previewSale({
    required PreviewSaleRequest request,
  }) async {
    try {
      final dto = await _remoteDataSource.previewSale(
        request: SalePreviewRequestDto.fromDomain(request).toJson(),
      );
      return SalePreviewMapper.toDomain(dto);
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
  Future<SaleReturnPreview> previewSaleReturn({
    required String saleId,
    required PreviewSaleReturnRequest request,
  }) async {
    try {
      final dto = await _remoteDataSource.previewSaleReturn(
        saleId: saleId,
        request: request.toJson(),
      );
      return SaleReturnMapper.toDomain(dto);
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
  Future<SaleDetail> recordSaleReturn({
    required String saleId,
    required RecordSaleReturnRequest request,
  }) async {
    try {
      final dto = await _remoteDataSource.recordSaleReturn(
        saleId: saleId,
        request: request.toJson(),
      );
      return SaleDetailMapper.toDomain(dto);
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
  Future<List<Sellable>> searchSellables({
    String? searchTerm,
    String? barcode,
  }) async {
    try {
      final dtos = await _remoteDataSource.searchSellables(
        searchTerm: searchTerm,
        barcode: barcode,
      );
      return dtos.map(SellableMapper.toDomain).toList();
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
  Future<void> voidSaleReturn({
    required String saleReturnId,
    required String reason,
  }) async {
    try {
      await _remoteDataSource.voidSaleReturn(
        saleReturnId: saleReturnId,
        reason: reason,
      );
    } on AppException {
      rethrow;
    } catch (error) {
      throw AppException(failure: Failure.unknown(message: error.toString()));
    }
  }
}
