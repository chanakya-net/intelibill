import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/suppliers/data/data_sources/supplier_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/suppliers/data/dto/create_supplier_request_dto.dart';
import 'package:mocktail/mocktail.dart';

import '../../../mocks/mock_api_client.dart';

void main() {
  late MockApiClient mockApiClient;
  late SupplierRemoteDataSourceImpl remoteDataSource;

  setUp(() {
    mockApiClient = MockApiClient();
    remoteDataSource = SupplierRemoteDataSourceImpl(mockApiClient);
  });

  group('SupplierRemoteDataSourceImpl', () {
    test('calls /suppliers endpoint and returns parsed DTOs', () async {
      final responseData = [
        {
          'supplierId': 'sup-1',
          'name': 'ABC Traders',
          'contactPersonName': 'John Doe',
          'contactPersonPhone': '9876543210',
          'address': '12 Main St',
          'city': 'Mumbai',
          'state': 'Maharashtra',
          'pin': '400001',
          'isSystem': false,
          'isActive': true,
          'isPreferred': true,
          'balanceDue': 1500.50,
        },
        {
          'supplierId': 'sup-2',
          'name': 'XYZ Suppliers',
          'isSystem': false,
          'isActive': true,
          'isPreferred': false,
          'balanceDue': 0.0,
        },
      ];

      when(() => mockApiClient.get<List<dynamic>>(any<String>())).thenAnswer(
        (_) async => Response(
          data: responseData,
          statusCode: 200,
          requestOptions: RequestOptions(path: '/suppliers'),
        ),
      );

      final dtos = await remoteDataSource.getSuppliers();

      expect(dtos.length, 2);
      expect(dtos[0].supplierId, 'sup-1');
      expect(dtos[0].name, 'ABC Traders');
      expect(dtos[1].supplierId, 'sup-2');
      expect(dtos[1].contactPersonName, isNull);

      verify(() => mockApiClient.get<List<dynamic>>('/suppliers')).called(1);
    });

    test('returns empty list when API returns empty array', () async {
      when(() => mockApiClient.get<List<dynamic>>(any<String>())).thenAnswer(
        (_) async => Response(
          data: <dynamic>[],
          statusCode: 200,
          requestOptions: RequestOptions(path: '/suppliers'),
        ),
      );

      final dtos = await remoteDataSource.getSuppliers();

      expect(dtos, isEmpty);
    });

    test('parses supplier with all fields present', () async {
      final responseData = [
        {
          'supplierId': 'sup-3',
          'name': 'DEF Corp',
          'contactPersonName': 'Jane Smith',
          'contactPersonPhone': '9111111111',
          'address': '22 Test Road',
          'city': 'Delhi',
          'state': 'Delhi',
          'pin': '110001',
          'isSystem': false,
          'isActive': false,
          'isPreferred': true,
          'balanceDue': 2500.75,
        },
      ];

      when(() => mockApiClient.get<List<dynamic>>(any<String>())).thenAnswer(
        (_) async => Response(
          data: responseData,
          statusCode: 200,
          requestOptions: RequestOptions(path: '/suppliers'),
        ),
      );

      final dtos = await remoteDataSource.getSuppliers();

      expect(dtos.length, 1);
      expect(dtos[0].supplierId, 'sup-3');
      expect(dtos[0].name, 'DEF Corp');
      expect(dtos[0].city, 'Delhi');
      expect(dtos[0].isPreferred, true);
      expect(dtos[0].balanceDue, 2500.75);
    });

    test('handles supplier with minimal fields', () async {
      final responseData = [
        {
          'supplierId': 'sup-4',
          'name': 'GHI Ltd',
          'isSystem': false,
          'isActive': true,
          'isPreferred': false,
        },
      ];

      when(() => mockApiClient.get<List<dynamic>>(any<String>())).thenAnswer(
        (_) async => Response(
          data: responseData,
          statusCode: 200,
          requestOptions: RequestOptions(path: '/suppliers'),
        ),
      );

      final dtos = await remoteDataSource.getSuppliers();

      expect(dtos.length, 1);
      expect(dtos[0].name, 'GHI Ltd');
      expect(dtos[0].contactPersonName, isNull);
      expect(dtos[0].balanceDue, 0.0);
    });

    test('preserves system supplier flag', () async {
      final responseData = [
        {
          'supplierId': 'sup-sys-1',
          'name': 'System Supplier',
          'isSystem': true,
          'isActive': true,
          'isPreferred': false,
        },
      ];

      when(() => mockApiClient.get<List<dynamic>>(any<String>())).thenAnswer(
        (_) async => Response(
          data: responseData,
          statusCode: 200,
          requestOptions: RequestOptions(path: '/suppliers'),
        ),
      );

      final dtos = await remoteDataSource.getSuppliers();

      expect(dtos[0].isSystem, true);
    });

    test('posts create supplier payload to /suppliers', () async {
      const request = CreateSupplierRequestDto(
        name: 'ABC Traders',
        contactPersonName: 'John Doe',
        contactPersonPhone: '+919876543210',
        address: '12 Main Street',
        city: 'Mumbai',
        state: 'Maharashtra',
        pin: '400001',
        isActive: true,
        isPreferred: true,
      );
      final responseData = {
        'supplierId': 'sup-1',
        'name': 'ABC Traders',
        'contactPersonName': 'John Doe',
        'contactPersonPhone': '+919876543210',
        'address': '12 Main Street',
        'city': 'Mumbai',
        'state': 'Maharashtra',
        'pin': '400001',
        'isSystem': false,
        'isActive': true,
        'isPreferred': true,
        'balanceDue': 0.0,
      };

      when(
        () => mockApiClient.post<Map<String, dynamic>>(
          any<String>(),
          data: any<Map<String, dynamic>>(named: 'data'),
        ),
      ).thenAnswer(
        (_) async => Response(
          data: responseData,
          statusCode: 201,
          requestOptions: RequestOptions(path: '/suppliers'),
        ),
      );

      final dto = await remoteDataSource.createSupplier(request);

      expect(dto.supplierId, 'sup-1');
      expect(dto.name, 'ABC Traders');
      verify(
        () => mockApiClient.post<Map<String, dynamic>>(
          '/suppliers',
          data: request.toJson(),
        ),
      ).called(1);
    });
  });
}
