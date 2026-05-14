import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/entities/supplier.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/use_cases/create_supplier.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/use_cases/get_suppliers.dart';
import 'package:intelibill_mobile/src/features/suppliers/presentation/controllers/suppliers_controller.dart';
import 'package:intelibill_mobile/src/features/suppliers/presentation/widgets/create_supplier_sheet.dart';
import 'package:mocktail/mocktail.dart';

class MockGetSuppliers extends Mock implements GetSuppliers {}

class MockCreateSupplier extends Mock implements CreateSupplier {}

void main() {
  late MockGetSuppliers getSuppliers;
  late MockCreateSupplier createSupplier;

  setUp(() {
    getSuppliers = MockGetSuppliers();
    createSupplier = MockCreateSupplier();
  });

  Widget buildSheet() {
    return ProviderScope(
      overrides: [
        getSuppliersUseCaseProvider.overrideWithValue(getSuppliers),
        createSupplierUseCaseProvider.overrideWithValue(createSupplier),
      ],
      child: const MaterialApp(
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: AppLocalizations.supportedLocales,
        home: Scaffold(
          body: CreateSupplierSheet(),
        ),
      ),
    );
  }

  testWidgets('validates required fields and phone pattern', (tester) async {
    when(() => getSuppliers()).thenAnswer((_) async => []);

    await tester.pumpWidget(buildSheet());
    await tester.pumpAndSettle();

    await tester.ensureVisible(find.byKey(CreateSupplierSheet.submitButtonKey));
    await tester.tap(
      find.byKey(CreateSupplierSheet.submitButtonKey),
      warnIfMissed: false,
    );
    await tester.pumpAndSettle();

    expect(find.text('Name is required.'), findsOneWidget);
    expect(find.text('Address is required.'), findsOneWidget);
    expect(find.text('City is required.'), findsOneWidget);
    expect(find.text('State is required.'), findsOneWidget);
    expect(find.text('PIN is required.'), findsOneWidget);

    await tester.enterText(
      find.byKey(CreateSupplierSheet.nameFieldKey),
      'ABC Traders',
    );
    await tester.enterText(
      find.byKey(CreateSupplierSheet.addressFieldKey),
      '12 Main Street',
    );
    await tester.enterText(
      find.byKey(CreateSupplierSheet.cityFieldKey),
      'Mumbai',
    );
    await tester.enterText(
      find.byKey(CreateSupplierSheet.stateFieldKey),
      'Maharashtra',
    );
    await tester.enterText(
      find.byKey(CreateSupplierSheet.pinFieldKey),
      '400001',
    );
    await tester.enterText(
      find.byKey(CreateSupplierSheet.contactPhoneFieldKey),
      '123',
    );
    await tester.ensureVisible(find.byKey(CreateSupplierSheet.submitButtonKey));
    await tester.tap(
      find.byKey(CreateSupplierSheet.submitButtonKey),
      warnIfMissed: false,
    );
    await tester.pumpAndSettle();

    expect(find.text('Enter a valid phone number.'), findsOneWidget);
  });

  testWidgets('validates max lengths', (tester) async {
    when(() => getSuppliers()).thenAnswer((_) async => []);

    await tester.pumpWidget(buildSheet());
    await tester.pumpAndSettle();

    await tester.enterText(
      find.byKey(CreateSupplierSheet.nameFieldKey),
      List.filled(181, 'a').join(),
    );
    await tester.enterText(
      find.byKey(CreateSupplierSheet.contactPersonFieldKey),
      List.filled(121, 'b').join(),
    );
    await tester.enterText(
      find.byKey(CreateSupplierSheet.contactPhoneFieldKey),
      List.filled(33, '1').join(),
    );
    await tester.enterText(
      find.byKey(CreateSupplierSheet.addressFieldKey),
      List.filled(321, 'c').join(),
    );
    await tester.enterText(
      find.byKey(CreateSupplierSheet.cityFieldKey),
      List.filled(121, 'd').join(),
    );
    await tester.enterText(
      find.byKey(CreateSupplierSheet.stateFieldKey),
      List.filled(121, 'e').join(),
    );
    await tester.enterText(
      find.byKey(CreateSupplierSheet.pinFieldKey),
      List.filled(17, '2').join(),
    );

    await tester.ensureVisible(find.byKey(CreateSupplierSheet.submitButtonKey));
    await tester.tap(
      find.byKey(CreateSupplierSheet.submitButtonKey),
      warnIfMissed: false,
    );
    await tester.pumpAndSettle();

    expect(find.text('Name must be 180 characters or fewer.'), findsOneWidget);
    expect(
      find.text('Contact person must be 120 characters or fewer.'),
      findsOneWidget,
    );
    expect(
      find.text('Contact phone must be 32 characters or fewer.'),
      findsOneWidget,
    );
    expect(
      find.text('Address must be 320 characters or fewer.'),
      findsOneWidget,
    );
    expect(find.text('City must be 120 characters or fewer.'), findsOneWidget);
    expect(find.text('State must be 120 characters or fewer.'), findsOneWidget);
    expect(find.text('PIN must be 16 characters or fewer.'), findsOneWidget);
  });

  testWidgets('keeps sheet open and shows failure message', (tester) async {
    when(() => getSuppliers()).thenAnswer((_) async => []);
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
        failure: const Failure.validation(message: 'Invalid payload'),
      ),
    );

    await tester.pumpWidget(buildSheet());
    await tester.pumpAndSettle();

    await tester.enterText(
      find.byKey(CreateSupplierSheet.nameFieldKey),
      'ABC Traders',
    );
    await tester.enterText(
      find.byKey(CreateSupplierSheet.addressFieldKey),
      '12 Main Street',
    );
    await tester.enterText(
      find.byKey(CreateSupplierSheet.cityFieldKey),
      'Mumbai',
    );
    await tester.enterText(
      find.byKey(CreateSupplierSheet.stateFieldKey),
      'Maharashtra',
    );
    await tester.enterText(
      find.byKey(CreateSupplierSheet.pinFieldKey),
      '400001',
    );

    await tester.ensureVisible(find.byKey(CreateSupplierSheet.submitButtonKey));
    await tester.tap(
      find.byKey(CreateSupplierSheet.submitButtonKey),
      warnIfMissed: false,
    );
    await tester.pumpAndSettle();

    expect(find.text('Invalid payload'), findsOneWidget);
    expect(find.byKey(CreateSupplierSheet.nameFieldKey), findsOneWidget);
  });

  testWidgets('submits trimmed values and null optional contact fields', (
    tester,
  ) async {
    when(() => getSuppliers()).thenAnswer((_) async => []);
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
    ).thenAnswer(
      (_) async => const Supplier(
        supplierId: 'sup-1',
        name: 'ABC Traders',
        address: '12 Main Street',
        city: 'Mumbai',
        state: 'Maharashtra',
        pin: '400001',
        isSystem: false,
        isActive: true,
        isPreferred: false,
        balanceDue: 0,
      ),
    );

    await tester.pumpWidget(buildSheet());
    await tester.pumpAndSettle();

    await tester.enterText(
      find.byKey(CreateSupplierSheet.nameFieldKey),
      '  ABC Traders  ',
    );
    await tester.enterText(
      find.byKey(CreateSupplierSheet.contactPersonFieldKey),
      '   ',
    );
    await tester.enterText(
      find.byKey(CreateSupplierSheet.contactPhoneFieldKey),
      '   ',
    );
    await tester.enterText(
      find.byKey(CreateSupplierSheet.addressFieldKey),
      ' 12 Main Street ',
    );
    await tester.enterText(
      find.byKey(CreateSupplierSheet.cityFieldKey),
      ' Mumbai ',
    );
    await tester.enterText(
      find.byKey(CreateSupplierSheet.stateFieldKey),
      ' Maharashtra ',
    );
    await tester.enterText(
      find.byKey(CreateSupplierSheet.pinFieldKey),
      ' 400001 ',
    );

    await tester.ensureVisible(find.byKey(CreateSupplierSheet.submitButtonKey));
    await tester.tap(
      find.byKey(CreateSupplierSheet.submitButtonKey),
      warnIfMissed: false,
    );
    await tester.pumpAndSettle();

    verify(
      () => createSupplier(
        name: 'ABC Traders',
        address: '12 Main Street',
        city: 'Mumbai',
        state: 'Maharashtra',
        pin: '400001',
        isActive: true,
        isPreferred: false,
      ),
    ).called(1);
  });
}
