import 'package:http/http.dart' as http;

/// Communicates with the MediaOrganizer API.
class ApiService {
  final String baseUrl;

  ApiService({required this.baseUrl});

  /// Builds the full URI, ensuring the scheme is present.
  Uri _uri(String path) {
    final url = baseUrl.startsWith('http') ? baseUrl : 'http://$baseUrl';
    return Uri.parse('$url$path');
  }

  /// Triggers the organize job via POST /trigger-job.
  /// Returns the response body on success, throws on failure.
  Future<String> triggerJob() async {
    final response = await http.post(_uri('/trigger-job'));
    if (response.statusCode >= 200 && response.statusCode < 300) {
      return response.body;
    }
    throw ApiException(response.statusCode, response.body);
  }

  /// Quick connectivity check via GET /health.
  Future<bool> healthCheck({Duration timeout = const Duration(seconds: 5)}) async {
    try {
      final response = await http.get(_uri('/health')).timeout(timeout);
      return response.statusCode == 200;
    } catch (_) {
      return false;
    }
  }
}

class ApiException implements Exception {
  final int statusCode;
  final String body;

  ApiException(this.statusCode, this.body);

  @override
  String toString() => 'ApiException($statusCode): $body';
}
