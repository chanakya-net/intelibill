import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/shell/mobile_menu_item.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';

class MobileAppShell extends ConsumerWidget {
  const MobileAppShell({required this.child, super.key});

  final Widget child;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context)!;
    final session = ref.watch(authControllerProvider).value?.session;
    final primaryItems = primaryNavigationItems(session);
    final moreItems = moreMenuItems(session);
    final selectedIndex = _resolveSelectedIndex(
      context,
      primaryItems,
      moreItems,
    );

    return Scaffold(
      body: child,
      bottomNavigationBar: primaryItems.length >= 2
          ? NavigationBar(
              selectedIndex: selectedIndex,
              labelBehavior: NavigationDestinationLabelBehavior.alwaysShow,
              destinations: [
                for (final item in primaryItems)
                  NavigationDestination(
                    icon: Icon(item.icon),
                    label: item.labelKey.label(l10n),
                  ),
              ],
              onDestinationSelected: (index) {
                _handlePrimarySelection(
                  context,
                  ref,
                  l10n,
                  primaryItems[index],
                  moreItems,
                );
              },
            )
          : null,
    );
  }
}

int _resolveSelectedIndex(
  BuildContext context,
  List<MobileMenuItem> primaryItems,
  List<MobileMenuItem> moreItems,
) {
  final location = GoRouterState.of(context).uri.toString();
  final moreIndex = primaryItems.indexWhere(
    (item) =>
        item.destination is MobileMenuAction &&
        (item.destination as MobileMenuAction).type ==
            MobileMenuActionType.openMoreMenu,
  );

  for (var index = 0; index < primaryItems.length; index++) {
    final item = primaryItems[index];
    final destination = item.destination;
    if (destination is MobileMenuRoute) {
      final matchPrefix = destination.matchPrefix ?? destination.route;
      if (_matchesLocation(location, matchPrefix)) {
        return index;
      }
    }
  }

  if (moreIndex >= 0 && _matchesMoreMenu(location, moreItems)) {
    return moreIndex;
  }

  return moreIndex >= 0 ? moreIndex : 0;
}

bool _matchesMoreMenu(String location, List<MobileMenuItem> moreItems) {
  for (final item in moreItems) {
    final destination = item.destination;
    if (destination is MobileMenuRoute) {
      final matchPrefix = destination.matchPrefix ?? destination.route;
      if (_matchesLocation(location, matchPrefix)) {
        return true;
      }
    }
  }

  return false;
}

bool _matchesLocation(String location, String matchPrefix) {
  if (location == matchPrefix) {
    return true;
  }

  return location.startsWith('$matchPrefix/');
}

void _handlePrimarySelection(
  BuildContext context,
  WidgetRef ref,
  AppLocalizations l10n,
  MobileMenuItem item,
  List<MobileMenuItem> moreItems,
) {
  final destination = item.destination;
  if (destination is MobileMenuRoute) {
    final extra = destination.extraBuilder?.call(l10n);
    context.go(destination.route, extra: extra);
    return;
  }

  if (destination is MobileMenuAction) {
    if (destination.type == MobileMenuActionType.openMoreMenu) {
      unawaited(_showMoreMenu(context, ref, l10n, moreItems));
      return;
    }
    if (destination.type == MobileMenuActionType.logout) {
      unawaited(ref.read(authControllerProvider.notifier).signOut());
    }
  }
}

Future<void> _showMoreMenu(
  BuildContext context,
  WidgetRef ref,
  AppLocalizations l10n,
  List<MobileMenuItem> items,
) {
  return showModalBottomSheet<void>(
    context: context,
    builder: (sheetContext) {
      final theme = Theme.of(sheetContext);
      final menuItems = items;
      return SafeArea(
        child: ListView(
          shrinkWrap: true,
          padding: const EdgeInsets.symmetric(vertical: 8),
          children: [
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
              child: Text(l10n.shellMore, style: theme.textTheme.titleMedium),
            ),
            const Divider(height: 1),
            ..._buildMoreMenuTiles(context, sheetContext, ref, l10n, menuItems),
          ],
        ),
      );
    },
  );
}

List<Widget> _buildMoreMenuTiles(
  BuildContext context,
  BuildContext sheetContext,
  WidgetRef ref,
  AppLocalizations l10n,
  List<MobileMenuItem> items,
) {
  final tiles = <Widget>[];
  MobileMenuSection? lastSection;

  for (final item in items) {
    if (lastSection != null && item.section != lastSection) {
      tiles.add(const Divider(height: 1));
    }
    lastSection = item.section;

    tiles.add(
      ListTile(
        leading: Icon(item.icon),
        title: Text(item.labelKey.label(l10n)),
        onTap: () {
          Navigator.of(sheetContext).pop();
          _handleMoreMenuSelection(context, ref, l10n, item);
        },
      ),
    );
  }

  return tiles;
}

void _handleMoreMenuSelection(
  BuildContext context,
  WidgetRef ref,
  AppLocalizations l10n,
  MobileMenuItem item,
) {
  final destination = item.destination;
  if (destination is MobileMenuRoute) {
    final extra = destination.extraBuilder?.call(l10n);
    context.go(destination.route, extra: extra);
    return;
  }

  if (destination is MobileMenuAction) {
    if (destination.type == MobileMenuActionType.logout) {
      unawaited(ref.read(authControllerProvider.notifier).signOut());
    }
  }
}
