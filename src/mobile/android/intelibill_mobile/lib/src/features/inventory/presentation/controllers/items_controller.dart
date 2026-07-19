import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/network/api_client_provider.dart';
import 'package:intelibill_mobile/src/features/inventory/data/data_sources/inventory_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/inventory/data/repositories/inventory_repository_impl.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/item.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/use_cases/generate_item_barcode.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/repositories/inventory_repository.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/use_cases/create_item.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/use_cases/get_items.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/use_cases/get_product_details.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/use_cases/update_item.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'items_controller.g.dart';

@riverpod
InventoryRemoteDataSource inventoryRemoteDataSource(Ref ref) {
  final apiClient = ref.watch(apiClientProvider);
  return InventoryRemoteDataSourceImpl(apiClient);
}

@riverpod
InventoryRepository inventoryRepository(Ref ref) {
  final remoteDataSource = ref.watch(inventoryRemoteDataSourceProvider);
  return InventoryRepositoryImpl(remoteDataSource);
}

@riverpod
GetItems getItems(Ref ref) {
  final repository = ref.watch(inventoryRepositoryProvider);
  return GetItems(repository);
}

@riverpod
GetProductDetails getProductDetails(Ref ref) {
  final repository = ref.watch(inventoryRepositoryProvider);
  return GetProductDetails(repository);
}

@riverpod
CreateItem createItem(Ref ref) {
  final repository = ref.watch(inventoryRepositoryProvider);
  return CreateItem(repository);
}

@riverpod
UpdateItem updateItem(Ref ref) {
  final repository = ref.watch(inventoryRepositoryProvider);
  return UpdateItem(repository);
}

@riverpod
GenerateItemBarcode generateItemBarcode(Ref ref) {
  final repository = ref.watch(inventoryRepositoryProvider);
  return GenerateItemBarcode(repository);
}

@immutable
class ItemsState {
  const ItemsState({
    this.items = const [],
    this.searchQuery = '',
    this.isLoading = false,
    this.isSubmitting = false,
    this.failure,
    this.submitFailure,
    this.lastAction,
  });

  final List<Item> items;
  final String searchQuery;
  final bool isLoading;
  final bool isSubmitting;
  final Failure? failure;
  final Failure? submitFailure;
  final String? lastAction;

  List<Item> get filteredItems {
    if (searchQuery.isEmpty) return items;
    final query = searchQuery.toLowerCase();
    return items.where((item) {
      return item.name.toLowerCase().contains(query) ||
          item.barcode.toLowerCase().contains(query) ||
          (item.description?.toLowerCase().contains(query) ?? false) ||
          item.uom.toLowerCase().contains(query);
    }).toList();
  }

  ItemsState copyWith({
    List<Item>? items,
    String? searchQuery,
    bool? isLoading,
    bool? isSubmitting,
    Failure? failure,
    Failure? submitFailure,
    String? lastAction,
    bool clearError = false,
    bool clearSubmitError = false,
    bool clearLastAction = false,
  }) {
    return ItemsState(
      items: items ?? this.items,
      searchQuery: searchQuery ?? this.searchQuery,
      isLoading: isLoading ?? this.isLoading,
      isSubmitting: isSubmitting ?? this.isSubmitting,
      failure: clearError ? null : (failure ?? this.failure),
      submitFailure: clearSubmitError
          ? null
          : (submitFailure ?? this.submitFailure),
      lastAction: clearLastAction ? null : (lastAction ?? this.lastAction),
    );
  }
}

@riverpod
class ItemsController extends _$ItemsController {
  @override
  ItemsState build() {
    unawaited(Future.microtask(_load));
    return const ItemsState(isLoading: true);
  }

  Future<void> _load() async {
    final useCase = ref.read(getItemsProvider);
    try {
      final items = await useCase();
      if (!ref.mounted) return;
      state = state.copyWith(items: items, isLoading: false);
    } on AppException catch (error) {
      if (!ref.mounted) return;
      state = state.copyWith(isLoading: false, failure: error.failure);
    } on Object {
      if (!ref.mounted) return;
      state = state.copyWith(
        isLoading: false,
        failure: const Failure.unknown(),
      );
    }
  }

  Future<void> refresh() async {
    state = state.copyWith(isLoading: true, clearError: true);
    await _load();
  }

  void updateSearch(String query) {
    state = state.copyWith(searchQuery: query);
  }

  Future<void> createItem({
    required String name,
    required String barcode,
    required String uom,
    String? description,
  }) async {
    if (state.isSubmitting) return;
    state = state.copyWith(
      isSubmitting: true,
      clearSubmitError: true,
      clearLastAction: true,
    );
    final useCase = ref.read(createItemProvider);
    try {
      await useCase(
        name: name,
        barcode: barcode,
        uom: uom,
        description: description,
      );
      if (!ref.mounted) return;
      state = state.copyWith(isSubmitting: false, lastAction: 'created');
      await refresh();
    } on AppException catch (error) {
      if (!ref.mounted) return;
      state = state.copyWith(isSubmitting: false, submitFailure: error.failure);
    } on Object {
      if (!ref.mounted) return;
      state = state.copyWith(
        isSubmitting: false,
        submitFailure: const Failure.unknown(),
      );
    }
  }

  Future<void> updateItem({
    required String itemId,
    required String name,
    required String barcode,
    required String uom,
    String? description,
    required bool isActive,
  }) async {
    if (state.isSubmitting) return;
    state = state.copyWith(
      isSubmitting: true,
      clearSubmitError: true,
      clearLastAction: true,
    );
    final useCase = ref.read(updateItemProvider);
    try {
      await useCase(
        itemId: itemId,
        name: name,
        barcode: barcode,
        uom: uom,
        description: description,
        isActive: isActive,
      );
      if (!ref.mounted) return;
      state = state.copyWith(isSubmitting: false, lastAction: 'updated');
      await refresh();
    } on AppException catch (error) {
      if (!ref.mounted) return;
      state = state.copyWith(isSubmitting: false, submitFailure: error.failure);
    } on Object {
      if (!ref.mounted) return;
      state = state.copyWith(
        isSubmitting: false,
        submitFailure: const Failure.unknown(),
      );
    }
  }
}
