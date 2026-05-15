import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/shops/presentation/widgets/bank_details_form.dart';

void main() {
  testWidgets('renders all bank detail fields', (tester) async {
    final formKey = GlobalKey<FormState>();

    await tester.pumpWidget(
      MaterialApp(
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: AppLocalizations.supportedLocales,
        home: Scaffold(
          body: BankDetailsForm(formKey: formKey),
        ),
      ),
    );

    expect(find.byKey(BankDetailsForm.bankNameFieldKey), findsOneWidget);
    expect(find.byKey(BankDetailsForm.accountNumberFieldKey), findsOneWidget);
    expect(find.byKey(BankDetailsForm.accountTypeFieldKey), findsOneWidget);
    expect(find.byKey(BankDetailsForm.ifscCodeFieldKey), findsOneWidget);
    expect(find.byKey(BankDetailsForm.accountHolderNameFieldKey), findsOneWidget);
  });

  testWidgets('validates required fields and IFSC pattern', (tester) async {
    final formKey = GlobalKey<FormState>();

    await tester.pumpWidget(
      MaterialApp(
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: AppLocalizations.supportedLocales,
        home: Scaffold(
          body: BankDetailsForm(formKey: formKey),
        ),
      ),
    );

    formKey.currentState!.validate();
    await tester.pump();

    expect(find.text('Bank Name is required.'), findsOneWidget);
    expect(find.text('Account Number is required.'), findsOneWidget);
    expect(find.text('Account type is required.'), findsOneWidget);
    expect(find.text('Enter a valid IFSC code.'), findsOneWidget);
    expect(find.text('Account Holder Name is required.'), findsOneWidget);

    await tester.enterText(find.byKey(BankDetailsForm.bankNameFieldKey), 'State Bank');
    await tester.enterText(
      find.byKey(BankDetailsForm.accountNumberFieldKey),
      '12345678',
    );
    await tester.tap(find.byKey(BankDetailsForm.accountTypeFieldKey));
    await tester.pumpAndSettle();
    await tester.tap(find.text(BankDetailsForm.accountTypeSavings).last);
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(BankDetailsForm.ifscCodeFieldKey), 'ABCD0123456');
    await tester.enterText(
      find.byKey(BankDetailsForm.accountHolderNameFieldKey),
      'John Doe',
    );

    formKey.currentState!.validate();
    await tester.pump();

    expect(find.text('Enter a valid IFSC code.'), findsNothing);
  });
}
