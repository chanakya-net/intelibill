import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/app/shell/menu_visibility.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/item.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/controllers/items_controller.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/widgets/create_item_sheet.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/widgets/edit_item_sheet.dart';

class ItemsPage extends ConsumerStatefulWidget {
  const ItemsPage({super.key});

  static const addProductFabKey = Key('items-add-fab');

  @override
  ConsumerState<ItemsPage> createState() => _ItemsPageState();
}

class _ItemsPageState extends ConsumerState<ItemsPage> {
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
    final itemsState = ref.watch(itemsControllerProvider);
    final authState = ref.watch(authControllerProvider);
    final l10n = AppLocalizations.of(context)!;
    final session = authState.value?.session;
    final canManage = canManageInventory(session);

    return Scaffold(
      appBar: AppBar(
        title: Text(l10n.inventoryTitle),
        actions: [
          IconButton(
            tooltip: l10n.shellProfile,
            icon: const Icon(Icons.account_circle),
            onPressed: () => context.push(AppRoutes.profile),
          ),
          PopupMenuButton<_InventoryMenuAction>(
            onSelected: (action) {
              switch (action) {
                case _InventoryMenuAction.addInventory:
                  unawaited(context.push(AppRoutes.inventoryBatch));
                case _InventoryMenuAction.batchOverview:
                  unawaited(context.push(AppRoutes.inventoryBatches));
                case _InventoryMenuAction.adjustmentHistory:
                  unawaited(context.push(AppRoutes.inventoryAdjustments));
              }
            },
            itemBuilder: (context) => [
              PopupMenuItem(
                value: _InventoryMenuAction.addInventory,
                child: Text(l10n.inventoryMenuAddInventory),
              ),
              PopupMenuItem(
                value: _InventoryMenuAction.batchOverview,
                child: Text(l10n.inventoryMenuBatchOverview),
              ),
              PopupMenuItem(
                value: _InventoryMenuAction.adjustmentHistory,
                child: Text(l10n.inventoryMenuAdjustmentHistory),
              ),
            ],
          ),
        ],
      ),
      floatingActionButton: canManage
          ? FloatingActionButton(
              key: ItemsPage.addProductFabKey,
              onPressed: _openCreateSheet,
              tooltip: l10n.inventoryProductsAddProduct,
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
                ref.read(itemsControllerProvider.notifier).updateSearch(query);
              },
            ),
          ),
          Expanded(
            child: _buildBody(context, itemsState, l10n, canManage),
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
      builder: (context) => const CreateItemSheet(),
    );

    if (!mounted || created != true) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(l10n.inventoryProductsCreateSuccess)),
    );
  }

  Future<void> _openEditSheet(Item item) async {
    final l10n = AppLocalizations.of(context)!;
    final updated = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      builder: (context) => EditItemSheet(item: item),
    );

    if (!mounted || updated != true) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(l10n.inventoryProductsUpdateSuccess)),
    );
  }

  Widget _buildBody(
    BuildContext context,
    ItemsState itemsState,
    AppLocalizations l10n,
    bool canManage,
  ) {
    if (itemsState.isLoading) {
      return const Center(child: CircularProgressIndicator());
    }

    if (itemsState.failure != null) {
      return Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Text(
                l10n.inventoryProductsUnableToLoad,
                style: const TextStyle(
                  fontSize: 18,
                  fontWeight: FontWeight.w600,
                ),
              ),
              const SizedBox(height: 8),
              Text(
                _localizeLoadFailure(l10n, itemsState.failure!),
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: 16),
              FilledButton(
                onPressed: () {
                  unawaited(
                    ref.read(itemsControllerProvider.notifier).refresh(),
                  );
                },
                child: Text(l10n.inventoryProductsRetry),
              ),
            ],
          ),
        ),
      );
    }

    final items = itemsState.filteredItems;

    return RefreshIndicator(
      onRefresh: () => ref.read(itemsControllerProvider.notifier).refresh(),
      child: items.isEmpty
          ? ListView(
              physics: const AlwaysScrollableScrollPhysics(),
              children: [
                Padding(
                  padding: const EdgeInsets.all(32),
                  child: Center(
                    child: Text(
                      l10n.inventoryProductsNoProductsFound,
                      style: const TextStyle(fontSize: 16),
                    ),
                  ),
                ),
              ],
            )
          : ListView.builder(
              physics: const AlwaysScrollableScrollPhysics(),
              itemCount: items.length,
              itemBuilder: (context, index) {
                return _ItemCard(
                  item: items[index],
                  canManage: canManage,
                  onTap: canManage ? () => _openEditSheet(items[index]) : null,
                );
              },
            ),
    );
  }
}

