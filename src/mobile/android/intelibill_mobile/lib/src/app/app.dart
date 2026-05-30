import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/core/localization/locale_controller.dart';

class IntelibillApp extends ConsumerWidget {
  const IntelibillApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final goRouter = ref.watch(goRouterProvider);
    final localeState = ref.watch(localeControllerProvider);
    final currentLocale = localeState.maybeWhen(
      data: (value) => value,
      orElse: () => intelibillDefaultLocale,
    );

    return MaterialApp.router(
      title: 'Intelibill',
      theme: AppTheme.lightTheme,
      locale: currentLocale,
      supportedLocales: intelibillSupportedLocales,
      localizationsDelegates: const [
        AppLocalizations.delegate,
        GlobalMaterialLocalizations.delegate,
        GlobalWidgetsLocalizations.delegate,
        GlobalCupertinoLocalizations.delegate,
      ],
      routerConfig: goRouter,
    );
  }
}
