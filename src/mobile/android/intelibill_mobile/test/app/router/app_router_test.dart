import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';

import 'package:intelibill_mobile/src/app/router/app_router.dart';

void main() {
  group('AppRouter', () {
    test('goRouterProvider returns a GoRouter instance', () {
      final container = ProviderContainer();
      final router = container.read(goRouterProvider);
      expect(router, isA<GoRouter>());
    });

    test('goRouter has root route at /', () {
      final container = ProviderContainer();
      final router = container.read(goRouterProvider);
      expect(router.routeInformationProvider.value.uri.path, equals('/'));
    });

    test('AppRoutes.root constant is /', () {
      expect(AppRoutes.root, equals('/'));
    });
  });
}
