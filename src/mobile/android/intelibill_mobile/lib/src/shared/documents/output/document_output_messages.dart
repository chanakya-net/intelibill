import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/shared/documents/output/document_export_service.dart';

String documentOutputFailureMessage(
  AppLocalizations l10n,
  ExportOperation operation,
) => switch (operation) {
  ExportOperation.build => l10n.documentOutputFailurePdfBuild,
  ExportOperation.print => l10n.documentOutputFailurePrint,
  ExportOperation.share => l10n.documentOutputFailureShare,
};