enum _InventoryMenuAction { addInventory, batchOverview, adjustmentHistory }

class _ItemCard extends StatelessWidget {
  const _ItemCard({
    required this.item,
    required this.canManage,
    this.onTap,
  });

  final Item item;
  final bool canManage;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final l10n = AppLocalizations.of(context)!;
    final initials = _initials(item.name);
    final avatarColor = _stableColor(item.name, theme);

    return Card(
      margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
      child: ListTile(
        onTap: onTap,
        leading: CircleAvatar(
          backgroundColor: avatarColor,
          child: Text(
            initials,
            style: theme.textTheme.labelLarge?.copyWith(
              color: Colors.white,
              fontWeight: FontWeight.w600,
            ),
          ),
        ),
        title: Row(
          children: [
            Expanded(
              child: Text(
                item.name,
                style: theme.textTheme.titleSmall,
              ),
            ),
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
              decoration: BoxDecoration(
                color: item.isActive
                    ? theme.colorScheme.secondaryContainer
                    : theme.colorScheme.errorContainer,
                borderRadius: BorderRadius.circular(4),
              ),
              child: Text(
                item.isActive
                    ? l10n.inventoryCreateProductActiveLabel
                    : l10n.inventoryProductsInactive,
                style: theme.textTheme.labelSmall?.copyWith(
                  color: item.isActive
                      ? theme.colorScheme.onSecondaryContainer
                      : theme.colorScheme.onErrorContainer,
                ),
              ),
            ),
          ],
        ),
        subtitle: Row(
          children: [
            Flexible(
              child: Wrap(
                spacing: 8,
                runSpacing: 6,
                children: [
                  _MetaChip(label: item.barcode),
                  _MetaChip(label: item.uom),
                  _StockChip(stock: item.currentStock),
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

class _StockChip extends StatelessWidget {
  const _StockChip({required this.stock});

  final double stock;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final Color color;
    if (stock <= 5) {
      color = theme.colorScheme.error;
    } else if (stock < 50) {
      color = theme.colorScheme.tertiary;
    } else {
      color = theme.colorScheme.secondary;
    }

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.15),
        borderRadius: BorderRadius.circular(4),
        border: Border.all(color: color.withValues(alpha: 0.4)),
      ),
      child: Text(
        stock.toStringAsFixed(stock.truncateToDouble() == stock ? 0 : 2),
        style: theme.textTheme.labelSmall?.copyWith(color: color),
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
      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
      decoration: BoxDecoration(
        color: theme.colorScheme.surfaceContainerHighest,
        borderRadius: BorderRadius.circular(4),
      ),
      child: Text(
        label,
        style: theme.textTheme.labelSmall,
      ),
    );
  }
}

String _localizeLoadFailure(AppLocalizations l10n, Failure failure) {
  return failure.when(
    validation: (String? message, Map<String, List<String>>? _) =>
        message ?? l10n.inventoryProductErrorGeneric,
    unauthorized: (String? _) => l10n.inventoryProductErrorUnauthorized,
    forbidden: (String? _) => l10n.inventoryProductErrorForbidden,
    notFound: (String? _) => l10n.inventoryProductErrorGeneric,
    server: (String? message, int? _) =>
        message ?? l10n.inventoryProductErrorGeneric,
    network: (String? _) => l10n.inventoryProductErrorNetwork,
    timeout: (String? _) => l10n.inventoryProductErrorTimeout,
    serialization: (String? _) => l10n.inventoryProductErrorGeneric,
    unknown: (String? _) => l10n.inventoryProductErrorGeneric,
  );
}
