import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/app/shell/menu_visibility.dart';
import 'package:intelibill_mobile/src/app/shell/mobile_menu_item.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';

typedef MoreMenuItemTap = void Function(MobileMenuItem item);

class MoreMenuSheet extends StatelessWidget {
  const MoreMenuSheet({
    required this.items,
    required this.session,
    required this.onItemTap,
    super.key,
  });

  final List<MobileMenuItem> items;
  final AuthSession? session;
  final MoreMenuItemTap onItemTap;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return SafeArea(
      child: ListView(
        shrinkWrap: true,
        padding: const EdgeInsets.fromLTRB(16, 0, 16, 16),
        children: [
          _MoreMenuHeader(session: session, l10n: l10n),
          const SizedBox(height: 8),
          ..._buildSectionedTiles(context, l10n, theme, colorScheme),
        ],
      ),
    );
  }

  List<Widget> _buildSectionedTiles(
    BuildContext context,
    AppLocalizations l10n,
    ThemeData theme,
    ColorScheme colorScheme,
  ) {
    final tiles = <Widget>[];
    MobileMenuSection? lastSection;

    for (final item in items) {
      if (lastSection != item.section) {
        final sectionLabel = item.section.sectionLabel(l10n);
        if (sectionLabel != null) {
          if (lastSection != null) {
            tiles.add(const SizedBox(height: 8));
          }
          tiles.add(
            Padding(
              padding: const EdgeInsets.fromLTRB(4, 12, 4, 8),
              child: Text(
                sectionLabel,
                style: theme.textTheme.labelLarge?.copyWith(
                  color: colorScheme.onSurface.withValues(alpha: 0.6),
                  fontWeight: FontWeight.w700,
                  letterSpacing: 0.6,
                ),
              ),
            ),
          );
        }
        lastSection = item.section;
      }

      tiles.add(
        _MoreMenuTile(
          item: item,
          label: item.labelKey.label(l10n),
          onTap: () => onItemTap(item),
        ),
      );
    }

    return tiles;
  }
}

class _MoreMenuHeader extends StatelessWidget {
  const _MoreMenuHeader({required this.session, required this.l10n});

  final AuthSession? session;
  final AppLocalizations l10n;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final user = session?.user;
    final activeShop = activeShopForSession(session);

    if (user == null) {
      return Padding(
        padding: const EdgeInsets.only(top: 8, bottom: 4),
        child: Text(l10n.shellMore, style: theme.textTheme.titleLarge),
      );
    }

    final displayName = '${user.firstName} ${user.lastName}'.trim();
    final initials = _initialsFor(displayName);
    final shopLabel = activeShop?.shopName;
    final roleLabel = activeShop?.role;

    return Padding(
      padding: const EdgeInsets.only(top: 4, bottom: 4),
      child: Row(
        children: [
          CircleAvatar(
            radius: 24,
            backgroundColor: colorScheme.primaryContainer,
            foregroundColor: colorScheme.onPrimaryContainer,
            child: Text(
              initials,
              style: theme.textTheme.titleMedium?.copyWith(
                fontWeight: FontWeight.w700,
              ),
            ),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  displayName,
                  style: theme.textTheme.titleMedium?.copyWith(
                    fontWeight: FontWeight.w700,
                  ),
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                ),
                if (shopLabel != null) ...[
                  const SizedBox(height: 2),
                  Text(
                    shopLabel,
                    style: theme.textTheme.bodyMedium?.copyWith(
                      color: colorScheme.onSurface.withValues(alpha: 0.7),
                    ),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                  ),
                ],
                if (roleLabel != null) ...[
                  const SizedBox(height: 2),
                  Text(
                    roleLabel,
                    style: theme.textTheme.bodySmall?.copyWith(
                      color: colorScheme.primary,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                ],
              ],
            ),
          ),
        ],
      ),
    );
  }

  String _initialsFor(String displayName) {
    final parts = displayName
        .split(RegExp(r'\s+'))
        .where((part) => part.isNotEmpty)
        .toList();
    if (parts.isEmpty) {
      return '?';
    }
    if (parts.length == 1) {
      return parts.first[0].toUpperCase();
    }
    return '${parts.first[0]}${parts.last[0]}'.toUpperCase();
  }
}

class _MoreMenuTile extends StatelessWidget {
  const _MoreMenuTile({
    required this.item,
    required this.label,
    required this.onTap,
  });

  final MobileMenuItem item;
  final String label;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final isDestructive = item.isDestructive;
    final iconColor = isDestructive ? colorScheme.error : colorScheme.primary;
    final iconBackground = isDestructive
        ? colorScheme.errorContainer.withValues(alpha: 0.45)
        : colorScheme.primaryContainer.withValues(alpha: 0.55);
    final titleColor = isDestructive ? colorScheme.error : null;

    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 3),
      child: Material(
        color: colorScheme.surface,
        borderRadius: BorderRadius.circular(14),
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          onTap: onTap,
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
            child: Row(
              children: [
                Container(
                  width: 40,
                  height: 40,
                  decoration: BoxDecoration(
                    color: iconBackground,
                    borderRadius: BorderRadius.circular(10),
                  ),
                  alignment: Alignment.center,
                  child: Icon(item.icon, size: 22, color: iconColor),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: Text(
                    label,
                    style: theme.textTheme.bodyLarge?.copyWith(
                      fontWeight: FontWeight.w600,
                      color: titleColor,
                    ),
                  ),
                ),
                Icon(
                  Icons.chevron_right,
                  color: colorScheme.onSurface.withValues(alpha: 0.35),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
