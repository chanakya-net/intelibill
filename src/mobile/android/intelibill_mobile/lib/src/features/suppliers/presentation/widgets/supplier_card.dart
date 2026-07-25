import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/entities/supplier.dart';

class SupplierCard extends StatelessWidget {
  const SupplierCard({required this.supplier, super.key});

  final Supplier supplier;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final l10n = AppLocalizations.of(context)!;
    final location = _formatLocation(supplier);
    final contactLine = _primaryContactLine(supplier);

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
                    supplier.name,
                    style: theme.textTheme.titleMedium,
                  ),
                ),
                if (!supplier.isActive)
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
                      l10n.suppliersInactive,
                      style: theme.textTheme.labelSmall?.copyWith(
                        color: theme.colorScheme.onErrorContainer,
                      ),
                    ),
                  ),
              ],
            ),
            if (contactLine != null) ...[
              const SizedBox(height: 4),
              Text(contactLine, style: theme.textTheme.bodyMedium),
            ],
            if (location != null) ...[
              const SizedBox(height: 2),
              Text(location, style: theme.textTheme.bodySmall),
            ],
            if (supplier.balanceDue > 0) ...[
              const SizedBox(height: 8),
              Text(
                '${l10n.suppliersBalanceDueLabel} '
                '₹${supplier.balanceDue.toStringAsFixed(2)}',
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

  String? _primaryContactLine(Supplier supplier) {
    final contactPerson = supplier.contactPersonName?.trim();
    if (contactPerson != null && contactPerson.isNotEmpty) {
      return contactPerson;
    }

    final phone = supplier.contactPersonPhone?.trim();
    if (phone != null && phone.isNotEmpty) {
      return phone;
    }

    return null;
  }

  String? _formatLocation(Supplier supplier) {
    final cityState = [
      if (supplier.city != null && supplier.city!.trim().isNotEmpty)
        supplier.city!.trim(),
      if (supplier.state != null && supplier.state!.trim().isNotEmpty)
        supplier.state!.trim(),
    ].join(', ');

    if (cityState.isNotEmpty) {
      return cityState;
    }

    final address = supplier.address?.trim();
    return address == null || address.isEmpty ? null : address;
  }
}
