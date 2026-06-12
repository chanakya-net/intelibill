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

    return Card(
      margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            CircleAvatar(
              backgroundColor: _avatarColor(user.firstName, user.lastName),
              foregroundColor: Colors.white,
              child: Text(
                _initials(user.firstName, user.lastName),
                style: const TextStyle(fontWeight: FontWeight.w600),
              ),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    user.fullName,
                    style: theme.textTheme.titleMedium,
                  ),
                  const SizedBox(height: 4),
                  if (user.phoneNumber != null && user.phoneNumber!.isNotEmpty)
                    Text(
                      user.phoneNumber!,
                      style: theme.textTheme.bodyMedium,
                    ),
                  if (user.email != null && user.email!.isNotEmpty) ...[
                    const SizedBox(height: 2),
                    Text(
                      user.email!,
                      style: theme.textTheme.bodySmall,
                    ),
                  ],
                  const SizedBox(height: 8),
                  Wrap(
                    spacing: 8,
                    runSpacing: 4,
                    children: [
                      _RoleChip(label: _roleLabel(l10n, user.role)),
                      _LoginStatusChip(
                        enabled: user.isLoginEnabled,
                        enabledLabel: l10n.usersLoginEnabled,
                        disabledLabel: l10n.usersLoginDisabled,
                      ),
                    ],
                  ),
                ],
              ),
            ),
            if (onEdit != null)
              IconButton(
                tooltip: l10n.commonEdit,
                icon: const Icon(Icons.edit_outlined),
                onPressed: onEdit,
              ),
          ],
        ),
      ),
    );
  }
}

class _RoleChip extends StatelessWidget {
  const _RoleChip({required this.label});

  final String label;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
      decoration: BoxDecoration(
        color: theme.colorScheme.surfaceContainerHighest,
        borderRadius: BorderRadius.circular(4),
      ),
      child: Text(label, style: theme.textTheme.labelSmall),
    );
  }
}

class _LoginStatusChip extends StatelessWidget {
  const _LoginStatusChip({
    required this.enabled,
    required this.enabledLabel,
    required this.disabledLabel,
  });

  final bool enabled;
  final String enabledLabel;
  final String disabledLabel;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
      decoration: BoxDecoration(
        color: enabled
            ? theme.colorScheme.primaryContainer
            : theme.colorScheme.errorContainer,
        borderRadius: BorderRadius.circular(4),
      ),
      child: Text(
        enabled ? enabledLabel : disabledLabel,
        style: theme.textTheme.labelSmall?.copyWith(
          color: enabled
              ? theme.colorScheme.onPrimaryContainer
              : theme.colorScheme.onErrorContainer,
        ),
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

Color _avatarColor(String firstName, String lastName) {
  const colors = [
    Color(0xFFB45309),
    Color(0xFF0369A1),
    Color(0xFF15803D),
    Color(0xFF7C3AED),
    Color(0xFFBE185D),
    Color(0xFFC2410C),
    Color(0xFF0F766E),
    Color(0xFF1D4ED8),
  ];

  final name = '$firstName$lastName';
  var hash = 0;
  for (var i = 0; i < name.length; i++) {
    hash = name.codeUnitAt(i) + ((hash << 5) - hash);
  }
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
