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
import 'package:intelibill_mobile/src/features/sales/domain/entities/sellable.dart';
import 'package:intelibill_mobile/src/features/sales/domain/repositories/sales_repository.dart';
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

@immutable
class NewSaleState {
  const NewSaleState({
    this.searchTerm = '',
    this.barcodeTerm = '',
    this.results = const [],
    this.cartLines = const [],
    this.searchFailure,
    this.selectedGoods,
    this.isSearching = false,
    this.availableCustomers = const [],
    this.isLoadingCustomers = false,
    this.customerLoadFailure,
    this.isCreatingCustomer = false,
    this.selectedCustomer,
    this.paymentMethod = PaymentMethod.cash,
    this.paidAmount = 0,
    this.dueAmount = 0,
    this.submissionFailure,
  });

  final String searchTerm;
  final String barcodeTerm;
  final List<Sellable> results;
  final List<NewSaleCartLine> cartLines;
  final Failure? searchFailure;
  final Sellable? selectedGoods;
  final bool isSearching;

  final List<Customer> availableCustomers;
  final bool isLoadingCustomers;
  final Failure? customerLoadFailure;
  final bool isCreatingCustomer;
  final Customer? selectedCustomer;

  final PaymentMethod paymentMethod;
  final double paidAmount;
  final double dueAmount;
  final Failure? submissionFailure;

  double get cartTotal =>
      cartLines.fold(0, (sum, line) => sum + line.lineTotal);

  double get payable => cartTotal;

  double get totalCartItems =>
      cartLines.fold(0, (sum, line) => sum + line.quantity);

  bool get canSubmit => submissionFailure == null;

  String? get submissionError => submissionFailure?.message;

  NewSaleState copyWith({
    String? searchTerm,
    String? barcodeTerm,
    List<Sellable>? results,
    List<NewSaleCartLine>? cartLines,
    Sellable? selectedGoods,
    Failure? searchFailure,
    bool clearFailure = false,
    bool clearSubmissionFailure = false,
    bool clearCustomerLoadFailure = false,
    bool clearSelectedGoods = false,
    bool? isSearching,
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
  }) {
    return NewSaleState(
      searchTerm: searchTerm ?? this.searchTerm,
      barcodeTerm: barcodeTerm ?? this.barcodeTerm,
      results: results ?? this.results,
      cartLines: cartLines ?? this.cartLines,
      searchFailure: clearFailure
          ? null
          : (searchFailure ?? this.searchFailure),
      selectedGoods: clearSelectedGoods
          ? null
          : (selectedGoods ?? this.selectedGoods),
      isSearching: isSearching ?? this.isSearching,
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
    );
  }
}

@riverpod
class NewSaleController extends _$NewSaleController {
  Timer? _searchDebounce;
  int _activeSearchRequest = 0;

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
      clearSubmissionFailure: true,
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
        clearSubmissionFailure: true,
      );
      _validatePayment();
      return true;
    } on AppException catch (error) {
      if (!ref.mounted) return false;
      state = state.copyWith(
        isCreatingCustomer: false,
        submissionFailure: error.failure,
      );
      return false;
    } on Object {
      if (!ref.mounted) return false;
      state = state.copyWith(
        isCreatingCustomer: false,
        submissionFailure: const Failure.unknown(),
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
    _setCartLines(updated);
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

  bool _isValidQuantity(double quantity) => quantity > 0;

  bool _isWithinStock(double quantity, Sellable sellable) {
    return quantity <= sellable.stock;
  }

  void _setCartLines(List<NewSaleCartLine> cartLines) {
    state = state.copyWith(cartLines: cartLines, clearFailure: true);
    _reconcilePaymentAfterCartChange();
  }

  void _reconcilePaymentAfterCartChange() {
    if (state.payable <= 0) {
      state = state.copyWith(paidAmount: 0, dueAmount: 0);
      _validatePayment();
      return;
    }

    final reconciledPaid = _coerceMoney(state.paidAmount, state.payable);
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
}
