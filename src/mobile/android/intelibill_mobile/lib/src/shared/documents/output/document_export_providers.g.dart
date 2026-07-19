// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'document_export_providers.dart';

// **************************************************************************
// RiverpodGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, type=warning

@ProviderFor(documentOutputGateway)
final documentOutputGatewayProvider = DocumentOutputGatewayProvider._();

final class DocumentOutputGatewayProvider
    extends
        $FunctionalProvider<
          DocumentOutputGateway,
          DocumentOutputGateway,
          DocumentOutputGateway
        >
    with $Provider<DocumentOutputGateway> {
  DocumentOutputGatewayProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'documentOutputGatewayProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$documentOutputGatewayHash();

  @$internal
  @override
  $ProviderElement<DocumentOutputGateway> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  DocumentOutputGateway create(Ref ref) {
    return documentOutputGateway(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(DocumentOutputGateway value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<DocumentOutputGateway>(value),
    );
  }
}

String _$documentOutputGatewayHash() =>
    r'536aef44bd4d0da48db57443b8979f9b7dbb6105';

@ProviderFor(documentExportService)
final documentExportServiceProvider = DocumentExportServiceProvider._();

final class DocumentExportServiceProvider
    extends
        $FunctionalProvider<
          DocumentExportService,
          DocumentExportService,
          DocumentExportService
        >
    with $Provider<DocumentExportService> {
  DocumentExportServiceProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'documentExportServiceProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$documentExportServiceHash();

  @$internal
  @override
  $ProviderElement<DocumentExportService> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  DocumentExportService create(Ref ref) {
    return documentExportService(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(DocumentExportService value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<DocumentExportService>(value),
    );
  }
}

String _$documentExportServiceHash() =>
    r'c9eb8a8e2c633a1c31ee2f908bfa05a2b4bc14cb';
