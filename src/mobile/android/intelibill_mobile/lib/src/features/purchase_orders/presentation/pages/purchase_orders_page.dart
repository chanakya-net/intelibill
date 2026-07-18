import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';

class PurchaseOrdersPage extends StatelessWidget {
  const PurchaseOrdersPage({super.key});

  static const pageKey = Key('purchase-orders-page');

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    return Scaffold(
      key: pageKey,
      appBar: AppBar(title: Text(l10n.shellManagePurchaseOrders)),
      body: const Center(child: Text('Purchase Orders')),
    );
  }
}
