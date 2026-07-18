import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_orders_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_card.dart';

class PurchaseOrdersPage extends ConsumerWidget {
  const PurchaseOrdersPage({super.key});

  static const pageKey = Key('purchase-orders-page');
  static const countKey = Key('purchase-orders-count');

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context)!;
    final state = ref.watch(purchaseOrdersControllerProvider);
    return Scaffold(
      key: pageKey,
      appBar: AppBar(title: Text(l10n.shellManagePurchaseOrders)),
      body: _buildBody(context, ref, state),
    );
  }

  Widget _buildBody(
    BuildContext context,
    WidgetRef ref,
    PurchaseOrdersState state,
  ) {
    if (state.isInitialLoading) {
      return const Center(child: CircularProgressIndicator());
    }
    if (state.failure != null) return _FailureView(failure: state.failure!);
    if (state.isEmpty) return const _EmptyView();

    return RefreshIndicator(
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
                key: countKey,
                style: Theme.of(context).textTheme.titleSmall,
              ),
            );
          }
          final purchaseOrder = state.items[index - 1];
          return PurchaseOrderCard(
            purchaseOrder: purchaseOrder,
            onTap: () => GoRouter.of(context).go(
              AppRoutes.purchaseOrderDetailsFor(purchaseOrder.purchaseOrderId),
            ),
          );
        },
      ),
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
