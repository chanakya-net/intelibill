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
            return Center(child: Text(l10n.profileUnableToLoad));
          }

          final user = session.user;
          final shops = session.shops ?? [];
          final activeShopId = session.activeShopId;

          return ListView(
            padding: const EdgeInsets.all(16),
            children: [
              ListTile(
                leading: const Icon(Icons.account_circle, size: 48),
                title: Text('${user.firstName} ${user.lastName}'),
                subtitle: user.email != null ? Text(user.email!) : null,
              ),
              const Divider(),
              if (shops.isNotEmpty) ...[
                Padding(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 16,
                    vertical: 8,
                  ),
                  child: DropdownButtonFormField<String>(
                    key: _shopDropdownKey,
                    decoration: InputDecoration(
                      labelText: l10n.profileSettingsActiveShop,
                      border: const OutlineInputBorder(),
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
                const Divider(),
              ],
              ListTile(
                key: _editProfileTileKey,
                leading: const Icon(Icons.edit),
                title: Text(l10n.profileSettingsEditProfile),
                trailing: const Icon(Icons.chevron_right),
                onTap: () => context.push(AppRoutes.profileEdit),
              ),
              ListTile(
                key: _changePasswordTileKey,
                leading: const Icon(Icons.lock),
                title: Text(l10n.profileSettingsChangePassword),
                trailing: const Icon(Icons.chevron_right),
                onTap: () => context.push(AppRoutes.profileChangePassword),
              ),
            ],
          );
        },
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (error, _) => Center(child: Text('$error')),
      ),
    );
  }
}
