// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'discount_rule_editor_controller.dart';

// **************************************************************************
// RiverpodGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, type=warning

@ProviderFor(createDiscountRule)
final createDiscountRuleProvider = CreateDiscountRuleProvider._();

final class CreateDiscountRuleProvider
    extends
        $FunctionalProvider<
          CreateDiscountRule,
          CreateDiscountRule,
          CreateDiscountRule
        >
    with $Provider<CreateDiscountRule> {
  CreateDiscountRuleProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'createDiscountRuleProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$createDiscountRuleHash();

  @$internal
  @override
  $ProviderElement<CreateDiscountRule> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  CreateDiscountRule create(Ref ref) {
    return createDiscountRule(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(CreateDiscountRule value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<CreateDiscountRule>(value),
    );
  }
}

String _$createDiscountRuleHash() =>
    r'158e51ea0136090e82d8ac305e434d9434e57786';

@ProviderFor(previewDiscountRule)
final previewDiscountRuleProvider = PreviewDiscountRuleProvider._();

final class PreviewDiscountRuleProvider
    extends
        $FunctionalProvider<
          PreviewDiscountRule,
          PreviewDiscountRule,
          PreviewDiscountRule
        >
    with $Provider<PreviewDiscountRule> {
  PreviewDiscountRuleProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'previewDiscountRuleProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$previewDiscountRuleHash();

  @$internal
  @override
  $ProviderElement<PreviewDiscountRule> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  PreviewDiscountRule create(Ref ref) {
    return previewDiscountRule(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(PreviewDiscountRule value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<PreviewDiscountRule>(value),
    );
  }
}

String _$previewDiscountRuleHash() =>
    r'254c059f774be53a98e8edc8636ec1fbfcc5b20d';

@ProviderFor(DiscountRuleEditorController)
final discountRuleEditorControllerProvider =
    DiscountRuleEditorControllerProvider._();

final class DiscountRuleEditorControllerProvider
    extends
        $NotifierProvider<
          DiscountRuleEditorController,
          DiscountRuleEditorState
        > {
  DiscountRuleEditorControllerProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'discountRuleEditorControllerProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$discountRuleEditorControllerHash();

  @$internal
  @override
  DiscountRuleEditorController create() => DiscountRuleEditorController();

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(DiscountRuleEditorState value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<DiscountRuleEditorState>(value),
    );
  }
}

String _$discountRuleEditorControllerHash() =>
    r'db3de51170a5fe12d712d7ded15cb9734b179d6e';

abstract class _$DiscountRuleEditorController
    extends $Notifier<DiscountRuleEditorState> {
  DiscountRuleEditorState build();
  @$mustCallSuper
  @override
  void runBuild() {
    final ref =
        this.ref as $Ref<DiscountRuleEditorState, DiscountRuleEditorState>;
    final element =
        ref.element
            as $ClassProviderElement<
              AnyNotifier<DiscountRuleEditorState, DiscountRuleEditorState>,
              DiscountRuleEditorState,
              Object?,
              Object?
            >;
    element.handleCreate(ref, build);
  }
}
