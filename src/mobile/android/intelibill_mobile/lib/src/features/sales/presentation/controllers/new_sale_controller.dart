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
    this.saleDiscountType = InstantDiscountType.none,
    this.saleDiscountValue = 0,
    this.saleDiscountError,
    this.itemDiscountErrors = const {},
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
  final int saleDiscountType;
  final double saleDiscountValue;
  final String? saleDiscountError;
  final Map<String, String> itemDiscountErrors;
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
      !isPreviewLoading &&
      !hasDiscountValidationErrors;

  bool get hasDiscountValidationErrors {
    if (saleDiscountError != null && saleDiscountError!.trim().isNotEmpty) {
      return true;
    }
    return itemDiscountErrors.values.any((value) => value.trim().isNotEmpty);
  }

  NewSaleState copyWith({
    String? searchTerm,
    String? barcodeTerm,
    List<Sellable>? results,
    List<NewSaleCartLine>? cartLines,
    int? saleDiscountType,
    double? saleDiscountValue,
    String? saleDiscountError,
    Map<String, String>? itemDiscountErrors,
    bool clearSaleDiscountError = false,
    bool clearItemDiscountErrors = false,
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
      saleDiscountType: saleDiscountType ?? this.saleDiscountType,
      saleDiscountValue: saleDiscountValue ?? this.saleDiscountValue,
      saleDiscountError: clearSaleDiscountError
          ? null
          : (saleDiscountError ?? this.saleDiscountError),
      itemDiscountErrors: clearItemDiscountErrors
          ? const {}
          : (itemDiscountErrors ?? this.itemDiscountErrors),
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
      _revalidateDiscountsAgainstPreview();
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

  void updateSaleDiscountType(int type) {
    final normalizedType = _normalizeDiscountType(type);
    if (normalizedType == InstantDiscountType.none) {
      state = state.copyWith(
        saleDiscountType: InstantDiscountType.none,
        saleDiscountValue: 0,
        clearSaleDiscountError: true,
      );
      _schedulePreviewRefresh();
      return;
    }

    if (state.preview == null) {
      state = state.copyWith(
        saleDiscountType: normalizedType,
        saleDiscountError: null,
      );
      _schedulePreviewRefresh();
      return;
    }

    final preview = state.preview;
    if (preview != null) {
      final limits = _calculateSaleDiscountLimits();
      final maxAllowed = normalizedType == InstantDiscountType.percentage
          ? limits.maxPercent
          : limits.maxFlat;
      if (!limits.isEligible || maxAllowed <= 0) {
        state = state.copyWith(
          saleDiscountType: InstantDiscountType.none,
          saleDiscountValue: 0,
          clearSaleDiscountError: true,
        );
        _schedulePreviewRefresh();
        return;
      }
      final clamped = _clampDecimal(state.saleDiscountValue, maxAllowed);
      state = state.copyWith(
        saleDiscountType: normalizedType,
        saleDiscountValue: clamped,
        clearSaleDiscountError: true,
      );
      _schedulePreviewRefresh();
      return;
    }
  }

  void updateSaleDiscountValue(double value) {
    final normalizedValue = _clampDecimal(_roundMoney(value), 100);
    if (state.saleDiscountType == InstantDiscountType.none) {
      state = state.copyWith(
        saleDiscountType: InstantDiscountType.none,
        saleDiscountValue: 0,
        clearSaleDiscountError: true,
      );
      return;
    }

    if (state.preview == null) {
      final maxValue = state.saleDiscountType == InstantDiscountType.percentage
          ? 100
          : double.infinity;
      if (normalizedValue > maxValue) {
        state = state.copyWith(
          saleDiscountType: state.saleDiscountType,
          saleDiscountValue: state.saleDiscountValue,
          saleDiscountError: 'Discount percentage cannot exceed 100%.',
        );
        return;
      }

      state = state.copyWith(
        saleDiscountValue: normalizedValue,
        clearSaleDiscountError: true,
      );
      _schedulePreviewRefresh();
      return;
    }

    final limits = _calculateSaleDiscountLimits();
    if (!limits.isEligible) {
      state = state.copyWith(
        saleDiscountType: InstantDiscountType.none,
        saleDiscountValue: 0,
        clearSaleDiscountError: true,
      );
      return;
    }

    final maxAllowed = state.saleDiscountType == InstantDiscountType.percentage
        ? limits.maxPercent
        : limits.maxFlat;
    if (normalizedValue > maxAllowed) {
      state = state.copyWith(
        saleDiscountType: state.saleDiscountType,
        saleDiscountValue: state.saleDiscountValue,
        saleDiscountError: state.saleDiscountType == InstantDiscountType.percentage
            ? 'Discount percentage exceeds sale maximum.'
            : 'Discount amount exceeds sale maximum.',
      );
      return;
    }

    state = state.copyWith(
      saleDiscountValue: normalizedValue,
      clearSaleDiscountError: true,
    );
    _schedulePreviewRefresh();
  }

  void updateCartItemDiscountType(String sellableId, int type) {
    final line = _findLine(sellableId);
    if (line == null || !line.sellable.isGoods) return;

    final normalizedType = _normalizeDiscountType(type);
    if (normalizedType == InstantDiscountType.none) {
      _updateLineDiscounts(sellableId, InstantDiscountType.none, 0);
      _schedulePreviewRefresh();
      return;
    }

    if (state.preview == null) {
      _updateLineDiscounts(sellableId, normalizedType, line.itemDiscountValue);
      _schedulePreviewRefresh();
      return;
    }

    final limits = _calculateLineDiscountLimits(sellableId);
    final maxAllowed = normalizedType == InstantDiscountType.percentage
        ? limits.maxPercent
        : limits.maxFlat;
    final isAllowed = normalizedType == InstantDiscountType.none || maxAllowed > 0;

    if (!isAllowed) {
      state = state.copyWith(
        cartLines: state.cartLines
            .map(
              (item) => item.sellable.id == sellableId
                  ? item.copyWith(
                      itemDiscountType: InstantDiscountType.none,
                      itemDiscountValue: 0,
                    )
                  : item,
            )
            .toList(),
        itemDiscountErrors: _itemDiscountErrorsWith(sellableId, ''),
      );
      _schedulePreviewRefresh();
      return;
    }

    final nextItemValue = _clampDecimal(line.itemDiscountValue, maxAllowed);
    _updateLineDiscounts(sellableId, normalizedType, nextItemValue);
    _schedulePreviewRefresh();
  }

  void updateCartItemDiscountValue(String sellableId, double value) {
    final line = _findLine(sellableId);
    if (line == null || !line.sellable.isGoods) return;

    if (line.itemDiscountType == InstantDiscountType.none) {
      _updateLineDiscounts(sellableId, InstantDiscountType.none, 0);
      _schedulePreviewRefresh();
      return;
    }

    final normalizedValue = _roundMoney(value);
    final safeValue = normalizedValue < 0 ? 0.0 : normalizedValue;
    if (state.preview == null && line.itemDiscountType == InstantDiscountType.percentage) {
      if (safeValue > 100) {
        state = state.copyWith(
          itemDiscountErrors: _itemDiscountErrorsWith(
            sellableId,
            'Discount percentage cannot exceed 100%.',
          ),
        );
        return;
      }
      _updateLineDiscounts(sellableId, line.itemDiscountType, safeValue);
      _schedulePreviewRefresh();
      return;
    }

    final limits = _calculateLineDiscountLimits(sellableId);
    final maxAllowed = line.itemDiscountType == InstantDiscountType.percentage
        ? limits.maxPercent
        : limits.maxFlat;

    if (maxAllowed.isFinite && safeValue > maxAllowed) {
      state = state.copyWith(
        itemDiscountErrors: _itemDiscountErrorsWith(
          sellableId,
          line.itemDiscountType == InstantDiscountType.percentage
              ? 'Discount percentage exceeds allowed maximum.'
              : 'Discount amount exceeds allowed maximum.',
        ),
      );
      return;
    }

    final roundedValue = _clampDecimal(safeValue, maxAllowed);
    _updateLineDiscounts(sellableId, line.itemDiscountType, roundedValue);
    _schedulePreviewRefresh();
  }

  void removeFromCart(String sellableId) {
    state = state.copyWith(
      cartLines: state.cartLines
          .where((item) => item.sellable.id != sellableId)
          .toList(),
      itemDiscountErrors: _itemDiscountErrorsWithout(sellableId),
      clearFailure: true,
    );
    _schedulePreviewRefresh();
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
      saleDiscountType: state.saleDiscountType,
      saleDiscountValue: state.saleDiscountValue,
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
      itemDiscountType: line.itemDiscountType,
      itemDiscountValue: line.itemDiscountValue,
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

  int _normalizeDiscountType(int type) {
    if (type == InstantDiscountType.percentage || type == InstantDiscountType.flat) {
      return type;
    }
    return InstantDiscountType.none;
  }

  double _clampDecimal(double value, double maxValue) {
    if (value < 0) return 0;
    if (!maxValue.isFinite) return _roundMoney(value);
    return _roundMoney(value > maxValue ? maxValue : value);
  }

  ({double maxFlat, double maxPercent}) _calculateLineDiscountLimits(
    String sellableId,
  ) {
    final preview = state.preview;
    if (preview == null) {
      return (maxFlat: double.infinity, maxPercent: 100.0);
    }

    for (final line in preview.lines) {
      if (line.clientLineKey == sellableId) {
        final baseMaxFlat = line.maxAllowedItemDiscountFlat > 0
            ? _roundMoney(line.maxAllowedItemDiscountFlat)
            : 0.0;
        final baseMaxPercent = line.maxAllowedItemDiscountPercent > 0
            ? _roundMoney(line.maxAllowedItemDiscountPercent)
            : 0.0;

        if (line.configuredBatchRulePercentage == null) {
          return (maxFlat: baseMaxFlat, maxPercent: baseMaxPercent);
        }

        final configuredPercent = line.configuredBatchRulePercentage!;
        final configuredAmount = _roundMoney(
          line.preTaxAmountBeforeDiscount * configuredPercent / 100,
        );
        return (
          maxFlat: _roundMoney(baseMaxFlat < configuredAmount ? baseMaxFlat : configuredAmount),
          maxPercent: _roundMoney(
            baseMaxPercent < configuredPercent ? baseMaxPercent : configuredPercent,
          ),
        );
      }
    }

    return (maxFlat: double.infinity, maxPercent: 100.0);
  }

  ({bool isEligible, double maxFlat, double maxPercent}) _calculateSaleDiscountLimits() {
    final preview = state.preview;
    if (preview == null) {
      return (isEligible: false, maxFlat: 0.0, maxPercent: 0.0);
    }

    final eligibleSubtotal = preview.saleLevelEligibleSubtotal > 0
        ? _roundMoney(preview.saleLevelEligibleSubtotal)
        : 0.0;
    if (eligibleSubtotal <= 0) {
      return (isEligible: false, maxFlat: 0.0, maxPercent: 0.0);
    }

    final totalCapacity = preview.lines.fold<double>(
      0,
      (sum, line) {
        if (line.lineType == 'Service') return sum;
        final preTax = line.preTaxAmountBeforeDiscount;
        final discount = line.itemDiscountAmount;
        final cost = (line.costPrice * line.quantity);
        final taxableAfterItem = preTax - discount;
        final eligible = taxableAfterItem - cost;
        return sum + (eligible > 0 ? eligible : 0);
      },
    );
    var maxFlat = totalCapacity > 0 ? _roundMoney(totalCapacity) : 0.0;
    final maxPercent = eligibleSubtotal == 0
        ? 0.0
        : _roundMoney((maxFlat * 100) / eligibleSubtotal);

    final configured = preview.configuredSaleRule;
    if (configured != null && configured.percentage > 0) {
      final configuredAmount = _roundMoney(
        (eligibleSubtotal * configured.percentage) / 100,
      );
      maxFlat = _roundMoney(maxFlat < configuredAmount ? maxFlat : configuredAmount);
      final configuredPercent = _roundMoney(configured.percentage);
      return (
        isEligible: maxFlat > 0,
        maxFlat: maxFlat,
        maxPercent: maxPercent < configuredPercent ? maxPercent : configuredPercent,
      );
    }

    return (isEligible: maxFlat > 0, maxFlat: maxFlat, maxPercent: maxPercent);
  }

  Map<String, String> _itemDiscountErrorsWith(String sellableId, String message) {
    final next = {...state.itemDiscountErrors};
    if (message.trim().isEmpty) {
      next.remove(sellableId);
    } else {
      next[sellableId] = message;
    }
    return next;
  }

  Map<String, String> _itemDiscountErrorsWithout(String sellableId) {
    final next = {...state.itemDiscountErrors};
    next.remove(sellableId);
    return next;
  }

  void _revalidateDiscountsAgainstPreview() {
    _revalidateSaleDiscountAgainstPreview();
    _revalidateLineDiscountsAgainstPreview();
  }

  void _revalidateSaleDiscountAgainstPreview() {
    if (state.saleDiscountType == InstantDiscountType.none) {
      state = state.copyWith(
        saleDiscountValue: 0,
        clearSaleDiscountError: true,
      );
      return;
    }

    final limits = _calculateSaleDiscountLimits();
    if (!limits.isEligible) {
      state = state.copyWith(
        saleDiscountType: InstantDiscountType.none,
        saleDiscountValue: 0,
        clearSaleDiscountError: true,
      );
      return;
    }

    final maxAllowed = state.saleDiscountType == InstantDiscountType.percentage
        ? limits.maxPercent
        : limits.maxFlat;
    final clamped = _clampDecimal(state.saleDiscountValue, maxAllowed);
    state = state.copyWith(
      saleDiscountValue: clamped,
      clearSaleDiscountError: true,
    );
  }

  void _revalidateLineDiscountsAgainstPreview() {
    final updates = <NewSaleCartLine>[];
    final nextErrors = <String, String>{};
    var didChange = false;
    var didClearItemErrors = false;

    if (state.itemDiscountErrors.isNotEmpty) {
      didClearItemErrors = true;
    }

    for (final line in state.cartLines) {
      if (!line.sellable.isGoods) {
        if (state.itemDiscountErrors.containsKey(line.sellable.id)) {
          didClearItemErrors = true;
        }
        updates.add(line);
        continue;
      }

      if (line.itemDiscountType == InstantDiscountType.none) {
        if (state.itemDiscountErrors.containsKey(line.sellable.id)) {
          didClearItemErrors = true;
        }
        updates.add(line);
        continue;
      }

      final limits = _calculateLineDiscountLimits(line.sellable.id);
      final maxAllowed = line.itemDiscountType == InstantDiscountType.percentage
          ? limits.maxPercent
          : limits.maxFlat;

      if (maxAllowed <= 0) {
        updates.add(
          line.copyWith(
            itemDiscountType: InstantDiscountType.none,
            itemDiscountValue: 0,
          ),
        );
        final next = _itemDiscountErrorsWithout(line.sellable.id);
        if (next.length != state.itemDiscountErrors.length) {
          didClearItemErrors = true;
        }
        didChange = true;
        continue;
      }

      final clamped = _clampDecimal(line.itemDiscountValue, maxAllowed);
      updates.add(
        line.copyWith(
          itemDiscountValue: clamped,
          itemDiscountType: line.itemDiscountType,
        ),
      );
      if (clamped != line.itemDiscountValue) {
        didChange = true;
      }
    }

    if (didChange || didClearItemErrors) {
      state = state.copyWith(
        cartLines: updates,
        itemDiscountErrors: didClearItemErrors ? nextErrors : state.itemDiscountErrors,
      );
    }
  }

  void _updateLineDiscounts(
    String sellableId,
    int type,
    double value,
  ) {
    state = state.copyWith(
      cartLines: state.cartLines
          .map(
            (item) => item.sellable.id == sellableId
                ? item.copyWith(
                    itemDiscountType: type,
                    itemDiscountValue: value,
                  )
                : item,
          )
          .toList(),
      itemDiscountErrors: _itemDiscountErrorsWithout(sellableId),
    );
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
