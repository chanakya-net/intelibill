import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/customers/domain/entities/customer.dart';
import 'package:intelibill_mobile/src/features/customers/domain/use_cases/create_customer.dart';
import 'package:intelibill_mobile/src/features/customers/domain/use_cases/get_customers.dart';
import 'package:intelibill_mobile/src/features/customers/presentation/controllers/customers_controller.dart';
import 'package:mocktail/mocktail.dart';

class MockGetCustomers extends Mock implements GetCustomers {}

class MockCreateCustomer extends Mock implements CreateCustomer {}

final _testCustomers = [
  const Customer(
    customerId: 'cust-1',
    name: 'Alice Sharma',
    phoneNumber: '9876543210',
    address: '12 Main St, Mumbai',
    isActive: true,
    outstandingDue: 100,
  ),
  const Customer(
    customerId: 'cust-2',
    name: 'Bob Kumar',
    phoneNumber: '9123456789',
    isActive: false,
  ),
  const Customer(
    customerId: 'cust-3',
    name: 'Carol Das',
    phoneNumber: '9000000001',
    address: 'Pune, Maharashtra',
    isActive: true,
  ),
];

void main() {
  late MockGetCustomers getCustomers;
  late MockCreateCustomer createCustomer;

  setUp(() {
    getCustomers = MockGetCustomers();
    createCustomer = MockCreateCustomer();
  });

  ProviderContainer makeContainer({
    required MockGetCustomers getCustomers,
    required MockCreateCustomer createCustomer,
  }) {
    return ProviderContainer(
      overrides: [
        getCustomersUseCaseProvider.overrideWithValue(getCustomers),
        createCustomerUseCaseProvider.overrideWithValue(createCustomer),
      ],
    );
  }

  group('CustomersController', () {
    test('starts in loading state', () {
      when(() => getCustomers()).thenAnswer((_) async => _testCustomers);

      final container = makeContainer(
        getCustomers: getCustomers,
        createCustomer: createCustomer,
      );
      addTearDown(container.dispose);

      expect(container.read(customersControllerProvider).isLoading, true);
      expect(container.read(customersControllerProvider).customers, isEmpty);
    });

    test('loads customers and transitions to loaded state', () async {
      when(() => getCustomers()).thenAnswer((_) async => _testCustomers);

      final container = makeContainer(
        getCustomers: getCustomers,
        createCustomer: createCustomer,
      );
      addTearDown(container.dispose);

      await container.read(customersControllerProvider.notifier).refresh();

      final state = container.read(customersControllerProvider);
      expect(state.isLoading, false);
      expect(state.customers, _testCustomers);
      expect(state.failure, isNull);
    });

    test('transitions to error state when use case throws', () async {
      when(() => getCustomers()).thenThrow(Exception('connection failed'));

      final container = makeContainer(
        getCustomers: getCustomers,
        createCustomer: createCustomer,
      );
      addTearDown(container.dispose);

      await container.read(customersControllerProvider.notifier).refresh();

      final state = container.read(customersControllerProvider);
      expect(state.isLoading, false);
      expect(state.customers, isEmpty);
      expect(state.failure, isNotNull);
    });

    test('refresh clears error and reloads', () async {
      when(() => getCustomers()).thenAnswer((_) async => _testCustomers);

      final container = makeContainer(
        getCustomers: getCustomers,
        createCustomer: createCustomer,
      );
      addTearDown(container.dispose);

      await container.read(customersControllerProvider.notifier).refresh();
      await container.read(customersControllerProvider.notifier).refresh();

      verify(() => getCustomers()).called(greaterThanOrEqualTo(2));
    });

    group('search filtering', () {
      test('filters customers by name', () async {
        when(() => getCustomers()).thenAnswer((_) async => _testCustomers);

        final container = makeContainer(
          getCustomers: getCustomers,
          createCustomer: createCustomer,
        );
        addTearDown(container.dispose);

        await container.read(customersControllerProvider.notifier).refresh();
        container
            .read(customersControllerProvider.notifier)
            .updateSearch('Alice');

        final filtered = container
            .read(customersControllerProvider)
            .filteredCustomers;
        expect(filtered.length, 1);
        expect(filtered[0].name, 'Alice Sharma');
      });

      test('filters customers by phone number', () async {
        when(() => getCustomers()).thenAnswer((_) async => _testCustomers);

        final container = makeContainer(
          getCustomers: getCustomers,
          createCustomer: createCustomer,
        );
        addTearDown(container.dispose);

        await container.read(customersControllerProvider.notifier).refresh();
        container
            .read(customersControllerProvider.notifier)
            .updateSearch('9123456789');

        final filtered = container
            .read(customersControllerProvider)
            .filteredCustomers;
        expect(filtered.length, 1);
        expect(filtered[0].name, 'Bob Kumar');
      });

      test('filters customers by address', () async {
        when(() => getCustomers()).thenAnswer((_) async => _testCustomers);

        final container = makeContainer(
          getCustomers: getCustomers,
          createCustomer: createCustomer,
        );
        addTearDown(container.dispose);

        await container.read(customersControllerProvider.notifier).refresh();
        container
            .read(customersControllerProvider.notifier)
            .updateSearch('Pune');

        final filtered = container
            .read(customersControllerProvider)
            .filteredCustomers;
        expect(filtered.length, 1);
        expect(filtered[0].name, 'Carol Das');
      });

      test('returns all customers when search is cleared', () async {
        when(() => getCustomers()).thenAnswer((_) async => _testCustomers);

        final container = makeContainer(
          getCustomers: getCustomers,
          createCustomer: createCustomer,
        );
        addTearDown(container.dispose);

        await container.read(customersControllerProvider.notifier).refresh();
        container
            .read(customersControllerProvider.notifier)
            .updateSearch('Alice');
        container.read(customersControllerProvider.notifier).updateSearch('');

        final filtered = container
            .read(customersControllerProvider)
            .filteredCustomers;
        expect(filtered.length, _testCustomers.length);
      });

      test('returns empty list when no match found', () async {
        when(() => getCustomers()).thenAnswer((_) async => _testCustomers);

        final container = makeContainer(
          getCustomers: getCustomers,
          createCustomer: createCustomer,
        );
        addTearDown(container.dispose);

        await container.read(customersControllerProvider.notifier).refresh();
        container
            .read(customersControllerProvider.notifier)
            .updateSearch('zzznomatch');

        final filtered = container
            .read(customersControllerProvider)
            .filteredCustomers;
        expect(filtered, isEmpty);
      });
    });

    group('create customer', () {
      test('creates customer and refreshes list', () async {
        when(() => getCustomers()).thenAnswer((_) async => _testCustomers);
        when(
          () => createCustomer(
            name: any(named: 'name'),
            phoneNumber: any(named: 'phoneNumber'),
            address: any(named: 'address'),
            isActive: any(named: 'isActive'),
          ),
        ).thenAnswer(
          (_) async => const Customer(
            customerId: 'cust-10',
            name: 'New Customer',
            phoneNumber: '9876543210',
            isActive: true,
          ),
        );

        final container = makeContainer(
          getCustomers: getCustomers,
          createCustomer: createCustomer,
        );
        addTearDown(container.dispose);

        final controller = container.read(customersControllerProvider.notifier);

        final result = await controller.createCustomer(
          name: 'New Customer',
          phoneNumber: '9876543210',
          address: 'Mumbai',
          isActive: true,
        );

        expect(result, true);
        final state = container.read(customersControllerProvider);
        expect(state.isSubmitting, false);
        expect(state.submitFailure, isNull);
        verify(
          () => createCustomer(
            name: 'New Customer',
            phoneNumber: '9876543210',
            address: 'Mumbai',
            isActive: true,
          ),
        ).called(1);
        verify(() => getCustomers()).called(greaterThanOrEqualTo(1));
      });

      test('stores submit failure when create throws AppException', () async {
        when(() => getCustomers()).thenAnswer((_) async => _testCustomers);
        when(
          () => createCustomer(
            name: any(named: 'name'),
            phoneNumber: any(named: 'phoneNumber'),
            address: any(named: 'address'),
            isActive: any(named: 'isActive'),
          ),
        ).thenThrow(
          AppException(
            failure: const Failure.validation(message: 'invalid'),
          ),
        );

        final container = makeContainer(
          getCustomers: getCustomers,
          createCustomer: createCustomer,
        );
        addTearDown(container.dispose);

        final controller = container.read(customersControllerProvider.notifier);

        final result = await controller.createCustomer(
          name: 'New Customer',
          phoneNumber: '9876543210',
          address: 'Mumbai',
          isActive: true,
        );

        expect(result, false);
        final state = container.read(customersControllerProvider);
        expect(state.isSubmitting, false);
        expect(state.submitFailure, isA<ValidationFailure>());
      });

      test('ignores duplicate submissions', () async {
        when(() => getCustomers()).thenAnswer((_) async => _testCustomers);

        final container = makeContainer(
          getCustomers: getCustomers,
          createCustomer: createCustomer,
        );
        addTearDown(container.dispose);

        final controller = container.read(customersControllerProvider.notifier)
          ..state = const CustomersState(isSubmitting: true);

        final result = await controller.createCustomer(
          name: 'New Customer',
          phoneNumber: '9876543210',
          address: 'Mumbai',
          isActive: true,
        );

        expect(result, false);
        verifyNever(
          () => createCustomer(
            name: any(named: 'name'),
            phoneNumber: any(named: 'phoneNumber'),
            address: any(named: 'address'),
            isActive: any(named: 'isActive'),
          ),
        );
      });
    });
  });
}
