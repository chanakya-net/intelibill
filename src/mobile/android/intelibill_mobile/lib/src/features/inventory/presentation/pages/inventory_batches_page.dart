import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/app/shell/menu_visibility.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/inventory_batch.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/controllers/inventory_batches_controller.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/widgets/adjust_batch_sheet.dart';

class InventoryBatchesPage extends ConsumerStatefulWidget {
  const InventoryBatchesPage({super.key});

  @override
  ConsumerState<InventoryBatchesPage> createState() =>
      _InventoryBatchesPageState();
}

class _InventoryBatchesPageState extends ConsumerState<InventoryBatchesPage> {
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
    final batchesState = ref.watch(inventoryBatchesControllerProvider);
    final authState = ref.watch(authControllerProvider);
    final l10n = AppLocalizations.of(context)!;
    final session = authState.value?.session;
    final canManage = canManageInventory(session);

    return Scaffold(
      appBar: AppBar(
        title: Text(l10n.shellInventoryBatchesOverview),
        leading: IconButton(
          icon: const Icon(Icons.arrow_back),
          onPressed: () => context.canPop() ? context.pop() : null,
        ),
        actions: [
          IconButton(
            tooltip: l10n.shellProfile,
            icon: const Icon(Icons.account_circle),
            onPressed: () => context.push(AppRoutes.profile),
          ),
        ],
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
                    .read(inventoryBatchesControllerProvider.notifier)
                    .updateSearch(query);
              },
            ),
          ),
          Expanded(
            child: _buildBody(context, batchesState, l10n, canManage),
          ),
        ],
      ),
    );
  }

  Widget _buildBody(
    BuildContext context,
    InventoryBatchesState batchesState,
    AppLocalizations l10n,
    bool canManage,
  ) {
    if (batchesState.isLoading) {
      return const Center(child: CircularProgressIndicator());
    }

    if (batchesState.failure != null) {
      return Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Text(
                l10n.inventoryBatchesUnableToLoad,
                style: const TextStyle(
                  fontSize: 18,
                  fontWeight: FontWeight.w600,
                ),
              ),
              const SizedBox(height: 8),
              Text(
                _localizeLoadFailure(l10n, batchesState.failure!),
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: 16),
              FilledButton(
                onPressed: () {
                  unawaited(
                    ref
                        .read(inventoryBatchesControllerProvider.notifier)
                        .refresh(),
                  );
                },
                child: Text(l10n.inventoryBatchesRetry),
              ),
            ],
          ),
        ),
      );
    }

    final batches = batchesState.filteredBatches;

    return RefreshIndicator(
      onRefresh: () =>
          ref.read(inventoryBatchesControllerProvider.notifier).refresh(),
      child: batches.isEmpty
          ? ListView(
              physics: const AlwaysScrollableScrollPhysics(),
              children: [
                Padding(
                  padding: const EdgeInsets.all(32),
                  child: Center(
                    child: Text(
                      l10n.inventoryBatchesNoBatchesFound,
                      style: const TextStyle(fontSize: 16),
                    ),
                  ),
                ),
              ],
            )
          : ListView.builder(
              physics: const AlwaysScrollableScrollPhysics(),
              itemCount: batches.length,
              itemBuilder: (context, index) {
                return _BatchCard(
                  batch: batches[index],
                  canManage: canManage,
                  onAdjust: () => _openAdjustSheet(batches[index], canManage),
                );
              },
            ),
    );
  }

  Future<void> _openAdjustSheet(InventoryBatch batch, bool canManage) async {
    final l10n = AppLocalizations.of(context)!;
    final adjusted = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      builder: (context) =>
          AdjustBatchSheet(batch: batch, canManage: canManage),
    );

    if (!mounted || adjusted != true) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(l10n.inventoryBatchesAdjustSuccess)),
    );
  }
}

class _BatchCard extends StatelessWidget {
  const _BatchCard({
    required this.batch,
    required this.canManage,
    required this.onAdjust,
  });

  final InventoryBatch batch;
  final bool canManage;
  final VoidCallback onAdjust;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final l10n = AppLocalizations.of(context)!;
    final initials = _initials(batch.itemName);
    final avatarColor = _stableColor(batch.itemName, theme);

