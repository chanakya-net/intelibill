import 'dart:async';

import 'package:flutter/widgets.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/network/api_client_provider.dart';
import 'package:intelibill_mobile/src/features/customers/domain/entities/customer.dart';
import 'package:intelibill_mobile/src/features/customers/presentation/controllers/customers_controller.dart';
import 'package:intelibill_mobile/src/features/sales/data/data_sources/sales_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/sales/data/repositories/sales_repository_impl.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/payment_method.dart';
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
    this.availableCustomers = const [],
    this.isLoadingCustomers = false,
    this.customerLoadFailure,
    this.isCreatingCustomer = false,
    this.selectedCustomer,
    this.paymentMethod = PaymentMethod.cash,
    this.paidAmount = 0,
    this.dueAmount = 0,
    this.submissionFailure,
    this.customerCreateFailure,
    this.hasExplicitPaymentSplit = false,
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

  final List<Customer> availableCustomers;
  final bool isLoadingCustomers;
  final Failure? customerLoadFailure;
  final bool isCreatingCustomer;
  final Customer? selectedCustomer;

  final PaymentMethod paymentMethod;
  final double paidAmount;
  final double dueAmount;
  final Failure? submissionFailure;
  final Failure? customerCreateFailure;
  final bool hasExplicitPaymentSplit;

  double get estimatedSubtotalAmount =>
      cartLines.fold(0, (sum, line) => sum + _estimateLineSubtotal(line));

  double get estimatedTaxAmount =>
      cartLines.fold(0, (sum, line) => sum + _estimateLineTax(line));

  double get estimatedTotalAmount =>
      estimatedSubtotalAmount + estimatedTaxAmount;

  double get subtotalAmount =>
      preview?.subtotalAmount ?? estimatedSubtotalAmount;

  double get taxAmount => preview?.totalTaxAmount ?? estimatedTaxAmount;

  double get discountAmount => preview?.totalDiscountAmount ?? 0;

  double get cartTotal => preview?.totalAmount ?? estimatedTotalAmount;

  double get discountCapacityAmount => preview?.saleLevelEligibleSubtotal ?? 0;

  double get payable => cartTotal;

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

  bool get canSubmit => submissionFailure == null;

  String? get submissionError => submissionFailure?.message;

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
    bool clearSubmissionFailure = false,
    bool clearCustomerLoadFailure = false,
    List<Customer>? availableCustomers,
    bool? isLoadingCustomers,
    bool? isCreatingCustomer,
    Failure? customerLoadFailure,
    Customer? selectedCustomer,
    bool clearSelectedCustomer = false,
    PaymentMethod? paymentMethod,
    double? paidAmount,
    double? dueAmount,
    Failure? submissionFailure,
    Failure? customerCreateFailure,
    bool clearCustomerCreateFailure = false,
    bool? hasExplicitPaymentSplit,
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
      availableCustomers: availableCustomers ?? this.availableCustomers,
      isLoadingCustomers: isLoadingCustomers ?? this.isLoadingCustomers,
      customerLoadFailure: clearCustomerLoadFailure
          ? null
          : (customerLoadFailure ?? this.customerLoadFailure),
      isCreatingCustomer: isCreatingCustomer ?? this.isCreatingCustomer,
      selectedCustomer: clearSelectedCustomer
          ? null
          : (selectedCustomer ?? this.selectedCustomer),
      paymentMethod: paymentMethod ?? this.paymentMethod,
      paidAmount: paidAmount ?? this.paidAmount,
      dueAmount: dueAmount ?? this.dueAmount,
      submissionFailure: clearSubmissionFailure
          ? null
          : (submissionFailure ?? this.submissionFailure),
      customerCreateFailure: clearCustomerCreateFailure
          ? null
          : (customerCreateFailure ?? this.customerCreateFailure),
      hasExplicitPaymentSplit:
          hasExplicitPaymentSplit ?? this.hasExplicitPaymentSplit,
    );
  }
}

const _serviceLineType = 'Service'; // matches Sellable.kind for service items

@riverpod
class NewSaleController extends _$NewSaleController {
  Timer? _searchDebounce;
  int _activeSearchRequest = 0;
  int _activePreviewRequest = 0;

