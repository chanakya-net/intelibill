import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/app/shell/menu_visibility.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/suppliers/presentation/controllers/suppliers_controller.dart';
import 'package:intelibill_mobile/src/features/suppliers/presentation/widgets/create_supplier_sheet.dart';
import 'package:intelibill_mobile/src/features/suppliers/presentation/widgets/supplier_card.dart';

class SuppliersPage extends ConsumerStatefulWidget {
  const SuppliersPage({super.key});

  static const addSupplierFabKey = Key('suppliers-add-fab');

  @override
  ConsumerState<SuppliersPage> createState() => _SuppliersPageState();
}

class _SuppliersPageState extends ConsumerState<SuppliersPage> {
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

  @override
  Widget build(BuildContext context) {
    final suppliersState = ref.watch(suppliersControllerProvider);
    final authState = ref.watch(authControllerProvider);
    final l10n = AppLocalizations.of(context)!;
    final session = authState.value?.session;
    final canCreateSupplier = canManageSuppliers(session);

    return Scaffold(
      appBar: AppBar(
        title: Text(l10n.suppliersTitle),
        actions: [
          IconButton(
            tooltip: l10n.shellProfile,
            icon: const Icon(Icons.account_circle),
            onPressed: () => context.push(AppRoutes.profile),
          ),
        ],
      ),
      floatingActionButton: canCreateSupplier
          ? FloatingActionButton(
              key: SuppliersPage.addSupplierFabKey,
              onPressed: _openCreateSupplierSheet,
              tooltip: l10n.suppliersAddSupplier,
              child: const Icon(Icons.add),
            )
          : null,
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 8),
            child: TextField(
              controller: _searchController,
              decoration: InputDecoration(
                hintText: l10n.commonSearch,
                prefixIcon: const Icon(Icons.search),
                border: const OutlineInputBorder(),
                isDense: true,
              ),
              onChanged: (query) {
                ref
                    .read(suppliersControllerProvider.notifier)
                    .updateSearch(query);
              },
            ),
          ),
          Expanded(child: _buildBody(context, suppliersState, l10n)),
        ],
      ),
    );
  }

  Future<void> _openCreateSupplierSheet() async {
    final l10n = AppLocalizations.of(context)!;
    final created = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      builder: (context) => const CreateSupplierSheet(),
    );

    if (!mounted || created != true) return;
    ScaffoldMessenger.of(
      context,
    ).showSnackBar(SnackBar(content: Text(l10n.suppliersCreateSuccess)));
  }

  Widget _buildBody(
    BuildContext context,
    SuppliersState suppliersState,
    AppLocalizations l10n,
  ) {
    if (suppliersState.isLoading) {
      return const Center(child: CircularProgressIndicator());
    }

    if (suppliersState.failure != null) {
      return Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Text(
                l10n.suppliersUnableToLoad,
                style: const TextStyle(
                  fontSize: 18,
                  fontWeight: FontWeight.w600,
                ),
              ),
              const SizedBox(height: 8),
              Text(
                _localizeFailure(l10n, suppliersState.failure!),
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: 16),
              FilledButton(
                onPressed: () {
                  unawaited(
                    ref.read(suppliersControllerProvider.notifier).refresh(),
                  );
                },
                child: Text(l10n.suppliersRetry),
              ),
            ],
          ),
        ),
      );
    }

    final suppliers = suppliersState.filteredSuppliers;

    return RefreshIndicator(
      onRefresh: () => ref.read(suppliersControllerProvider.notifier).refresh(),
      child: suppliers.isEmpty
          ? ListView(
              physics: const AlwaysScrollableScrollPhysics(),
              children: [
                Padding(
                  padding: const EdgeInsets.all(32),
                  child: Center(
                    child: Text(
                      l10n.suppliersNoSuppliersFound,
                      style: const TextStyle(fontSize: 16),
                    ),
                  ),
                ),
              ],
            )
          : ListView.builder(
              physics: const AlwaysScrollableScrollPhysics(),
              itemCount: suppliers.length,
              itemBuilder: (context, index) {
                return SupplierCard(supplier: suppliers[index]);
              },
            ),
    );
  }
}

String _localizeFailure(AppLocalizations l10n, Failure failure) {
  return failure.when(
    validation: (String? message, Map<String, List<String>>? _) =>
        message ?? l10n.suppliersErrorGeneric,
    unauthorized: (String? _) => l10n.suppliersErrorUnauthorized,
    forbidden: (String? _) => l10n.suppliersErrorForbidden,
    notFound: (String? _) => l10n.suppliersErrorGeneric,
    server: (String? message, int? _) => message ?? l10n.suppliersErrorGeneric,
    network: (String? _) => l10n.suppliersErrorNetwork,
    timeout: (String? _) => l10n.suppliersErrorTimeout,
    serialization: (String? _) => l10n.suppliersErrorGeneric,
    unknown: (String? _) => l10n.suppliersErrorGeneric,
  );
}
