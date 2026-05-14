import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import 'package:intelibill_mobile/src/app/pages/placeholder_page.dart';

class AppRoutes {
  static const String root = '/';
}

final goRouterProvider = Provider<GoRouter>((ref) {
  return GoRouter(
    initialLocation: AppRoutes.root,
    redirect: (context, state) async {
      // TODO(auth): Add auth guard logic here
      return null;
    },
    routes: [
      GoRoute(
        path: AppRoutes.root,
        builder: (context, state) => const PlaceholderPage(),
      ),
    ],
  );
});
