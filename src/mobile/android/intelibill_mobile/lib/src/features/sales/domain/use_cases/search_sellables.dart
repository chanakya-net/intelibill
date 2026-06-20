import 'package:intelibill_mobile/src/features/sales/domain/entities/sellable.dart';
import 'package:intelibill_mobile/src/features/sales/domain/repositories/sales_repository.dart';

class SearchSellables {
  const SearchSellables(this._repository);

  final SalesRepository _repository;

  Future<List<Sellable>> call({
    String? searchTerm,
    String? barcode,
  }) {
    return _repository.searchSellables(
      searchTerm: searchTerm,
      barcode: barcode,
    );
  }
}
