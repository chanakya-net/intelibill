import 'dart:convert';

import 'package:equatable/equatable.dart';
import 'package:flutter/foundation.dart';
import 'package:intelibill_mobile/src/core/storage/preferences_storage.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_draft.dart';

@immutable
class PurchaseOrderDraftLocalKey {
  const PurchaseOrderDraftLocalKey({
    required this.userId,
    required this.shopId,
    required this.target,
  });

  final String userId;
  final String shopId;
  final String target;

  String get storageKey => 'purchase_order_draft:v1:$userId:$shopId:$target';
}

@immutable
class PurchaseOrderDraftLocalRecord extends Equatable {
  const PurchaseOrderDraftLocalRecord({
    required this.updatedAt,
    required this.draft,
    this.version = currentVersion,
  });

  static const currentVersion = 1;

  final int version;
  final DateTime updatedAt;
  final PurchaseOrderDraft draft;

  Map<String, Object?> toJson() => {
    'version': version,
    'updatedAt': updatedAt.toUtc().toIso8601String(),
    'draft': _draftToJson(draft),
  };

  static PurchaseOrderDraftLocalRecord? tryParse(String value) {
    try {
      final decoded = jsonDecode(value);
      if (decoded is! Map<String, dynamic>) return null;
      final json = decoded;
      if (json['version'] != currentVersion) return null;
      final updatedAt = json['updatedAt'];
      final draftJson = json['draft'];
      if (updatedAt is! String || draftJson is! Map<String, dynamic>) {
        return null;
      }
      final draft = _draftFromJson(draftJson);
      if (draft == null) return null;
      return PurchaseOrderDraftLocalRecord(
        updatedAt: DateTime.parse(updatedAt).toUtc(),
        draft: draft,
      );
    } on FormatException {
      return null;
    }
  }

  @override
  List<Object?> get props => [version, updatedAt, draft];
}

class PurchaseOrderDraftLocalDataSource {
  const PurchaseOrderDraftLocalDataSource(this._storage);

  final PreferencesStorage _storage;

  Future<PurchaseOrderDraftLocalRecord?> load(
    PurchaseOrderDraftLocalKey key,
  ) async {
    final value = _storage.getString(key.storageKey);
    return value == null ? null : PurchaseOrderDraftLocalRecord.tryParse(value);
  }

  Future<void> save(
    PurchaseOrderDraftLocalKey key,
    PurchaseOrderDraftLocalRecord record,
  ) {
    return _storage.setString(key.storageKey, jsonEncode(record.toJson()));
  }

  Future<void> remove(PurchaseOrderDraftLocalKey key) {
    return _storage.remove(key.storageKey);
  }
}

Map<String, Object?> _draftToJson(PurchaseOrderDraft draft) => {
  'supplierId': draft.supplierId,
  'orderDate': draft.orderDate?.toIso8601String(),
  'expectedDeliveryDate': draft.expectedDeliveryDate?.toIso8601String(),
  'supplierReferenceNumber': draft.supplierReferenceNumber,
  'notes': draft.notes,
  'supplierName': draft.supplierName,
  'supplierReference': draft.supplierReference,
  'lines': draft.lines
      .map(
        (line) => <String, Object?>{
          'itemId': line.itemId,
          'description': line.description,
          'expectedQuantity': line.expectedQuantity,
          'unitCost': line.unitCost,
        },
      )
      .toList(growable: false),
};

PurchaseOrderDraft? _draftFromJson(Map<String, dynamic> json) {
  final linesJson = json['lines'];
  if (linesJson is! List<dynamic> || !_hasValidOptionalStrings(json)) {
    return null;
  }
  final lines = <PurchaseOrderDraftLine>[];
  for (final value in linesJson) {
    final line = _lineFromJson(value);
    if (line == null) return null;
    lines.add(line);
  }
  return PurchaseOrderDraft(
    supplierId: _stringOrNull(json['supplierId']),
    orderDate: _dateFromJson(json['orderDate']),
    expectedDeliveryDate: _dateFromJson(json['expectedDeliveryDate']),
    supplierReferenceNumber: _stringOrNull(
      json['supplierReferenceNumber'],
    ),
    notes: _stringOrNull(json['notes']),
    supplierName: _stringOrNull(json['supplierName']),
    supplierReference: _stringOrNull(json['supplierReference']),
    lines: lines,
  );
}

bool _hasValidOptionalStrings(Map<String, dynamic> json) {
  const keys = [
    'supplierId',
    'supplierReferenceNumber',
    'notes',
    'supplierName',
    'supplierReference',
  ];
  return keys.every((key) {
    final value = json[key];
    return value == null || value is String;
  });
}

String? _stringOrNull(Object? value) => value is String ? value : null;

DateTime? _dateFromJson(Object? value) {
  if (value == null) return null;
  if (value is! String) throw const FormatException('Invalid date');
  return DateTime.parse(value);
}

PurchaseOrderDraftLine? _lineFromJson(Object? value) {
  if (value is! Map<String, dynamic>) return null;
  final itemId = value['itemId'];
  final description = value['description'];
  final quantity = value['expectedQuantity'];
  final unitCost = value['unitCost'];
  if (itemId is! String ||
      description is! String ||
      quantity is! num ||
      unitCost is! num) {
    return null;
  }
  return PurchaseOrderDraftLine(
    itemId: itemId,
    description: description,
    expectedQuantity: quantity.toInt(),
    unitCost: unitCost.toDouble(),
  );
}
