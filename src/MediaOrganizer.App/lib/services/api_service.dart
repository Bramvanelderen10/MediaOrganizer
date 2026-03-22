import 'package:http/http.dart' as http;
import 'dart:convert';

import 'sse_client.dart';

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

  /// Forgets move history for a specific show season via POST /forget-show-season.
  Future<String> forgetShowSeason({
    required String showName,
    required int seasonNumber,
  }) async {
    final response = await http.post(
      _uri('/forget-show-season'),
      headers: {'Content-Type': 'application/json'},
      body: jsonEncode({'showName': showName, 'seasonNumber': seasonNumber}),
    );

    if (response.statusCode >= 200 && response.statusCode < 300) {
      return response.body;
    }

    throw ApiException(response.statusCode, response.body);
  }

  /// Forgets move history for a specific movie via POST /forget-movie.
  Future<String> forgetMovie({required String movieName}) async {
    final response = await http.post(
      _uri('/forget-movie'),
      headers: {'Content-Type': 'application/json'},
      body: jsonEncode({'movieName': movieName}),
    );

    if (response.statusCode >= 200 && response.statusCode < 300) {
      return response.body;
    }

    throw ApiException(response.statusCode, response.body);
  }

  /// Forgets all move history for a show (all seasons) via POST /forget-show.
  Future<String> forgetShow({required String showName}) async {
    final response = await http.post(
      _uri('/forget-show'),
      headers: {'Content-Type': 'application/json'},
      body: jsonEncode({'showName': showName}),
    );

    if (response.statusCode >= 200 && response.statusCode < 300) {
      return response.body;
    }

    throw ApiException(response.statusCode, response.body);
  }

  /// Forgets move history for a specific episode via POST /forget-episode.
  Future<String> forgetEpisode({
    required String showName,
    required int seasonNumber,
    required int episodeNumber,
  }) async {
    final response = await http.post(
      _uri('/forget-episode'),
      headers: {'Content-Type': 'application/json'},
      body: jsonEncode({
        'showName': showName,
        'seasonNumber': seasonNumber,
        'episodeNumber': episodeNumber,
      }),
    );

    if (response.statusCode >= 200 && response.statusCode < 300) {
      return response.body;
    }

    throw ApiException(response.statusCode, response.body);
  }

  /// Quick connectivity check via GET /health.
  Future<bool> healthCheck({
    Duration timeout = const Duration(seconds: 5),
  }) async {
    try {
      final response = await http.get(_uri('/health')).timeout(timeout);
      return response.statusCode == 200;
    } catch (_) {
      return false;
    }
  }

  /// Fetches storage information via GET /storage-info.
  /// Returns a map with keys: folder, totalBytes, freeBytes, usedBytes.
  Future<Map<String, dynamic>> getStorageInfo() async {
    final response = await http.get(_uri('/storage-info'));
    if (response.statusCode >= 200 && response.statusCode < 300) {
      return jsonDecode(response.body) as Map<String, dynamic>;
    }
    throw ApiException(response.statusCode, response.body);
  }

  /// Streams live log lines from GET /logs/stream (Server-Sent Events).
  Stream<String> streamLogs({int tail = 200}) {
    final clampedTail = tail.clamp(0, 1000);
    return SseClient.connect(_uri('/logs/stream?tail=$clampedTail'));
  }

  /// Fetches the organized media library structure via GET /library.
  Future<Map<String, dynamic>> getLibrary() async {
    final response = await http.get(_uri('/library'));
    if (response.statusCode >= 200 && response.statusCode < 300) {
      return jsonDecode(response.body) as Map<String, dynamic>;
    }
    throw ApiException(response.statusCode, response.body);
  }
}

class ApiException implements Exception {
  final int statusCode;
  final String body;

  ApiException(this.statusCode, this.body);

  @override
  String toString() => 'ApiException($statusCode): $body';
}
