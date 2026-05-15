import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/shops/presentation/widgets/shop_info_form.dart';

void main() {
  testWidgets('renders all shop info fields', (tester) async {
    final formKey = GlobalKey<FormState>();

    await tester.pumpWidget(
      MaterialApp(
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: AppLocalizations.supportedLocales,
        home: Scaffold(
          body: SingleChildScrollView(
            child: ShopInfoForm(formKey: formKey),
          ),
        ),
      ),
    );

    expect(find.byKey(ShopInfoForm.shopNameFieldKey), findsOneWidget);
    expect(find.byKey(ShopInfoForm.addressFieldKey), findsOneWidget);
    expect(find.byKey(ShopInfoForm.cityFieldKey), findsOneWidget);
    expect(find.byKey(ShopInfoForm.stateFieldKey), findsOneWidget);
    expect(find.byKey(ShopInfoForm.pincodeFieldKey), findsOneWidget);
    expect(find.byKey(ShopInfoForm.contactPersonFieldKey), findsOneWidget);
    expect(find.byKey(ShopInfoForm.mobileNumberFieldKey), findsOneWidget);
    expect(find.byKey(ShopInfoForm.gstNumberFieldKey), findsOneWidget);
  });

  testWidgets('validates required fields and mobile/ GST formats', (tester) async {
    final formKey = GlobalKey<FormState>();

    await tester.pumpWidget(
      MaterialApp(
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: AppLocalizations.supportedLocales,
        home: Scaffold(
          body: SingleChildScrollView(
            child: ShopInfoForm(formKey: formKey),
          ),
        ),
      ),
    );

    formKey.currentState!.validate();
    await tester.pump();

    expect(find.text('Shop Name is required.'), findsOneWidget);
    expect(find.text('Address is required.'), findsOneWidget);
    expect(find.text('City is required.'), findsOneWidget);
    expect(find.text('State is required.'), findsOneWidget);
    expect(find.text('Pincode is required.'), findsOneWidget);

    await tester.enterText(
      find.byKey(ShopInfoForm.shopNameFieldKey),
      'Acme',
    );
    await tester.enterText(
      find.byKey(ShopInfoForm.addressFieldKey),
      'Baker Street',
    );
    await tester.enterText(
      find.byKey(ShopInfoForm.cityFieldKey),
      'London',
    );
    await tester.enterText(
      find.byKey(ShopInfoForm.stateFieldKey),
      'City',
    );
    await tester.enterText(find.byKey(ShopInfoForm.pincodeFieldKey), '123456');
    await tester.enterText(find.byKey(ShopInfoForm.mobileNumberFieldKey), '123');
    await tester.enterText(
      find.byKey(ShopInfoForm.gstNumberFieldKey),
      '12ABCDE1234F1Z1',
    );

    formKey.currentState!.validate();
    await tester.pump();

    expect(find.text('Mobile number must be 10 digits.'), findsOneWidget);
    expect(find.text('Enter a valid GST number.'), findsNothing);
  });
}
