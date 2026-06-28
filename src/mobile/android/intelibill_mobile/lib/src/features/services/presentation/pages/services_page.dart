import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/app/shell/menu_visibility.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/services/domain/entities/service.dart';
import 'package:intelibill_mobile/src/features/services/presentation/controllers/services_controller.dart';
import 'package:intelibill_mobile/src/features/services/presentation/widgets/create_service_sheet.dart';
import 'package:intelibill_mobile/src/features/services/presentation/widgets/edit_service_sheet.dart';

class ServicesPage extends ConsumerStatefulWidget {
  const ServicesPage({super.key});

  static const addServiceFabKey = Key('services-add-fab');

  static Key editServiceActionKey(String serviceId) =>
      Key('services-edit-$serviceId');

  static Key toggleServiceActionKey(String serviceId) =>
      Key('services-toggle-$serviceId');

  static Key statusServiceChipKey(String serviceId) =>
      Key('services-status-$serviceId');

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
    final authState = ref.watch(authControllerProvider);
    final canManage = canManageServices(authState.value?.session);
    final l10n = AppLocalizations.of(context)!;

    return Scaffold(
      appBar: AppBar(title: Text(l10n.servicesTitle)),
      floatingActionButton: canManage
          ? FloatingActionButton.extended(
              key: ServicesPage.addServiceFabKey,
              onPressed: () => unawaited(_openCreateSheet()),
              icon: const Icon(Icons.add),
              label: Text(l10n.servicesAddService),
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
          Expanded(
            child: _buildBody(
              context,
              servicesState,
              l10n,
              canManage,
            ),
          ),
        ],
      ),
    );
  }

  Future<void> _openCreateSheet() async {
    final l10n = AppLocalizations.of(context)!;
    final created = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      builder: (context) => const CreateServiceSheet(),
    );

    if (!mounted || created != true) return;
    ScaffoldMessenger.of(
      context,
    ).showSnackBar(SnackBar(content: Text(l10n.servicesCreateSuccess)));
  }

  Future<void> _openEditSheet(Service service) async {
    final l10n = AppLocalizations.of(context)!;
    final updated = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      builder: (context) => EditServiceSheet(service: service),
    );

    if (!mounted || updated != true) return;
    ScaffoldMessenger.of(
      context,
    ).showSnackBar(SnackBar(content: Text(l10n.servicesUpdateSuccess)));
  }

  Future<void> _toggleServiceActive(Service service) async {
    final l10n = AppLocalizations.of(context)!;
    final notifier = ref.read(servicesControllerProvider.notifier);
    final success = service.isActive
        ? await notifier.deactivateService(service.serviceId)
        : await notifier.activateService(service.serviceId);

    if (!mounted) return;

    if (!success) {
      final failure = ref.read(servicesControllerProvider).submitFailure;
      if (failure != null) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(_localizeMutationFailure(l10n, failure))),
        );
      }
      return;
    }

    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(
          service.isActive
              ? l10n.servicesDeactivatedSuccess
              : l10n.servicesActivatedSuccess,
        ),
      ),
    );
  }

  Widget _buildBody(
    BuildContext context,
    ServicesState servicesState,
    AppLocalizations l10n,
    bool canManage,
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
                canManage: canManage,
                l10n: l10n,
                isSubmitting: servicesState.isSubmitting,
                onEdit: () => _openEditSheet(services[index]),
                onToggleActive: () => _toggleServiceActive(services[index]),
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
    required this.canManage,
    required this.l10n,
    required this.isSubmitting,
    required this.onEdit,
    required this.onToggleActive,
  });

  final Service service;
  final bool canManage;
  final AppLocalizations l10n;
  final bool isSubmitting;
  final VoidCallback onEdit;
  final VoidCallback onToggleActive;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Card(
      margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
      child: InkWell(
        onTap: canManage && !isSubmitting ? onEdit : null,
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(service.name, style: theme.textTheme.titleMedium),
                        const SizedBox(height: 4),
                        Text(
                          service.code,
                          style: theme.textTheme.labelMedium?.copyWith(
                            letterSpacing: 0,
                          ),
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(width: 12),
                  _StatusChip(
                    key: ServicesPage.statusServiceChipKey(service.serviceId),
                    label: service.isActive
                        ? l10n.servicesActive
                        : l10n.servicesInactive,
                    active: service.isActive,
                  ),
                  if (canManage) ...[
                    const SizedBox(width: 4),
                    IconButton(
                      key: ServicesPage.editServiceActionKey(service.serviceId),
                      tooltip: l10n.commonEdit,
                      icon: const Icon(Icons.edit_outlined),
                      onPressed: isSubmitting ? null : onEdit,
                    ),
                    IconButton(
                      key: ServicesPage.toggleServiceActionKey(
                        service.serviceId,
                      ),
                      tooltip: service.isActive
                          ? l10n.servicesDeactivate
                          : l10n.servicesActivate,
                      icon: Icon(
                        service.isActive ? Icons.toggle_off : Icons.toggle_on,
                      ),
                      color: service.isActive
                          ? theme.colorScheme.error
                          : theme.colorScheme.primary,
                      onPressed: isSubmitting ? null : onToggleActive,
                    ),
                  ],
                ],
              ),
              if (service.description != null &&
                  service.description!.trim().isNotEmpty) ...[
                const SizedBox(height: 8),
                Text(
                  service.description!,
                  style: theme.textTheme.bodyMedium,
                ),
              ],
              const SizedBox(height: 12),
              Wrap(
                spacing: 8,
                runSpacing: 8,
                children: [
                  _MetaChip(label: service.price.toStringAsFixed(2)),
                  _MetaChip(
                    label: '${service.taxRatePercent.toStringAsFixed(0)}%',
                  ),
                  _MetaChip(label: service.hsnCode ?? 'HSN -'),
                  _MetaChip(
                    label: service.taxIncluded
                        ? l10n.servicesTaxIncluded
                        : l10n.servicesTaxExcluded,
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _StatusChip extends StatelessWidget {
  const _StatusChip({
    super.key,
    required this.label,
    required this.active,
  });

  final String label;
  final bool active;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final background = active
        ? theme.colorScheme.secondaryContainer
        : theme.colorScheme.errorContainer;
    final foreground = active
        ? theme.colorScheme.onSecondaryContainer
        : theme.colorScheme.onErrorContainer;

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
      decoration: BoxDecoration(
        color: background,
        borderRadius: BorderRadius.circular(4),
      ),
      child: Text(
        label,
        style: theme.textTheme.labelSmall?.copyWith(
          color: foreground,
          letterSpacing: 0,
        ),
      ),
    );
  }
}

class _MetaChip extends StatelessWidget {
  const _MetaChip({required this.label});

  final String label;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
      decoration: BoxDecoration(
        color: theme.colorScheme.surfaceContainerHighest,
        borderRadius: BorderRadius.circular(999),
      ),
      child: Text(
        label,
        style: theme.textTheme.labelSmall?.copyWith(letterSpacing: 0),
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

String _localizeMutationFailure(AppLocalizations l10n, Failure failure) {
  return failure.when(
    validation: (String? message, Map<String, List<String>>? _) =>
        message ?? l10n.servicesMutationErrorGeneric,
    unauthorized: (String? _) => l10n.servicesMutationErrorUnauthorized,
    forbidden: (String? _) => l10n.servicesMutationErrorForbidden,
    notFound: (String? _) => l10n.servicesMutationErrorGeneric,
    server: (String? message, int? _) =>
        message ?? l10n.servicesMutationErrorGeneric,
    network: (String? _) => l10n.servicesMutationErrorNetwork,
    timeout: (String? _) => l10n.servicesMutationErrorTimeout,
    serialization: (String? _) => l10n.servicesMutationErrorGeneric,
    unknown: (String? _) => l10n.servicesMutationErrorGeneric,
  );
}
