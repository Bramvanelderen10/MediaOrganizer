import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;

/// Minimal Server-Sent Events (SSE) client.
///
/// It emits the concatenated `data:` lines for each SSE event.
class SseClient {
  static Stream<String> connect(
    Uri uri, {
    Map<String, String>? headers,
  }) {
    final controller = StreamController<String>();

    http.Client? client;
    StreamSubscription<String>? lineSub;

    controller.onListen = () async {
      client = http.Client();

      try {
        final request = http.Request('GET', uri);
        request.headers.addAll({
          'Accept': 'text/event-stream',
          'Cache-Control': 'no-cache',
          ...?headers,
        });

        final response = await client!.send(request);

        if (response.statusCode < 200 || response.statusCode >= 300) {
          final body = await response.stream.bytesToString();
          throw HttpException(response.statusCode, body);
        }

        final eventDataLines = <String>[];

        lineSub = response.stream
            .transform(utf8.decoder)
            .transform(const LineSplitter())
            .listen(
          (line) {
            // Empty line = end of event.
            if (line.isEmpty) {
              if (eventDataLines.isNotEmpty) {
                controller.add(eventDataLines.join('\n'));
                eventDataLines.clear();
              }
              return;
            }

            // Comment / keep-alive.
            if (line.startsWith(':')) return;

            if (line.startsWith('data:')) {
              final data = line.substring('data:'.length).trimLeft();
              eventDataLines.add(data);
            }
          },
          onError: (Object error, StackTrace st) {
            if (!controller.isClosed) controller.addError(error, st);
          },
          onDone: () async {
            if (!controller.isClosed) await controller.close();
          },
          cancelOnError: false,
        );
      } catch (e, st) {
        if (!controller.isClosed) controller.addError(e, st);
        if (!controller.isClosed) await controller.close();
      }
    };

    controller.onCancel = () async {
      await lineSub?.cancel();
      client?.close();
    };

    return controller.stream;
  }
}

class HttpException implements Exception {
  final int statusCode;
  final String body;

  HttpException(this.statusCode, this.body);

  @override
  String toString() => 'HttpException($statusCode): $body';
}
