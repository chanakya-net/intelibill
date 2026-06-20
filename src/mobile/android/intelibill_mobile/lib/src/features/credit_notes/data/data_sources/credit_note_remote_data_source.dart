import 'package:intelibill_mobile/src/core/network/api_client.dart';
import 'package:intelibill_mobile/src/features/credit_notes/data/dto/credit_note_dto.dart';

interface class CreditNoteRemoteDataSource {
  Future<CreditNotesResponseDto> getCreditNotes({
    String? search,
    String? status,
    required int page,
    required int pageSize,
  }) {
    throw UnimplementedError();
  }

  Future<CreditNoteDto> getCreditNoteByCode(String code) {
    throw UnimplementedError();
  }

  Future<CreditNotePrintDto> getCreditNotePrintByCode(String code) {
    throw UnimplementedError();
  }
}

class CreditNoteRemoteDataSourceImpl implements CreditNoteRemoteDataSource {
  CreditNoteRemoteDataSourceImpl(this._apiClient);

  final ApiClient _apiClient;

  static const String _endpoint = '/credit-notes';

  @override
  Future<CreditNotesResponseDto> getCreditNotes({
    String? search,
    String? status,
    required int page,
    required int pageSize,
  }) async {
    final response = await _apiClient.get<Map<String, dynamic>>(
      _endpoint,
      queryParameters: {
        if (search?.trim().isNotEmpty == true) 'search': search!.trim(),
        if (status?.trim().isNotEmpty == true) 'status': status!.trim(),
        'page': page,
        'pageSize': pageSize,
      },
    );
    return CreditNotesResponseDto.fromJson(response.data!);
  }

  @override
  Future<CreditNoteDto> getCreditNoteByCode(String code) async {
    final response = await _apiClient.get<Map<String, dynamic>>(
      '$_endpoint/$code',
    );
    return CreditNoteDto.fromJson(response.data!);
  }

  @override
  Future<CreditNotePrintDto> getCreditNotePrintByCode(String code) async {
    final response = await _apiClient.get<Map<String, dynamic>>(
      '$_endpoint/$code/print',
    );
    return CreditNotePrintDto.fromJson(response.data!);
  }
}
