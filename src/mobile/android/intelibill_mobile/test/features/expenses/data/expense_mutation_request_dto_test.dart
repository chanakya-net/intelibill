import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/expenses/data/dto/expense_mutation_request_dto.dart';

void main() {
  test('serializes DateOnly and trims nullable description', () {
    final request = ExpenseMutationRequestDto(
      categoryName: '  Rent  ',
      amount: 125.5,
      paidTo: '  Landlord  ',
      description: '  July rent  ',
      expenseDate: DateTime(2026, 7, 2, 23, 59),
    );

    expect(request.toJson(), {
      'categoryName': 'Rent',
      'amount': 125.5,
      'paidTo': 'Landlord',
      'description': 'July rent',
      'expenseDate': '2026-07-02',
    });
  });

  test('serializes whitespace description as null', () {
    final request = ExpenseMutationRequestDto(
      categoryName: 'Rent',
      amount: 1,
      paidTo: 'Landlord',
      description: '  ',
      expenseDate: DateTime(2026, 7, 2),
    );

    expect(request.toJson()['description'], isNull);
  });
}
