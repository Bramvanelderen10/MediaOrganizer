import 'dart:async';

import 'package:receive_sharing_intent/receive_sharing_intent.dart';

/// A torrent source that was opened with, or shared into, the app.
///
/// Either a local `.torrent` file ([filePath]) or a magnet link / torrent URL
/// ([magnetLink]) - never both.
class TorrentIntentRequest {
  final String? filePath;
  final String? magnetLink;

  const TorrentIntentRequest.file(this.filePath) : magnetLink = null;
  const TorrentIntentRequest.magnet(this.magnetLink) : filePath = null;

  /// A short label for the confirmation dialog.
  String get displayName {
    if (filePath != null) {
      return filePath!.split('/').last;
    }

    final link = magnetLink ?? '';
    final dn = _queryValue(link, 'dn');
    if (dn != null && dn.isNotEmpty) {
      return dn;
    }

    final hash = _infoHash(link);
    return hash == null ? 'Magnet link' : 'Magnet: $hash';
  }

  static String? _queryValue(String link, String key) {
    final start = link.indexOf('?');
    if (start < 0) return null;

    for (final pair in link.substring(start + 1).split('&')) {
      final separator = pair.indexOf('=');
      if (separator <= 0) continue;
      if (pair.substring(0, separator).toLowerCase() == key) {
        return Uri.decodeComponent(pair.substring(separator + 1));
      }
    }

    return null;
  }

  static String? _infoHash(String link) {
    const marker = 'xt=urn:btih:';
    final index = link.toLowerCase().indexOf(marker);
    if (index < 0) return null;

    final rest = link.substring(index + marker.length);
    final end = rest.indexOf('&');
    return end < 0 ? rest : rest.substring(0, end);
  }
}

/// Surfaces torrent sources that were opened with, or shared into, the app.
///
/// Handles both `.torrent` files (from a file manager or a download notification)
/// and `magnet:` links (from a browser or any app that shares a link).
///
/// Intents can arrive before the UI is ready (cold start), so received requests are
/// queued until a handler is registered via [setHandler].
class TorrentIntentService {
  final List<TorrentIntentRequest> _pending = [];

  StreamSubscription<List<SharedMediaFile>>? _subscription;
  void Function(TorrentIntentRequest request)? _handler;
  bool _started = false;

  /// Begins listening for incoming intents. Safe to call more than once.
  Future<void> start() async {
    if (_started) {
      return;
    }
    _started = true;

    try {
      _subscription = ReceiveSharingIntent.instance.getMediaStream().listen(
        _handleFiles,
        onError: (Object _) {
          // Opening torrents is best effort; ignore sharing errors.
        },
      );

      // Covers the case where the app was launched by opening a .torrent or magnet link.
      final initial = await ReceiveSharingIntent.instance.getInitialMedia();
      _handleFiles(initial);
      ReceiveSharingIntent.instance.reset();
    } catch (_) {
      // The plugin is only available on Android and iOS.
    }
  }

  /// Registers the handler that receives torrent requests and flushes anything
  /// that arrived before the handler was set.
  void setHandler(void Function(TorrentIntentRequest request) handler) {
    _handler = handler;

    if (_pending.isEmpty) {
      return;
    }

    final queued = List<TorrentIntentRequest>.from(_pending);
    _pending.clear();

    for (final request in queued) {
      handler(request);
    }
  }

  /// Removes the current handler.
  void clearHandler() {
    _handler = null;
  }

  void _handleFiles(List<SharedMediaFile> files) {
    for (final file in files) {
      final request = _toRequest(file);
      if (request == null) {
        continue;
      }

      final handler = _handler;
      if (handler != null) {
        handler(request);
      } else {
        _pending.add(request);
      }
    }
  }

  /// Turns a shared item into a torrent request, or null when it is not a torrent.
  static TorrentIntentRequest? _toRequest(SharedMediaFile file) {
    // Magnet links and torrent URLs arrive as links or plain text in `path`.
    if (file.type == SharedMediaType.url || file.type == SharedMediaType.text) {
      final value = file.path.trim();
      if (value.toLowerCase().startsWith('magnet:')) {
        return TorrentIntentRequest.magnet(value);
      }
      if (value.toLowerCase().endsWith('.torrent')) {
        return TorrentIntentRequest.magnet(value);
      }
      return null;
    }

    // Files: only .torrent is interesting.
    final name = file.path.split('/').last.toLowerCase();
    if (name.endsWith('.torrent')) {
      return TorrentIntentRequest.file(file.path);
    }

    return null;
  }

  Future<void> dispose() async {
    await _subscription?.cancel();
    _subscription = null;
    _handler = null;
    _pending.clear();
    _started = false;
  }
}
