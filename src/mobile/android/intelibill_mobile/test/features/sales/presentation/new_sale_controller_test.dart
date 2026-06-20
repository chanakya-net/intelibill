import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sellable.dart';
import 'package:intelibill_mobile/src/features/sales/domain/use_cases/search_sellables.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/new_sale_controller.dart';
import 'package:mocktail/mocktail.dart';

class MockSearchSellables extends Mock implements SearchSellables {}

Sellable _goods({
  required String id,
  required String name,
  required double stock,
}) {
  return Sellable(
    id: id,
    kind: 'Goods',
    name: name,
    stock: stock,
    price: 10,
    barcode: 'BAR-$id',
    batchNumber: 'BN-$id',
  );
}

void main() {
  late MockSearchSellables mockSearchSellables;

  setUp(() {
    mockSearchSellables = MockSearchSellables();
  });

  ProviderContainer makeContainer() {
    return ProviderContainer(
      overrides: [
        searchSellablesProvider.overrideWithValue(mockSearchSellables),
      ],
    );
  }

  group('NewSaleController', () {
    test('search maps sellables to state', () async {
      final goods = _goods(id: 'g1', name: 'Flour', stock: 5);
      const service = Sellable(
        id: 's1',
        kind: 'Service',
        name: 'Service Charge',
        stock: 0,
        price: 50,
      );

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
      expect(state.results, equals([goods]));
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
          .addToCart(
            _goods(id: 'g1', name: 'Flour', stock: 2),
          );

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

      verify(
        () => mockSearchSellables(barcode: 'BARCODE-1'),
      ).called(1);
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

      verify(
        () => mockSearchSellables(barcode: 'BARCODE-1'),
      ).called(1);
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
  });
}
