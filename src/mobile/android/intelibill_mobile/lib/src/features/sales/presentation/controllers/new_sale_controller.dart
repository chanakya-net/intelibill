import 'dart:async';

import 'package:flutter/widgets.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/network/api_client_provider.dart';
import 'package:intelibill_mobile/src/features/sales/data/data_sources/sales_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/sales/data/repositories/sales_repository_impl.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_preview.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sellable.dart';
import 'package:intelibill_mobile/src/features/sales/domain/repositories/sales_repository.dart';
import 'package:intelibill_mobile/src/features/sales/domain/use_cases/preview_sale.dart';
import 'package:intelibill_mobile/src/features/sales/domain/use_cases/search_sellables.dart';
import 'package:intelibill_mobile/src/shared/barcode_scanner/barcode_scan_result.dart';
import 'package:intelibill_mobile/src/shared/barcode_scanner/show_barcode_scanner.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'new_sale_controller.g.dart';

@riverpod
SalesRemoteDataSource salesRemoteDataSource(Ref ref) {
  final apiClient = ref.watch(apiClientProvider);
  return SalesRemoteDataSourceImpl(apiClient);
}

@riverpod
SalesRepository salesRepository(Ref ref) {
  final remoteDataSource = ref.watch(salesRemoteDataSourceProvider);
  return SalesRepositoryImpl(remoteDataSource);
}

@riverpod
SearchSellables searchSellables(Ref ref) {
  final repository = ref.watch(salesRepositoryProvider);
  return SearchSellables(repository);
}

@riverpod
PreviewSale previewSale(Ref ref) {
  final repository = ref.watch(salesRepositoryProvider);
  return PreviewSale(repository);
}

@immutable
class NewSaleState {
  const NewSaleState({
    this.searchTerm = '',
    this.barcodeTerm = '',
    this.results = const [],
    this.cartLines = const [],
    this.searchFailure,
    this.preview,
    this.previewFailure,
    this.selectedGoods,
    this.isSearching = false,
    this.isPreviewLoading = false,
  });

  final String searchTerm;
  final String barcodeTerm;
  final List<Sellable> results;
  final List<NewSaleCartLine> cartLines;
  final Failure? searchFailure;
  final SalePreview? preview;
  final Failure? previewFailure;
  final Sellable? selectedGoods;
  final bool isSearching;
  final bool isPreviewLoading;

  double get estimatedSubtotalAmount => cartLines.fold(
    0,
    (sum, line) => sum + _estimateLineSubtotal(line),
  );

  double get estimatedTaxAmount => cartLines.fold(
    0,
    (sum, line) => sum + _estimateLineTax(line),
  );

  double get estimatedTotalAmount =>
      estimatedSubtotalAmount + estimatedTaxAmount;

  double get subtotalAmount =>
      preview?.subtotalAmount ?? estimatedSubtotalAmount;

  double get taxAmount => preview?.totalTaxAmount ?? estimatedTaxAmount;

  double get discountAmount => preview?.totalDiscountAmount ?? 0;

  double get cartTotal => preview?.totalAmount ?? estimatedTotalAmount;

  double get discountCapacityAmount => preview?.saleLevelEligibleSubtotal ?? 0;

  double get totalCartItems =>
      cartLines.fold(0, (sum, line) => sum + line.quantity);

  bool get canSubmitCheckout =>
      cartLines.isNotEmpty &&
      preview != null &&
      previewFailure == null &&
      !isPreviewLoading;

