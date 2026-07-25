import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/app/shell/menu_visibility.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/users/domain/entities/shop_user.dart';
import 'package:intelibill_mobile/src/features/users/presentation/controllers/users_controller.dart';
import 'package:intelibill_mobile/src/features/users/presentation/widgets/add_shop_user_sheet.dart';
import 'package:intelibill_mobile/src/features/users/presentation/widgets/edit_shop_user_sheet.dart';
import 'package:intelibill_mobile/src/features/users/presentation/widgets/shop_user_card.dart';

class UsersPage extends ConsumerStatefulWidget {
  const UsersPage({super.key});

  static const addUserFabKey = Key('users-add-fab');

  @override
  ConsumerState<UsersPage> createState() => _UsersPageState();
}

class _UsersPageState extends ConsumerState<UsersPage> {
  late final TextEditingController _searchController;

  @override
  void initState() {
    super.initState();
    _searchController = TextEditingController();
  }

  @override
  void dispose() {
    _searchController.dispose();
    super.dispose();
  }

  Future<void> _openAddUserSheet() async {
    final l10n = AppLocalizations.of(context)!;
    final created = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      builder: (context) => const AddShopUserSheet(),
    );

    if (!mounted || created != true) return;
    ScaffoldMessenger.of(
      context,
    ).showSnackBar(SnackBar(content: Text(l10n.usersAddSuccess)));
  }

  Future<void> _openEditUserSheet(ShopUser user) async {
    final l10n = AppLocalizations.of(context)!;
    final updated = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      builder: (context) => EditShopUserSheet(user: user),
    );

    if (!mounted || updated != true) return;
    ScaffoldMessenger.of(
      context,
    ).showSnackBar(SnackBar(content: Text(l10n.usersEditSuccess)));
  }

  @override
  Widget build(BuildContext context) {
    final usersState = ref.watch(usersControllerProvider);
    final authState = ref.watch(authControllerProvider);
    final session = authState.value?.session;
    final canManageUsers = isOwner(session);
    final l10n = AppLocalizations.of(context)!;

    return Scaffold(
      appBar: AppBar(
        title: Text(l10n.usersTitle),
        actions: [
          IconButton(
            tooltip: l10n.shellProfile,
            icon: const Icon(Icons.account_circle),
            onPressed: () => context.push(AppRoutes.profile),
          ),
        ],
      ),
      floatingActionButton: canManageUsers
          ? FloatingActionButton.extended(
              key: UsersPage.addUserFabKey,
              onPressed: _openAddUserSheet,
              tooltip: l10n.usersAddUser,
              icon: const Icon(Icons.add),
              label: Text(l10n.usersAddUser),
            )
          : null,
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 8),
            child: TextField(
              controller: _searchController,
              decoration: InputDecoration(
                hintText: l10n.usersSearchPlaceholder,
                prefixIcon: const Icon(Icons.search),
                suffixIcon: usersState.searchQuery.isEmpty
                    ? null
                    : IconButton(
                        tooltip: l10n.commonClear,
                        icon: const Icon(Icons.clear),
                        onPressed: () {
                          _searchController.clear();
                          ref
                              .read(usersControllerProvider.notifier)
                              .updateSearch('');
                        },
                      ),
                border: const OutlineInputBorder(),
                isDense: true,
              ),
              onChanged: (query) {
                ref.read(usersControllerProvider.notifier).updateSearch(query);
              },
            ),
          ),
          Expanded(
            child: _buildBody(context, usersState, l10n, canManageUsers),
          ),
        ],
      ),
    );
  }

  Widget _buildBody(
    BuildContext context,
    UsersState usersState,
    AppLocalizations l10n,
    bool canManageUsers,
  ) {
    final theme = Theme.of(context);

    if (usersState.isLoading) {
      return const Center(child: CircularProgressIndicator());
    }

    if (usersState.failure != null) {
      return Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(
                Icons.cloud_off_outlined,
                size: 48,
                color: theme.colorScheme.onSurfaceVariant,
              ),
              const SizedBox(height: 16),
              Text(
                l10n.usersUnableToLoad,
                style: theme.textTheme.titleMedium?.copyWith(
                  fontWeight: FontWeight.w600,
                ),
              ),
              const SizedBox(height: 8),
              Text(
                _localizeFailure(l10n, usersState.failure!),
                style: theme.textTheme.bodyMedium?.copyWith(
                  color: theme.colorScheme.onSurfaceVariant,
                ),
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: 16),
              FilledButton(
                onPressed: () {
                  unawaited(
                    ref.read(usersControllerProvider.notifier).refresh(),
                  );
                },
                child: Text(l10n.usersRetry),
              ),
            ],
          ),
        ),
      );
    }

    final users = usersState.filteredUsers;

    return RefreshIndicator(
      onRefresh: () => ref.read(usersControllerProvider.notifier).refresh(),
      child: users.isEmpty
          ? ListView(
              physics: const AlwaysScrollableScrollPhysics(),
              children: [
                Padding(
                  padding: const EdgeInsets.all(32),
                  child: Column(
                    children: [
                      Icon(
                        Icons.people_outline,
                        size: 56,
                        color: theme.colorScheme.primary.withValues(
                          alpha: 0.7,
                        ),
                      ),
                      const SizedBox(height: 16),
                      Text(
                        l10n.usersNoUsersFound,
                        style: theme.textTheme.titleMedium,
                        textAlign: TextAlign.center,
                      ),
                      const SizedBox(height: 8),
                      Text(
                        l10n.usersNoUsersDescription,
                        style: theme.textTheme.bodyMedium?.copyWith(
                          color: theme.colorScheme.onSurfaceVariant,
                        ),
                        textAlign: TextAlign.center,
                      ),
                    ],
                  ),
                ),
              ],
            )
          : ListView.builder(
              physics: const AlwaysScrollableScrollPhysics(),
              padding: canManageUsers
                  ? const EdgeInsets.only(bottom: 88)
                  : EdgeInsets.zero,
              itemCount: users.length,
              itemBuilder: (context, index) {
                final user = users[index];
                return ShopUserCard(
                  user: user,
                  onEdit: canManageUsers && !user.isOwner
                      ? () => _openEditUserSheet(user)
                      : null,
                );
              },
            ),
    );
  }
}

String _localizeFailure(AppLocalizations l10n, Failure failure) {
  return failure.when(
    validation: (String? message, Map<String, List<String>>? _) =>
        message ?? l10n.usersErrorGeneric,
    unauthorized: (String? _) => l10n.usersErrorUnauthorized,
    forbidden: (String? _) => l10n.usersErrorForbidden,
    notFound: (String? _) => l10n.usersErrorGeneric,
    server: (String? message, int? _) => message ?? l10n.usersErrorGeneric,
    network: (String? _) => l10n.usersErrorNetwork,
    timeout: (String? _) => l10n.usersErrorTimeout,
    serialization: (String? _) => l10n.usersErrorGeneric,
    unknown: (String? _) => l10n.usersErrorGeneric,
  );
}
