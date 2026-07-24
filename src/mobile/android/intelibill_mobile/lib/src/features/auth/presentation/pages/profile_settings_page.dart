import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';

const _shopDropdownKey = Key('profile-settings-shop');
const _editProfileTileKey = Key('profile-settings-edit-profile');
const _changePasswordTileKey = Key('profile-settings-change-password');

class ProfileSettingsPage extends ConsumerWidget {
  const ProfileSettingsPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final l10n = AppLocalizations.of(context)!;
    final authAsync = ref.watch(authControllerProvider);

    return Scaffold(
      appBar: AppBar(
        title: Text(l10n.profileSettingsTitle),
        actions: [
          IconButton(
            tooltip: l10n.shellProfile,
            icon: const Icon(Icons.account_circle),
            onPressed: () => context.push(AppRoutes.profile),
          ),
        ],
      ),
      body: authAsync.when(
        data: (state) {
          final session = state.session;
          if (session == null) {
            return Center(
              child: Padding(
                padding: const EdgeInsets.all(24),
                child: Text(
                  l10n.profileUnableToLoad,
                  style: theme.textTheme.bodyLarge,
                ),
              ),
            );
          }

          final user = session.user;
          final shops = session.shops ?? [];
          final activeShopId = session.activeShopId;
          final displayName = '${user.firstName} ${user.lastName}'.trim();

          return SafeArea(
            child: ListView(
              padding: const EdgeInsets.fromLTRB(20, 16, 20, 20),
              children: [
                Card(
                  child: Padding(
                    padding: const EdgeInsets.all(16),
                    child: Row(
                      children: [
                        CircleAvatar(
                          radius: 28,
                          backgroundColor: theme.colorScheme.primaryContainer,
                          foregroundColor: theme.colorScheme.onPrimaryContainer,
                          child: Text(
                            _initialsFor(displayName),
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
                              ),
                              if (user.email != null) ...[
                                const SizedBox(height: 2),
                                Text(
                                  user.email!,
                                  style: theme.textTheme.bodyMedium?.copyWith(
                                    color: theme.colorScheme.onSurface
                                        .withValues(alpha: 0.7),
                                  ),
                                ),
                              ],
                            ],
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
                if (shops.isNotEmpty) ...[
                  const SizedBox(height: 16),
                  Card(
                    child: Padding(
                      padding: const EdgeInsets.all(16),
                      child: DropdownButtonFormField<String>(
                        key: _shopDropdownKey,
                        decoration: InputDecoration(
                          labelText: l10n.profileSettingsActiveShop,
                        ),
                        initialValue: activeShopId,
                        items: shops
                            .map(
                              (shop) => DropdownMenuItem(
                                value: shop.shopId,
                                child: Text(shop.shopName),
                              ),
                            )
                            .toList(),
                        onChanged: (shopId) {
                          if (shopId != null && shopId != activeShopId) {
                            unawaited(
                              ref
                                  .read(authControllerProvider.notifier)
                                  .switchShop(shopId: shopId),
                            );
                          }
                        },
                      ),
                    ),
                  ),
                ],
                const SizedBox(height: 16),
                Card(
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      ListTile(
                        key: _editProfileTileKey,
                        leading: Icon(
                          Icons.edit_outlined,
                          color: theme.colorScheme.primary,
                        ),
                        title: Text(l10n.profileSettingsEditProfile),
                        trailing: Icon(
                          Icons.chevron_right,
                          color: theme.colorScheme.onSurface.withValues(
                            alpha: 0.35,
                          ),
                        ),
                        onTap: () => context.push(AppRoutes.profileEdit),
                      ),
                      const Divider(height: 1),
                      ListTile(
                        key: _changePasswordTileKey,
                        leading: Icon(
                          Icons.lock_outline,
                          color: theme.colorScheme.primary,
                        ),
                        title: Text(l10n.profileSettingsChangePassword),
                        trailing: Icon(
                          Icons.chevron_right,
                          color: theme.colorScheme.onSurface.withValues(
                            alpha: 0.35,
                          ),
                        ),
                        onTap: () =>
                            context.push(AppRoutes.profileChangePassword),
                      ),
                    ],
                  ),
                ),
              ],
            ),
          );
        },
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (error, _) => Center(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Text(
              '$error',
              style: theme.textTheme.bodyLarge,
              textAlign: TextAlign.center,
            ),
          ),
        ),
      ),
    );
  }
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
