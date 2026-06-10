import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:intelibill_mobile/src/app/shell/mobile_menu_item.dart';
import 'package:intelibill_mobile/src/app/shell/more_menu_sheet.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';

AuthSession _ownerSession() {
  return AuthSession(
    accessToken: 'access-token',
    refreshToken: 'refresh-token',
    accessTokenExpiresAt: DateTime.utc(2026, 5, 15, 10),
    refreshTokenExpiresAt: DateTime.utc(2026, 6, 15, 10),
    user: const AuthUser(
      id: 'user-1',
      email: 'owner@example.com',
      phoneNumber: null,
      firstName: 'Alex',
      lastName: 'Smith',
      language: 'en-IN',
    ),
    activeShopId: 'shop-1',
    shops: const [
      UserShop(
        shopId: 'shop-1',
        shopName: 'Primary Shop',
        role: 'Owner',
        isDefault: true,
        lastUsedAt: null,
      ),
    ],
    rememberMe: false,
  );
}

List<MobileMenuItem> _sampleItems() {
  return moreMenuItems(_ownerSession());
}

Future<void> _pumpSheet(
  WidgetTester tester, {
  required void Function(MobileMenuItem item) onItemTap,
  AuthSession? Function()? sessionBuilder = _ownerSession,
}) async {
  await tester.pumpWidget(
    MaterialApp(
      locale: const Locale('en', 'IN'),
      supportedLocales: const [Locale('en', 'IN')],
      localizationsDelegates: const [
        AppLocalizations.delegate,
        GlobalMaterialLocalizations.delegate,
        GlobalWidgetsLocalizations.delegate,
        GlobalCupertinoLocalizations.delegate,
      ],
      home: MediaQuery(
        data: const MediaQueryData(size: Size(400, 900)),
        child: Scaffold(
          body: MoreMenuSheet(
            items: _sampleItems(),
            session: sessionBuilder?.call(),
            onItemTap: onItemTap,
          ),
        ),
      ),
    ),
  );
  await tester.pumpAndSettle();
}

void main() {
  group('MoreMenuSheet', () {
    testWidgets('shows user header with shop and role', (tester) async {
      await _pumpSheet(tester, onItemTap: (_) {});

      expect(find.text('Alex Smith'), findsOneWidget);
      expect(find.text('Primary Shop'), findsOneWidget);
      expect(find.text('Owner'), findsOneWidget);
      expect(find.text('AS'), findsOneWidget);
    });

    testWidgets('shows section headers for grouped items', (tester) async {
      await _pumpSheet(tester, onItemTap: (_) {});

      for (final header in [
        'Management',
        'Account',
        'Shop',
        'Settings',
      ]) {
        await tester.scrollUntilVisible(find.text(header), 120);
        expect(find.text(header), findsOneWidget);
      }
    });

    testWidgets('invokes callback when menu item is tapped', (tester) async {
      MobileMenuItem? tappedItem;

      await _pumpSheet(
        tester,
        onItemTap: (item) => tappedItem = item,
      );

      await tester.scrollUntilVisible(find.text('Language'), 120);
      await tester.tap(find.text('Language'));
      await tester.pumpAndSettle();

      expect(tappedItem?.labelKey, MobileMenuLabelKey.language);
    });

    testWidgets('logout item uses destructive styling', (tester) async {
      await _pumpSheet(tester, onItemTap: (_) {});

      await tester.scrollUntilVisible(find.text('Logout'), 120);
      final logoutText = tester.widget<Text>(find.text('Logout'));
      final theme = Theme.of(tester.element(find.text('Logout')));
      expect(logoutText.style?.color, theme.colorScheme.error);
    });

    testWidgets('falls back to title when session is missing', (tester) async {
      await _pumpSheet(tester, sessionBuilder: () => null, onItemTap: (_) {});

      expect(find.text('More'), findsOneWidget);
      expect(find.text('Alex Smith'), findsNothing);
    });
  });
}
