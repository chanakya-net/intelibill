import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_filters.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';

void main() {
  group('PurchaseOrderFilters', () {
    test('constructs with all fields', () {
      final from = DateTime(2026, 1, 1);
      final to = DateTime(2026, 12, 31);
      final filters = PurchaseOrderFilters(
        search: 'test',
        status: PurchaseOrderStatus.placed,
        orderDateFrom: from,
        orderDateTo: to,
        page: 2,
        pageSize: 50,
      );

      expect(filters.search, 'test');
      expect(filters.status, PurchaseOrderStatus.placed);
      expect(filters.orderDateFrom, from);
      expect(filters.orderDateTo, to);
      expect(filters.page, 2);
      expect(filters.pageSize, 50);
    });

    test('defaults omitted fields', () {
      final filters = const PurchaseOrderFilters();

      expect(filters.search, isNull);
      expect(filters.status, isNull);
      expect(filters.orderDateFrom, isNull);
      expect(filters.orderDateTo, isNull);
      expect(filters.page, 1);
      expect(filters.pageSize, 20);
    });

    test('preserves all status values', () {
      for (final status in PurchaseOrderStatus.values) {
        final filters = PurchaseOrderFilters(status: status);
        expect(filters.status, status);
      }
    });

    test('copyWith preserves search and status', () {
      final from = DateTime(2026, 1, 1);
      final to = DateTime(2026, 12, 31);
      final original = PurchaseOrderFilters(
        search: 'widget',
        status: PurchaseOrderStatus.received,
        orderDateFrom: from,
        orderDateTo: to,
        page: 1,
        pageSize: 20,
      );

      final updated = original.copyWith(page: 2, pageSize: 50);

      expect(updated.search, 'widget');
      expect(updated.status, PurchaseOrderStatus.received);
      expect(updated.orderDateFrom, from);
      expect(updated.orderDateTo, to);
      expect(updated.page, 2);
      expect(updated.pageSize, 50);
    });

    test('copyWith can override search', () {
      const original = PurchaseOrderFilters(search: 'old');
      final updated = original.copyWith(
        search: 'new',
        page: 1,
        pageSize: 20,
      );

      expect(updated.search, 'new');
    });

    test('copyWith can override status', () {
      const original = PurchaseOrderFilters(
        status: PurchaseOrderStatus.draft,
      );
      final updated = original.copyWith(
        status: PurchaseOrderStatus.placed,
        page: 1,
        pageSize: 20,
      );

      expect(updated.status, PurchaseOrderStatus.placed);
    });

    test('copyWith can override orderDateFrom', () {
      final from1 = DateTime(2026, 1, 1);
      final from2 = DateTime(2026, 2, 1);
      final original = PurchaseOrderFilters(
        orderDateFrom: from1,
      );
      final updated = original.copyWith(
        orderDateFrom: from2,
        page: 1,
        pageSize: 20,
      );

      expect(updated.orderDateFrom, from2);
    });

    test('copyWith can override orderDateTo', () {
      final to1 = DateTime(2026, 12, 31);
      final to2 = DateTime(2026, 6, 30);
      final original = PurchaseOrderFilters(
        orderDateTo: to1,
      );
      final updated = original.copyWith(
        orderDateTo: to2,
        page: 1,
        pageSize: 20,
      );

      expect(updated.orderDateTo, to2);
    });

    test('equals when all fields match', () {
      final from = DateTime(2026, 1, 1);
      final to = DateTime(2026, 12, 31);
      final a = PurchaseOrderFilters(
        search: 'test',
        status: PurchaseOrderStatus.placed,
        orderDateFrom: from,
        orderDateTo: to,
        page: 2,
        pageSize: 50,
      );
      final b = PurchaseOrderFilters(
        search: 'test',
        status: PurchaseOrderStatus.placed,
        orderDateFrom: from,
        orderDateTo: to,
        page: 2,
        pageSize: 50,
      );

      expect(a, b);
    });

    test('differs when search differs', () {
      const a = PurchaseOrderFilters(search: 'a');
      const b = PurchaseOrderFilters(search: 'b');

      expect(a, isNot(b));
    });

    test('differs when status differs', () {
      const a = PurchaseOrderFilters(status: PurchaseOrderStatus.draft);
      const b = PurchaseOrderFilters(status: PurchaseOrderStatus.placed);

      expect(a, isNot(b));
    });

    test('differs when orderDateFrom differs', () {
      final a = PurchaseOrderFilters(
        orderDateFrom: DateTime(2026, 1, 1),
      );
      final b = PurchaseOrderFilters(
        orderDateFrom: DateTime(2026, 2, 1),
      );

      expect(a, isNot(b));
    });

    test('differs when orderDateTo differs', () {
      final a = PurchaseOrderFilters(
        orderDateTo: DateTime(2026, 12, 31),
      );
      final b = PurchaseOrderFilters(
        orderDateTo: DateTime(2026, 6, 30),
      );

      expect(a, isNot(b));
    });

    test('differs when page differs', () {
      const a = PurchaseOrderFilters(page: 1);
      const b = PurchaseOrderFilters(page: 2);

      expect(a, isNot(b));
    });

    test('differs when pageSize differs', () {
      const a = PurchaseOrderFilters(pageSize: 20);
      const b = PurchaseOrderFilters(pageSize: 50);

      expect(a, isNot(b));
    });
  });
}
