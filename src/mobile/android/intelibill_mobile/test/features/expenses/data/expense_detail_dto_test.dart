import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/expenses/data/dto/expense_detail_dto.dart';

void main() {
  group('ExpenseDetailDto', () {
    test('parses every expense detail field', () {
      final dto = ExpenseDetailDto.fromJson({
        'id': 'expense-1',
        'shopId': 'shop-1',
        'categoryId': 'category-1',
        'categoryName': 'Rent',
        'amount': 1250.5,
        'paidTo': 'Landlord',
        'description': 'July rent',
        'expenseDate': '2026-07-01',
        'actorUserId': 'user-1',
        'isVoided': false,
        'originalExpenseId': 'expense-original',
        'supplierLedgerEntryId': 'ledger-1',
        'createdAt': '2026-07-01T08:30:00.000Z',
      });

      expect(dto.id, 'expense-1');
      expect(dto.shopId, 'shop-1');
      expect(dto.categoryId, 'category-1');
      expect(dto.categoryName, 'Rent');
      expect(dto.amount, 1250.5);
      expect(dto.paidTo, 'Landlord');
      expect(dto.description, 'July rent');
      expect(dto.expenseDate, DateTime(2026, 7));
      expect(dto.actorUserId, 'user-1');
      expect(dto.isVoided, isFalse);
      expect(dto.originalExpenseId, 'expense-original');
      expect(dto.supplierLedgerEntryId, 'ledger-1');
      expect(dto.createdAt, DateTime.utc(2026, 7, 1, 8, 30));
    });

    test('accepts nullable description and relationships', () {
      final dto = ExpenseDetailDto.fromJson({
        'id': 'expense-1',
        'shopId': 'shop-1',
        'categoryId': 'category-1',
        'categoryName': 'Rent',
        'amount': 1250,
        'paidTo': 'Landlord',
        'description': null,
        'expenseDate': '2026-07-01',
        'actorUserId': 'user-1',
        'isVoided': true,
        'originalExpenseId': null,
        'supplierLedgerEntryId': null,
        'createdAt': '2026-07-01T08:30:00.000Z',
      });

      expect(dto.description, isNull);
      expect(dto.originalExpenseId, isNull);
      expect(dto.supplierLedgerEntryId, isNull);
      expect(dto.isVoided, isTrue);
    });
  });
}
