import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/storage/preferences_storage.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/data_sources/purchase_order_draft_local_data_source.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_draft.dart';

void main() {
  test('builds keys isolated by user, shop, and new or edit target', () {
    const newKey = PurchaseOrderDraftLocalKey(
      userId: 'user-1',
      shopId: 'shop-1',
      target: 'new',
    );
    const editKey = PurchaseOrderDraftLocalKey(
      userId: 'user-1',
      shopId: 'shop-1',
      target: 'po-1',
    );

    expect(
      newKey.storageKey,
      'purchase_order_draft:v1:user-1:shop-1:new',
    );
    expect(
      editKey.storageKey,
      'purchase_order_draft:v1:user-1:shop-1:po-1',
    );
    expect(
      const PurchaseOrderDraftLocalKey(
        userId: 'user-2',
        shopId: 'shop-1',
        target: 'new',
      ).storageKey,
      isNot(newKey.storageKey),
    );
    expect(
      const PurchaseOrderDraftLocalKey(
        userId: 'user-1',
        shopId: 'shop-2',
        target: 'new',
      ).storageKey,
      isNot(newKey.storageKey),
    );
  });

  test('saves and loads a versioned draft record', () async {
    final storage = _MemoryPreferencesStorage();
    final source = PurchaseOrderDraftLocalDataSource(storage);
    final record = PurchaseOrderDraftLocalRecord(
      updatedAt: DateTime.utc(2026, 7, 19, 12, 30),
      draft: const PurchaseOrderDraft(
        supplierId: 'supplier-1',
        supplierReferenceNumber: 'REF-1',
        notes: 'Keep this',
        lines: [
          PurchaseOrderDraftLine(
            itemId: 'item-1',
            description: 'Widget',
            expectedQuantity: 3,
            unitCost: 12.5,
          ),
        ],
      ),
    );

    await source.save(_newKey, record);

    final raw =
        jsonDecode(storage.values[_newKey.storageKey]!) as Map<String, Object?>;
    expect(raw['version'], 1);
    expect(raw['updatedAt'], '2026-07-19T12:30:00.000Z');
    expect(await source.load(_newKey), record);
  });

  test('rejects unsupported versions without deleting stored data', () async {
    final storage = _MemoryPreferencesStorage();
    final source = PurchaseOrderDraftLocalDataSource(storage);
    storage.values[_newKey.storageKey] = jsonEncode({
      'version': 2,
      'updatedAt': '2026-07-19T12:30:00.000Z',
      'draft': {'lines': <Object?>[]},
    });

    expect(await source.load(_newKey), isNull);
    expect(storage.values, contains(_newKey.storageKey));
  });

  test('returns no record for malformed JSON without deleting it', () async {
    final storage = _MemoryPreferencesStorage();
    final source = PurchaseOrderDraftLocalDataSource(storage);
    storage.values[_newKey.storageKey] = '{not-json';

    expect(await source.load(_newKey), isNull);
    expect(storage.values[_newKey.storageKey], '{not-json');
  });

  test(
    'remove clears only the exact target and never clears preferences',
    () async {
      final storage = _MemoryPreferencesStorage();
      final source = PurchaseOrderDraftLocalDataSource(storage);
      const otherShopKey = PurchaseOrderDraftLocalKey(
        userId: 'user-1',
        shopId: 'shop-2',
        target: 'new',
      );
      const editKey = PurchaseOrderDraftLocalKey(
        userId: 'user-1',
        shopId: 'shop-1',
        target: 'po-1',
      );
      final record = PurchaseOrderDraftLocalRecord(
        updatedAt: DateTime.utc(2026, 7, 19),
        draft: const PurchaseOrderDraft(notes: 'draft'),
      );
      await source.save(_newKey, record);
      await source.save(otherShopKey, record);
      await source.save(editKey, record);

      await source.remove(_newKey);

      expect(storage.values, isNot(contains(_newKey.storageKey)));
      expect(storage.values, contains(otherShopKey.storageKey));
      expect(storage.values, contains(editKey.storageKey));
      expect(storage.clearCalled, isFalse);
    },
  );
}

const _newKey = PurchaseOrderDraftLocalKey(
  userId: 'user-1',
  shopId: 'shop-1',
  target: 'new',
);

class _MemoryPreferencesStorage implements PreferencesStorage {
  final Map<String, String> values = {};
  bool clearCalled = false;

  @override
  Future<void> setString(String key, String value) async {
    values[key] = value;
  }

  @override
  String? getString(String key) => values[key];

  @override
  Future<void> remove(String key) async {
    values.remove(key);
  }

  @override
  Future<void> clear() async {
    clearCalled = true;
    values.clear();
  }

  @override
  Future<void> setInt(String key, int value) async {}

  @override
  int? getInt(String key) => null;

  @override
  Future<void> setBool({required String key, required bool value}) async {}

  @override
  bool? getBool(String key) => null;
}
