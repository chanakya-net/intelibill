import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/shops/presentation/widgets/shop_info_form.dart';

void main() {
  late AppLocalizations l10n;

  setUp(() async {
    l10n = await AppLocalizations.delegate.load(const Locale('en'));
  });

  testWidgets('renders all shop info fields', (tester) async {
    final formKey = GlobalKey<FormState>();

    await tester.pumpWidget(
      MaterialApp(
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: AppLocalizations.supportedLocales,
        home: Scaffold(
          body: SingleChildScrollView(child: ShopInfoForm(formKey: formKey)),
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

  testWidgets('validates required fields and mobile/ GST formats', (
    tester,
  ) async {
    final formKey = GlobalKey<FormState>();

    await tester.pumpWidget(
      MaterialApp(
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: AppLocalizations.supportedLocales,
        home: Scaffold(
          body: SingleChildScrollView(child: ShopInfoForm(formKey: formKey)),
        ),
      ),
    );

    formKey.currentState!.validate();
    await tester.pump();

    expect(find.text(l10n.shopsCreateShopNameRequired), findsOneWidget);
    expect(find.text(l10n.shopsCreateAddressRequired), findsOneWidget);
    expect(find.text(l10n.shopsCreateCityRequired), findsOneWidget);
    expect(find.text(l10n.shopsCreateStateRequired), findsOneWidget);
    expect(find.text(l10n.shopsCreatePincodeRequired), findsOneWidget);

    await tester.enterText(find.byKey(ShopInfoForm.shopNameFieldKey), 'Acme');
    await tester.enterText(
      find.byKey(ShopInfoForm.addressFieldKey),
      'Baker Street',
    );
    await tester.enterText(find.byKey(ShopInfoForm.cityFieldKey), 'London');
    await tester.enterText(find.byKey(ShopInfoForm.stateFieldKey), 'City');
    await tester.enterText(find.byKey(ShopInfoForm.pincodeFieldKey), '123456');
    await tester.enterText(
      find.byKey(ShopInfoForm.mobileNumberFieldKey),
      '123',
    );
    await tester.enterText(
      find.byKey(ShopInfoForm.gstNumberFieldKey),
      '12ABCDE1234F1Z1',
    );

    formKey.currentState!.validate();
    await tester.pump();

    expect(find.text(l10n.shopsCreateMobileNumberInvalid), findsOneWidget);
    expect(find.text(l10n.shopsCreateGstNumberInvalid), findsNothing);
  });
}
