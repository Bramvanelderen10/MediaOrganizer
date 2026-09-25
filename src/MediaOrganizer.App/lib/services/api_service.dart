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

  /// Starts a background transcode job via POST /transcode and returns immediately (202).
  ///
  /// When [paths] is provided only those files are transcoded; otherwise the whole media
  /// library is scanned. [label] is a friendly name shown on the job screen.
  ///
  /// Throws [ApiException] with [ApiException.isNotFound] on older backends, with
  /// [ApiException.isNotConfigured] when transcoding is disabled on the server, and with a
  /// 409 status when another job is already running.
  Future<Map<String, dynamic>> startTranscode({
    List<String>? paths,
    String? label,
  }) async {
    final response = await http.post(
      _uri('/transcode'),
      headers: {'Content-Type': 'application/json'},
      body: jsonEncode({
        if (paths != null && paths.isNotEmpty) 'paths': paths,
        if (label != null && label.isNotEmpty) 'label': label,
      }),
    );
    if (response.statusCode >= 200 && response.statusCode < 300) {
      return _decodeMap(response.body);
    }
    throw ApiException(response.statusCode, response.body);
  }

  /// Current or most recent transcode job via GET /transcode/job.
  ///
  /// Throws [ApiException] with [ApiException.isNotFound] on older backends.
  Future<Map<String, dynamic>> getTranscodeJob() async {
    final response = await http.get(_uri('/transcode/job'));
    if (response.statusCode >= 200 && response.statusCode < 300) {
      return _decodeMap(response.body);
    }
    throw ApiException(response.statusCode, response.body);
  }

  /// Runs a short real encode to verify hardware acceleration via GET /transcode/selftest.
  Future<Map<String, dynamic>> runTranscodeSelfTest() async {
    final response = await http.get(_uri('/transcode/selftest'));
    if (response.statusCode >= 200 && response.statusCode < 300) {
      return _decodeMap(response.body);
    }
    throw ApiException(response.statusCode, response.body);
  }

  Map<String, dynamic> _decodeMap(String body) {
    final decoded = jsonDecode(body);
    return decoded is Map<String, dynamic> ? decoded : <String, dynamic>{};
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

  /// Forgets move history for multiple items at once via POST /forget-batch.
  /// Each item is a map with 'type' and relevant identifiers.
  Future<String> forgetBatch(List<Map<String, dynamic>> items) async {
    final response = await http.post(
      _uri('/forget-batch'),
      headers: {'Content-Type': 'application/json'},
      body: jsonEncode({'items': items}),
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

  /// Browses directory contents under the source folder via GET /browse.
  /// [path] is relative to the source root. Omit for the root listing.
  Future<Map<String, dynamic>> browse({String? path}) async {
    final uri =
        path != null && path.isNotEmpty
            ? _uri('/browse?path=${Uri.encodeQueryComponent(path)}')
            : _uri('/browse');
    final response = await http.get(uri);
    if (response.statusCode >= 200 && response.statusCode < 300) {
      return jsonDecode(response.body) as Map<String, dynamic>;
    }
    throw ApiException(response.statusCode, response.body);
  }

  /// Renames a file or directory via POST /rename.
  Future<String> rename({required String path, required String newName}) async {
    final response = await http.post(
      _uri('/rename'),
      headers: {'Content-Type': 'application/json'},
      body: jsonEncode({'path': path, 'newName': newName}),
    );
    if (response.statusCode >= 200 && response.statusCode < 300) {
      return response.body;
    }
    throw ApiException(response.statusCode, response.body);
  }

  /// Moves a file or directory to a different folder via POST /move.
  Future<String> moveItem({
    required String sourcePath,
    required String destinationFolder,
  }) async {
    final response = await http.post(
      _uri('/move'),
      headers: {'Content-Type': 'application/json'},
      body: jsonEncode({
        'sourcePath': sourcePath,
        'destinationFolder': destinationFolder,
      }),
    );
    if (response.statusCode >= 200 && response.statusCode < 300) {
      return response.body;
    }
    throw ApiException(response.statusCode, response.body);
  }

  /// Deletes one or more files or directories via POST /delete.
  Future<Map<String, dynamic>> deleteItems({
    required List<String> paths,
  }) async {
    final response = await http.post(
      _uri('/delete'),
      headers: {'Content-Type': 'application/json'},
      body: jsonEncode({'paths': paths}),
    );
    if (response.statusCode >= 200 && response.statusCode < 300) {
      return jsonDecode(response.body) as Map<String, dynamic>;
    }
    throw ApiException(response.statusCode, response.body);
  }

  /// Uploads a `.torrent` file via POST /torrents/add and starts the download
  /// in qBittorrent.
  ///
  /// [filePath] must be a local path to the .torrent file. Optionally pass
  /// [folderPath] to override the server's configured download folder.
  /// Returns a map with keys: message, fileName, savePath, sizeBytes, executedAt.
  Future<Map<String, dynamic>> addTorrent({
    required String filePath,
    String? folderPath,
  }) async {
    final request = http.MultipartRequest('POST', _uri('/torrents/add'));
    request.files.add(await http.MultipartFile.fromPath('file', filePath));

    if (folderPath != null && folderPath.isNotEmpty) {
      request.fields['folderPath'] = folderPath;
    }

    final streamed = await request.send();
    final response = await http.Response.fromStream(streamed);

    if (response.statusCode >= 200 && response.statusCode < 300) {
      return jsonDecode(response.body) as Map<String, dynamic>;
    }

    throw ApiException(response.statusCode, response.body);
  }

  /// Adds a magnet link via POST /torrents/add-magnet.
  ///
  /// Throws [ApiException] with [ApiException.isNotFound] when the server does not
  /// support this endpoint (older backend).
  Future<Map<String, dynamic>> addMagnet({
    required String magnetLink,
    String? folderPath,
  }) async {
    final response = await http.post(
      _uri('/torrents/add-magnet'),
      headers: {'Content-Type': 'application/json'},
      body: jsonEncode({
        'magnetLink': magnetLink,
        if (folderPath != null && folderPath.isNotEmpty) 'folderPath': folderPath,
      }),
    );

    if (response.statusCode >= 200 && response.statusCode < 300) {
      return jsonDecode(response.body) as Map<String, dynamic>;
    }

    throw ApiException(response.statusCode, response.body);
  }

  /// Fetches all torrents with progress via GET /torrents.
  ///
  /// Throws [ApiException] with [ApiException.isNotFound] when the server does not
  /// support this endpoint (older backend).
  Future<List<Map<String, dynamic>>> getTorrents() async {
    final response = await http.get(_uri('/torrents'));

    if (response.statusCode >= 200 && response.statusCode < 300) {
      final decoded = jsonDecode(response.body) as Map<String, dynamic>;
      final torrents = decoded['torrents'];
      if (torrents is! List) {
        return const [];
      }
      return torrents.cast<Map<String, dynamic>>();
    }

    throw ApiException(response.statusCode, response.body);
  }
}

class ApiException implements Exception {
  final int statusCode;
  final String body;

  ApiException(this.statusCode, this.body);

  /// The endpoint does not exist on the server, which usually means the backend is
  /// an older build that predates the feature.
  bool get isNotFound => statusCode == 404;

  /// The server could not perform the request because a dependent service
  /// (e.g. qBittorrent) is not configured.
  bool get isNotConfigured => statusCode == 503;

  /// The server reached the dependent service but it rejected the request.
  bool get isUpstreamFailure => statusCode == 502;

  /// Extracts the `message` field from the JSON error body, when present.
  String? get serverMessage {
    try {
      final decoded = jsonDecode(body);
      if (decoded is Map<String, dynamic>) {
        final message = decoded['message'];
        if (message is String && message.trim().isNotEmpty) {
          return message.trim();
        }
      }
    } catch (_) {
      // Body was not JSON; fall through to the raw text.
    }

    final trimmed = body.trim();
    return trimmed.isEmpty ? null : trimmed;
  }

  /// A message suitable for showing directly to the user.
  ///
  /// [feature] names the capability being used, e.g. `magnet links`.
  String userMessage({String? feature}) {
    final suffix = feature == null ? '' : ' ($feature)';

    if (isNotFound) {
      return 'This server does not support this yet$suffix. '
          'Update MediaOrganizer on the server to a newer version.';
    }

    if (isNotConfigured) {
      return serverMessage ??
          'The server is not configured for downloads yet. '
              'Set up qBittorrent on the server.';
    }

    if (isUpstreamFailure) {
      return serverMessage ?? 'The download client rejected the request.';
    }

    return serverMessage ?? 'Server error ($statusCode).';
  }

  @override
  String toString() => 'ApiException($statusCode): $body';
}
