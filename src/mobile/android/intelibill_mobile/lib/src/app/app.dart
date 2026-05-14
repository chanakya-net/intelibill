import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';

class IntelibillApp extends ConsumerWidget {
  const IntelibillApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final goRouter = ref.watch(goRouterProvider);

    return MaterialApp.router(
      title: 'Intelibill',
      theme: AppTheme.lightTheme,
      routerConfig: goRouter,
    );
  }
}
