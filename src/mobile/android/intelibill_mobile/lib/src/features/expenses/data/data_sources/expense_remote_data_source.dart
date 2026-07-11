import 'package:intelibill_mobile/src/core/network/api_client.dart';
import 'package:intelibill_mobile/src/features/expenses/data/dto/expense_category_dto.dart';
import 'package:intelibill_mobile/src/features/expenses/data/dto/expense_detail_dto.dart';
import 'package:intelibill_mobile/src/features/expenses/data/dto/expense_mutation_request_dto.dart';
import 'package:intelibill_mobile/src/features/expenses/data/dto/expenses_page_dto.dart';

interface class ExpenseRemoteDataSource {
  Future<ExpensesPageDto> getExpenses({
    int? page,
    int? pageSize,
    String? search,
  }) {
    throw UnimplementedError();
  }

  Future<ExpenseDetailDto> getExpenseDetail(String id) {
    throw UnimplementedError();
  }

  Future<List<ExpenseCategoryDto>> getCategories() {
    throw UnimplementedError();
  }

  Future<ExpenseDetailDto> recordExpense(ExpenseMutationRequestDto request) {
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
    String? search,
  }) async {
    final queryParameters = <String, dynamic>{
      'page': page ?? 1,
      'pageSize': pageSize ?? 20,
    };
    final normalizedSearch = search?.trim();
    if (normalizedSearch != null && normalizedSearch.isNotEmpty) {
      queryParameters['search'] = normalizedSearch;
    }

    final response = await _apiClient.get<Map<String, dynamic>>(
      '/expenses',
      queryParameters: queryParameters,
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

  @override
  Future<List<ExpenseCategoryDto>> getCategories() async {
    final response = await _apiClient.get<List<dynamic>>(
      '/expenses/categories',
    );
    return response.data!
        .cast<Map<String, dynamic>>()
        .map(ExpenseCategoryDto.fromJson)
        .toList();
  }

  @override
  Future<ExpenseDetailDto> recordExpense(
    ExpenseMutationRequestDto request,
  ) async {
    final response = await _apiClient.post<Map<String, dynamic>>(
      '/expenses',
      data: request.toJson(),
    );
    return ExpenseDetailDto.fromJson(response.data!);
  }
}