  NewSaleState copyWith({
    String? searchTerm,
    String? barcodeTerm,
    List<Sellable>? results,
    List<NewSaleCartLine>? cartLines,
    Failure? searchFailure,
    bool clearFailure = false,
    SalePreview? preview,
    bool clearPreview = false,
    Failure? previewFailure,
    bool clearPreviewFailure = false,
    Sellable? selectedGoods,
    bool clearSelectedGoods = false,
    bool? isSearching,
    bool? isPreviewLoading,
  }) {
    return NewSaleState(
      searchTerm: searchTerm ?? this.searchTerm,
      barcodeTerm: barcodeTerm ?? this.barcodeTerm,
      results: results ?? this.results,
      cartLines: cartLines ?? this.cartLines,
      searchFailure: clearFailure
          ? null
          : (searchFailure ?? this.searchFailure),
      preview: clearPreview ? null : (preview ?? this.preview),
      previewFailure: clearPreviewFailure
          ? null
          : (previewFailure ?? this.previewFailure),
      selectedGoods: clearSelectedGoods
          ? null
          : (selectedGoods ?? this.selectedGoods),
      isSearching: isSearching ?? this.isSearching,
      isPreviewLoading: isPreviewLoading ?? this.isPreviewLoading,
    );
  }
}

@riverpod
class NewSaleController extends _$NewSaleController {
  Timer? _searchDebounce;
  int _activeSearchRequest = 0;
  int _activePreviewRequest = 0;

  @override
  NewSaleState build() {
    ref.onDispose(() => _searchDebounce?.cancel());
    return const NewSaleState();
  }

  Future<void> search({
    String? searchTerm,
    String? barcode,
  }) async {
    final term = (searchTerm ?? state.searchTerm).trim();
    final code = (barcode ?? state.barcodeTerm).trim();

    final useCaseSearchTerm = code.isNotEmpty
        ? null
        : (term.isNotEmpty ? term : null);
    final useCaseBarcode = code.isNotEmpty ? code : null;

    if (useCaseSearchTerm == null && useCaseBarcode == null) {
      state = state.copyWith(
        results: const [],
        isSearching: false,
        searchFailure: const Failure.validation(
          message: 'Enter search or barcode.',
        ),
      );
      return;
    }

    state = state.copyWith(
      isSearching: true,
      clearFailure: true,
      searchTerm: useCaseSearchTerm ?? '',
      barcodeTerm: code,
      clearSelectedGoods: true,
    );
    final requestId = ++_activeSearchRequest;

    try {
      final useCase = ref.read(searchSellablesProvider);
      final results = await useCase(
        searchTerm: useCaseSearchTerm,
        barcode: useCaseBarcode,
      );
      if (!_isActiveSearchRequest(requestId)) {
        return;
      }
      state = state.copyWith(
        results: results,
        isSearching: false,
      );
    } on AppException catch (error) {
      if (!_isActiveSearchRequest(requestId)) {
        return;
      }
      state = state.copyWith(
        results: const [],
        isSearching: false,
        searchFailure: error.failure,
      );
    } on Object {
      if (!_isActiveSearchRequest(requestId)) {
        return;
      }
      state = state.copyWith(
        results: const [],
        isSearching: false,
        searchFailure: const Failure.unknown(),
      );
    }
  }

  Future<void> refreshPreview() async {
    final cartLines = state.cartLines;
    final requestId = ++_activePreviewRequest;

    if (cartLines.isEmpty) {
      if (!ref.mounted) return;
      state = state.copyWith(
        clearPreview: true,
        clearPreviewFailure: true,
        isPreviewLoading: false,
      );
      return;
    }

    state = state.copyWith(
      clearPreview: true,
      clearPreviewFailure: true,
      isPreviewLoading: true,
    );

    try {
      final request = _buildPreviewRequest(cartLines);
      final preview = await ref.read(previewSaleProvider)(
        request: request,
      );
      if (!_isActivePreviewRequest(requestId) || !ref.mounted) {
        return;
      }
      state = state.copyWith(
        preview: preview,
        isPreviewLoading: false,
      );
    } on AppException catch (error) {
      if (!_isActivePreviewRequest(requestId) || !ref.mounted) {
        return;
      }
      state = state.copyWith(
        preview: null,
        previewFailure: error.failure,
        isPreviewLoading: false,
      );
    } on Object {
      if (!_isActivePreviewRequest(requestId) || !ref.mounted) {
        return;
      }
      state = state.copyWith(
        preview: null,
        previewFailure: const Failure.unknown(),
        isPreviewLoading: false,
      );
    }
  }

