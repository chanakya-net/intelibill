import 'package:intelibill_mobile/src/shared/documents/output/document_export_service.dart';
import 'package:intelibill_mobile/src/shared/documents/output/document_output_gateway.dart';
import 'package:intelibill_mobile/src/shared/documents/output/printing_gateway.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'document_export_providers.g.dart';

@riverpod
DocumentOutputGateway documentOutputGateway(Ref ref) {
  return PrintingGateway();
}

@riverpod
DocumentExportService documentExportService(Ref ref) {
  final gateway = ref.watch(documentOutputGatewayProvider);
  return DocumentExportService(gateway);
}
