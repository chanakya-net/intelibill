import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/customers/domain/entities/customer.dart';
import 'package:intelibill_mobile/src/features/customers/domain/use_cases/create_customer.dart';
import 'package:intelibill_mobile/src/features/customers/domain/use_cases/get_customers.dart';
import 'package:intelibill_mobile/src/features/customers/presentation/controllers/customers_controller.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sellable.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/payment_method.dart';
import 'package:intelibill_mobile/src/features/sales/domain/use_cases/search_sellables.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/new_sale_controller.dart';
import 'package:mocktail/mocktail.dart';

class MockSearchSellables extends Mock implements SearchSellables {}

class MockGetCustomers extends Mock implements GetCustomers {}

class MockCreateCustomer extends Mock implements CreateCustomer {}

Sellable _goods({
  required String id,
  required String name,
  required double stock,
  double price = 10,
}) {
  return Sellable(
    id: id,
    kind: 'Goods',
    name: name,
    stock: stock,
    price: price,
    barcode: 'BAR-$id',
    batchNumber: 'BN-$id',
  );
}

Customer _customer({
  required String customerId,
  required String name,
  required String phoneNumber,
}) {
  return Customer(
    customerId: customerId,
    name: name,
    phoneNumber: phoneNumber,
    isActive: true,
  );
}

Sellable _service({
  required String id,
  required String name,
  double price = 50,
}) {
  return Sellable(
    id: id,
    kind: 'Service',
    name: name,
    stock: 0,
    price: price,
    barcode: 'SRV-$id',
  );
}