    return Card(
      margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            CircleAvatar(
              backgroundColor: avatarColor,
              child: Text(
                initials,
                style: theme.textTheme.labelLarge?.copyWith(
                  color: Colors.white,
                  fontWeight: FontWeight.w600,
                ),
              ),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Expanded(
                        child: Text(
                          batch.itemName,
                          style: theme.textTheme.titleSmall,
                        ),
                      ),
                      if (batch.isVoided)
                        Chip(
                          label: Text(l10n.inventoryBatchesVoided),
                          backgroundColor: theme.colorScheme.errorContainer,
                          labelStyle: theme.textTheme.labelSmall?.copyWith(
                            color: theme.colorScheme.onErrorContainer,
                          ),
                          padding: EdgeInsets.zero,
                          materialTapTargetSize:
                              MaterialTapTargetSize.shrinkWrap,
                        ),
                    ],
                  ),
                  Text(
                    batch.batchNumber,
                    style: theme.textTheme.bodySmall?.copyWith(
                      color: theme.colorScheme.onSurfaceVariant,
                    ),
                  ),
                  const SizedBox(height: 6),
                  Wrap(
                    spacing: 8,
                    runSpacing: 4,
                    children: [
                      _MetaChip(
                        label: 'Qty: ${_formatQty(batch.quantity)}',
                      ),
                      _MetaChip(
                        label: 'Cost: ₹${batch.costPrice.toStringAsFixed(2)}',
                      ),
                      _MetaChip(
                        label: 'Sale: ₹${batch.salesPrice.toStringAsFixed(2)}',
                      ),
                    ],
                  ),
                  if (batch.expiryDate != null || batch.supplierName != null)
                    Padding(
                      padding: const EdgeInsets.only(top: 4),
                      child: Wrap(
                        spacing: 8,
                        runSpacing: 4,
                        children: [
                          if (batch.expiryDate != null)
                            _MetaChip(
                              label:
                                  'Exp: ${batch.expiryDate!.day.toString().padLeft(2, '0')}/${batch.expiryDate!.month.toString().padLeft(2, '0')}/${batch.expiryDate!.year}',
                            ),
                          if (batch.supplierName != null)
                            _MetaChip(label: batch.supplierName!),
                        ],
                      ),
                    ),
                  if (!batch.isVoided && canManage)
                    Align(
                      alignment: Alignment.centerRight,
                      child: TextButton(
                        onPressed: onAdjust,
                        child: Text(l10n.inventoryBatchesAdjustAction),
                      ),
                    ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  String _initials(String name) {
    final parts = name.trim().split(RegExp(r'\s+'));
    if (parts.isEmpty) return '?';
    if (parts.length == 1) {
      return parts[0].isNotEmpty ? parts[0][0].toUpperCase() : '?';
    }
    final first = parts[0].isNotEmpty ? parts[0][0].toUpperCase() : '';
    final last = parts.last.isNotEmpty ? parts.last[0].toUpperCase() : '';
    return '$first$last';
  }

  Color _stableColor(String name, ThemeData theme) {
    final hash = name.codeUnits.fold(0, (acc, c) => acc * 31 + c);
    final colors = [
      theme.colorScheme.primary,
      theme.colorScheme.secondary,
      theme.colorScheme.tertiary,
      Colors.teal,
      Colors.indigo,
      Colors.deepOrange,
    ];
    return colors[hash.abs() % colors.length];
  }
}

class _MetaChip extends StatelessWidget {
  const _MetaChip({required this.label});

  final String label;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
      decoration: BoxDecoration(
        color: theme.colorScheme.surfaceContainerHighest,
        borderRadius: BorderRadius.circular(4),
      ),
      child: Text(label, style: theme.textTheme.labelSmall),
    );
  }
}

String _formatQty(double qty) {
  return qty.toStringAsFixed(qty.truncateToDouble() == qty ? 0 : 2);
}

String _localizeLoadFailure(AppLocalizations l10n, Failure failure) {
  return failure.when(
    validation: (String? message, Map<String, List<String>>? _) =>
        message ?? l10n.inventoryBatchesErrorGeneric,
    unauthorized: (String? _) => l10n.inventoryBatchesErrorUnauthorized,
    forbidden: (String? _) => l10n.inventoryBatchesErrorForbidden,
    notFound: (String? _) => l10n.inventoryBatchesErrorGeneric,
    server: (String? message, int? _) =>
        message ?? l10n.inventoryBatchesErrorGeneric,
    network: (String? _) => l10n.inventoryBatchesErrorNetwork,
    timeout: (String? _) => l10n.inventoryBatchesErrorTimeout,
    serialization: (String? _) => l10n.inventoryBatchesErrorGeneric,
    unknown: (String? _) => l10n.inventoryBatchesErrorGeneric,
  );
}
