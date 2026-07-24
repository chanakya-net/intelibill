import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/users/domain/entities/shop_user.dart';

class ShopUserCard extends StatelessWidget {
  const ShopUserCard({
    required this.user,
    this.onEdit,
    super.key,
  });

  final ShopUser user;
  final VoidCallback? onEdit;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final l10n = AppLocalizations.of(context)!;
    final avatarColor = _stableColor(user.fullName, theme);

    return Card(
      margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
      child: ListTile(
        contentPadding: const EdgeInsets.fromLTRB(16, 12, 8, 12),
        leading: CircleAvatar(
          backgroundColor: avatarColor,
          child: Text(
            _initials(user.firstName, user.lastName),
            style: theme.textTheme.labelLarge?.copyWith(
              color: Colors.white,
              fontWeight: FontWeight.w600,
            ),
          ),
        ),
        title: Row(
          children: [
            Expanded(
              child: Text(
                user.fullName,
                style: theme.textTheme.titleSmall,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
              ),
            ),
            const SizedBox(width: 8),
            _StatusBadge(
              label: _roleLabel(l10n, user.role),
              backgroundColor: theme.colorScheme.surfaceContainerHighest,
              foregroundColor: theme.colorScheme.onSurface,
            ),
            const SizedBox(width: 4),
            _StatusBadge(
              label: user.isLoginEnabled
                  ? l10n.usersLoginEnabled
                  : l10n.usersLoginDisabled,
              backgroundColor: user.isLoginEnabled
                  ? theme.colorScheme.secondaryContainer
                  : theme.colorScheme.errorContainer,
              foregroundColor: user.isLoginEnabled
                  ? theme.colorScheme.onSecondaryContainer
                  : theme.colorScheme.onErrorContainer,
            ),
          ],
        ),
        subtitle: Padding(
          padding: const EdgeInsets.only(top: 8),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              if (user.phoneNumber != null && user.phoneNumber!.isNotEmpty)
                _InfoRow(
                  icon: Icons.phone_outlined,
                  text: user.phoneNumber!,
                ),
              if (user.email != null && user.email!.isNotEmpty)
                _InfoRow(
                  icon: Icons.email_outlined,
                  text: user.email!,
                ),
            ],
          ),
        ),
        trailing: onEdit != null
            ? IconButton(
                tooltip: l10n.commonEdit,
                icon: const Icon(Icons.edit_outlined),
                onPressed: onEdit,
              )
            : null,
      ),
    );
  }
}

class _StatusBadge extends StatelessWidget {
  const _StatusBadge({
    required this.label,
    required this.backgroundColor,
    required this.foregroundColor,
  });

  final String label;
  final Color backgroundColor;
  final Color foregroundColor;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
      decoration: BoxDecoration(
        color: backgroundColor,
        borderRadius: BorderRadius.circular(4),
      ),
      child: Text(
        label,
        style: Theme.of(context).textTheme.labelSmall?.copyWith(
          color: foregroundColor,
          fontWeight: FontWeight.w600,
        ),
      ),
    );
  }
}

class _InfoRow extends StatelessWidget {
  const _InfoRow({required this.icon, required this.text});

  final IconData icon;
  final String text;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Padding(
      padding: const EdgeInsets.only(bottom: 4),
      child: Row(
        children: [
          Icon(icon, size: 16, color: theme.colorScheme.onSurfaceVariant),
          const SizedBox(width: 6),
          Expanded(
            child: Text(
              text,
              style: theme.textTheme.bodySmall?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
            ),
          ),
        ],
      ),
    );
  }
}

String _initials(String firstName, String lastName) {
  final first = firstName.trim();
  final last = lastName.trim();
  if (first.isNotEmpty && last.isNotEmpty) {
    return '${first[0]}${last[0]}'.toUpperCase();
  }
  if (first.isNotEmpty) {
    return first.substring(0, first.length >= 2 ? 2 : 1).toUpperCase();
  }
  return '?';
}

Color _stableColor(String name, ThemeData theme) {
  final hash = name.codeUnits.fold(0, (acc, c) => acc * 31 + c);
  final colors = [
    theme.colorScheme.primary,
    theme.colorScheme.secondary,
    theme.colorScheme.tertiary,
    Colors.teal,
    Colors.indigo,
    Colors.deepOrange,
  ];
  return colors[hash.abs() % colors.length];
}

String _roleLabel(AppLocalizations l10n, String role) {
  switch (role.trim().toLowerCase()) {
    case 'owner':
      return l10n.usersRoleOwner;
    case 'manager':
      return l10n.usersRoleManager;
    case 'staff':
    case 'salesperson':
      return l10n.usersRoleStaff;
    default:
      return role;
  }
}
