import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/shops/presentation/widgets/bank_details_form.dart';

void main() {
  late AppLocalizations l10n;

  setUp(() async {
    l10n = await AppLocalizations.delegate.load(const Locale('en'));
  });

  testWidgets('renders all bank detail fields', (tester) async {
    final formKey = GlobalKey<FormState>();

    await tester.pumpWidget(
      MaterialApp(
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: AppLocalizations.supportedLocales,
        home: Scaffold(body: BankDetailsForm(formKey: formKey)),
      ),
    );

    expect(find.byKey(BankDetailsForm.bankNameFieldKey), findsOneWidget);
    expect(find.byKey(BankDetailsForm.accountNumberFieldKey), findsOneWidget);
    expect(find.byKey(BankDetailsForm.accountTypeFieldKey), findsOneWidget);
    expect(find.byKey(BankDetailsForm.ifscCodeFieldKey), findsOneWidget);
    expect(
      find.byKey(BankDetailsForm.accountHolderNameFieldKey),
      findsOneWidget,
    );
  });

  testWidgets('validates required fields and IFSC pattern', (tester) async {
    final formKey = GlobalKey<FormState>();

    await tester.pumpWidget(
      MaterialApp(
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: AppLocalizations.supportedLocales,
        home: Scaffold(body: BankDetailsForm(formKey: formKey)),
      ),
    );

    formKey.currentState!.validate();
    await tester.pump();

    expect(find.text(l10n.shopsCreateBankNameRequired), findsOneWidget);
    expect(find.text(l10n.shopsCreateAccountNumberRequired), findsOneWidget);
    expect(find.text(l10n.shopsCreateAccountTypeRequired), findsOneWidget);
    expect(find.text(l10n.shopsCreateIfscCodeInvalid), findsOneWidget);
    expect(
      find.text(l10n.shopsCreateAccountHolderNameRequired),
      findsOneWidget,
    );

    await tester.enterText(
      find.byKey(BankDetailsForm.bankNameFieldKey),
      'State Bank',
    );
    await tester.enterText(
      find.byKey(BankDetailsForm.accountNumberFieldKey),
      '12345678',
    );
    await tester.tap(find.byKey(BankDetailsForm.accountTypeFieldKey));
    await tester.pumpAndSettle();
    await tester.tap(find.text(l10n.shopsCreateAccountTypeSavings).last);
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(BankDetailsForm.ifscCodeFieldKey),
      'ABCD0123456',
    );
    await tester.enterText(
      find.byKey(BankDetailsForm.accountHolderNameFieldKey),
      'John Doe',
    );

    formKey.currentState!.validate();
    await tester.pump();

    expect(find.text(l10n.shopsCreateIfscCodeInvalid), findsNothing);
  });
}
