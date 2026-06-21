import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_preview.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sellable.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/record_sale.dart';
import 'package:intelibill_mobile/src/features/sales/domain/use_cases/preview_sale.dart';
import 'package:intelibill_mobile/src/features/sales/domain/use_cases/search_sellables.dart';
import 'package:intelibill_mobile/src/features/sales/domain/use_cases/record_sale.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/new_sale_controller.dart';
import 'package:mocktail/mocktail.dart';

class MockSearchSellables extends Mock implements SearchSellables {}

class MockPreviewSale extends Mock implements PreviewSale {}

class MockRecordSale extends Mock implements RecordSale {}

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

SalePreview _preview({
  double totalAmount = 236,
  double totalTaxableAmount = 200,
  double totalTaxAmount = 36,
  double totalDiscountAmount = 14,
  double saleLevelEligibleSubtotal = 120,
  List<SalePreviewLine>? lines,
  List<SalePreviewInfo>? infos,
  List<SalePreviewWarning>? warnings,
}) {
  return SalePreview(
    totalAmount: totalAmount,
    totalTaxableAmount: totalTaxableAmount,
    totalTaxAmount: totalTaxAmount,
    totalDiscountAmount: totalDiscountAmount,
    saleLevelEligibleSubtotal: saleLevelEligibleSubtotal,
    configuredSaleRule: const SalePreviewConfiguredSaleRule(
      ruleId: 'rule-1',
      ruleType: 'SalePercentage',
      percentage: 10,
      thresholdAmount: 100,
    ),
    lines:
        lines ??
        const [
          SalePreviewLine(
            lineType: 'Goods',
            itemId: 'item-1',
            barcode: 'BAR-g1',
            itemName: 'Flour',
            inventoryBatchId: 'batch-1',
            batchNumber: 'BN-g1',
            quantity: 1,
            costPrice: 10,
            salesPrice: 12,
            mrp: 12,
            taxRatePercent: 5,
            isPriceIncludingTax: false,
            preTaxAmountBeforeDiscount: 12,
            itemDiscountAmount: 0,
            saleDiscountAmount: 0,
            taxableAmount: 12,
            taxAmount: 0.6,
            lineTotalAmount: 12.6,
            maxAllowedItemDiscountFlat: 0,
            maxAllowedItemDiscountPercent: 0,
            hasClientPriceMismatch: false,
          ),
        ],
    infos:
        infos ??
        const [
          SalePreviewInfo(
            code: 'sale_preview.info.configured_rule_applied',
            message: 'Configured sale rule applied.',
          ),
        ],
    warnings:
        warnings ??
        const [
          SalePreviewWarning(
            code: 'sale_preview.warning.validation',
            message: 'Sale-level discount is limited by configured rule.',
            severity: 'info',
          ),
        ],
  );
}

const PreviewSaleRequest _emptyPreviewRequest = PreviewSaleRequest(
  saleDiscountType: 0,
  saleDiscountValue: 0,
  items: [],
);
const RecordSaleRequest _emptyRecordSaleRequest = RecordSaleRequest(
  idempotencyKey: 'empty',
  customerId: 'cust-empty',
  customerName: null,
  customerPhone: null,
  paymentMethod: 1,
  paidAmount: 0,
  dueAmount: 0,
  items: [],
  saleDiscount: null,
);

SaleDetail _recordedSale() {
  return SaleDetail(
    saleId: 'sale-abc',
    invoiceNumber: 'INV-ABC-001',
    paymentMethod: 1,
    soldAt: DateTime.utc(2026, 5, 11, 10, 0),
    paidAmount: 236,
    dueAmount: 0,
    totalBeforeDiscount: 236,
    totalDiscountAmount: 0,
    totalAmount: 236,
    totalTaxAmount: 0,
    creditNoteAppliedAmount: 0,
    status: 'paid',
  );
}

