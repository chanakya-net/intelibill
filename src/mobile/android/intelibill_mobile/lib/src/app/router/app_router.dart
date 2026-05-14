import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import 'package:intelibill_mobile/src/app/pages/placeholder_page.dart';
import 'package:intelibill_mobile/src/features/app_status/presentation/pages/app_status_page.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/pages/login_page.dart';

class AppRoutes {
  static const String root = '/';
  static const String login = '/login';
  static const String forgotPassword = '/forgot-password';
  static const String register = '/register';
}

final goRouterProvider = Provider<GoRouter>((ref) {
  return GoRouter(
    initialLocation: AppRoutes.root,
    redirect: (context, state) async {
      final authState = await ref.watch(authControllerProvider.future);
      final isAuthenticated = authState.isAuthenticated;
      final isLoggingIn = state.matchedLocation == AppRoutes.login;

      if (!isAuthenticated && !isLoggingIn) {
        // Redirect unauthenticated users to login
        return AppRoutes.login;
      }

      if (isAuthenticated && isLoggingIn) {
        // Redirect authenticated users away from login to home
        return AppRoutes.root;
      }

      return null;
    },
    routes: [
      GoRoute(
        path: AppRoutes.root,
        builder: (context, state) => const AppStatusPage(),
      ),
      GoRoute(
        path: AppRoutes.login,
        builder: (context, state) => const LoginPage(),
      ),
      GoRoute(
        path: AppRoutes.forgotPassword,
        builder: (context, state) => const PlaceholderPage(),
      ),
      GoRoute(
        path: AppRoutes.register,
        builder: (context, state) => const PlaceholderPage(),
      ),
    ],
  );
});
