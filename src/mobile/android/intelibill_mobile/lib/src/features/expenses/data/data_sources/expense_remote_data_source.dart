import 'package:intelibill_mobile/src/core/network/api_client.dart';
import 'package:intelibill_mobile/src/features/expenses/data/dto/expense_detail_dto.dart';
import 'package:intelibill_mobile/src/features/expenses/data/dto/expenses_page_dto.dart';

interface class ExpenseRemoteDataSource {
  Future<ExpensesPageDto> getExpenses({
    int? page,
    int? pageSize,
  }) {
    throw UnimplementedError();
  }

  Future<ExpenseDetailDto> getExpenseDetail(String id) {
    throw UnimplementedError();
  }
}

class ExpenseRemoteDataSourceImpl implements ExpenseRemoteDataSource {
  ExpenseRemoteDataSourceImpl(this._apiClient);

  final ApiClient _apiClient;

  @override
  Future<ExpensesPageDto> getExpenses({
    int? page,
    int? pageSize,
  }) async {
    final response = await _apiClient.get<Map<String, dynamic>>(
      '/expenses',
      queryParameters: {
        'page': page ?? 1,
        'pageSize': pageSize ?? 20,
      },
    );
    return ExpensesPageDto.fromJson(response.data!);
  }

  @override
  Future<ExpenseDetailDto> getExpenseDetail(String id) async {
    final response = await _apiClient.get<Map<String, dynamic>>(
      '/expenses/$id',
    );
    return ExpenseDetailDto.fromJson(response.data!);
  }
}
