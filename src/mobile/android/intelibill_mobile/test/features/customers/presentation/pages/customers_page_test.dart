import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/customers/domain/entities/customer.dart';
import 'package:intelibill_mobile/src/features/customers/domain/use_cases/create_customer.dart';
import 'package:intelibill_mobile/src/features/customers/domain/use_cases/get_customers.dart';
import 'package:intelibill_mobile/src/features/customers/presentation/controllers/customers_controller.dart';
import 'package:intelibill_mobile/src/features/customers/presentation/pages/customers_page.dart';
import 'package:intelibill_mobile/src/features/customers/presentation/widgets/create_customer_sheet.dart';
import 'package:mocktail/mocktail.dart';

class _StubCustomersController extends CustomersController {
  _StubCustomersController(this._state);

  final CustomersState _state;

  @override
  CustomersState build() => _state;
}

class MockGetCustomers extends Mock implements GetCustomers {}

class MockCreateCustomer extends Mock implements CreateCustomer {}

const _loadedState = CustomersState(
  customers: [
    Customer(
      customerId: 'cust-1',
      name: 'Alice Sharma',
      phoneNumber: '9876543210',
      address: '12 Main St, Mumbai',
      isActive: true,
      outstandingDue: 200,
    ),
    Customer(
      customerId: 'cust-2',
      name: 'Bob Kumar',
      phoneNumber: '9123456789',
      isActive: false,
    ),
  ],
);

Widget _buildApp(CustomersState state) {
  return ProviderScope(
    overrides: [
      customersControllerProvider.overrideWith(
        () => _StubCustomersController(state),
      ),
    ],
    child: const MaterialApp(
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      home: CustomersPage(),
    ),
  );
}

Widget _buildAppWithOverrides({
  required MockGetCustomers getCustomers,
  required MockCreateCustomer createCustomer,
}) {
  return ProviderScope(
    overrides: [
      getCustomersUseCaseProvider.overrideWithValue(getCustomers),
      createCustomerUseCaseProvider.overrideWithValue(createCustomer),
    ],
    child: const MaterialApp(
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      home: CustomersPage(),
    ),
  );
}