void main() {
  late MockSearchSellables mockSearchSellables;
  late MockPreviewSale mockPreviewSale;
  late MockRecordSale mockRecordSale;

  setUp(() {
    mockSearchSellables = MockSearchSellables();
    mockPreviewSale = MockPreviewSale();
    mockRecordSale = MockRecordSale();
    when(
      () => mockPreviewSale(
        request: any(named: 'request'),
      ),
    ).thenAnswer((_) async => _preview());
  });

  setUpAll(() {
    registerFallbackValue(_emptyPreviewRequest);
    registerFallbackValue(_emptyRecordSaleRequest);
  });

  ProviderContainer makeContainer() {
    return ProviderContainer(
      overrides: [
        searchSellablesProvider.overrideWithValue(mockSearchSellables),
        previewSaleProvider.overrideWithValue(mockPreviewSale),
        recordSaleProvider.overrideWithValue(mockRecordSale),
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

    test('submit records sale and clears cart on success', () async {
      final capturedRequests = <RecordSaleRequest>[];
      final goods = _goods(id: 'g1', name: 'Flour', stock: 5);

      when(
        () => mockRecordSale(
          request: any(named: 'request'),
        ),
      ).thenAnswer((invocation) async {
        capturedRequests.add(
          invocation.namedArguments[#request] as RecordSaleRequest,
        );
        return _recordedSale();
      });

      final container = makeContainer();
      addTearDown(container.dispose);
      final controller = container.read(newSaleControllerProvider.notifier);

      controller.state = NewSaleState(
        cartLines: [NewSaleCartLine(sellable: goods, quantity: 2)],
        preview: _preview(
          totalAmount: 236,
          totalTaxAmount: 0,
          totalDiscountAmount: 0,
        ),
        customerId: 'cust-1',
        customerName: 'John Doe',
        customerPhone: '+91-9999999999',
        paymentMethod: 1,
      );

      await controller.submit();

      final state = container.read(newSaleControllerProvider);
      expect(state.recordedSale, isNotNull);
      expect(state.recordedSale!.saleId, 'sale-abc');
      expect(state.cartLines, isEmpty);
      expect(state.results, isEmpty);
      expect(state.preview, isNull);
      expect(state.isSubmitting, isFalse);
      expect(state.submitFailure, isNull);
      expect(capturedRequests, hasLength(1));
      final request = capturedRequests.single;
      expect(request.idempotencyKey, isNotEmpty);
      expect(request.idempotencyKey, contains('new-sale-'));
      expect(request.customerId, 'cust-1');
      expect(request.customerName, 'John Doe');
      expect(request.customerPhone, '+91-9999999999');
      expect(request.items, hasLength(1));
      expect(request.items.single.lineType, 'Goods');
      expect(request.paidAmount, 236);
      expect(request.dueAmount, 0);
    });

    test('submit stores backend failure in submitFailure', () async {
      final goods = _goods(id: 'g1', name: 'Flour', stock: 5);

      when(
        () => mockRecordSale(
          request: any(named: 'request'),
        ),
      ).thenThrow(
        AppException(
          failure: const Failure.network(message: 'backend rejected'),
        ),
      );

      final container = makeContainer();
      addTearDown(container.dispose);
      final controller = container.read(newSaleControllerProvider.notifier);

      controller.state = NewSaleState(
        cartLines: [NewSaleCartLine(sellable: goods, quantity: 1)],
        preview: _preview(),
      );

      await controller.submit();

      final state = container.read(newSaleControllerProvider);
      expect(state.recordedSale, isNull);
      expect(state.submitFailure, isA<Failure>());
      expect(state.isSubmitting, isFalse);
      verify(() => mockRecordSale(request: any(named: 'request'))).called(1);
    });

    test('submit uses same idempotency key on retry after failure', () async {
      final capturedRequests = <RecordSaleRequest>[];
      final goods = _goods(id: 'g1', name: 'Flour', stock: 5);
      var callCount = 0;

      when(
        () => mockRecordSale(request: any(named: 'request')),
      ).thenAnswer((invocation) async {
        capturedRequests.add(
          invocation.namedArguments[#request] as RecordSaleRequest,
        );
        callCount++;
        if (callCount == 1) {
          throw AppException(
            failure: const Failure.network(message: 'timeout'),
          );
        }
        return _recordedSale();
      });

      final container = makeContainer();
      addTearDown(container.dispose);
      final controller = container.read(newSaleControllerProvider.notifier);

      controller.state = NewSaleState(
        cartLines: [NewSaleCartLine(sellable: goods, quantity: 1)],
        preview: _preview(),
        customerId: 'cust-1',
        paymentMethod: 1,
      );

      await controller.submit();
      expect(
        container.read(newSaleControllerProvider).submitFailure,
        isA<Failure>(),
      );

      await controller.submit();
      expect(container.read(newSaleControllerProvider).recordedSale, isNotNull);

      expect(capturedRequests, hasLength(2));
      expect(
        capturedRequests[0].idempotencyKey,
        equals(capturedRequests[1].idempotencyKey),
        reason: 'retry must reuse the same idempotency key',
      );
    });

    test('submit guard blocks duplicate submissions', () async {
      final goods = _goods(id: 'g1', name: 'Flour', stock: 5);

      final container = makeContainer();
      addTearDown(container.dispose);
      final controller = container.read(newSaleControllerProvider.notifier);

      controller.state = NewSaleState(
        cartLines: [NewSaleCartLine(sellable: goods, quantity: 1)],
        preview: _preview(),
        isSubmitting: true,
      );

      await controller.submit();

      final state = container.read(newSaleControllerProvider);
      expect(state.submitFailure, isA<Failure>());
      verifyNever(() => mockRecordSale(request: any(named: 'request')));
      expect(state.isSubmitting, isTrue);
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

    test('builds preview payload for mixed goods and service lines', () async {
      final capturedRequests = <PreviewSaleRequest>[];
      when(
        () => mockPreviewSale(
          request: any(named: 'request'),
        ),
      ).thenAnswer((invocation) async {
        final request =
            invocation.namedArguments[#request] as PreviewSaleRequest;
        capturedRequests.add(request);
        return _preview();
      });

      final container = makeContainer();
      addTearDown(container.dispose);
      final controller = container.read(newSaleControllerProvider.notifier);
      final goods = Sellable(
        id: 'g1',
        kind: 'Goods',
        name: 'Rice',
        stock: 5,
        price: 60,
        barcode: 'BAR-1',
        batchNumber: 'BN-1',
        mrp: 62,
        taxRatePercent: 12,
      );
      final service = _service(id: 's1', name: 'Installation', price: 150);

      controller.state = NewSaleState(
        cartLines: [
          NewSaleCartLine(sellable: goods, quantity: 2),
          NewSaleCartLine(sellable: service, quantity: 1, unitPrice: 175),
        ],
      );

      await controller.refreshPreview();

      expect(capturedRequests, hasLength(1));
      final request = capturedRequests.single;
      expect(request.saleDiscountType, 0);
      expect(request.saleDiscountValue, 0);
      expect(request.items, hasLength(2));
      expect(request.items.first.lineType, 'Goods');
      expect(request.items.first.inventoryBatchId, 'g1');
      expect(request.items.first.salesPrice, 60);
      expect(request.items.last.lineType, 'Service');
      expect(request.items.last.serviceId, 's1');
      expect(
        request.items.last.inventoryBatchId,
        '00000000-0000-0000-0000-000000000000',
      );
      expect(request.items.last.salesPrice, 175);
      final state = container.read(newSaleControllerProvider);
      expect(state.preview, isNotNull);
      expect(state.canSubmitCheckout, isTrue);
    });

    test(
      'sets preview failure and blocks checkout when preview throws',
      () async {
        when(
          () => mockPreviewSale(
            request: any(named: 'request'),
          ),
        ).thenThrow(
          AppException(failure: const Failure.network(message: 'offline')),
        );

        final container = makeContainer();
        addTearDown(container.dispose);
        final controller = container.read(newSaleControllerProvider.notifier);
        controller.state = NewSaleState(
          cartLines: [
            NewSaleCartLine(
              sellable: _goods(id: 'g1', name: 'Flour', stock: 5),
              quantity: 1,
            ),
          ],
        );

        await controller.refreshPreview();

        final state = container.read(newSaleControllerProvider);
        expect(state.preview, isNull);
        expect(state.previewFailure, isA<Failure>());
        expect(state.canSubmitCheckout, isFalse);
        expect(state.isPreviewLoading, isFalse);
      },
    );

    test('clears stale preview while refreshing cart changes', () async {
      final previewBeforeChange = _preview(
        totalAmount: 100,
        totalTaxAmount: 10,
      );
      final previewAfterChange = Completer<SalePreview>();
      var requestCount = 0;

      when(
        () => mockPreviewSale(
          request: any(named: 'request'),
        ),
      ).thenAnswer((_) async {
        requestCount += 1;
        if (requestCount == 1) {
          return previewBeforeChange;
        }

        return previewAfterChange.future;
      });

      final container = makeContainer();
      addTearDown(container.dispose);
      final controller = container.read(newSaleControllerProvider.notifier);
      final goods = _goods(id: 'g1', name: 'Flour', stock: 5);

      controller.state = NewSaleState(
        cartLines: [NewSaleCartLine(sellable: goods, quantity: 1)],
      );
      await controller.refreshPreview();

      expect(
        container.read(newSaleControllerProvider).preview,
        previewBeforeChange,
      );

      controller.updateCartQuantity(goods.id, 2);

      final refreshingState = container.read(newSaleControllerProvider);
      expect(refreshingState.preview, isNull);
      expect(refreshingState.isPreviewLoading, isTrue);
      expect(refreshingState.canSubmitCheckout, isFalse);

      previewAfterChange.complete(
        _preview(totalAmount: 200, totalTaxAmount: 20),
      );
      await Future<void>.delayed(const Duration(milliseconds: 20));
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
  });
}
