import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/customers/domain/entities/customer.dart';
import 'package:intelibill_mobile/src/features/customers/domain/use_cases/create_customer.dart';
import 'package:intelibill_mobile/src/features/customers/domain/use_cases/get_customers.dart';
import 'package:intelibill_mobile/src/features/customers/presentation/controllers/customers_controller.dart';
import 'package:intelibill_mobile/src/features/customers/presentation/widgets/create_customer_sheet.dart';
import 'package:mocktail/mocktail.dart';

class MockGetCustomers extends Mock implements GetCustomers {}

class MockCreateCustomer extends Mock implements CreateCustomer {}

void main() {
  late MockGetCustomers getCustomers;
  late MockCreateCustomer createCustomer;

  setUp(() {
    getCustomers = MockGetCustomers();
    createCustomer = MockCreateCustomer();
  });

  Widget buildSheet() {
    return ProviderScope(
      overrides: [
        getCustomersUseCaseProvider.overrideWithValue(getCustomers),
        createCustomerUseCaseProvider.overrideWithValue(createCustomer),
      ],
      child: const MaterialApp(
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: AppLocalizations.supportedLocales,
        home: Scaffold(body: CreateCustomerSheet()),
      ),
    );
  }

  testWidgets('validates required and phone pattern', (tester) async {
    when(() => getCustomers()).thenAnswer((_) async => []);
    when(
      () => createCustomer(
        name: any(named: 'name'),
        phoneNumber: any(named: 'phoneNumber'),
        address: any(named: 'address'),
        isActive: any(named: 'isActive'),
      ),
    ).thenAnswer(
      (_) async => const Customer(
        customerId: 'cust-1',
        name: 'Alice',
        phoneNumber: '9876543210',
        isActive: true,
      ),
    );

    await tester.pumpWidget(buildSheet());
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(CreateCustomerSheet.submitButtonKey));
    await tester.pumpAndSettle();

    expect(find.text('Name is required.'), findsOneWidget);
    expect(find.text('Phone number is required.'), findsOneWidget);

    await tester.enterText(
      find.byKey(CreateCustomerSheet.nameFieldKey),
      'Alice',
    );
    await tester.enterText(
      find.byKey(CreateCustomerSheet.phoneFieldKey),
      '123',
    );
    await tester.tap(find.byKey(CreateCustomerSheet.submitButtonKey));
    await tester.pumpAndSettle();

    expect(find.text('Enter a valid phone number.'), findsOneWidget);
  });

  testWidgets('validates address length', (tester) async {
    when(() => getCustomers()).thenAnswer((_) async => []);

    await tester.pumpWidget(buildSheet());
    await tester.pumpAndSettle();

    final longAddress = List.filled(321, 'a').join();
    await tester.enterText(
      find.byKey(CreateCustomerSheet.nameFieldKey),
      'Alice',
    );
    await tester.enterText(
      find.byKey(CreateCustomerSheet.phoneFieldKey),
      '+919876543210',
    );
    await tester.enterText(
      find.byKey(CreateCustomerSheet.addressFieldKey),
      longAddress,
    );
    await tester.tap(find.byKey(CreateCustomerSheet.submitButtonKey));
    await tester.pumpAndSettle();

    expect(
      find.text('Address must be 320 characters or fewer.'),
      findsOneWidget,
    );
  });

  testWidgets('keeps sheet open and shows failure message', (tester) async {
    when(() => getCustomers()).thenAnswer((_) async => []);
    when(
      () => createCustomer(
        name: any(named: 'name'),
        phoneNumber: any(named: 'phoneNumber'),
        address: any(named: 'address'),
        isActive: any(named: 'isActive'),
      ),
    ).thenThrow(
      AppException(
        failure: const Failure.validation(message: 'Invalid payload'),
      ),
    );

    await tester.pumpWidget(buildSheet());
    await tester.pumpAndSettle();

    await tester.enterText(
      find.byKey(CreateCustomerSheet.nameFieldKey),
      'Alice',
    );
    await tester.enterText(
      find.byKey(CreateCustomerSheet.phoneFieldKey),
      '+919876543210',
    );
    await tester.tap(find.byKey(CreateCustomerSheet.submitButtonKey));
    await tester.pumpAndSettle();

    expect(find.text('Invalid payload'), findsOneWidget);
    expect(find.byKey(CreateCustomerSheet.nameFieldKey), findsOneWidget);
  });
}
