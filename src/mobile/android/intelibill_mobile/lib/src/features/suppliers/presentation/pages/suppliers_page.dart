import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/app/shell/menu_visibility.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/formatting/currency_formatter.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/entities/supplier.dart';
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
    final visibleSuppliers = suppliersState.suppliers
        .where((supplier) => !supplier.isSystem)
        .toList();

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
          ? FloatingActionButton.extended(
              key: SuppliersPage.addSupplierFabKey,
              onPressed: _openCreateSupplierSheet,
              tooltip: l10n.suppliersAddSupplier,
              icon: const Icon(Icons.add),
              label: Text(l10n.suppliersAddSupplier),
            )
          : null,
      body: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          if (!suppliersState.isLoading &&
              suppliersState.failure == null &&
              visibleSuppliers.isNotEmpty)
            _SuppliersSummaryStrip(suppliers: visibleSuppliers),
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 8),
            child: TextField(
              controller: _searchController,
              decoration: InputDecoration(
                hintText: l10n.commonSearch,
                prefixIcon: const Icon(Icons.search),
                suffixIcon: suppliersState.searchQuery.isEmpty
                    ? null
                    : IconButton(
                        tooltip: l10n.commonClear,
                        icon: const Icon(Icons.clear),
                        onPressed: () {
                          _searchController.clear();
                          ref
                              .read(suppliersControllerProvider.notifier)
                              .updateSearch('');
                        },
                      ),
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
              Icon(
                Icons.cloud_off_outlined,
                size: 48,
                color: Theme.of(context).colorScheme.onSurfaceVariant,
              ),
              const SizedBox(height: 16),
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
                  child: Column(
                    children: [
                      Icon(
                        Icons.local_shipping_outlined,
                        size: 56,
                        color: Theme.of(context).colorScheme.primary.withValues(
                          alpha: 0.7,
                        ),
                      ),
                      const SizedBox(height: 16),
                      Text(
                        l10n.suppliersNoSuppliersFound,
                        style: Theme.of(context).textTheme.titleMedium,
                        textAlign: TextAlign.center,
                      ),
                      const SizedBox(height: 8),
                      Text(
                        l10n.suppliersEmptyDescription,
                        style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                          color: Theme.of(context).colorScheme.onSurfaceVariant,
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
              padding: const EdgeInsets.only(bottom: 88),
              itemCount: suppliers.length,
              itemBuilder: (context, index) {
                return SupplierCard(supplier: suppliers[index]);
              },
            ),
    );
  }
}

class _SuppliersSummaryStrip extends StatelessWidget {
  const _SuppliersSummaryStrip({required this.suppliers});

  final List<Supplier> suppliers;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final theme = Theme.of(context);
    final activeCount = suppliers.where((supplier) => supplier.isActive).length;
    final preferredCount = suppliers
        .where((supplier) => supplier.isPreferred)
        .length;
    final totalPayable = suppliers
        .where((supplier) => supplier.balanceDue > 0)
        .fold<double>(0, (sum, supplier) => sum + supplier.balanceDue);

    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 12, 16, 0),
      child: Card(
        elevation: 0,
        color: theme.colorScheme.primaryContainer.withValues(alpha: 0.35),
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 14),
          child: Row(
            children: [
              Expanded(
                child: _SummaryStat(
                  icon: Icons.storefront_outlined,
                  label: l10n.suppliersSummaryCount(suppliers.length),
                ),
              ),
              Expanded(
                child: _SummaryStat(
                  icon: Icons.check_circle_outline,
                  label: l10n.suppliersSummaryActive(activeCount),
                ),
              ),
              Expanded(
                child: _SummaryStat(
                  icon: Icons.star_outline,
                  label: l10n.suppliersSummaryPreferred(preferredCount),
                ),
              ),
              if (totalPayable > 0)
                Expanded(
                  child: _SummaryStat(
                    icon: Icons.payments_outlined,
                    label: l10n.suppliersSummaryPayable(
                      formatInr(totalPayable),
                    ),
                    emphasize: true,
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }
}

class _SummaryStat extends StatelessWidget {
  const _SummaryStat({
    required this.icon,
    required this.label,
    this.emphasize = false,
  });

  final IconData icon;
  final String label;
  final bool emphasize;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Column(
      children: [
        Icon(
          icon,
          size: 18,
          color: emphasize
              ? theme.colorScheme.error
              : theme.colorScheme.onSurfaceVariant,
        ),
        const SizedBox(height: 4),
        Text(
          label,
          style: theme.textTheme.labelSmall?.copyWith(
            fontWeight: emphasize ? FontWeight.w700 : FontWeight.w500,
            color: emphasize
                ? theme.colorScheme.error
                : theme.colorScheme.onSurface,
          ),
          textAlign: TextAlign.center,
          maxLines: 2,
          overflow: TextOverflow.ellipsis,
        ),
      ],
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
