import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/data_sources/purchase_order_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/cancel_purchase_order_request_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/close_purchase_order_request_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/create_purchase_order_draft_request_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/purchase_order_detail_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/receive_purchase_order_request_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/mappers/purchase_order_mapper.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_draft.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_filters.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/receive_purchase_order_input.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_page.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/repositories/purchase_order_repository.dart';

class PurchaseOrderRepositoryImpl implements PurchaseOrderRepository {
  const PurchaseOrderRepositoryImpl(this._remoteDataSource);

  final PurchaseOrderRemoteDataSource _remoteDataSource;

  @override
  Future<PurchaseOrderPage> getPurchaseOrders(
    PurchaseOrderFilters filters,
  ) async {
    try {
      final dto = await _remoteDataSource.getPurchaseOrders(filters);
      return PurchaseOrderMapper.pageToDomain(dto);
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
  Future<PurchaseOrder> getPurchaseOrder(String purchaseOrderId) async {
    try {
      final dto = await _remoteDataSource.getPurchaseOrder(purchaseOrderId);
      return PurchaseOrderMapper.detailToDomain(dto);
    } on AppException {
      rethrow;
    } on FormatException catch (error) {
      throw AppException(
        failure: Failure.serialization(message: error.message),
      );
    } catch (error) {
      if (error is TypeError) {
        throw AppException(
          failure: Failure.serialization(message: error.toString()),
        );
      }

      throw AppException(failure: Failure.unknown(message: error.toString()));
    }
  }

  @override
  Future<PurchaseOrder> createDraft(PurchaseOrderDraft draft) async {
    return _mapDraftResponse(
      () => _remoteDataSource.createDraft(_toRequest(draft)),
    );
  }

  @override
  Future<PurchaseOrder> updateDraft(
    String purchaseOrderId,
    PurchaseOrderDraft draft,
  ) {
    return _mapDraftResponse(
      () => _remoteDataSource.updateDraft(purchaseOrderId, _toRequest(draft)),
    );
  }

  Future<PurchaseOrder> _mapDraftResponse(
    Future<PurchaseOrderDetailDto> Function() request,
  ) async {
    try {
      return PurchaseOrderMapper.detailToDomain(await request());
    } on AppException {
      rethrow;
    } on FormatException catch (error) {
      throw AppException(
        failure: Failure.serialization(message: error.message),
      );
    } catch (error) {
      if (error is TypeError) {
        throw AppException(
          failure: Failure.serialization(message: error.toString()),
        );
      }
      throw AppException(failure: Failure.unknown(message: error.toString()));
    }
  }

  CreatePurchaseOrderDraftRequestDto _toRequest(PurchaseOrderDraft draft) {
    return CreatePurchaseOrderDraftRequestDto(
      supplierId: _normalize(draft.supplierId),
      orderDate: _formatDate(draft.orderDate),
      expectedDeliveryDate: _formatDate(draft.expectedDeliveryDate),
      supplierReferenceNumber: _normalize(draft.supplierReferenceNumber),
      notes: _normalize(draft.notes),
      supplierName: _normalize(draft.supplierName),
      supplierReference: _normalize(draft.supplierReference),
      lines: draft.lines
          .map(
            (line) => CreatePurchaseOrderDraftLineRequestDto(
              itemId: line.itemId.trim(),
              description: line.description.trim(),
              expectedQuantity: line.expectedQuantity,
              unitCost: line.unitCost,
            ),
          )
          .toList(growable: false),
    );
  }

  @override
  Future<PurchaseOrder> cancel(String purchaseOrderId, String reason) async {
    final trimmed = reason.trim();
    if (trimmed.isEmpty || trimmed.length > 500) {
      throw AppException(
        failure: const Failure.validation(
          message: 'Reason must be between 1 and 500 characters.',
        ),
      );
    }
    try {
      final request = CancelPurchaseOrderRequestDto(reason: trimmed);
      final dto = await _remoteDataSource.cancel(purchaseOrderId, request);
      return PurchaseOrderMapper.detailToDomain(dto);
    } on AppException {
      rethrow;
    } on FormatException catch (error) {
      throw AppException(
        failure: Failure.serialization(message: error.message),
      );
    } catch (error) {
      if (error is TypeError) {
        throw AppException(
          failure: Failure.serialization(message: error.toString()),
        );
      }
      throw AppException(failure: Failure.unknown(message: error.toString()));
    }
  }

  @override
  Future<PurchaseOrder> close(String purchaseOrderId, String reason) async {
    final trimmed = reason.trim();
    if (trimmed.isEmpty || trimmed.length > 500) {
      throw AppException(
        failure: const Failure.validation(
          message: 'Reason must be between 1 and 500 characters.',
        ),
      );
    }
    try {
      final request = ClosePurchaseOrderRequestDto(reason: trimmed);
      final dto = await _remoteDataSource.close(purchaseOrderId, request);
      return PurchaseOrderMapper.detailToDomain(dto);
    } on AppException {
      rethrow;
    } on FormatException catch (error) {
      throw AppException(
        failure: Failure.serialization(message: error.message),
      );
    } catch (error) {
      if (error is TypeError) {
        throw AppException(
          failure: Failure.serialization(message: error.toString()),
        );
      }
      throw AppException(failure: Failure.unknown(message: error.toString()));
    }
  }

  @override
  Future<PurchaseOrder> receive(
    String purchaseOrderId,
    ReceivePurchaseOrderInput input,
  ) async {
    if (input.lines.isEmpty) {
      throw AppException(
        failure: const Failure.validation(message: 'No lines to receive.'),
      );
    }

    final ids = <String>{};
    for (final line in input.lines) {
      if (!ids.add(line.purchaseOrderLineId)) {
        throw AppException(
          failure: const Failure.validation(
            message: 'Duplicate purchase-order line IDs are not allowed.',
          ),
        );
      }
    }

    try {
      final dto = await _remoteDataSource.receive(
        purchaseOrderId,
        _toReceiveRequest(input),
      );
      return PurchaseOrderMapper.detailToDomain(dto);
    } on AppException {
      rethrow;
    } on FormatException catch (error) {
      throw AppException(
        failure: Failure.serialization(message: error.message),
      );
    } catch (error) {
      if (error is TypeError) {
        throw AppException(
          failure: Failure.serialization(message: error.toString()),
        );
      }
      throw AppException(failure: Failure.unknown(message: error.toString()));
    }
  }

  @override
  Future<PurchaseOrder> place(String purchaseOrderId) async {
    try {
      final dto = await _remoteDataSource.place(purchaseOrderId);
      return PurchaseOrderMapper.detailToDomain(dto);
    } on AppException {
      rethrow;
    } on FormatException catch (error) {
      throw AppException(
        failure: Failure.serialization(message: error.message),
      );
    } catch (error) {
      if (error is TypeError) {
        throw AppException(
          failure: Failure.serialization(message: error.toString()),
        );
      }
      throw AppException(failure: Failure.unknown(message: error.toString()));
    }
  }

  String? _normalize(String? value) {
    final trimmed = value?.trim();
    return trimmed == null || trimmed.isEmpty ? null : trimmed;
  }

  String? _formatDate(DateTime? date) {
    if (date == null) return null;
    return '${date.year.toString().padLeft(4, '0')}-'
        '${date.month.toString().padLeft(2, '0')}-'
        '${date.day.toString().padLeft(2, '0')}';
  }

  ReceivePurchaseOrderRequestDto _toReceiveRequest(
    ReceivePurchaseOrderInput input,
  ) {
    return ReceivePurchaseOrderRequestDto(
      referenceNumber: _normalize(input.referenceNumber),
      notes: _normalize(input.notes),
      receivedAt: input.receivedAt,
      lines: input.lines
          .map(
            (line) => ReceivePurchaseOrderLineRequestDto(
              purchaseOrderLineId: line.purchaseOrderLineId,
              barcode: line.barcode,
              batchNumber: line.batchNumber,
              quantity: line.quantity,
              totalPurchaseCost: line.totalPurchaseCost,
              unitCost: line.unitCost,
              mrp: line.mrp,
              salesPrice: line.salesPrice,
              taxRatePercent: line.taxRatePercent,
              taxIncluded: line.taxIncluded,
              purchaseTaxIncluded: line.purchaseTaxIncluded,
              expiryDate: line.expiryDate,
              manufacturingDate: line.manufacturingDate,
            ),
          )
          .toList(growable: false),
    );
  }
}
