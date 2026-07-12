import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/entities/save_bank_account_request.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/presentation/widgets/bank_account_form.dart';

Widget _buildForm({
  required Future<void> Function(SaveBankAccountRequest request) onSubmit,
}) {
  return MaterialApp(
    localizationsDelegates: AppLocalizations.localizationsDelegates,
    supportedLocales: AppLocalizations.supportedLocales,
    home: Scaffold(body: BankAccountForm(onSubmit: onSubmit)),
  );
}

void main() {
  testWidgets('requires bank name, account number, and Savings or Current', (
    tester,
  ) async {
    await tester.pumpWidget(_buildForm(onSubmit: (_) async {}));

    await tester.tap(find.byKey(BankAccountForm.submitButtonKey));
    await tester.pump();

    expect(find.text('Bank name is required.'), findsOneWidget);
    expect(find.text('Account number is required.'), findsOneWidget);
    expect(find.text('Select Savings or Current.'), findsOneWidget);
  });

  testWidgets('uppercases IFSC and sends trimmed optional values', (
    tester,
  ) async {
    SaveBankAccountRequest? submitted;
    await tester.pumpWidget(
      _buildForm(onSubmit: (request) async => submitted = request),
    );

    await tester.enterText(
      find.byKey(BankAccountForm.bankNameFieldKey),
      ' HDFC ',
    );
    await tester.enterText(
      find.byKey(BankAccountForm.accountNumberFieldKey),
      '00123456',
    );
    await tester.tap(find.byKey(BankAccountForm.accountTypeFieldKey));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Current').last);
    await tester.enterText(
      find.byKey(BankAccountForm.ifscCodeFieldKey),
      'hdfc0123456',
    );
    await tester.enterText(
      find.byKey(BankAccountForm.accountHolderNameFieldKey),
      ' Alex ',
    );
    await tester.tap(find.byKey(BankAccountForm.submitButtonKey));
    await tester.pumpAndSettle();

    expect(
      submitted,
      const SaveBankAccountRequest(
        bankName: 'HDFC',
        accountNumber: '00123456',
        accountType: 'Current',
        ifscCode: 'HDFC0123456',
        accountHolderName: 'Alex',
      ),
    );
  });

  testWidgets('rejects a malformed supplied IFSC', (tester) async {
    await tester.pumpWidget(_buildForm(onSubmit: (_) async {}));
    await tester.enterText(find.byKey(BankAccountForm.ifscCodeFieldKey), 'bad');
    await tester.tap(find.byKey(BankAccountForm.submitButtonKey));
    await tester.pump();

    expect(find.text('Enter a valid IFSC code.'), findsOneWidget);
  });
}
