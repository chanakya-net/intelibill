import 'package:intelibill_mobile/src/core/network/api_client.dart';
import 'package:intelibill_mobile/src/features/inventory/data/dto/add_inventory_batch_request_dto.dart';
import 'package:intelibill_mobile/src/features/inventory/data/dto/add_inventory_batch_response_dto.dart';
import 'package:intelibill_mobile/src/features/inventory/data/dto/adjust_inventory_batch_request_dto.dart';
import 'package:intelibill_mobile/src/features/inventory/data/dto/create_item_request_dto.dart';
import 'package:intelibill_mobile/src/features/inventory/data/dto/generate_item_barcode_response_dto.dart';
import 'package:intelibill_mobile/src/features/inventory/data/dto/inventory_adjustment_history_response_dto.dart';
import 'package:intelibill_mobile/src/features/inventory/data/dto/inventory_batch_dto.dart';
import 'package:intelibill_mobile/src/features/inventory/data/dto/item_catalog_response_dto.dart';
import 'package:intelibill_mobile/src/features/inventory/data/dto/item_dto.dart';
import 'package:intelibill_mobile/src/features/inventory/data/dto/product_details_dto.dart';
import 'package:intelibill_mobile/src/features/inventory/data/dto/update_item_request_dto.dart';

interface class InventoryRemoteDataSource {
  Future<List<ItemDto>> getItems() {
    throw UnimplementedError();
  }

  Future<ItemDto> createItem(CreateItemRequestDto request) {
    throw UnimplementedError();
  }

  Future<ProductDetailsDto> getProductDetails({String? name, String? barcode}) {
    throw UnimplementedError();
  }

  Future<void> updateItem(String itemId, UpdateItemRequestDto request) {
    throw UnimplementedError();
  }

  Future<AddInventoryBatchResponseDto> addInventoryInbound(
    AddInventoryBatchRequestDto request,
  ) {
    throw UnimplementedError();
  }

  Future<List<InventoryBatchDto>> getInventoryBatches() {
    throw UnimplementedError();
  }

  Future<void> adjustInventoryBatch(
    String batchId,
    AdjustInventoryBatchRequestDto request,
  ) {
    throw UnimplementedError();
  }

  Future<InventoryAdjustmentHistoryResponseDto> getAdjustmentHistory({
    required int pageNumber,
    required int pageSize,
  }) {
    throw UnimplementedError();
  }

  Future<GenerateItemBarcodeResponseDto> generateItemBarcode() {
    throw UnimplementedError();
  }
}

class InventoryRemoteDataSourceImpl implements InventoryRemoteDataSource {
  InventoryRemoteDataSourceImpl(this._apiClient);

  final ApiClient _apiClient;

  static const String _itemsEndpoint = '/items';
  static const String _inventoryInboundBatchEndpoint =
      '/inventory/inbound/batch';
  static const String _inventoryBatchesEndpoint = '/inventory/batches';
  static const String _inventoryAdjustmentsEndpoint = '/inventory/adjustments';
  static const String _generateItemBarcodeEndpoint = '/items/barcodes/generate';

  static const int _itemsPageSize = 100;

  @override
  Future<List<ItemDto>> getItems() async {
    final allItems = <ItemDto>[];
    var pageNumber = 1;
    var totalCount = 0;

    do {
      final response = await _apiClient.get<Map<String, dynamic>>(
        _itemsEndpoint,
        queryParameters: {
          'pageNumber': pageNumber,
          'pageSize': _itemsPageSize,
        },
      );
      final catalog = ItemCatalogResponseDto.fromJson(response.data!);
      allItems.addAll(catalog.items);
      totalCount = catalog.totalCount;
      pageNumber += 1;
    } while (allItems.length < totalCount);

    return allItems;
  }

  @override
  Future<ItemDto> createItem(CreateItemRequestDto request) async {
    final response = await _apiClient.post<Map<String, dynamic>>(
      _itemsEndpoint,
      data: request.toJson(),
    );
    return ItemDto.fromJson(response.data!);
  }

  @override
  Future<ProductDetailsDto> getProductDetails({
    String? name,
    String? barcode,
  }) async {
    final response = await _apiClient.get<Map<String, dynamic>>(
      '$_itemsEndpoint/details',
      queryParameters: {
        if (name?.trim().isNotEmpty == true) 'name': name!.trim(),
        if (barcode?.trim().isNotEmpty == true) 'barcode': barcode!.trim(),
      },
    );
    return ProductDetailsDto.fromJson(response.data!);
  }

  @override
  Future<void> updateItem(String itemId, UpdateItemRequestDto request) async {
    await _apiClient.patch<void>(
      '$_itemsEndpoint/$itemId',
      data: request.toJson(),
    );
  }

  @override
  Future<AddInventoryBatchResponseDto> addInventoryInbound(
    AddInventoryBatchRequestDto request,
  ) async {
    final response = await _apiClient.post<Map<String, dynamic>>(
      _inventoryInboundBatchEndpoint,
      data: request.toJson(),
    );
    return AddInventoryBatchResponseDto.fromJson(response.data!);
  }

  @override
  Future<List<InventoryBatchDto>> getInventoryBatches() async {
    final response = await _apiClient.get<List<dynamic>>(
      _inventoryBatchesEndpoint,
    );
    return response.data!
        .cast<Map<String, dynamic>>()
        .map(InventoryBatchDto.fromJson)
        .toList();
  }

  @override
  Future<void> adjustInventoryBatch(
    String batchId,
    AdjustInventoryBatchRequestDto request,
  ) async {
    await _apiClient.post<void>(
      '$_inventoryBatchesEndpoint/$batchId/adjust',
      data: request.toJson(),
    );
  }

  @override
  Future<InventoryAdjustmentHistoryResponseDto> getAdjustmentHistory({
    required int pageNumber,
    required int pageSize,
  }) async {
    final response = await _apiClient.get<Map<String, dynamic>>(
      _inventoryAdjustmentsEndpoint,
      queryParameters: {'pageNumber': pageNumber, 'pageSize': pageSize},
    );
    return InventoryAdjustmentHistoryResponseDto.fromJson(response.data!);
  }

  @override
  Future<GenerateItemBarcodeResponseDto> generateItemBarcode() async {
    final response = await _apiClient.post<Map<String, dynamic>>(
      _generateItemBarcodeEndpoint,
    );
    return GenerateItemBarcodeResponseDto.fromJson(response.data!);
  }
}
