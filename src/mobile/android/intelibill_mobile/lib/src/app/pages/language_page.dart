import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/core/localization/locale_controller.dart';

class LanguagePage extends ConsumerWidget {
  const LanguagePage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context)!;
    final localeState = ref.watch(localeControllerProvider);
    final currentLocale = localeState.maybeWhen(
      data: (value) => value,
      orElse: () => intelibillDefaultLocale,
    );

    return Scaffold(
      appBar: AppBar(
        title: Text(l10n.shellLanguage),
      ),
      body: ListView.separated(
        itemCount: intelibillSupportedLocales.length,
        separatorBuilder: (_, index) => const Divider(height: 1),
        itemBuilder: (context, index) {
          final locale = intelibillSupportedLocales[index];
          final isSelected = _isSameLocale(locale, currentLocale);

          return ListTile(
            title: Text(_labelForLocale(l10n, locale)),
            trailing: isSelected ? const Icon(Icons.check) : null,
            onTap: () async {
              await ref
                  .read(localeControllerProvider.notifier)
                  .setLocale(locale);
              if (context.mounted) {
                if (context.canPop()) {
                  context.pop();
                } else {
                  context.go(AppRoutes.dashboard);
                }
              }
            },
          );
        },
      ),
    );
  }
}

bool _isSameLocale(Locale candidate, Locale selected) {
  return candidate.languageCode == selected.languageCode &&
      candidate.countryCode == selected.countryCode;
}

String _labelForLocale(AppLocalizations l10n, Locale locale) {
  final tag = '${locale.languageCode}-${locale.countryCode}';
  switch (tag) {
    case 'en-IN':
      return l10n.languageEnIn;
    case 'hi-IN':
      return l10n.languageHiIn;
    case 'ta-IN':
      return l10n.languageTaIn;
    case 'te-IN':
      return l10n.languageTeIn;
    case 'bn-IN':
      return l10n.languageBnIn;
    case 'ml-IN':
      return l10n.languageMlIn;
    case 'kn-IN':
      return l10n.languageKnIn;
    case 'mr-IN':
      return l10n.languageMrIn;
    case 'gu-IN':
      return l10n.languageGuIn;
  }

  return tag;
}
