import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/entities/supplier.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/use_cases/create_supplier.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/use_cases/get_suppliers.dart';
import 'package:intelibill_mobile/src/features/suppliers/presentation/controllers/suppliers_controller.dart';
import 'package:mocktail/mocktail.dart';

class MockGetSuppliers extends Mock implements GetSuppliers {}

class MockCreateSupplier extends Mock implements CreateSupplier {}

final _testSuppliers = [
  const Supplier(
    supplierId: 'sup-1',
    name: 'Alpha Suppliers',
    contactPersonName: 'Alice',
    contactPersonPhone: '9876543210',
    address: '12 Main Street',
    city: 'Mumbai',
    state: 'Maharashtra',
    pin: '400001',
    isSystem: false,
    isActive: true,
    isPreferred: true,
    balanceDue: 120,
  ),
  const Supplier(
    supplierId: 'sup-2',
    name: 'Beta Traders',
    contactPersonName: 'Bob',
    contactPersonPhone: '9012345678',
    address: 'Market Road',
    city: 'Pune',
    state: 'Maharashtra',
    pin: '411001',
    isSystem: true,
    isActive: true,
    isPreferred: false,
    balanceDue: 0,
  ),
  const Supplier(
    supplierId: 'sup-3',
    name: 'Gamma Wholesale',
    contactPersonName: 'Carol',
    contactPersonPhone: '9123456789',
    address: 'Lake View',
    city: 'Bengaluru',
    state: 'Karnataka',
    pin: '560001',
    isSystem: false,
    isActive: false,
    isPreferred: false,
    balanceDue: -45.5,
  ),
];