  @override
  NewSaleState build() {
    ref.onDispose(() => _searchDebounce?.cancel());
    unawaited(Future.microtask(_loadCustomers));
    return const NewSaleState();
  }

  Future<void> search({String? searchTerm, String? barcode}) async {
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
      state = state.copyWith(results: results, isSearching: false);
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
      _reconcilePaymentAfterCartChange();
      return;
    }

    state = state.copyWith(
      clearPreview: true,
      clearPreviewFailure: true,
      isPreviewLoading: true,
    );

    try {
      final request = _buildPreviewRequest(cartLines);
      final preview = await ref.read(previewSaleProvider)(request: request);
      if (!_isActivePreviewRequest(requestId) || !ref.mounted) {
        return;
      }
      state = state.copyWith(preview: preview, isPreviewLoading: false);
      _revalidateDiscountsAgainstPreview();
      _reconcilePaymentAfterCartChange();
    } on AppException catch (error) {
      if (!_isActivePreviewRequest(requestId) || !ref.mounted) {
        return;
      }
      state = state.copyWith(
        preview: null,
        previewFailure: error.failure,
        isPreviewLoading: false,
      );
      _validatePayment();
    } on Object {
      if (!_isActivePreviewRequest(requestId) || !ref.mounted) {
        return;
      }
      state = state.copyWith(
        preview: null,
        previewFailure: const Failure.unknown(),
        isPreviewLoading: false,
      );
      _validatePayment();
    }
  }

  Future<void> loadCustomers() {
    return _loadCustomers();
  }

  Future<void> _loadCustomers() async {
    if (state.isLoadingCustomers) {
      return;
    }

    state = state.copyWith(
      isLoadingCustomers: true,
      clearCustomerLoadFailure: true,
    );

    try {
      final useCase = ref.read(getCustomersUseCaseProvider);
      final customers = await useCase();
      if (!ref.mounted) return;
      state = state.copyWith(
        availableCustomers: customers,
        isLoadingCustomers: false,
      );
    } on AppException catch (error) {
      if (!ref.mounted) return;
      state = state.copyWith(
        isLoadingCustomers: false,
        customerLoadFailure: error.failure,
      );
    } on Object {
      if (!ref.mounted) return;
      state = state.copyWith(
        isLoadingCustomers: false,
        customerLoadFailure: const Failure.unknown(),
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
  }

  void updateSaleDiscountValue(double value) {
    final rounded = _roundMoney(value);
    final safeValue = rounded < 0 ? 0.0 : rounded;
    if (state.saleDiscountType == InstantDiscountType.none) {
      state = state.copyWith(
        saleDiscountType: InstantDiscountType.none,
        saleDiscountValue: 0,
        clearSaleDiscountError: true,
      );
      return;
    }

    if (state.preview == null) {
      if (state.saleDiscountType == InstantDiscountType.percentage &&
          safeValue > 100) {
        state = state.copyWith(
          saleDiscountError: 'Discount percentage cannot exceed 100%.',
        );
        return;
      }
      state = state.copyWith(
        saleDiscountValue: safeValue,
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
    if (safeValue > maxAllowed) {
      state = state.copyWith(
        saleDiscountType: state.saleDiscountType,
        saleDiscountValue: state.saleDiscountValue,
        saleDiscountError:
            state.saleDiscountType == InstantDiscountType.percentage
            ? 'Discount percentage exceeds sale maximum.'
            : 'Discount amount exceeds sale maximum.',
      );
      return;
    }

    state = state.copyWith(
      saleDiscountValue: safeValue,
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
    final isAllowed =
        normalizedType == InstantDiscountType.none || maxAllowed > 0;

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
    if (state.preview == null &&
        line.itemDiscountType == InstantDiscountType.percentage) {
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

  void selectCustomer(String? customerId) {
    Customer? selected;
    if (customerId != null && customerId.isNotEmpty) {
      for (final customer in state.availableCustomers) {
        if (customer.customerId == customerId) {
          selected = customer;
          break;
        }
      }
    }

    final nextMethod =
        selected == null && state.paymentMethod == PaymentMethod.credit
        ? PaymentMethod.cash
        : state.paymentMethod;

    state = state.copyWith(
      selectedCustomer: selected,
      clearSelectedCustomer: selected == null,
      paymentMethod: nextMethod,
    );
    _validatePayment();
  }

  Future<bool> createAndSelectCustomer({
    required String name,
    required String phoneNumber,
    String? address,
    bool isActive = true,
  }) async {
    if (state.isCreatingCustomer) {
      return false;
    }

    state = state.copyWith(
      isCreatingCustomer: true,
      clearCustomerCreateFailure: true,
    );

    try {
      final useCase = ref.read(createCustomerUseCaseProvider);
      final created = await useCase(
        name: name,
        phoneNumber: phoneNumber,
        address: address,
        isActive: isActive,
      );
      if (!ref.mounted) return false;

      final customers =
          state.availableCustomers.any(
            (customer) => customer.customerId == created.customerId,
          )
          ? state.availableCustomers
          : [...state.availableCustomers, created];
      state = state.copyWith(
        availableCustomers: customers,
        selectedCustomer: created,
        isCreatingCustomer: false,
        clearCustomerCreateFailure: true,
      );
      _validatePayment();
      return true;
    } on AppException catch (error) {
      if (!ref.mounted) return false;
      state = state.copyWith(
        isCreatingCustomer: false,
        customerCreateFailure: error.failure,
      );
      return false;
    } on Object {
      if (!ref.mounted) return false;
      state = state.copyWith(
        isCreatingCustomer: false,
        customerCreateFailure: const Failure.unknown(),
      );
      return false;
    }
  }

  void setPaymentMethod(PaymentMethod method) {
    if (method == state.paymentMethod) return;

    if (method == PaymentMethod.credit && state.selectedCustomer == null) {
      state = state.copyWith(
        submissionFailure: const Failure.validation(
          message: 'Select a customer for credit or due payment.',
        ),
      );
      return;
    }

    state = state.copyWith(paymentMethod: method);
    if (state.submissionFailure == null ||
        state.submissionFailure is ValidationFailure) {
      state = state.copyWith(clearSubmissionFailure: true);
    }
    _validatePayment();
  }

  void setPaidAmount(double paidAmount) {
    final payable = state.payable;
    final normalized = _normalizeMoney(paidAmount);
    final reconciledPaid = _coerceMoney(normalized, payable);
    final reconciledDue = _coerceMoney(
      state.payable - reconciledPaid,
      state.payable,
    );

    state = state.copyWith(
      paidAmount: reconciledPaid,
      dueAmount: reconciledDue,
      hasExplicitPaymentSplit: true,
    );
    _validatePayment();
  }

  void setDueAmount(double dueAmount) {
    final payable = state.payable;
    final normalized = _normalizeMoney(dueAmount);
    final reconciledDue = _coerceMoney(normalized, payable);
    final reconciledPaid = _coerceMoney(
      state.payable - reconciledDue,
      state.payable,
    );

    state = state.copyWith(
      paidAmount: reconciledPaid,
      dueAmount: reconciledDue,
      hasExplicitPaymentSplit: true,
    );
    _validatePayment();
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

    _setCartLines(updated);
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
      _setCartLines(updated);
      state = state.copyWith(
        searchFailure: const Failure.validation(
          message: 'Unit price must be greater than zero.',
        ),
      );
      return;
    }

    final updated = state.cartLines
        .map(
          (item) => item.sellable.id == sellableId
              ? item.copyWith(unitPrice: nextUnitPrice)
              : item,
        )
        .toList();
    _setCartLines(updated);
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
    _setCartLines(updated);
  }

  void removeFromCart(String sellableId) {
    final updated = state.cartLines
        .where((item) => item.sellable.id != sellableId)
        .toList();
    _setCartLines(
      updated,
      itemDiscountErrors: _itemDiscountErrorsWithout(sellableId),
    );
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

  void _setCartLines(
    List<NewSaleCartLine> cartLines, {
    Map<String, String>? itemDiscountErrors,
  }) {
    state = state.copyWith(
      cartLines: cartLines,
      itemDiscountErrors: itemDiscountErrors,
      clearFailure: true,
    );
    _invalidatePreviewState();
    _reconcilePaymentAfterCartChange();
    unawaited(refreshPreview());
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

  void _reconcilePaymentAfterCartChange() {
    if (state.payable <= 0) {
      state = state.copyWith(
        paidAmount: 0,
        dueAmount: 0,
        hasExplicitPaymentSplit: false,
      );
      _validatePayment();
      return;
    }

    final reconciledPaid = _coerceMoney(state.paidAmount, state.payable);
    if (!state.hasExplicitPaymentSplit &&
        state.paymentMethod != PaymentMethod.credit) {
      state = state.copyWith(paidAmount: state.payable, dueAmount: 0);
      _validatePayment();
      return;
    }

    final reconciledDue = _coerceMoney(
      state.payable - reconciledPaid,
      state.payable,
    );
    state = state.copyWith(
      paidAmount: reconciledPaid,
      dueAmount: reconciledDue,
    );
    _validatePayment();
  }

  void _validatePayment() {
    if (state.selectedCustomer == null &&
        (state.dueAmount > 0 || state.paymentMethod == PaymentMethod.credit)) {
      state = state.copyWith(
        submissionFailure: const Failure.validation(
          message: 'Select a customer for credit or due payments.',
        ),
      );
      return;
    }

    if (!arePaymentAmountsEqual(
      state.paidAmount,
      state.dueAmount,
      state.payable,
    )) {
      state = state.copyWith(
        submissionFailure: Failure.validation(
          message:
              'Paid and due must equal ₹${state.payable.toStringAsFixed(2)} in total.',
        ),
      );
      return;
    }

    state = state.copyWith(clearSubmissionFailure: true);
  }

  double _normalizeMoney(double value) {
    if (value.isNaN || value.isInfinite || value <= 0) return 0;
    return double.parse(value.toStringAsFixed(2));
  }

  double _coerceMoney(double value, double payable) {
    if (payable <= 0) return 0;
    final normalized = _normalizeMoney(value);
    if (normalized <= 0) return 0;
    if (normalized > payable) return payable;
    return normalized;
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

    _setCartLines(updated);
  }

  int _normalizeDiscountType(int type) {
    if (type == InstantDiscountType.percentage ||
        type == InstantDiscountType.flat) {
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
          maxFlat: _roundMoney(
            baseMaxFlat < configuredAmount ? baseMaxFlat : configuredAmount,
          ),
          maxPercent: _roundMoney(
            baseMaxPercent < configuredPercent
                ? baseMaxPercent
                : configuredPercent,
          ),
        );
      }
    }

    return (maxFlat: double.infinity, maxPercent: 100.0);
  }

  ({bool isEligible, double maxFlat, double maxPercent})
  _calculateSaleDiscountLimits() {
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

    final totalCapacity = preview.lines.fold<double>(0, (sum, line) {
      if (line.lineType == _serviceLineType) return sum;
      final preTax = line.preTaxAmountBeforeDiscount;
      final discount = line.itemDiscountAmount;
      final cost = (line.costPrice * line.quantity);
      final taxableAfterItem = preTax - discount;
      final eligible = taxableAfterItem - cost;
      return sum + (eligible > 0 ? eligible : 0);
    });
    var maxFlat = totalCapacity > 0 ? _roundMoney(totalCapacity) : 0.0;
    final maxPercent = eligibleSubtotal == 0
        ? 0.0
        : _roundMoney((maxFlat * 100) / eligibleSubtotal);

    final configured = preview.configuredSaleRule;
    if (configured != null && configured.percentage > 0) {
      final configuredAmount = _roundMoney(
        (eligibleSubtotal * configured.percentage) / 100,
      );
      maxFlat = _roundMoney(
        maxFlat < configuredAmount ? maxFlat : configuredAmount,
      );
      final configuredPercent = _roundMoney(configured.percentage);
      return (
        isEligible: maxFlat > 0,
        maxFlat: maxFlat,
        maxPercent: maxPercent < configuredPercent
            ? maxPercent
            : configuredPercent,
      );
    }

    return (isEligible: maxFlat > 0, maxFlat: maxFlat, maxPercent: maxPercent);
  }

  Map<String, String> _itemDiscountErrorsWith(
    String sellableId,
    String message,
  ) {
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
        itemDiscountErrors: didClearItemErrors
            ? nextErrors
            : state.itemDiscountErrors,
      );
    }
  }

  void _updateLineDiscounts(String sellableId, int type, double value) {
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
