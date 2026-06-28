import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/sale_detail_controller.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/widgets/receipts/sale_receipt_view.dart';

class SalesReceiptPage extends ConsumerWidget {
  const SalesReceiptPage({required this.saleId, this.initialSale, super.key});

  final String saleId;
  final SaleDetail? initialSale;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final state = ref.watch(saleDetailControllerProvider(saleId));
    final l10n = AppLocalizations.of(context)!;
    final sale = state.detail ?? initialSale;

    if (state.isLoading && sale == null) {
      return Scaffold(
        appBar: AppBar(title: Text(l10n.salesDetailReceipt)),
        body: const Center(child: CircularProgressIndicator()),
      );
    }

    if (state.failure != null && sale == null) {
      return Scaffold(
        appBar: AppBar(title: Text(l10n.salesDetailReceipt)),
        body: Center(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                const Icon(Icons.error_outline, size: 48),
                const SizedBox(height: 12),
                Text(l10n.salesDetailUnableToLoad),
                const SizedBox(height: 12),
                FilledButton(
                  onPressed: () =>
                      ref.invalidate(saleDetailControllerProvider(saleId)),
                  child: Text(l10n.salesHistoryRetry),
                ),
              ],
            ),
          ),
        ),
      );
    }

    if (sale == null) {
      return Scaffold(
        appBar: AppBar(title: Text(l10n.salesDetailReceipt)),
        body: const SizedBox.shrink(),
      );
    }

    return Scaffold(
      appBar: AppBar(title: Text(l10n.salesDetailReceipt)),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          Card(
            child: Padding(
              padding: const EdgeInsets.all(16),
              child: SalesReceiptView(sale: sale),
            ),
          ),
        ],
      ),
    );
  }
}
