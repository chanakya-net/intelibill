import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/utils/date_time_wire.dart';
import 'package:intelibill_mobile/src/features/inventory/data/data_sources/inventory_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/inventory/data/dto/add_inventory_batch_request_dto.dart';
import 'package:intelibill_mobile/src/features/inventory/data/dto/add_inventory_batch_row_dto.dart';
import 'package:intelibill_mobile/src/features/inventory/data/dto/adjust_inventory_batch_request_dto.dart';
import 'package:intelibill_mobile/src/features/inventory/data/dto/create_item_request_dto.dart';
import 'package:intelibill_mobile/src/features/inventory/data/dto/update_item_request_dto.dart';
import 'package:intelibill_mobile/src/features/inventory/data/mappers/generated_item_barcode_mapper.dart';
import 'package:intelibill_mobile/src/features/inventory/data/mappers/inventory_adjustment_mapper.dart';
import 'package:intelibill_mobile/src/features/inventory/data/mappers/inventory_batch_mapper.dart';
import 'package:intelibill_mobile/src/features/inventory/data/mappers/item_mapper.dart';
import 'package:intelibill_mobile/src/features/inventory/data/mappers/product_details_mapper.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/inventory_adjustment.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/inventory_batch.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/item.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/generated_item_barcode.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/product_details.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/repositories/inventory_repository.dart';

class InventoryRepositoryImpl implements InventoryRepository {
  const InventoryRepositoryImpl(this._remoteDataSource);

  final InventoryRemoteDataSource _remoteDataSource;

  @override
  Future<List<Item>> getItems() async {
    try {
      final dtos = await _remoteDataSource.getItems();
      return dtos.map(ItemMapper.toDomain).toList();
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
  Future<Item> createItem({
    required String name,
    required String barcode,
    required String uom,
    String? description,
  }) async {
    try {
      final normalizedName = name.trim();
      final normalizedBarcode = barcode.trim();
      final normalizedUom = uom.trim();
      final normalizedDescription = description?.trim();
      final request = CreateItemRequestDto(
        name: normalizedName,
        barcode: normalizedBarcode,
        uom: normalizedUom,
        description:
            (normalizedDescription == null || normalizedDescription.isEmpty)
            ? null
            : normalizedDescription,
        isActive: true,
      );
      final dto = await _remoteDataSource.createItem(request);
      return ItemMapper.toDomain(dto);
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
  Future<ProductDetails> getProductDetails({
    String? name,
    String? barcode,
  }) async {
    try {
      final normalizedName = name?.trim();
      final normalizedBarcode = barcode?.trim();
      final dto = await _remoteDataSource.getProductDetails(
        name: normalizedName?.isEmpty == true ? null : normalizedName,
        barcode: normalizedBarcode?.isEmpty == true ? null : normalizedBarcode,
      );
      return ProductDetailsMapper.toDomain(dto);
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
  Future<Item> updateItem({
    required String itemId,
    required String name,
    required String barcode,
    required String uom,
    String? description,
    required bool isActive,
  }) async {
    try {
      final normalizedName = name.trim();
      final normalizedBarcode = barcode.trim();
      final normalizedUom = uom.trim();
      final normalizedDescription = description?.trim();
      final request = UpdateItemRequestDto(
        name: normalizedName,
        barcode: normalizedBarcode,
        uom: normalizedUom,
        description:
            (normalizedDescription == null || normalizedDescription.isEmpty)
            ? null
            : normalizedDescription,
        isActive: isActive,
      );
      await _remoteDataSource.updateItem(itemId, request);
      final dtos = await _remoteDataSource.getItems();
      final dto = dtos.firstWhere((d) => d.id == itemId);
      return ItemMapper.toDomain(dto);
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
  Future<void> addInventoryInbound({
    required String itemName,
    required String barcode,
    required String uom,
    String? batchNumber,
    required double quantity,
    required double costPrice,
    required double mrp,
    required double salesPrice,
    double taxRate = 0.0,
    bool taxIncluded = false,
    DateTime? expiryDate,
    DateTime? manufacturingDate,
    String? referenceNumber,
    String? notes,
  }) async {
    try {
      final clientRowId = DateTime.now().millisecondsSinceEpoch.toString();
      final effectiveBatchNumber = batchNumber?.trim().isNotEmpty == true
          ? batchNumber!.trim()
          : 'AUTO';
      final row = AddInventoryBatchRowDto(
        clientRowId: clientRowId,
        itemName: itemName.trim(),
        barcode: barcode.trim(),
        uom: uom.trim(),
        batchNumber: effectiveBatchNumber,
        quantity: quantity,
        costPrice: costPrice,
        mrp: mrp,
        salesPrice: salesPrice,
        taxRatePercent: taxRate,
        taxIncluded: taxIncluded,
        expiryDate: expiryDate != null ? _formatDate(expiryDate) : null,
        manufacturingDate: manufacturingDate != null
            ? _formatDate(manufacturingDate)
            : null,
        referenceNumber: referenceNumber,
        notes: notes,
      );
      final request = AddInventoryBatchRequestDto(items: [row]);
      final response = await _remoteDataSource.addInventoryInbound(request);
      if (response.failedCount > 0) {
        final failed = response.failed.isNotEmpty
            ? response.failed.first
            : null;
        final errorMessage = failed?.errors
            .map((e) => e.description)
            .join('; ');
        throw AppException(
          failure: Failure.validation(
            message: errorMessage?.isNotEmpty == true
                ? errorMessage
                : 'Inventory row failed to be added',
          ),
        );
      }
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
  Future<List<InventoryBatch>> getInventoryBatches() async {
    try {
      final dtos = await _remoteDataSource.getInventoryBatches();
      return dtos.map(InventoryBatchMapper.toDomain).toList();
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
  Future<void> adjustInventoryBatch({
    required String batchId,
    required String direction,
    required String reason,
    required double quantity,
    DateTime? performedAt,
    String? notes,
  }) async {
    try {
      final request = AdjustInventoryBatchRequestDto(
        direction: direction,
        reason: reason,
        quantity: quantity,
        performedAt: performedAt == null
            ? null
            : formatUtcIsoInstant(performedAt),
        notes: notes,
      );
      await _remoteDataSource.adjustInventoryBatch(batchId, request);
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
  Future<({List<InventoryAdjustment> items, bool hasMore})>
  getAdjustmentHistory({required int pageNumber, required int pageSize}) async {
    try {
      final response = await _remoteDataSource.getAdjustmentHistory(
        pageNumber: pageNumber,
        pageSize: pageSize,
      );
      final items = response.items
          .map(InventoryAdjustmentMapper.toDomain)
          .toList();
      final hasMore = (pageNumber * pageSize) < response.totalCount;
      return (items: items, hasMore: hasMore);
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
  Future<GeneratedItemBarcode> generateItemBarcode() async {
    try {
      final dto = await _remoteDataSource.generateItemBarcode();
      return GeneratedItemBarcodeMapper.toDomain(dto);
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

  String _formatDate(DateTime date) {
    return formatLocalIsoDate(date);
  }
}
