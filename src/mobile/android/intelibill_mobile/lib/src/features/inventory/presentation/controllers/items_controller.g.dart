// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'items_controller.dart';

// **************************************************************************
// RiverpodGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, type=warning

@ProviderFor(inventoryRemoteDataSource)
final inventoryRemoteDataSourceProvider = InventoryRemoteDataSourceProvider._();

final class InventoryRemoteDataSourceProvider
    extends
        $FunctionalProvider<
          InventoryRemoteDataSource,
          InventoryRemoteDataSource,
          InventoryRemoteDataSource
        >
    with $Provider<InventoryRemoteDataSource> {
  InventoryRemoteDataSourceProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'inventoryRemoteDataSourceProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$inventoryRemoteDataSourceHash();

  @$internal
  @override
  $ProviderElement<InventoryRemoteDataSource> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  InventoryRemoteDataSource create(Ref ref) {
    return inventoryRemoteDataSource(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(InventoryRemoteDataSource value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<InventoryRemoteDataSource>(value),
    );
  }
}

String _$inventoryRemoteDataSourceHash() =>
    r'68de99a4d7e6c6a3cb291b6db8d07f28bf415ded';

@ProviderFor(inventoryRepository)
final inventoryRepositoryProvider = InventoryRepositoryProvider._();

final class InventoryRepositoryProvider
    extends
        $FunctionalProvider<
          InventoryRepository,
          InventoryRepository,
          InventoryRepository
        >
    with $Provider<InventoryRepository> {
  InventoryRepositoryProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'inventoryRepositoryProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$inventoryRepositoryHash();

  @$internal
  @override
  $ProviderElement<InventoryRepository> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  InventoryRepository create(Ref ref) {
    return inventoryRepository(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(InventoryRepository value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<InventoryRepository>(value),
    );
  }
}

String _$inventoryRepositoryHash() =>
    r'eef189ba03ce688bbef48adbc42a7133382c753e';

@ProviderFor(getItems)
final getItemsProvider = GetItemsProvider._();

final class GetItemsProvider
    extends $FunctionalProvider<GetItems, GetItems, GetItems>
    with $Provider<GetItems> {
  GetItemsProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'getItemsProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$getItemsHash();

  @$internal
  @override
  $ProviderElement<GetItems> $createElement($ProviderPointer pointer) =>
      $ProviderElement(pointer);

  @override
  GetItems create(Ref ref) {
    return getItems(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(GetItems value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<GetItems>(value),
    );
  }
}

String _$getItemsHash() => r'1ab44d247f9d1d8d06052fc7bad97bd193161f01';

@ProviderFor(getProductDetails)
final getProductDetailsProvider = GetProductDetailsProvider._();

final class GetProductDetailsProvider
    extends
        $FunctionalProvider<
          GetProductDetails,
          GetProductDetails,
          GetProductDetails
        >
    with $Provider<GetProductDetails> {
  GetProductDetailsProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'getProductDetailsProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$getProductDetailsHash();

  @$internal
  @override
  $ProviderElement<GetProductDetails> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  GetProductDetails create(Ref ref) {
    return getProductDetails(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(GetProductDetails value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<GetProductDetails>(value),
    );
  }
}

String _$getProductDetailsHash() => r'7d099891ff956ac0aa5e95216d323a5901a2123a';

@ProviderFor(createItem)
final createItemProvider = CreateItemProvider._();

final class CreateItemProvider
    extends $FunctionalProvider<CreateItem, CreateItem, CreateItem>
    with $Provider<CreateItem> {
  CreateItemProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'createItemProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$createItemHash();

  @$internal
  @override
  $ProviderElement<CreateItem> $createElement($ProviderPointer pointer) =>
      $ProviderElement(pointer);

  @override
  CreateItem create(Ref ref) {
    return createItem(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(CreateItem value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<CreateItem>(value),
    );
  }
}

String _$createItemHash() => r'c39202f2daa49c7ce77a371b8473991cb06debfd';

@ProviderFor(updateItem)
final updateItemProvider = UpdateItemProvider._();

final class UpdateItemProvider
    extends $FunctionalProvider<UpdateItem, UpdateItem, UpdateItem>
    with $Provider<UpdateItem> {
  UpdateItemProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'updateItemProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$updateItemHash();

  @$internal
  @override
  $ProviderElement<UpdateItem> $createElement($ProviderPointer pointer) =>
      $ProviderElement(pointer);

  @override
  UpdateItem create(Ref ref) {
    return updateItem(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(UpdateItem value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<UpdateItem>(value),
    );
  }
}

String _$updateItemHash() => r'b571a9033fabb14c05e00a7c2b77602814f6d1d1';

@ProviderFor(generateItemBarcode)
final generateItemBarcodeProvider = GenerateItemBarcodeProvider._();

final class GenerateItemBarcodeProvider
    extends
        $FunctionalProvider<
          GenerateItemBarcode,
          GenerateItemBarcode,
          GenerateItemBarcode
        >
    with $Provider<GenerateItemBarcode> {
  GenerateItemBarcodeProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'generateItemBarcodeProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$generateItemBarcodeHash();

  @$internal
  @override
  $ProviderElement<GenerateItemBarcode> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  GenerateItemBarcode create(Ref ref) {
    return generateItemBarcode(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(GenerateItemBarcode value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<GenerateItemBarcode>(value),
    );
  }
}

String _$generateItemBarcodeHash() =>
    r'670ff6148c69427fcbe645447f34d731a57ef5f4';

@ProviderFor(ItemsController)
final itemsControllerProvider = ItemsControllerProvider._();

final class ItemsControllerProvider
    extends $NotifierProvider<ItemsController, ItemsState> {
  ItemsControllerProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'itemsControllerProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$itemsControllerHash();

  @$internal
  @override
  ItemsController create() => ItemsController();

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(ItemsState value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<ItemsState>(value),
    );
  }
}

String _$itemsControllerHash() => r'030b03f7b6e99fa8e9c275a708f142b05d111215';

abstract class _$ItemsController extends $Notifier<ItemsState> {
  ItemsState build();
  @$mustCallSuper
  @override
  void runBuild() {
    final ref = this.ref as $Ref<ItemsState, ItemsState>;
    final element =
        ref.element
            as $ClassProviderElement<
              AnyNotifier<ItemsState, ItemsState>,
              ItemsState,
              Object?,
              Object?
            >;
    element.handleCreate(ref, build);
  }
}