void main() {
  late MockSearchSellables mockSearchSellables;
  late MockGetCustomers mockGetCustomers;
  late MockCreateCustomer mockCreateCustomer;

  setUp(() {
    mockSearchSellables = MockSearchSellables();
    mockGetCustomers = MockGetCustomers();
    mockCreateCustomer = MockCreateCustomer();

    when(() => mockGetCustomers()).thenAnswer((_) async => const []);
    when(
      () => mockCreateCustomer(
        name: any(named: 'name'),
        phoneNumber: any(named: 'phoneNumber'),
        address: any(named: 'address'),
        isActive: any(named: 'isActive'),
      ),
    ).thenAnswer(
      (_) async => const Customer(
        customerId: 'created-customer-id',
        name: 'Created Customer',
        phoneNumber: '9000000000',
        isActive: true,
      ),
    );
  });

  ProviderContainer makeContainer() {
    return ProviderContainer(
      overrides: [
        searchSellablesProvider.overrideWithValue(mockSearchSellables),
        getCustomersUseCaseProvider.overrideWithValue(mockGetCustomers),
        createCustomerUseCaseProvider.overrideWithValue(mockCreateCustomer),
      ],
    );
  }

  group('NewSaleController', () {
    test('search maps sellables to state', () async {
      final goods = _goods(id: 'g1', name: 'Flour', stock: 5);
      final service = _service(id: 's1', name: 'Service Charge');

      when(
        () => mockSearchSellables(
          searchTerm: any(named: 'searchTerm'),
          barcode: any(named: 'barcode'),
        ),
      ).thenAnswer((_) async => [goods, service]);

      final container = makeContainer();
      addTearDown(container.dispose);

      await container
          .read(newSaleControllerProvider.notifier)
          .search(searchTerm: 'foo');

      final state = container.read(newSaleControllerProvider);
      expect(state.results, equals([goods, service]));
      expect(state.searchFailure, isNull);
      expect(state.isSearching, isFalse);
    });

    test('search handles AppException', () async {
      when(
        () => mockSearchSellables(
          searchTerm: any(named: 'searchTerm'),
          barcode: any(named: 'barcode'),
        ),
      ).thenThrow(
        AppException(failure: const Failure.network(message: 'offline')),
      );

      final container = makeContainer();
      addTearDown(container.dispose);

      await container
          .read(newSaleControllerProvider.notifier)
          .search(searchTerm: 'any');
      final state = container.read(newSaleControllerProvider);
      expect(state.searchFailure, isA<Failure>());
      expect(state.results, isEmpty);
      expect(state.isSearching, isFalse);
    });

    test('invalid search clears stale results', () async {
      final goods = _goods(id: 'g1', name: 'Flour', stock: 5);
      when(
        () => mockSearchSellables(
          searchTerm: any(named: 'searchTerm'),
          barcode: any(named: 'barcode'),
        ),
      ).thenAnswer((_) async => [goods]);

      final container = makeContainer();
      addTearDown(container.dispose);
      final controller = container.read(newSaleControllerProvider.notifier);

      await controller.search(searchTerm: 'flour');
      await controller.search(searchTerm: '   ');

      final state = container.read(newSaleControllerProvider);
      expect(state.results, isEmpty);
      expect(state.searchFailure, isA<Failure>());
      expect(state.isSearching, isFalse);
    });

    test('failed search clears stale results from prior success', () async {
      final goods = _goods(id: 'g1', name: 'Flour', stock: 5);
      when(
        () => mockSearchSellables(
          searchTerm: 'flour',
          barcode: any(named: 'barcode'),
        ),
      ).thenAnswer((_) async => [goods]);
      when(
        () => mockSearchSellables(
          searchTerm: 'rice',
          barcode: any(named: 'barcode'),
        ),
      ).thenThrow(
        AppException(failure: const Failure.network(message: 'offline')),
      );

      final container = makeContainer();
      addTearDown(container.dispose);
      final controller = container.read(newSaleControllerProvider.notifier);

      await controller.search(searchTerm: 'flour');
      await controller.search(searchTerm: 'rice');

      final state = container.read(newSaleControllerProvider);
      expect(state.results, isEmpty);
      expect(state.searchFailure, isA<Failure>());
      expect(state.isSearching, isFalse);
    });

    test('addToCart adds goods line with quantity 1', () async {
      when(
        () => mockSearchSellables(
          searchTerm: any(named: 'searchTerm'),
          barcode: any(named: 'barcode'),
        ),
      ).thenAnswer((_) async => []);

      final container = makeContainer();
      addTearDown(container.dispose);

      await container
          .read(newSaleControllerProvider.notifier)
          .addToCart(_goods(id: 'g1', name: 'Flour', stock: 2));

      final state = container.read(newSaleControllerProvider);
      expect(state.cartLines.length, 1);
      expect(state.cartLines.first.quantity, 1.0);
      expect(state.searchFailure, isNull);
    });

    test('quantity cannot exceed available stock', () async {
      when(
        () => mockSearchSellables(
          searchTerm: any(named: 'searchTerm'),
          barcode: any(named: 'barcode'),
        ),
      ).thenAnswer((_) async => []);

      final container = makeContainer();
      addTearDown(container.dispose);
      final controller = container.read(newSaleControllerProvider.notifier);
      final goods = _goods(id: 'g1', name: 'Flour', stock: 2);

      await controller.addToCart(goods, quantity: 2);
      await controller.addToCart(goods, quantity: 0.1);

      final state = container.read(newSaleControllerProvider);
      expect(state.searchFailure, isNotNull);
      expect(state.cartLines.first.quantity, 2.0);
    });

    test('supports fractional cart quantities within stock', () async {
      when(
        () => mockSearchSellables(
          searchTerm: any(named: 'searchTerm'),
          barcode: any(named: 'barcode'),
        ),
      ).thenAnswer((_) async => []);

      final container = makeContainer();
      addTearDown(container.dispose);
      final controller = container.read(newSaleControllerProvider.notifier);
      final goods = _goods(id: 'g1', name: 'Flour', stock: 1.25);

      await controller.addToCart(goods, quantity: 0.5);
      controller.updateCartQuantity(goods.id, 1.25);

      final state = container.read(newSaleControllerProvider);
      expect(state.cartLines.single.quantity, 1.25);
      expect(state.searchFailure, isNull);
    });

    test('rejects fractional quantity above stock', () async {
      when(
        () => mockSearchSellables(
          searchTerm: any(named: 'searchTerm'),
          barcode: any(named: 'barcode'),
        ),
      ).thenAnswer((_) async => []);

      final container = makeContainer();
      addTearDown(container.dispose);
      final controller = container.read(newSaleControllerProvider.notifier);
      final goods = _goods(id: 'g1', name: 'Flour', stock: 0.5);

      await controller.addToCart(goods, quantity: 0.5);
      await controller.addToCart(goods, quantity: 0.25);

      final state = container.read(newSaleControllerProvider);
      expect(state.cartLines.single.quantity, 0.5);
      expect(state.searchFailure, isA<Failure>());
    });

    test('barcode lookup queries usecase by barcode', () async {
      when(
        () => mockSearchSellables(
          searchTerm: any(named: 'searchTerm'),
          barcode: any(named: 'barcode'),
        ),
      ).thenAnswer((_) async => []);

      final container = makeContainer();
      addTearDown(container.dispose);
      final notifier = container.read(newSaleControllerProvider.notifier);
      await notifier.search(searchTerm: 'Flour', barcode: 'BARCODE-1');

      verify(() => mockSearchSellables(barcode: 'BARCODE-1')).called(1);
    });

    test('barcode lookup ignores stale text search term', () async {
      when(
        () => mockSearchSellables(
          searchTerm: any(named: 'searchTerm'),
          barcode: any(named: 'barcode'),
        ),
      ).thenAnswer((_) async => []);

      final container = makeContainer();
      addTearDown(container.dispose);
      final notifier = container.read(newSaleControllerProvider.notifier);

      await notifier.search(searchTerm: 'Flour');
      await notifier.search(barcode: 'BARCODE-1');

      verify(() => mockSearchSellables(barcode: 'BARCODE-1')).called(1);
    });

    test('search ignores stale in-flight responses', () async {
      final olderSearch = Completer<List<Sellable>>();
      final newerSearch = Completer<List<Sellable>>();
      final olderGoods = _goods(id: 'g1', name: 'Older Flour', stock: 5);
      final newerGoods = _goods(id: 'g2', name: 'Fresh Flour', stock: 4);

      when(
        () => mockSearchSellables(
          searchTerm: any(named: 'searchTerm'),
          barcode: any(named: 'barcode'),
        ),
      ).thenAnswer((invocation) {
        final searchTerm = invocation.namedArguments[#searchTerm] as String?;

        if (searchTerm == 'old') {
          return olderSearch.future;
        }

        if (searchTerm == 'new') {
          return newerSearch.future;
        }

        throw StateError('Unexpected search term: $searchTerm');
      });

      final container = makeContainer();
      addTearDown(container.dispose);
      final notifier = container.read(newSaleControllerProvider.notifier);

      final oldRequest = notifier.search(searchTerm: 'old');
      final newRequest = notifier.search(searchTerm: 'new');

      newerSearch.complete([newerGoods]);
      await newRequest;

      olderSearch.complete([olderGoods]);
      await oldRequest;

      final state = container.read(newSaleControllerProvider);
      expect(state.results, equals([newerGoods]));
      expect(state.searchTerm, 'new');
      expect(state.searchFailure, isNull);
      expect(state.isSearching, isFalse);
    });

    test('addToCart adds service line with editable unit price', () async {
      when(
        () => mockSearchSellables(
          searchTerm: any(named: 'searchTerm'),
          barcode: any(named: 'barcode'),
        ),
      ).thenAnswer((_) async => []);

      final container = makeContainer();
      addTearDown(container.dispose);
      final controller = container.read(newSaleControllerProvider.notifier);
      final service = _service(id: 's1', name: 'Installation', price: 120);

      await controller.addToCart(service);
      controller.updateCartUnitPrice(service.id, 150);
      controller.updateCartQuantity(service.id, 2);

      final state = container.read(newSaleControllerProvider);
      expect(state.cartLines.single.quantity, 2.0);
      expect(state.cartLines.single.unitPrice, 150.0);
      expect(state.cartLines.single.lineTotal, 300.0);
      expect(state.searchFailure, isNull);
    });

    test('mixed goods and service lines coexist in cart', () async {
      when(
        () => mockSearchSellables(
          searchTerm: any(named: 'searchTerm'),
          barcode: any(named: 'barcode'),
        ),
      ).thenAnswer((_) async => []);

      final container = makeContainer();
      addTearDown(container.dispose);
      final controller = container.read(newSaleControllerProvider.notifier);
      final goods = _goods(id: 'g1', name: 'Flour', stock: 5);
      final service = _service(id: 's1', name: 'Delivery', price: 80);

      await controller.addToCart(goods);
      await controller.addToCart(service, quantity: 2);
      controller.updateCartUnitPrice(service.id, 90);

      final state = container.read(newSaleControllerProvider);
      expect(state.cartLines, hasLength(2));
      expect(
        state.cartLines.firstWhere((line) => line.sellable.isGoods).quantity,
        1,
      );
      expect(
        state.cartLines.firstWhere((line) => line.sellable.isService).unitPrice,
        90.0,
      );
    });

    test('updateCartUnitPrice with invalid price sets searchFailure', () async {
      when(
        () => mockSearchSellables(
          searchTerm: any(named: 'searchTerm'),
          barcode: any(named: 'barcode'),
        ),
      ).thenAnswer((_) async => []);

      final container = makeContainer();
      addTearDown(container.dispose);
      final controller = container.read(newSaleControllerProvider.notifier);
      final service = _service(id: 's1', name: 'Setup', price: 100);

      await controller.addToCart(service);
      controller.updateCartUnitPrice(service.id, 0.0);

      final state = container.read(newSaleControllerProvider);
      expect(state.searchFailure, isNotNull);
      expect(state.cartLines.single.unitPrice, isNull);
    });

    test('addToCart accumulates quantity for existing service line', () async {
      when(
        () => mockSearchSellables(
          searchTerm: any(named: 'searchTerm'),
          barcode: any(named: 'barcode'),
        ),
      ).thenAnswer((_) async => []);

      final container = makeContainer();
      addTearDown(container.dispose);
      final controller = container.read(newSaleControllerProvider.notifier);
      final service = _service(id: 's1', name: 'Repair', price: 250);

      await controller.addToCart(service, quantity: 1);
      await controller.addToCart(service, quantity: 2);

      final state = container.read(newSaleControllerProvider);
      expect(state.cartLines, hasLength(1));
      expect(state.cartLines.single.quantity, 3.0);
    });

    test('payment method maps to and from wire codes', () {
      expect(PaymentMethod.cash.toWireCode(), 1);
      expect(PaymentMethod.upi.toWireCode(), 2);
      expect(PaymentMethod.card.toWireCode(), 3);
      expect(PaymentMethod.credit.toWireCode(), 4);
      expect(paymentMethodFromWireCode(1), PaymentMethod.cash);
      expect(paymentMethodFromWireCode(2), PaymentMethod.upi);
      expect(paymentMethodFromWireCode(3), PaymentMethod.card);
      expect(paymentMethodFromWireCode(4), PaymentMethod.credit);
    });

    test('loads customers and selects by id', () async {
      final customers = [
        _customer(
          customerId: 'cust-1',
          name: 'Alice',
          phoneNumber: '9999999999',
        ),
      ];
      when(() => mockGetCustomers()).thenAnswer((_) async => customers);

      final container = makeContainer();
      addTearDown(container.dispose);
      final controller = container.read(newSaleControllerProvider.notifier);

      await controller.loadCustomers();
      controller.selectCustomer('cust-1');

      final state = container.read(newSaleControllerProvider);
      expect(state.selectedCustomer, customers.first);
      expect(state.selectedCustomer?.customerId, 'cust-1');
    });

    test('creates and selects customer and adds to list', () async {
      final customers = [
        _customer(
          customerId: 'cust-1',
          name: 'Alice',
          phoneNumber: '9999999999',
        ),
      ];
      when(() => mockGetCustomers()).thenAnswer((_) async => customers);
      when(
        () => mockCreateCustomer(
          name: any(named: 'name'),
          phoneNumber: any(named: 'phoneNumber'),
          address: any(named: 'address'),
          isActive: any(named: 'isActive'),
        ),
      ).thenAnswer(
        (_) async => const Customer(
          customerId: 'cust-2',
          name: 'Bob',
          phoneNumber: '8888888888',
          isActive: true,
        ),
      );

      final container = makeContainer();
      addTearDown(container.dispose);
      final controller = container.read(newSaleControllerProvider.notifier);

      final created = await controller.createAndSelectCustomer(
        name: 'Bob',
        phoneNumber: '8888888888',
      );
      final state = container.read(newSaleControllerProvider);

      expect(created, isTrue);
      expect(state.submissionFailure, isNull);
      expect(state.selectedCustomer?.customerId, 'cust-2');
      expect(
        state.availableCustomers,
        contains(
          const Customer(
            customerId: 'cust-2',
            name: 'Bob',
            phoneNumber: '8888888888',
            isActive: true,
          ),
        ),
      );
    });

    test('allows walk-in cash sale with paid equal payable', () async {
      final container = makeContainer();
      addTearDown(container.dispose);
      final controller = container.read(newSaleControllerProvider.notifier);

      await controller.addToCart(_goods(id: 'g1', name: 'Flour', stock: 2));
      controller.setPaidAmount(20);

      final state = container.read(newSaleControllerProvider);
      expect(state.canSubmit, isTrue);
      expect(state.paymentMethod, PaymentMethod.cash);
      expect(state.submissionFailure, isNull);
      expect(state.paidAmount + state.dueAmount, closeTo(state.payable, 0.01));
    });

    test('blocks credit method without selected customer', () async {
      final customers = [
        _customer(
          customerId: 'cust-1',
          name: 'Alice',
          phoneNumber: '9999999999',
        ),
      ];
      when(() => mockGetCustomers()).thenAnswer((_) async => customers);
      final container = makeContainer();
      addTearDown(container.dispose);
      final controller = container.read(newSaleControllerProvider.notifier);
      await controller.loadCustomers();

      await controller.addToCart(_goods(id: 'g1', name: 'Flour', stock: 2));
      controller.setPaidAmount(20);
      controller.setPaymentMethod(PaymentMethod.credit);

      final blocked = container.read(newSaleControllerProvider);
      expect(blocked.paymentMethod, PaymentMethod.cash);
      expect(blocked.canSubmit, isFalse);
      expect(blocked.submissionFailure, isA<ValidationFailure>());

      controller.selectCustomer('cust-1');
      controller.setPaymentMethod(PaymentMethod.credit);
      final allowed = container.read(newSaleControllerProvider);
      expect(allowed.paymentMethod, PaymentMethod.credit);
      expect(allowed.canSubmit, isTrue);
    });

    test('requires customer when due amount is set', () async {
      final customers = [
        _customer(
          customerId: 'cust-1',
          name: 'Alice',
          phoneNumber: '9999999999',
        ),
      ];
      when(() => mockGetCustomers()).thenAnswer((_) async => customers);

      final container = makeContainer();
      addTearDown(container.dispose);
      final controller = container.read(newSaleControllerProvider.notifier);
      await controller.loadCustomers();

      await controller.addToCart(_goods(id: 'g1', name: 'Flour', stock: 2));
      controller.setDueAmount(5);

      final withDue = container.read(newSaleControllerProvider);
      expect(withDue.submissionFailure, isA<ValidationFailure>());
      expect(withDue.canSubmit, isFalse);

      controller.selectCustomer('cust-1');
      final withCustomer = container.read(newSaleControllerProvider);
      expect(withCustomer.canSubmit, isTrue);
    });

    test('reconciles paid/due when cart total changes', () async {
      final container = makeContainer();
      addTearDown(container.dispose);
      final controller = container.read(newSaleControllerProvider.notifier);

      await controller.addToCart(_goods(id: 'g1', name: 'Flour', stock: 10));
      controller.setPaidAmount(5);

      controller.updateCartQuantity('g1', 2);
      var state = container.read(newSaleControllerProvider);
      expect(state.paidAmount + state.dueAmount, closeTo(state.payable, 0.01));
      expect(state.dueAmount, closeTo(15, 0.01));

      controller.updateCartQuantity('g1', 1);
      state = container.read(newSaleControllerProvider);
      expect(state.paidAmount + state.dueAmount, closeTo(state.payable, 0.01));
      expect(state.dueAmount, closeTo(5, 0.01));
    });
  });
}