  void updateSearchTerm(String value) {
    state = state.copyWith(
      searchTerm: value,
      barcodeTerm: '',
      clearFailure: true,
    );
    _searchDebounce?.cancel();
    _searchDebounce = Timer(const Duration(milliseconds: 250), () {
      unawaited(search());
    });
  }

  void updateBarcodeTerm(String value) {
    state = state.copyWith(
      barcodeTerm: value,
      searchTerm: '',
      clearFailure: true,
    );
    _searchDebounce?.cancel();
  }

  void selectGoodsForCart(Sellable sellable) {
    state = state.copyWith(selectedGoods: sellable);
  }

  Future<void> addToCart(Sellable sellable, {double quantity = 1}) async {
    if (!sellable.isGoods) {
      if (sellable.isService) {
        _addServiceToCart(sellable, quantity: quantity);
      }
      return;
    }
    final existing = _findLine(sellable.id);
    final currentQuantity = existing?.quantity ?? 0;
    final nextQuantity = currentQuantity + quantity;

    if (!_isValidQuantity(quantity) ||
        !_isWithinStock(nextQuantity, sellable)) {
      state = state.copyWith(
        searchFailure: const Failure.validation(
          message: 'Quantity cannot exceed available stock.',
        ),
      );
      return;
    }

    final updated = <NewSaleCartLine>[
      for (final line in state.cartLines)
        if (line.sellable.id == sellable.id)
          line.copyWith(quantity: nextQuantity)
        else
          line,
    ];
    if (existing == null) {
      updated.add(NewSaleCartLine(sellable: sellable, quantity: quantity));
    }

    state = state.copyWith(
      cartLines: updated,
      clearFailure: true,
    );
    _schedulePreviewRefresh();
  }

  void updateCartUnitPrice(String sellableId, double nextUnitPrice) {
    final line = _findLine(sellableId);
    if (line == null || !line.sellable.isService) return;
    if (!_isValidQuantity(nextUnitPrice)) {
      final updated = state.cartLines
          .map(
            (item) => item.sellable.id == sellableId
                ? item.copyWith(unitPrice: null)
                : item,
          )
          .toList();
      state = state.copyWith(
        cartLines: updated,
        searchFailure: const Failure.validation(
          message: 'Unit price must be greater than zero.',
        ),
      );
      _schedulePreviewRefresh();
      return;
    }

    final updated = state.cartLines
        .map(
          (item) => item.sellable.id == sellableId
              ? item.copyWith(unitPrice: nextUnitPrice)
              : item,
        )
        .toList();
    state = state.copyWith(cartLines: updated, clearFailure: true);
    _schedulePreviewRefresh();
  }

  void updateCartQuantity(String sellableId, double nextQuantity) {
    final line = _findLine(sellableId);
    if (line == null) return;
    if (nextQuantity <= 0) {
      removeFromCart(sellableId);
      return;
    }
    if (!_isValidQuantity(nextQuantity) ||
        (line.sellable.isGoods &&
            !_isWithinStock(nextQuantity, line.sellable))) {
      state = state.copyWith(
        searchFailure: const Failure.validation(
          message: 'Quantity cannot exceed available stock.',
        ),
      );
      return;
    }

    final updated = state.cartLines
        .map(
          (item) => item.sellable.id == sellableId
              ? item.copyWith(quantity: nextQuantity)
              : item,
        )
        .toList();
    state = state.copyWith(cartLines: updated, clearFailure: true);
    _schedulePreviewRefresh();
  }

  void removeFromCart(String sellableId) {
    state = state.copyWith(
      cartLines: state.cartLines
          .where((item) => item.sellable.id != sellableId)
          .toList(),
      clearFailure: true,
    );
    _schedulePreviewRefresh();
  }

  Future<void> scanAndLookupBarcode(BuildContext context) async {
    final result = await showBarcodeScanner(context);
    if (result is BarcodeScanResult && result.value.trim().isNotEmpty) {
      state = state.copyWith(
        barcodeTerm: result.value,
        searchTerm: '',
        clearFailure: true,
      );
      await search(barcode: result.value, searchTerm: '');
    }
  }

