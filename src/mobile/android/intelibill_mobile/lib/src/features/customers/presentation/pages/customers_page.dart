import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/customers/domain/entities/customer.dart';
import 'package:intelibill_mobile/src/features/customers/presentation/controllers/customers_controller.dart';
import 'package:intelibill_mobile/src/features/customers/presentation/widgets/create_customer_sheet.dart';

class CustomersPage extends ConsumerStatefulWidget {
  const CustomersPage({super.key});

  static const addCustomerFabKey = Key('customers-add-fab');

  @override
  ConsumerState<CustomersPage> createState() => _CustomersPageState();
}

class _CustomersPageState extends ConsumerState<CustomersPage> {
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
    final customersState = ref.watch(customersControllerProvider);
    final l10n = AppLocalizations.of(context)!;

    return Scaffold(
      appBar: AppBar(
        title: Text(l10n.customersTitle),
        actions: [
          IconButton(
            tooltip: l10n.shellProfile,
            icon: const Icon(Icons.account_circle),
            onPressed: () => context.push(AppRoutes.profile),
          ),
        ],
      ),
      floatingActionButton: FloatingActionButton(
        key: CustomersPage.addCustomerFabKey,
        onPressed: _openCreateCustomerSheet,
        tooltip: l10n.customersAddCustomer,
        child: const Icon(Icons.add),
      ),
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
                    .read(customersControllerProvider.notifier)
                    .updateSearch(query);
              },
            ),
          ),
          Expanded(
            child: _buildBody(context, customersState, l10n),
          ),
        ],
      ),
    );
  }

  Future<void> _openCreateCustomerSheet() async {
    final l10n = AppLocalizations.of(context)!;
    final created = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      builder: (context) => const CreateCustomerSheet(),
    );

    if (!mounted || created != true) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(l10n.customersCreateSuccess)),
    );
  }

  Widget _buildBody(
    BuildContext context,
    CustomersState customersState,
    AppLocalizations l10n,
  ) {
    if (customersState.isLoading) {
      return const Center(child: CircularProgressIndicator());
    }

    if (customersState.failure != null) {
      return Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Text(
                l10n.customersUnableToLoad,
                style: const TextStyle(
                  fontSize: 18,
                  fontWeight: FontWeight.w600,
                ),
              ),
              const SizedBox(height: 8),
              Text(
                _localizeFailure(l10n, customersState.failure!),
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: 16),
              FilledButton(
                onPressed: () {
                  unawaited(
                    ref.read(customersControllerProvider.notifier).refresh(),
                  );
                },
                child: Text(l10n.customersRetry),
              ),
            ],
          ),
        ),
      );
    }

    final customers = customersState.filteredCustomers;

    return RefreshIndicator(
      onRefresh: () => ref.read(customersControllerProvider.notifier).refresh(),
      child: customers.isEmpty
          ? ListView(
              physics: const AlwaysScrollableScrollPhysics(),
              children: [
                Padding(
                  padding: const EdgeInsets.all(32),
                  child: Center(
                    child: Text(
                      l10n.customersNoCustomersFound,
                      style: const TextStyle(fontSize: 16),
                    ),
                  ),
                ),
              ],
            )
          : ListView.builder(
              physics: const AlwaysScrollableScrollPhysics(),
              itemCount: customers.length,
              itemBuilder: (context, index) {
                return _CustomerCard(customer: customers[index]);
              },
            ),
    );
  }
}

class _CustomerCard extends StatelessWidget {
  const _CustomerCard({required this.customer});

  final Customer customer;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final l10n = AppLocalizations.of(context)!;

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
                    customer.name,
                    style: theme.textTheme.titleMedium,
                  ),
                ),
                if (!customer.isActive)
                  Container(
                    padding: const EdgeInsets.symmetric(
                      horizontal: 8,
                      vertical: 2,
                    ),
                    decoration: BoxDecoration(
                      color: theme.colorScheme.errorContainer,
                      borderRadius: BorderRadius.circular(4),
                    ),
                    child: Text(
                      l10n.customersInactive,
                      style: theme.textTheme.labelSmall?.copyWith(
                        color: theme.colorScheme.onErrorContainer,
                      ),
                    ),
                  ),
              ],
            ),
            const SizedBox(height: 4),
            Text(
              customer.phoneNumber,
              style: theme.textTheme.bodyMedium,
            ),
            if (customer.address != null) ...[
              const SizedBox(height: 2),
              Text(
                customer.address!,
                style: theme.textTheme.bodySmall,
              ),
            ],
            if (customer.outstandingDue > 0) ...[
              const SizedBox(height: 8),
              Text(
                '${l10n.customersOutstandingLabel} '
                '₹${customer.outstandingDue.toStringAsFixed(2)}',
                style: theme.textTheme.bodyMedium?.copyWith(
                  color: theme.colorScheme.error,
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
        message ?? l10n.customersErrorGeneric,
    unauthorized: (String? _) => l10n.customersErrorUnauthorized,
    forbidden: (String? _) => l10n.customersErrorForbidden,
    notFound: (String? _) => l10n.customersErrorGeneric,
    server: (String? message, int? _) => message ?? l10n.customersErrorGeneric,
    network: (String? _) => l10n.customersErrorNetwork,
    timeout: (String? _) => l10n.customersErrorTimeout,
    serialization: (String? _) => l10n.customersErrorGeneric,
    unknown: (String? _) => l10n.customersErrorGeneric,
  );
}
