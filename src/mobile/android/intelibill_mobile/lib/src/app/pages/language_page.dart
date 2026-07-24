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
        actions: [
          IconButton(
            tooltip: l10n.shellProfile,
            icon: const Icon(Icons.account_circle),
            onPressed: () => context.push(AppRoutes.profile),
          ),
        ],
      ),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          Card(
            clipBehavior: Clip.antiAlias,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                for (
                  var index = 0;
                  index < intelibillSupportedLocales.length;
                  index++
                ) ...[
                  if (index > 0) const Divider(height: 1),
                  _LanguageOptionTile(
                    label: _labelForLocale(
                      l10n,
                      intelibillSupportedLocales[index],
                    ),
                    isSelected: _isSameLocale(
                      intelibillSupportedLocales[index],
                      currentLocale,
                    ),
                    onTap: () async {
                      await ref
                          .read(localeControllerProvider.notifier)
                          .setLocale(intelibillSupportedLocales[index]);
                      if (context.mounted) {
                        if (context.canPop()) {
                          context.pop();
                        } else {
                          context.go(AppRoutes.dashboard);
                        }
                      }
                    },
                  ),
                ],
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _LanguageOptionTile extends StatelessWidget {
  const _LanguageOptionTile({
    required this.label,
    required this.isSelected,
    required this.onTap,
  });

  final String label;
  final bool isSelected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return ListTile(
      contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 4),
      title: Text(
        label,
        style: theme.textTheme.bodyLarge?.copyWith(
          fontWeight: isSelected ? FontWeight.w700 : FontWeight.w500,
          color: isSelected ? colorScheme.primary : colorScheme.onSurface,
        ),
      ),
      trailing: isSelected
          ? Icon(Icons.check_rounded, color: colorScheme.primary)
          : null,
      onTap: onTap,
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
