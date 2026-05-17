import 'package:intelibill_mobile/src/features/inventory/domain/entities/inventory_adjustment.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/inventory_batch.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/item.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/product_details.dart';

interface class InventoryRepository {
  Future<List<Item>> getItems() {
    throw UnimplementedError();
  }

  Future<Item> createItem({
    required String name,
    required String barcode,
    required String uom,
    String? description,
  }) {
    throw UnimplementedError();
  }

  Future<ProductDetails> getProductDetails({String? name, String? barcode}) {
    throw UnimplementedError();
  }

  Future<Item> updateItem({
    required String itemId,
    required String name,
    required String barcode,
    required String uom,
    String? description,
    required bool isActive,
  }) {
    throw UnimplementedError();
  }

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
  }) {
    throw UnimplementedError();
  }

  Future<List<InventoryBatch>> getInventoryBatches() {
    throw UnimplementedError();
  }

  Future<void> adjustInventoryBatch({
    required String batchId,
    required String direction,
    required String reason,
    required double quantity,
    DateTime? performedAt,
    String? notes,
  }) {
    throw UnimplementedError();
  }

  Future<({List<InventoryAdjustment> items, bool hasMore})>
  getAdjustmentHistory({required int pageNumber, required int pageSize}) {
    throw UnimplementedError();
  }
}
