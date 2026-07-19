import 'dart:async';

import 'package:intelibill_mobile/src/features/purchase_orders/data/data_sources/purchase_order_draft_local_data_source.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_draft.dart';

class PurchaseOrderDraftPersistence {
  PurchaseOrderDraftPersistence({
    required PurchaseOrderDraftLocalDataSource source,
    required PurchaseOrderDraftLocalKey key,
    required void Function() onSaveSuccess,
    required void Function(Object error) onSaveError,
    DateTime Function()? now,
  }) : _source = source,
       _key = key,
       _onSaveSuccess = onSaveSuccess,
       _onSaveError = onSaveError,
       _now = now ?? DateTime.now;

  static const headerDebounce = Duration(milliseconds: 275);

  final PurchaseOrderDraftLocalDataSource _source;
  final PurchaseOrderDraftLocalKey _key;
  final void Function() _onSaveSuccess;
  final void Function(Object error) _onSaveError;
  final DateTime Function() _now;
  Future<void> _writeTail = Future<void>.value();
  Timer? _headerTimer;
  bool _acceptWrites = true;

  void scheduleHeader(PurchaseOrderDraft Function() readDraft) {
    if (!_acceptWrites) return;
    _headerTimer?.cancel();
    _headerTimer = Timer(headerDebounce, () {
      unawaited(persistNow(readDraft()));
    });
  }

  Future<void> persistNow(PurchaseOrderDraft draft) {
    _headerTimer?.cancel();
    if (!_acceptWrites) return Future<void>.value();
    final record = PurchaseOrderDraftLocalRecord(
      updatedAt: _now().toUtc(),
      draft: draft,
    );
    final operation = _writeTail.then((_) => _write(record));
    _writeTail = operation;
    return operation;
  }

  Future<void> _write(PurchaseOrderDraftLocalRecord record) async {
    if (!_acceptWrites) return;
    try {
      await _source.save(_key, record);
      if (_acceptWrites) _onSaveSuccess();
    } on Object catch (error) {
      if (_acceptWrites) _onSaveError(error);
    }
  }

  Future<void> stopAndRemove() async {
    _acceptWrites = false;
    _headerTimer?.cancel();
    await _writeTail;
    await _source.remove(_key);
  }

  void resume() {
    _acceptWrites = true;
  }

  void dispose() {
    _acceptWrites = false;
    _headerTimer?.cancel();
  }
}
