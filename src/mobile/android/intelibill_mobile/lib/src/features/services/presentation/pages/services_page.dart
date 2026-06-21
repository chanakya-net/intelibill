import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/services/domain/entities/service.dart';
import 'package:intelibill_mobile/src/features/services/presentation/controllers/services_controller.dart';

class ServicesPage extends ConsumerStatefulWidget {
  const ServicesPage({super.key});

  @override
  ConsumerState<ServicesPage> createState() => _ServicesPageState();
}

class _ServicesPageState extends ConsumerState<ServicesPage> {
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
    final servicesState = ref.watch(servicesControllerProvider);
    final l10n = AppLocalizations.of(context)!;

    return Scaffold(
      appBar: AppBar(title: Text(l10n.servicesTitle)),
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 8),
            child: TextField(
              controller: _searchController,
              decoration: InputDecoration(
                hintText: l10n.commonSearch,
                prefixIcon: const Icon(Icons.search),
                suffixIcon: servicesState.searchQuery.isEmpty
                    ? null
                    : IconButton(
                        tooltip: l10n.commonClear,
                        icon: const Icon(Icons.clear),
                        onPressed: () {
                          _searchController.clear();
                          ref
                              .read(servicesControllerProvider.notifier)
                              .updateSearch('');
                        },
                      ),
                border: const OutlineInputBorder(),
                isDense: true,
              ),
              onChanged: (query) {
                ref
                    .read(servicesControllerProvider.notifier)
                    .updateSearch(query);
              },
            ),
          ),
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 0, 16, 12),
            child: _ServiceFilterChips(
              l10n: l10n,
              selectedFilter: servicesState.filter,
              onFilterChanged: (filter) {
                ref.read(servicesControllerProvider.notifier).setFilter(filter);
              },
            ),
          ),
          Expanded(child: _buildBody(context, servicesState, l10n)),
        ],
      ),
    );
  }

  Widget _buildBody(
    BuildContext context,
    ServicesState servicesState,
    AppLocalizations l10n,
  ) {
    if (servicesState.isLoading) {
      return const Center(child: CircularProgressIndicator());
    }

    if (servicesState.failure != null) {
      return Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Text(
                l10n.servicesUnableToLoad,
                style: const TextStyle(
                  fontSize: 18,
                  fontWeight: FontWeight.w600,
                ),
              ),
              const SizedBox(height: 8),
              Text(
                _localizeFailure(l10n, servicesState.failure!),
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: 16),
              FilledButton(
                onPressed: () {
                  unawaited(
                    ref.read(servicesControllerProvider.notifier).refresh(),
                  );
                },
                child: Text(l10n.servicesRetry),
              ),
            ],
          ),
        ),
      );
    }

    final services = servicesState.filteredServices;

    return RefreshIndicator(
      onRefresh: () => ref.read(servicesControllerProvider.notifier).refresh(),
      child: services.isEmpty
          ? ListView(
              physics: const AlwaysScrollableScrollPhysics(),
              children: [
                Padding(
                  padding: const EdgeInsets.all(32),
                  child: Center(
                    child: Text(
                      l10n.servicesNoServicesFound,
                      style: const TextStyle(fontSize: 16),
                    ),
                  ),
                ),
              ],
            )
          : ListView.builder(
              physics: const AlwaysScrollableScrollPhysics(),
              itemCount: services.length,
              itemBuilder: (context, index) => _ServiceCard(
                service: services[index],
                l10n: l10n,
              ),
            ),
    );
  }
}

class _ServiceFilterChips extends StatelessWidget {
  const _ServiceFilterChips({
    required this.l10n,
    required this.selectedFilter,
    required this.onFilterChanged,
  });

  final AppLocalizations l10n;
  final ServiceFilterOption selectedFilter;
  final ValueChanged<ServiceFilterOption> onFilterChanged;

  @override
  Widget build(BuildContext context) {
    return Wrap(
      spacing: 8,
      children: [
        FilterChip(
          label: Text(l10n.servicesFilterAll),
          selected: selectedFilter == ServiceFilterOption.all,
          onSelected: (_) => onFilterChanged(ServiceFilterOption.all),
        ),
        FilterChip(
          label: Text(l10n.servicesFilterActive),
          selected: selectedFilter == ServiceFilterOption.active,
          onSelected: (_) => onFilterChanged(ServiceFilterOption.active),
        ),
        FilterChip(
          label: Text(l10n.servicesFilterInactive),
          selected: selectedFilter == ServiceFilterOption.inactive,
          onSelected: (_) => onFilterChanged(ServiceFilterOption.inactive),
        ),
      ],
    );
  }
}

class _ServiceCard extends StatelessWidget {
  const _ServiceCard({
    required this.service,
    required this.l10n,
  });

  final Service service;
  final AppLocalizations l10n;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Card(
      margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
      child: ListTile(
        title: Text(service.name),
        subtitle: Text(
          '${service.code} • ${service.price.toStringAsFixed(2)}',
          style: theme.textTheme.bodySmall,
        ),
        trailing: Container(
          padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
          decoration: BoxDecoration(
            color: service.isActive
                ? theme.colorScheme.secondaryContainer
                : theme.colorScheme.errorContainer,
            borderRadius: BorderRadius.circular(4),
          ),
          child: Text(
            service.isActive ? l10n.servicesActive : l10n.servicesInactive,
            style: theme.textTheme.labelSmall?.copyWith(
              color: service.isActive
                  ? theme.colorScheme.onSecondaryContainer
                  : theme.colorScheme.onErrorContainer,
            ),
          ),
        ),
      ),
    );
  }
}

String _localizeFailure(AppLocalizations l10n, Failure failure) {
  return failure.when(
    validation: (String? message, Map<String, List<String>>? _) =>
        message ?? l10n.servicesErrorGeneric,
    unauthorized: (String? _) => l10n.servicesErrorUnauthorized,
    forbidden: (String? _) => l10n.servicesErrorForbidden,
    notFound: (String? _) => l10n.servicesErrorGeneric,
    server: (String? message, int? _) => message ?? l10n.servicesErrorGeneric,
    network: (String? _) => l10n.servicesErrorNetwork,
    timeout: (String? _) => l10n.servicesErrorTimeout,
    serialization: (String? _) => l10n.servicesErrorGeneric,
    unknown: (String? _) => l10n.servicesErrorGeneric,
  );
}
