import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/credit_notes/data/dto/credit_note_dto.dart';

void main() {
  group('CreditNote DTOs', () {
    test('parses list query response', () {
      final dto = CreditNotesResponseDto.fromJson({
        'items': [
          {
            'creditNoteId': 'cn-1',
            'code': 'CN-001',
            'status': 'active',
            'originalAmount': 250,
            'availableBalance': 150,
            'expiresAt': '2026-06-30T00:00:00.000Z',
            'issuedAt': '2026-06-10T10:00:00.000Z',
            'saleReturnId': 'ret-1',
            'returnNumber': 'RET-001',
            'saleId': 'sale-1',
            'invoiceNumber': 'INV-001',
            'customerName': 'John',
          },
        ],
        'totalCount': 1,
        'pageNumber': 1,
        'pageSize': 20,
      });

      expect(dto.items.first.code, 'CN-001');
    });

    test('parses detail dto', () {
      final dto = CreditNoteDto.fromJson({
        'creditNoteId': 'cn-1',
        'code': 'CN-001',
        'status': 'active',
        'originalAmount': 250,
        'availableBalance': 150,
        'expiresAt': null,
        'isVoided': false,
        'saleReturnId': 'ret-1',
        'reason': 'Damaged item',
        'voidReason': null,
        'returnNumber': 'RET-001',
        'invoiceNumber': 'INV-001',
        'customerName': 'John',
      });

      expect(dto.reason, 'Damaged item');
    });

    test('parses print dto', () {
      final dto = CreditNotePrintDto.fromJson({
        'creditNoteId': 'cn-1',
        'code': 'CN-001',
        'status': 'active',
        'isUsable': true,
        'originalAmount': 250,
        'availableBalance': 150,
        'issuedAt': '2026-06-10T10:00:00.000Z',
        'expiresAt': '2026-06-30T00:00:00.000Z',
        'saleId': 'sale-1',
        'invoiceNumber': 'INV-001',
        'saleReturnId': 'ret-1',
        'returnNumber': 'RET-001',
        'customerDisplayName': 'John',
        'reason': 'Damaged item',
        'voidReason': null,
      });

      expect(dto.isUsable, isTrue);
    });
  });
}
