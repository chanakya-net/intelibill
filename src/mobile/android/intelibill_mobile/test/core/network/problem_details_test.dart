import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/network/problem_details.dart';

void main() {
  group('ProblemDetails', () {
    test('should parse from JSON correctly', () {
      final json = <String, dynamic>{
        'type': 'https://tools.ietf.org/html/rfc7231#section-6.5.1',
        'title': 'One or more validation errors occurred.',
        'status': 400,
        'detail': 'Invalid request',
        'instance': '/api/items',
        'errors': {
          'Name': ['The Name field is required.']
        }
      };

      final problem = ProblemDetails.fromJson(json);

      expect(problem.type, json['type']);
      expect(problem.title, json['title']);
      expect(problem.status, json['status']);
      expect(problem.detail, json['detail']);
      expect(problem.instance, json['instance']);
      expect(problem.errors['Name'], isA<List<dynamic>>());
      expect(
        (problem.errors['Name'] as List<dynamic>).first,
        'The Name field is required.',
      );
    });

    test('should handle empty errors', () {
      final json = <String, dynamic>{
        'title': 'Error',
        'status': 500,
      };

      final problem = ProblemDetails.fromJson(json);

      expect(problem.title, 'Error');
      expect(problem.status, 500);
      expect(problem.errors, isEmpty);
    });
  });
}
