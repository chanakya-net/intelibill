import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/entities/supplier.dart';
import 'package:intl/intl.dart';

final NumberFormat _supplierBalanceFormatter = NumberFormat.currency(
  locale: 'en_IN',
  symbol: '₹',
  decimalDigits: 2,
);

class SupplierCard extends StatelessWidget {
  const SupplierCard({required this.supplier, super.key});

  final Supplier supplier;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final l10n = AppLocalizations.of(context)!;
    final location = _formatLocation(supplier);
    final avatarColor = _stableColor(supplier.name, theme);

    return Card(
      margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
      child: ListTile(
        contentPadding: const EdgeInsets.fromLTRB(16, 12, 16, 12),
        leading: CircleAvatar(
          backgroundColor: avatarColor,
          child: Text(
            _initials(supplier.name),
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
                supplier.name,
                style: theme.textTheme.titleSmall,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
              ),
            ),
            const SizedBox(width: 8),
            _StatusBadge(
              label: supplier.isActive
                  ? l10n.suppliersCreateActiveLabel
                  : l10n.suppliersInactive,
              backgroundColor: supplier.isActive
                  ? theme.colorScheme.secondaryContainer
                  : theme.colorScheme.errorContainer,
              foregroundColor: supplier.isActive
                  ? theme.colorScheme.onSecondaryContainer
                  : theme.colorScheme.onErrorContainer,
            ),
            if (supplier.isPreferred) ...[
              const SizedBox(width: 4),
              _StatusBadge(
                label: l10n.suppliersPreferred,
                backgroundColor: theme.colorScheme.primaryContainer,
                foregroundColor: theme.colorScheme.onPrimaryContainer,
              ),
            ],
          ],
        ),
        subtitle: Padding(
          padding: const EdgeInsets.only(top: 8),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              if (supplier.contactPersonName != null &&
                  supplier.contactPersonName!.trim().isNotEmpty)
                _InfoRow(
                  icon: Icons.person_outline,
                  text: supplier.contactPersonName!,
                ),
              if (supplier.contactPersonPhone != null &&
                  supplier.contactPersonPhone!.trim().isNotEmpty)
                _InfoRow(
                  icon: Icons.phone_outlined,
                  text: supplier.contactPersonPhone!,
                ),
              if (location != null)
                _InfoRow(icon: Icons.location_on_outlined, text: location),
              if (supplier.balanceDue != 0) ...[
                const SizedBox(height: 8),
                _BalanceChip(
                  label: l10n.suppliersBalanceDueLabel,
                  amount: _supplierBalanceFormatter.format(
                    supplier.balanceDue.abs(),
                  ),
                  isPayable: supplier.balanceDue > 0,
                ),
              ],
            ],
          ),
        ),
      ),
    );
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

class _StatusBadge extends StatelessWidget {
  const _StatusBadge({
    required this.label,
    required this.backgroundColor,
    required this.foregroundColor,
  });

  final String label;
  final Color backgroundColor;
  final Color foregroundColor;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
      decoration: BoxDecoration(
        color: backgroundColor,
        borderRadius: BorderRadius.circular(4),
      ),
      child: Text(
        label,
        style: Theme.of(context).textTheme.labelSmall?.copyWith(
          color: foregroundColor,
          fontWeight: FontWeight.w600,
        ),
      ),
    );
  }
}

class _InfoRow extends StatelessWidget {
  const _InfoRow({required this.icon, required this.text});

  final IconData icon;
  final String text;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Padding(
      padding: const EdgeInsets.only(bottom: 4),
      child: Row(
        children: [
          Icon(icon, size: 16, color: theme.colorScheme.onSurfaceVariant),
          const SizedBox(width: 6),
          Expanded(
            child: Text(
              text,
              style: theme.textTheme.bodySmall?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
            ),
          ),
        ],
      ),
    );
  }
}

class _BalanceChip extends StatelessWidget {
  const _BalanceChip({
    required this.label,
    required this.amount,
    required this.isPayable,
  });

  final String label;
  final String amount;
  final bool isPayable;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final color = isPayable
        ? theme.colorScheme.error
        : theme.colorScheme.primary;

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.12),
        borderRadius: BorderRadius.circular(6),
        border: Border.all(color: color.withValues(alpha: 0.35)),
      ),
      child: Text(
        '$label $amount',
        style: theme.textTheme.labelMedium?.copyWith(
          color: color,
          fontWeight: FontWeight.w600,
        ),
      ),
    );
  }
}