  NewSaleCartLine? _findLine(String sellableId) {
    for (final line in state.cartLines) {
      if (line.sellable.id == sellableId) return line;
    }
    return null;
  }

  bool _isActiveSearchRequest(int requestId) {
    return requestId == _activeSearchRequest;
  }

  bool _isActivePreviewRequest(int requestId) {
    return requestId == _activePreviewRequest;
  }

  bool _isValidQuantity(double quantity) => quantity > 0;

  bool _isWithinStock(double quantity, Sellable sellable) {
    return quantity <= sellable.stock;
  }

  void _schedulePreviewRefresh() {
    _invalidatePreviewState();
    unawaited(refreshPreview());
  }

  void _invalidatePreviewState() {
    _activePreviewRequest += 1;
    state = state.copyWith(
      clearPreview: true,
      clearPreviewFailure: true,
      isPreviewLoading: state.cartLines.isNotEmpty,
    );
  }

  PreviewSaleRequest _buildPreviewRequest(List<NewSaleCartLine> lines) {
    return PreviewSaleRequest(
      saleDiscountType: 0,
      saleDiscountValue: 0,
      items: lines.map(_previewItemFromLine).toList(growable: false),
    );
  }

  PreviewSaleItemRequest _previewItemFromLine(NewSaleCartLine line) {
    final sellable = line.sellable;
    final isService = sellable.isService;
    final effectiveUnitPrice = line.effectiveUnitPrice;
    return PreviewSaleItemRequest(
      inventoryBatchId: isService
          ? '00000000-0000-0000-0000-000000000000'
          : sellable.id,
      barcode: sellable.barcode ?? '',
      batchNumber: sellable.batchNumber ?? '',
      itemName: sellable.name,
      quantity: line.quantity,
      costPrice: isService ? 0 : sellable.price,
      salesPrice: effectiveUnitPrice,
      mrp: isService
          ? effectiveUnitPrice
          : (sellable.mrp > 0 ? sellable.mrp : effectiveUnitPrice),
      taxRatePercent: sellable.taxRatePercent,
      isPriceIncludingTax: sellable.taxIncluded,
      itemDiscountType: 0,
      itemDiscountValue: 0,
      clientLineKey: sellable.id,
      hsnCode: null,
      lineType: sellable.kind,
      serviceId: isService ? sellable.id : null,
    );
  }

  void _addServiceToCart(Sellable sellable, {double quantity = 1}) {
    if (!_isValidQuantity(quantity)) {
      state = state.copyWith(
        searchFailure: const Failure.validation(
          message: 'Quantity must be greater than zero.',
        ),
      );
      return;
    }

    final existing = _findLine(sellable.id);
    final updated = <NewSaleCartLine>[
      for (final line in state.cartLines)
        if (line.sellable.id == sellable.id)
          line.copyWith(quantity: (existing?.quantity ?? 0) + quantity)
        else
          line,
    ];
    if (existing == null) {
      updated.add(
        NewSaleCartLine(
          sellable: sellable,
          quantity: quantity,
          unitPrice: sellable.price,
        ),
      );
    }

    state = state.copyWith(cartLines: updated, clearFailure: true);
    _schedulePreviewRefresh();
  }
}

double _estimateLineSubtotal(NewSaleCartLine line) {
  final total = line.lineTotal;
  final taxRate = line.sellable.taxRatePercent;

  if (taxRate <= 0 || !line.sellable.taxIncluded) {
    return _roundMoney(total);
  }

  final divisor = 1 + (taxRate / 100);
  if (divisor <= 0) {
    return _roundMoney(total);
  }

  return _roundMoney(total / divisor);
}

double _estimateLineTax(NewSaleCartLine line) {
  final taxRate = line.sellable.taxRatePercent;
  if (taxRate <= 0) {
    return 0;
  }

  final subtotal = _estimateLineSubtotal(line);
  if (line.sellable.taxIncluded) {
    return _roundMoney(line.lineTotal - subtotal);
  }

  return _roundMoney(subtotal * taxRate / 100);
}

double _roundMoney(double value) {
  return (value * 100).roundToDouble() / 100;
}
