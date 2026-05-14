import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/app/shell/menu_visibility.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/entities/supplier.dart';
import 'package:intelibill_mobile/src/features/suppliers/presentation/controllers/suppliers_controller.dart';
import 'package:intelibill_mobile/src/features/suppliers/presentation/widgets/create_supplier_sheet.dart';

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
          Expanded(
            child: _buildBody(context, suppliersState, l10n),
          ),
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
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(l10n.suppliersCreateSuccess)),
    );
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
                return _SupplierCard(supplier: suppliers[index]);
              },
            ),
    );
  }
}

class _SupplierCard extends StatelessWidget {
  const _SupplierCard({required this.supplier});

  final Supplier supplier;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Card(
      margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(
                    supplier.name,
                    style: theme.textTheme.titleMedium,
                  ),
                ),
                if (supplier.isPreferred)
                  Container(
                    padding: const EdgeInsets.symmetric(
                      horizontal: 8,
                      vertical: 2,
                    ),
                    decoration: BoxDecoration(
                      color: theme.colorScheme.primaryContainer,
                      borderRadius: BorderRadius.circular(4),
                    ),
                    child: Text(
                      AppLocalizations.of(context)!.suppliersPreferred,
                      style: theme.textTheme.labelSmall?.copyWith(
                        color: theme.colorScheme.onPrimaryContainer,
                      ),
                    ),
                  ),
                if (!supplier.isActive)
                  Container(
                    padding: const EdgeInsets.symmetric(
                      horizontal: 8,
                      vertical: 2,
                    ),
                    margin: EdgeInsets.only(
                      left: supplier.isPreferred ? 4 : 0,
                    ),
                    decoration: BoxDecoration(
                      color: theme.colorScheme.errorContainer,
                      borderRadius: BorderRadius.circular(4),
                    ),
                    child: Text(
                      AppLocalizations.of(context)!.suppliersInactive,
                      style: theme.textTheme.labelSmall?.copyWith(
                        color: theme.colorScheme.onErrorContainer,
                      ),
                    ),
                  ),
              ],
            ),
            const SizedBox(height: 4),
            Text(
              supplier.contactPersonName ?? '',
              style: theme.textTheme.bodyMedium,
            ),
            const SizedBox(height: 2),
            Text(
              '${supplier.city ?? ''}'
              '${supplier.city != null && supplier.state != null ? ', ' : ''}'
              '${supplier.state ?? ''}',
              style: theme.textTheme.bodySmall,
            ),
            if (supplier.balanceDue != 0) ...[
              const SizedBox(height: 8),
              Text(
                '${AppLocalizations.of(context)!.suppliersBalanceDueLabel} '
                '${supplier.balanceDue.toStringAsFixed(2)}',
                style: theme.textTheme.bodyMedium?.copyWith(
                  color: supplier.balanceDue > 0
                      ? theme.colorScheme.error
                      : theme.colorScheme.primary,
                  fontWeight: FontWeight.w500,
                ),
              ),
            ],
          ],
        ),
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