void main() {
  late MockGetSuppliers getSuppliers;
  late MockCreateSupplier createSupplier;

  setUp(() {
    getSuppliers = MockGetSuppliers();
    createSupplier = MockCreateSupplier();
  });

  ProviderContainer makeContainer({
    required MockGetSuppliers getSuppliers,
    required MockCreateSupplier createSupplier,
    SuppliersController Function()? controllerFactory,
  }) {
    return ProviderContainer(
      overrides: [
        getSuppliersUseCaseProvider.overrideWithValue(getSuppliers),
        createSupplierUseCaseProvider.overrideWithValue(createSupplier),
        if (controllerFactory != null)
          suppliersControllerProvider.overrideWith(controllerFactory),
      ],
    );
  }

  group('SuppliersController', () {
    test('starts in loading state', () {
      when(() => getSuppliers()).thenAnswer((_) async => _testSuppliers);

      final container = makeContainer(
        getSuppliers: getSuppliers,
        createSupplier: createSupplier,
      );
      addTearDown(container.dispose);

      expect(container.read(suppliersControllerProvider).isLoading, true);
      expect(container.read(suppliersControllerProvider).suppliers, isEmpty);
    });

    test('loads suppliers and transitions to loaded state', () async {
      when(() => getSuppliers()).thenAnswer((_) async => _testSuppliers);

      final container = makeContainer(
        getSuppliers: getSuppliers,
        createSupplier: createSupplier,
      );
      addTearDown(container.dispose);

      await container.read(suppliersControllerProvider.notifier).refresh();

      final state = container.read(suppliersControllerProvider);
      expect(state.isLoading, false);
      expect(state.suppliers, _testSuppliers);
      expect(state.failure, isNull);
    });

    test('transitions to error state when use case throws', () async {
      when(() => getSuppliers()).thenThrow(Exception('connection failed'));

      final container = makeContainer(
        getSuppliers: getSuppliers,
        createSupplier: createSupplier,
      );
      addTearDown(container.dispose);

      await container.read(suppliersControllerProvider.notifier).refresh();

      final state = container.read(suppliersControllerProvider);
      expect(state.isLoading, false);
      expect(state.suppliers, isEmpty);
      expect(state.failure, isA<UnknownFailure>());
    });

    test('filters out system suppliers', () async {
      when(() => getSuppliers()).thenAnswer((_) async => _testSuppliers);

      final container = makeContainer(
        getSuppliers: getSuppliers,
        createSupplier: createSupplier,
      );
      addTearDown(container.dispose);

      await container.read(suppliersControllerProvider.notifier).refresh();

      final filtered = container
          .read(suppliersControllerProvider)
          .filteredSuppliers;
      expect(filtered.length, 2);
      expect(filtered.any((supplier) => supplier.isSystem), false);
      expect(
        filtered.map((supplier) => supplier.supplierId),
        isNot(contains('sup-2')),
      );
    });

    group('search filtering', () {
      test('filters suppliers by name', () async {
        when(() => getSuppliers()).thenAnswer((_) async => _testSuppliers);

        final container = makeContainer(
          getSuppliers: getSuppliers,
          createSupplier: createSupplier,
        );
        addTearDown(container.dispose);

        await container.read(suppliersControllerProvider.notifier).refresh();
        container
            .read(suppliersControllerProvider.notifier)
            .updateSearch('gamma');

        final filtered = container
            .read(suppliersControllerProvider)
            .filteredSuppliers;
        expect(filtered.length, 1);
        expect(filtered[0].name, 'Gamma Wholesale');
      });

      test('filters suppliers by city', () async {
        when(() => getSuppliers()).thenAnswer((_) async => _testSuppliers);

        final container = makeContainer(
          getSuppliers: getSuppliers,
          createSupplier: createSupplier,
        );
        addTearDown(container.dispose);

        await container.read(suppliersControllerProvider.notifier).refresh();
        container
            .read(suppliersControllerProvider.notifier)
            .updateSearch('mumbai');

        final filtered = container
            .read(suppliersControllerProvider)
            .filteredSuppliers;
        expect(filtered.length, 1);
        expect(filtered[0].name, 'Alpha Suppliers');
      });

      test('filters suppliers by state', () async {
        when(() => getSuppliers()).thenAnswer((_) async => _testSuppliers);

        final container = makeContainer(
          getSuppliers: getSuppliers,
          createSupplier: createSupplier,
        );
        addTearDown(container.dispose);

        await container.read(suppliersControllerProvider.notifier).refresh();
        container
            .read(suppliersControllerProvider.notifier)
            .updateSearch('Karnataka');

        final filtered = container
            .read(suppliersControllerProvider)
            .filteredSuppliers;
        expect(filtered.length, 1);
        expect(filtered[0].name, 'Gamma Wholesale');
      });

      test('filters suppliers by contact person name', () async {
        when(() => getSuppliers()).thenAnswer((_) async => _testSuppliers);

        final container = makeContainer(
          getSuppliers: getSuppliers,
          createSupplier: createSupplier,
        );
        addTearDown(container.dispose);

        await container.read(suppliersControllerProvider.notifier).refresh();
        container
            .read(suppliersControllerProvider.notifier)
            .updateSearch('alice');

        final filtered = container
            .read(suppliersControllerProvider)
            .filteredSuppliers;
        expect(filtered.length, 1);
        expect(filtered[0].name, 'Alpha Suppliers');
      });

      test('returns all suppliers when search is cleared', () async {
        when(() => getSuppliers()).thenAnswer((_) async => _testSuppliers);

        final container = makeContainer(
          getSuppliers: getSuppliers,
          createSupplier: createSupplier,
        );
        addTearDown(container.dispose);

        await container.read(suppliersControllerProvider.notifier).refresh();
        container
            .read(suppliersControllerProvider.notifier)
            .updateSearch('alpha');
        container.read(suppliersControllerProvider.notifier).updateSearch('');

        final filtered = container
            .read(suppliersControllerProvider)
            .filteredSuppliers;
        expect(filtered.length, 2);
      });
    });

    test('refresh clears error and reloads', () async {
      when(() => getSuppliers()).thenThrow(Exception('temporary error'));

      final container = makeContainer(
        getSuppliers: getSuppliers,
        createSupplier: createSupplier,
      );
      addTearDown(container.dispose);

      await Future<void>.delayed(Duration.zero);

      await container.read(suppliersControllerProvider.notifier).refresh();
      expect(
        container.read(suppliersControllerProvider).failure,
        isA<UnknownFailure>(),
      );

      when(() => getSuppliers()).thenAnswer((_) async => _testSuppliers);
      await container.read(suppliersControllerProvider.notifier).refresh();

      final state = container.read(suppliersControllerProvider);
      expect(state.failure, isNull);
      expect(state.suppliers, _testSuppliers);
    });

    test('creates supplier, clears submit state, and refreshes list', () async {
      when(() => getSuppliers()).thenAnswer((_) async => _testSuppliers);
      when(
        () => createSupplier(
          name: any(named: 'name'),
          contactPersonName: any(named: 'contactPersonName'),
          contactPersonPhone: any(named: 'contactPersonPhone'),
          address: any(named: 'address'),
          city: any(named: 'city'),
          state: any(named: 'state'),
          pin: any(named: 'pin'),
          isActive: any(named: 'isActive'),
          isPreferred: any(named: 'isPreferred'),
        ),
      ).thenAnswer((_) async => _testSuppliers.first);

      final container = makeContainer(
        getSuppliers: getSuppliers,
        createSupplier: createSupplier,
      );
      addTearDown(container.dispose);

      await container.read(suppliersControllerProvider.notifier).refresh();
      final success = await container
          .read(suppliersControllerProvider.notifier)
          .createSupplier(
            name: 'ABC Traders',
            address: '12 Main Street',
            city: 'Mumbai',
            state: 'Maharashtra',
            pin: '400001',
            isActive: true,
            isPreferred: true,
          );

      expect(success, isTrue);
      final state = container.read(suppliersControllerProvider);
      expect(state.isSubmitting, isFalse);
      expect(state.submitFailure, isNull);
      verify(() => getSuppliers()).called(greaterThanOrEqualTo(2));
    });

    test('keeps create form open when post-create refresh fails', () async {
      var loadCount = 0;
      const refreshFailure = Failure.server(
        message: 'Unable to reload suppliers',
        statusCode: 500,
      );
      when(() => getSuppliers()).thenAnswer((_) async {
        loadCount += 1;
        if (loadCount == 1) {
          return _testSuppliers;
        }
        throw AppException(failure: refreshFailure);
      });
      when(
        () => createSupplier(
          name: any(named: 'name'),
          contactPersonName: any(named: 'contactPersonName'),
          contactPersonPhone: any(named: 'contactPersonPhone'),
          address: any(named: 'address'),
          city: any(named: 'city'),
          state: any(named: 'state'),
          pin: any(named: 'pin'),
          isActive: any(named: 'isActive'),
          isPreferred: any(named: 'isPreferred'),
        ),
      ).thenAnswer((_) async => _testSuppliers.first);

      final container = makeContainer(
        getSuppliers: getSuppliers,
        createSupplier: createSupplier,
      );
      addTearDown(container.dispose);

      await Future<void>.delayed(Duration.zero);
      final success = await container
          .read(suppliersControllerProvider.notifier)
          .createSupplier(
            name: 'ABC Traders',
            address: '12 Main Street',
            city: 'Mumbai',
            state: 'Maharashtra',
            pin: '400001',
            isActive: true,
            isPreferred: true,
          );

      expect(success, isFalse);
      final state = container.read(suppliersControllerProvider);
      expect(state.isSubmitting, isFalse);
      expect(state.failure, refreshFailure);
      expect(state.submitFailure, refreshFailure);
      expect(state.suppliers, _testSuppliers);
    });

    test(
      'rejects create attempts when submission is already in progress',
      () async {
        final container = makeContainer(
          getSuppliers: getSuppliers,
          createSupplier: createSupplier,
          controllerFactory: () => _DelayedRefreshSuppliersController(
            SuppliersState(suppliers: _testSuppliers, isSubmitting: true),
            Completer<void>(),
          ),
        );
        addTearDown(container.dispose);

        final success = await container
            .read(suppliersControllerProvider.notifier)
            .createSupplier(
              name: 'ABC Traders',
              address: '12 Main Street',
              city: 'Mumbai',
              state: 'Maharashtra',
              pin: '400001',
              isActive: true,
              isPreferred: true,
            );

        expect(success, isFalse);
        verifyNever(
          () => createSupplier(
            name: any(named: 'name'),
            contactPersonName: any(named: 'contactPersonName'),
            contactPersonPhone: any(named: 'contactPersonPhone'),
            address: any(named: 'address'),
            city: any(named: 'city'),
            state: any(named: 'state'),
            pin: any(named: 'pin'),
            isActive: any(named: 'isActive'),
            isPreferred: any(named: 'isPreferred'),
          ),
        );
      },
    );

    test(
      'stores submit failure and keeps list state on create failure',
      () async {
        when(() => getSuppliers()).thenAnswer((_) async => _testSuppliers);
        when(
          () => createSupplier(
            name: any(named: 'name'),
            contactPersonName: any(named: 'contactPersonName'),
            contactPersonPhone: any(named: 'contactPersonPhone'),
            address: any(named: 'address'),
            city: any(named: 'city'),
            state: any(named: 'state'),
            pin: any(named: 'pin'),
            isActive: any(named: 'isActive'),
            isPreferred: any(named: 'isPreferred'),
          ),
        ).thenThrow(
          AppException(
            failure: const Failure.validation(message: 'Server rejected input'),
          ),
        );

        final container = makeContainer(
          getSuppliers: getSuppliers,
          createSupplier: createSupplier,
        );
        addTearDown(container.dispose);

        await container.read(suppliersControllerProvider.notifier).refresh();
        final success = await container
            .read(suppliersControllerProvider.notifier)
            .createSupplier(
              name: 'ABC Traders',
              address: '12 Main Street',
              city: 'Mumbai',
              state: 'Maharashtra',
              pin: '400001',
              isActive: true,
              isPreferred: false,
            );

        expect(success, isFalse);
        final state = container.read(suppliersControllerProvider);
        expect(state.isSubmitting, isFalse);
        expect(
          state.submitFailure,
          const Failure.validation(message: 'Server rejected input'),
        );
        expect(state.suppliers, _testSuppliers);
      },
    );
  });
}

class _DelayedRefreshSuppliersController extends SuppliersController {
  _DelayedRefreshSuppliersController(this._state, this._refreshCompleter);

  final SuppliersState _state;
  final Completer<void> _refreshCompleter;

  @override
  SuppliersState build() => _state;

  @override
  Future<void> refresh() async {
    state = state.copyWith(isLoading: true, clearError: true);
    await _refreshCompleter.future;
    state = state.copyWith(isLoading: false);
  }
}
