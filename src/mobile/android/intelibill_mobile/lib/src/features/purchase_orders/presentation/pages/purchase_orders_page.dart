import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_orders_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_card.dart';

class PurchaseOrdersPage extends ConsumerStatefulWidget {
  const PurchaseOrdersPage({super.key});

  static const pageKey = Key('purchase-orders-page');
  static const countKey = Key('purchase-orders-count');
  static const searchFieldKey = Key('search-field');

  @override
  ConsumerState<PurchaseOrdersPage> createState() => _PurchaseOrdersPageState();
}

class _PurchaseOrdersPageState extends ConsumerState<PurchaseOrdersPage> {
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
    final l10n = AppLocalizations.of(context)!;
    final state = ref.watch(purchaseOrdersControllerProvider);
    return Scaffold(
      key: PurchaseOrdersPage.pageKey,
      appBar: AppBar(title: Text(l10n.shellManagePurchaseOrders)),
      body: _buildBody(context, state),
    );
  }

  Widget _buildBody(
    BuildContext context,
    PurchaseOrdersState state,
  ) {
    if (state.isInitialLoading) {
      return const Center(child: CircularProgressIndicator());
    }
    if (state.failure != null) return _FailureView(failure: state.failure!);

    final hasActiveSearch = _searchController.text.trim().isNotEmpty;
    final hasResults = state.items.isNotEmpty;

    final searchBar = Padding(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
      child: SearchBar(
        controller: _searchController,
        key: PurchaseOrdersPage.searchFieldKey,
        hintText: 'Search purchase orders',
        onChanged: (value) {
          ref
              .read(purchaseOrdersControllerProvider.notifier)
              .updateSearch(value);
          setState(() {});
        },
      ),
    );

    if (!hasResults && hasActiveSearch) {
      return Column(
        children: [
          searchBar,
          Expanded(
            child: _FilteredEmptyView(query: _searchController.text),
          ),
        ],
      );
    }
    if (state.isEmpty) {
      return Column(
        children: [
          searchBar,
          const Expanded(child: _EmptyView()),
        ],
      );
    }

    return Column(
      children: [
        searchBar,
        Expanded(
          child: RefreshIndicator(
            onRefresh: () =>
                ref.read(purchaseOrdersControllerProvider.notifier).refresh(),
            child: ListView.builder(
              physics: const AlwaysScrollableScrollPhysics(),
              padding: const EdgeInsets.only(top: 8, bottom: 24),
              itemCount: state.items.length + 1,
              itemBuilder: (context, index) {
                if (index == 0) {
                  return Padding(
                    padding: const EdgeInsets.fromLTRB(16, 8, 16, 4),
                    child: Text(
                      '${state.totalCount} purchase order${state.totalCount == 1 ? '' : 's'}',
                      key: PurchaseOrdersPage.countKey,
                      style: Theme.of(context).textTheme.titleSmall,
                    ),
                  );
                }
                final purchaseOrder = state.items[index - 1];
                return PurchaseOrderCard(
                  purchaseOrder: purchaseOrder,
                  onTap: () => GoRouter.of(context).go(
                    AppRoutes.purchaseOrderDetailsFor(
                      purchaseOrder.purchaseOrderId,
                    ),
                  ),
                );
              },
            ),
          ),
        ),
      ],
    );
  }
}

class _FailureView extends ConsumerWidget {
  const _FailureView({required this.failure});

  final Failure failure;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final isSerializationFailure = failure is SerializationFailure;
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Icon(Icons.error_outline, size: 48),
            const SizedBox(height: 12),
            Text(
              isSerializationFailure
                  ? 'Data could not be read.'
                  : 'Could not load purchase orders.',
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: 16),
            FilledButton(
              onPressed: () =>
                  ref.read(purchaseOrdersControllerProvider.notifier).retry(),
              child: const Text('Retry'),
            ),
          ],
        ),
      ),
    );
  }
}

class _EmptyView extends StatelessWidget {
  const _EmptyView();

  @override
  Widget build(BuildContext context) {
    return const Center(child: Text('No purchase orders yet'));
  }
}

class _FilteredEmptyView extends StatelessWidget {
  const _FilteredEmptyView({required this.query});

  final String query;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Icon(Icons.search_off, size: 48),
            const SizedBox(height: 12),
            Text(
              'No results for "$query"',
              textAlign: TextAlign.center,
            ),
          ],
        ),
      ),
    );
  }
}