void main() {
  group('CustomersPage', () {
    testWidgets('shows loading indicator when isLoading is true', (
      tester,
    ) async {
      await tester.pumpWidget(
        _buildApp(const CustomersState(isLoading: true)),
      );

      expect(find.byType(CircularProgressIndicator), findsOneWidget);
    });

    testWidgets('shows empty state when customer list is empty', (
      tester,
    ) async {
      await tester.pumpWidget(
        _buildApp(const CustomersState()),
      );
      await tester.pumpAndSettle();

      expect(find.text('No customers found'), findsOneWidget);
    });

    testWidgets('shows error state with retry button when error is set', (
      tester,
    ) async {
      await tester.pumpWidget(
        _buildApp(
          const CustomersState(failure: NetworkFailure()),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Unable to load customers'), findsOneWidget);
      expect(
        find.text('Unable to connect. Please check your network.'),
        findsOneWidget,
      );
      expect(find.widgetWithText(FilledButton, 'Retry'), findsOneWidget);
    });

    testWidgets('shows customer cards when customers are loaded', (
      tester,
    ) async {
      await tester.pumpWidget(_buildApp(_loadedState));
      await tester.pumpAndSettle();

      expect(find.text('Alice Sharma'), findsOneWidget);
      expect(find.text('9876543210'), findsOneWidget);
      expect(find.text('12 Main St, Mumbai'), findsOneWidget);
      expect(find.text('Bob Kumar'), findsOneWidget);
      expect(find.text('9123456789'), findsOneWidget);
    });

    testWidgets('shows outstanding due when greater than zero', (
      tester,
    ) async {
      await tester.pumpWidget(_buildApp(_loadedState));
      await tester.pumpAndSettle();

      expect(find.textContaining('₹200.00'), findsOneWidget);
    });

    testWidgets('shows inactive label for inactive customers', (tester) async {
      await tester.pumpWidget(_buildApp(_loadedState));
      await tester.pumpAndSettle();

      expect(find.text('Inactive'), findsOneWidget);
    });

    testWidgets('shows search field', (tester) async {
      await tester.pumpWidget(_buildApp(_loadedState));
      await tester.pumpAndSettle();

      expect(find.byType(TextField), findsOneWidget);
    });

    testWidgets('shows title from localization', (tester) async {
      await tester.pumpWidget(_buildApp(_loadedState));
      await tester.pumpAndSettle();

      expect(find.text('Customers'), findsWidgets);
    });

    testWidgets('shows RefreshIndicator in loaded state', (tester) async {
      await tester.pumpWidget(_buildApp(_loadedState));
      await tester.pumpAndSettle();

      expect(find.byType(RefreshIndicator), findsOneWidget);
    });

    testWidgets('pull-to-refresh calls refresh on controller', (tester) async {
      var refreshCount = 0;

      final controller = _CountingRefreshController(_loadedState, () {
        refreshCount++;
      });

      await tester.pumpWidget(
        ProviderScope(
          overrides: [
            customersControllerProvider.overrideWith(() => controller),
          ],
          child: const MaterialApp(
            localizationsDelegates: AppLocalizations.localizationsDelegates,
            supportedLocales: AppLocalizations.supportedLocales,
            home: CustomersPage(),
          ),
        ),
      );
      await tester.pumpAndSettle();

      await tester.fling(
        find.byType(ListView),
        const Offset(0, 300),
        1000,
      );
      await tester.pumpAndSettle();

      expect(refreshCount, greaterThanOrEqualTo(1));
    });

    testWidgets('pull-to-refresh works in empty state', (tester) async {
      var refreshCount = 0;

      final controller = _CountingRefreshController(
        const CustomersState(),
        () {
          refreshCount++;
        },
      );

      await tester.pumpWidget(
        ProviderScope(
          overrides: [
            customersControllerProvider.overrideWith(() => controller),
          ],
          child: const MaterialApp(
            localizationsDelegates: AppLocalizations.localizationsDelegates,
            supportedLocales: AppLocalizations.supportedLocales,
            home: CustomersPage(),
          ),
        ),
      );
      await tester.pumpAndSettle();

      await tester.fling(
        find.byType(ListView),
        const Offset(0, 300),
        1000,
      );
      await tester.pumpAndSettle();

      expect(refreshCount, greaterThanOrEqualTo(1));
    });

    testWidgets('filters shown customers based on search query', (
      tester,
    ) async {
      final searchState = CustomersState(
        customers: _loadedState.customers,
        searchQuery: 'Alice',
      );

      await tester.pumpWidget(_buildApp(searchState));
      await tester.pumpAndSettle();

      expect(find.text('Alice Sharma'), findsOneWidget);
      expect(find.text('Bob Kumar'), findsNothing);
    });

    testWidgets('creates customer and shows success snackbar', (tester) async {
      final getCustomers = MockGetCustomers();
      final createCustomer = MockCreateCustomer();

      when(getCustomers.call).thenAnswer((_) async => _loadedState.customers);
      when(
        () => createCustomer(
          name: any(named: 'name'),
          phoneNumber: any(named: 'phoneNumber'),
          address: any(named: 'address'),
          isActive: any(named: 'isActive'),
        ),
      ).thenAnswer(
        (_) async => const Customer(
          customerId: 'cust-3',
          name: 'New Customer',
          phoneNumber: '9876543210',
          isActive: true,
        ),
      );

      await tester.pumpWidget(
        _buildAppWithOverrides(
          getCustomers: getCustomers,
          createCustomer: createCustomer,
        ),
      );
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(CustomersPage.addCustomerFabKey));
      await tester.pumpAndSettle();

      await tester.enterText(
        find.byKey(CreateCustomerSheet.nameFieldKey),
        'New Customer',
      );
      await tester.enterText(
        find.byKey(CreateCustomerSheet.phoneFieldKey),
        '9876543210',
      );

      await tester.tap(find.byKey(CreateCustomerSheet.submitButtonKey));
      await tester.pumpAndSettle();

      expect(find.text('Customer created successfully.'), findsOneWidget);
      expect(find.byKey(CreateCustomerSheet.nameFieldKey), findsNothing);
      verify(getCustomers.call).called(greaterThanOrEqualTo(2));
    });
  });
}

class _CountingRefreshController extends CustomersController {
  _CountingRefreshController(this._state, this._onRefresh);

  final CustomersState _state;
  final VoidCallback _onRefresh;

  @override
  CustomersState build() => _state;

  @override
  Future<void> refresh() async {
    _onRefresh();
  }
}
